using System.Net;
using AF_mobile_web_api.Domain;
using AF_mobile_web_api.DTO.Enums;
using AF_mobile_web_api.Helper;
using AF_mobile_web_api.Services.Interfaces;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AF_mobile_web_api.Services
{
    public class OLXAPIService : IOLXAPIService
    {
        private readonly IHTTPClientServices _httpClient;
        private readonly ILogger<OLXAPIService> _logger;
        private readonly OlxScrapeOptions _options;

        public OLXAPIService(IHTTPClientServices httpClient, ILogger<OLXAPIService> logger,
            IOptions<OlxScrapeOptions> options)
        {
            _httpClient = httpClient;
            _logger = logger;
            _options = options.Value;
        }

        private sealed record RangeResult(List<SearchData> Offers, Exception? Error = null);

        public async Task<List<SearchData>> GetAllOffersAsync(
            int categoryId, int? regionId, int? cityId, int priceFrom, int priceTo)
        {
            const int bucketSize = 50_000;
            var ranges = new List<(int From, int To)>();
            for (var from = priceFrom; from <= priceTo; from += bucketSize)
                ranges.Add((from, Math.Min(from + bucketSize - 1, priceTo)));

            // Previously every one of ~200 ranges started an HTTP request immediately.
            using var slots = new SemaphoreSlim(Math.Clamp(_options.MaxConcurrentBuckets, 1, 8));
            Exception? permanentFailure = null;
            var tasks = ranges.Select(async range =>
            {
                await slots.WaitAsync();
                try
                {
                    var fatal = Volatile.Read(ref permanentFailure);
                    if (fatal != null) return new RangeResult(new(), fatal);
                    var result = await FetchRangeAsync(categoryId, regionId, cityId, range.From, range.To);
                    // Do not hammer an endpoint that rejects our query or denies access.
                    if (result.Error is HttpRequestException http && http.StatusCode is
                        HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
                        Interlocked.CompareExchange(ref permanentFailure, result.Error, null);
                    return result;
                }
                finally { slots.Release(); }
            });
            var results = await Task.WhenAll(tasks);
            var offers = results.SelectMany(result => result.Offers).DistinctBy(offer => offer.Id).ToList();
            var failures = results.Where(result => result.Error != null).ToList();
            if (failures.Count > 0)
                throw new PartialScrapeException(
                    $"OLX download incomplete for city_id {cityId}: {failures.Count}/{ranges.Count} price ranges failed; {offers.Count} offers retained.",
                    offers, failures[0].Error!);

            _logger.LogInformation("OLX city_id {CityId}: downloaded {Count} offers in {Ranges} price ranges", cityId, offers.Count, ranges.Count);
            return offers;
        }

        private async Task<RangeResult> FetchRangeAsync(
            int categoryId, int? regionId, int? cityId, int priceFrom, int priceTo)
        {
            var offers = new List<SearchData>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            const int limit = 40;
            const int maxOffset = 1000;
            try
            {
                for (var offset = 0; offset < maxOffset; offset += limit)
                {
                    var data = await GetPageAsync(BuildOffersUrl(categoryId, regionId, cityId, priceFrom, priceTo, offset, limit));
                    var page = data.Data;
                    var newIds = page.Where(offer => offer != null && !string.IsNullOrWhiteSpace(offer.Id))
                        .Where(offer => seen.Add(offer.Id)).ToList();
                    offers.AddRange(ExtractListOfParameters(newIds));
                    // Promoted offers repeat across pages. A repeated page is not more data.
                    var visibleCount = data.metadata?.visible_total_count;
                    if (offset + limit >= maxOffset && data.metadata?.total_elements > maxOffset)
                        return new(offers, new InvalidOperationException("OLX search contains more offers than its pagination limit."));
                    if (newIds.Count == 0 || (visibleCount > 0 && offset + limit >= visibleCount)
                        || (visibleCount is null or <= 0 && page.Count < limit)) return new(offers);
                    if (offset + limit >= maxOffset)
                    {
                        _logger.LogWarning("OLX pagination cap reached for {From}-{To}; retaining {Count} offers", priceFrom, priceTo, offers.Count);
                        return new(offers, new InvalidOperationException("OLX pagination limit reached before the range was exhausted."));
                    }
                    await Task.Delay(Math.Clamp(_options.PageDelayMilliseconds, 0, 60_000));
                }
                return new(offers);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OLX price bucket {PriceFrom}-{PriceTo} failed; retaining {Count} downloaded offers", priceFrom, priceTo, offers.Count);
                return new(offers, ex);
            }
        }

        private async Task<QueryData> GetPageAsync(string url)
        {
            var attempts = Math.Clamp(_options.MaxAttempts, 1, 5);
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    using var response = await _httpClient.GetRaw(url,
                        timeoutInSeconds: Math.Clamp(_options.RequestTimeoutSeconds, 1, 120),
                        headers: new Dictionary<string, string> { ["Accept"] = "application/json" });
                    var body = await response.Content.ReadAsStringAsync();
                    var json = JObject.Parse(body);
                    if (json["data"] is not JArray)
                        throw new JsonSerializationException("OLX response is missing its data array.");
                    return json.ToObject<QueryData>() ?? throw new JsonSerializationException("OLX response was empty.");
                }
                catch (Exception ex) when (attempt < attempts && IsTransient(ex))
                {
                    var delay = TimeSpan.FromMilliseconds(Math.Clamp(_options.RetryDelayMilliseconds, 0, 60_000) * Math.Pow(2, attempt - 1));
                    if (ex is UpstreamHttpException upstream && upstream.RetryAfter > delay)
                        delay = upstream.RetryAfter.Value;
                    // Respect a long server-requested pause by ending this range instead of
                    // retrying earlier than requested or holding the entire scrape indefinitely.
                    if (delay > TimeSpan.FromMinutes(2)) throw;
                    _logger.LogWarning(ex, "OLX request attempt {Attempt} failed; retrying after {Delay} seconds", attempt, delay.TotalSeconds);
                    await Task.Delay(delay);
                }
            }
        }

        private static bool IsTransient(Exception error) => error is TaskCanceledException
            || error is HttpRequestException http && (http.StatusCode is null or HttpStatusCode.RequestTimeout
                or HttpStatusCode.TooManyRequests || (int)http.StatusCode.Value >= 500);

        private static string BuildOffersUrl(int categoryId, int? regionId, int? cityId,
            int priceFrom, int priceTo, int offset, int limit)
        {
            var query = new List<string> {
                $"offset={offset}", $"limit={limit}", $"category_id={categoryId}",
                $"filter_float_price%3Afrom={priceFrom}", $"filter_float_price%3Ato={priceTo}"
            };
            if (regionId.HasValue) query.Add($"region_id={regionId.Value}");
            if (cityId.HasValue) query.Add($"city_id={cityId.Value}");
            return $"{ConstantHelper.OLXAPI}?{string.Join("&", query)}";
        }

        public async Task<MarketplaceSearch> GetOLXResponse(CityEnum city = CityEnum.Krakow)
        {
            var cityId = city.ToEncodedOLXString();
            if (cityId == 0)
            {
                _logger.LogInformation("No OLX city_id configured for {City}; OLX is not supported for this city", city);
                return new MarketplaceSearch();
            }
            var offers = await GetAllOffersAsync(ConstantHelper.RealEstateCategory,
                city.ToEncodedRegionOLXString(), cityId, 50_000, 10_000_000);
            var sorted = offers.OrderBy(offer => offer.PricePerMeter).ToList();
            return new MarketplaceSearch { Data = sorted, TotalCount = sorted.Count };
        }

        private List<SearchData> ExtractListOfParameters(List<Data> responses)
        {
            List<SearchData> result = new List<SearchData>();
            foreach (var response in responses)
            {
                if (response == null || string.IsNullOrWhiteSpace(response.Id) || string.IsNullOrWhiteSpace(response.Url)) continue;
                if (response.Title?.Contains("TBS", StringComparison.OrdinalIgnoreCase) == true)
                    continue;
                var extractedObject = ExtractParameter(response);
                result.Add(extractedObject);
            }

            return result;
        }

        private SearchData ExtractParameter(Data data)
        {
            var record = MapParamsToSearchData(data.Params);

            record.Id = data.Id;
            record.Url = data.Url;
            record.Title = data.Title;
            record.Private = !data.Business;
            record.Location = new LocationPlace()
            {
                City = data?.Location?.City?.Name ?? string.Empty,
                District = data?.Location?.District?.Name ?? string.Empty,
                Lat = data?.Map?.Lat ?? 0,
                Lon = data?.Map?.Lon ?? 0
            };
            record.WebName = WebName.OLX;
                   

            return record;
        }

        private SearchData MapParamsToSearchData(List<Param>? parameters)
        {
            var data = new SearchData();
            foreach (var param in parameters ?? new List<Param>())
            {
                if (param == null) continue;
                switch (param.Key)
                {
                    case ConstantHelper.Price:
                        if (param.Value?.Label != null)
                            data.Price = ParseNumberToDouble(param.Value.Label);
                        break;
                    case ConstantHelper.PricePerMeter:
                        data.PricePerMeter = ParseNumberToDouble(param?.Value?.Key ?? string.Empty);
                        break;
                    case ConstantHelper.FloorSelect:
                        if (int.TryParse(param.Value?.Label, out var floor))
                            data.Floor = floor;
                        break;
                    case ConstantHelper.Market:
                        data.Market = param.Value?.Label ?? string.Empty;
                        break;
                    case ConstantHelper.BuildType:
                        data.BuildingType = param.Value?.Label ?? string.Empty;
                        break;
                    case ConstantHelper.Area:
                        data.Area = ParseNumberToDouble(param?.Value?.Key ?? string.Empty);
                        break;
                }
            }

            // Fallback computed only after all params are parsed (param order is not guaranteed);
            // the Area/Price guards keep Infinity/NaN out of the database
            if (data.PricePerMeter <= 0 && data.Area > 0 && data.Price > 0)
                data.PricePerMeter = data.Price / data.Area;

            return data;
        }

        private double ParseNumberToDouble(string priceString)
        {
            if (string.IsNullOrWhiteSpace(priceString))
                return 0;

            var cleaned = priceString.Replace("zł", "", StringComparison.OrdinalIgnoreCase)
                                     .Replace(" ", "").Replace("\u00a0", "").Replace("\u202f", "")
                                     .Replace(",", ".", StringComparison.OrdinalIgnoreCase)
                                     .Trim();

            if (double.TryParse(cleaned, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double result))
            {
                return double.IsFinite(result) ? result : 0;
            }

            return 0;
        }
    }
}
