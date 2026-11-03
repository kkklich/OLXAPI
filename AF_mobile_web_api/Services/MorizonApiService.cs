using AF_mobile_web_api.Domain;
using AF_mobile_web_api.DTO.Enums;
using AF_mobile_web_api.Helper;
using AF_mobile_web_api.Services.Interfaces;
using Newtonsoft.Json.Linq;
using System.Globalization;
using System.Text.RegularExpressions;

namespace AF_mobile_web_api.Services
{
    public class MorizonApiService: IMorizonApiService
    {
        private readonly IHTTPClientServices _httpClient;
        private readonly ILogger<MorizonApiService> _logger;
        public MorizonApiService(IHTTPClientServices httpClient, ILogger<MorizonApiService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        private sealed record SearchPage(MarketplaceSearch Search, int RawCount);

        public async Task<MarketplaceSearch> GetPropertyListingDataAsync(CityEnum city = CityEnum.Krakow, string? searchUrl = null)
        {
            var failures = new System.Collections.Concurrent.ConcurrentBag<Exception>();
            var requestCount = 0;
            using var slots = new SemaphoreSlim(3);

            async Task<SearchPage> Fetch(string url)
            {
                await slots.WaitAsync();
                try
                {
                    Interlocked.Increment(ref requestCount);
                    return await FetchPageAsync(url);
                }
                finally { slots.Release(); }
            }

            async Task<List<SearchData>> Band(BuildingType building, MarketKind market, int from, int to)
            {
                var offers = new List<SearchData>();
                var url = $"/mieszkania/{building.ToString().ToLowerInvariant()}/{city.ToString().ToLowerInvariant()}/?ps%5Bprice_from%5D={from}&ps%5Bprice_to%5D={to}&ps%5Bmarket_type%5D={(int)market}";
                try
                {
                    var first = await Fetch(url);
                    void Add(SearchPage page)
                    {
                        foreach (var offer in page.Search.Data)
                        {
                            offer.Market = market.ToString();
                            offer.BuildingType = building.ToString();
                            offers.Add(offer);
                        }
                    }
                    Add(first);
                    var seen = first.Search.Data.Select(offer => offer.Id).ToHashSet(StringComparer.Ordinal);
                    if (first.RawCount == 0)
                    {
                        if (first.Search.TotalCount > 0)
                            throw new InvalidOperationException("Morizon returned an empty page for a non-empty search.");
                        return offers;
                    }
                    // Probe once. Split only a search that cannot fit within five pages.
                    // Overlap the split price so fractional prices cannot fall into a gap.
                    if (first.Search.TotalCount > first.RawCount * 5 && to - from > 5000)
                    {
                        var middle = from + (to - from) / 2;
                        offers.AddRange(await Band(building, market, from, middle));
                        offers.AddRange(await Band(building, market, middle, to));
                        return offers;
                    }
                    for (var page = 2; page <= 5 && seen.Count < first.Search.TotalCount; page++)
                    {
                        var next = await Fetch($"{url}&page={page}");
                        if (next.RawCount == 0)
                            throw new InvalidOperationException("Morizon pagination ended before its reported total.");
                        Add(next);
                        if (next.Search.Data.Count(offer => seen.Add(offer.Id)) == 0)
                            throw new InvalidOperationException("Morizon repeated a page before its reported total.");
                    }
                    if (seen.Count < first.Search.TotalCount && to - from > 5000)
                    {
                        var middle = from + (to - from) / 2;
                        offers.AddRange(await Band(building, market, from, middle));
                        offers.AddRange(await Band(building, market, middle, to));
                    }
                    else if (seen.Count < first.Search.TotalCount)
                        throw new InvalidOperationException("Morizon search exceeds the pagination limit even in a narrow price band.");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Morizon price band {From}-{To} failed; retaining {Count} offers", from, to, offers.Count);
                    failures.Add(ex);
                }
                return offers;
            }

            List<SearchData> all;
            if (!string.IsNullOrWhiteSpace(searchUrl))
            {
                // A supplied URL is an exact search, not a template for arbitrary price bands.
                var first = await Fetch(searchUrl);
                all = first.Search.Data;
                var seen = all.Select(offer => offer.Id).ToHashSet(StringComparer.Ordinal);
                try
                {
                    for (var page = 2; page <= 5 && seen.Count < first.Search.TotalCount; page++)
                    {
                        var next = await Fetch($"{searchUrl}{(searchUrl.Contains('?') ? "&" : "?")}page={page}");
                        all.AddRange(next.Search.Data);
                        if (next.RawCount == 0 || next.Search.Data.Count(offer => seen.Add(offer.Id)) == 0)
                            throw new InvalidOperationException("Morizon custom search pagination ended before its reported total.");
                    }
                }
                catch (Exception ex) { failures.Add(ex); }
                if (seen.Count < first.Search.TotalCount)
                    failures.Add(new InvalidOperationException("Morizon custom search exceeds five pages."));
            }
            else
            {
                var tasks = from building in Enum.GetValues<BuildingType>()
                            from market in Enum.GetValues<MarketKind>()
                            select Band(building, market, 100_000, 10_000_000);
                all = (await Task.WhenAll(tasks)).SelectMany(items => items).ToList();
            }
            var unique = all.DistinctBy(offer => offer.Id).ToList();
            var unpriced = unique.RemoveAll(offer => offer.Price <= 0);
            _logger.LogInformation("Morizon {City}: {Requests} searches, {Count} priced offers, {Unpriced} unpriced skipped", city, requestCount, unique.Count, unpriced);
            if (!failures.IsEmpty)
                throw new PartialScrapeException("Morizon download incomplete; retained successfully downloaded offers.", unique, failures.First());
            return new MarketplaceSearch { Data = unique, TotalCount = unique.Count };
        }

        public async Task<MarketplaceSearch?> FetchSingleSearchAsync(string? searchUrl = null)
        {
            try { return (await FetchPageAsync(searchUrl ?? ConstantHelper.DefaultSearchUrl)).Search; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching Morizon search {SearchUrl}", searchUrl);
                return null;
            }
        }

        private async Task<SearchPage> FetchPageAsync(string searchUrl)
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    var request = new { query = ConstantHelper.GraphqlQuery, variables = new { url = searchUrl } };
                    var response = await _httpClient.PostAsync<object, JObject>(ConstantHelper.MorizonAPI, request, false, 30.0);
                    if (response["errors"] is JArray errors && errors.Count > 0)
                        throw new InvalidOperationException("Morizon returned GraphQL errors.");
                    if (response["data"]?["searchResult"]?["properties"]?["nodes"] is not JArray nodes)
                        throw new InvalidOperationException("Morizon response is missing its property nodes.");
                    if (response["data"]?["searchResult"]?["properties"]?["totalCount"]?.Type != JTokenType.Integer)
                        throw new InvalidOperationException("Morizon response is missing its result count.");
                    return new SearchPage(TransformToMarketplaceSearch(response), nodes.Count);
                }
                catch (Exception ex) when (attempt < 3 &&
                    (ex is TaskCanceledException || ex is HttpRequestException http &&
                    (http.StatusCode is null or System.Net.HttpStatusCode.RequestTimeout or System.Net.HttpStatusCode.TooManyRequests
                        || (int)http.StatusCode.Value >= 500)))
                {
                    var delay = TimeSpan.FromMilliseconds(500 * Math.Pow(2, attempt - 1));
                    if (ex is UpstreamHttpException upstream && upstream.RetryAfter > delay) delay = upstream.RetryAfter.Value;
                    if (delay > TimeSpan.FromMinutes(2)) throw;
                    await Task.Delay(delay);
                }
            }
        }

        private MarketplaceSearch TransformToMarketplaceSearch(JObject jsonResponse)
        {
            var marketplaceSearch = new MarketplaceSearch();

            try
            {
                var searchResult = jsonResponse["data"]["searchResult"];
                if(searchResult == null) return marketplaceSearch;
                var properties = searchResult["properties"];

                // Get total count
                if (properties["totalCount"] != null)
                {
                    marketplaceSearch.TotalCount = properties["totalCount"].Value<int>();
                }

                var nodes = properties["nodes"];

                foreach (var property in nodes)
                {
                    var searchData = TransformPropertyToSearchData(property);
                    if (searchData != null)
                    {
                        marketplaceSearch.Data.Add(searchData);
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Error transforming JSON to MarketplaceSearch: {ex.Message}", ex);
            }

            return marketplaceSearch;
        }

        private SearchData TransformPropertyToSearchData(JToken property)
        {
            try
            {
                var searchData = new SearchData();

                // Basic properties
                searchData.Id = property["id"]?.Value<string>() ?? property["idOnFrontend"]?.Value<string>() ?? "0";
                searchData.Url = "https://www.morizon.pl" + property["url"]?.Value<string>();
                searchData.Title = property["title"]?.Value<string>() ?? property["advertisementText"]?.Value<string>();

                // Price information
                // JSON nulls arrive as JValue(Null), which passes a reference null-check but
                // throws on child access - hence the JTokenType.Object guards below. A null price
                // is an offer listed without one: it parses to 0, and GetPropertyListingDataAsync
                // skips it.
                var priceElement = property["price"];
                if (priceElement != null && priceElement.Type == JTokenType.Object)
                {
                    var amountString = priceElement["amount"]?.Value<string>();
                    if (double.TryParse(amountString, NumberStyles.Any, CultureInfo.InvariantCulture, out var price))
                    {
                        searchData.Price = price;
                    }
                }

                var priceM2Element = property["priceM2"];
                if (priceM2Element != null && priceM2Element.Type == JTokenType.Object)
                {
                    var amountM2String = priceM2Element["amount"]?.Value<string>();
                    if (double.TryParse(amountM2String, NumberStyles.Any, CultureInfo.InvariantCulture, out var pricePerMeter))
                    {
                        searchData.PricePerMeter = pricePerMeter;
                    }
                }

                // Floor information
                var floorFormatted = property["floorFormatted"]?.Value<string>();
                searchData.Floor = ExtractFloorNumber(floorFormatted);

                // Area
                var areaString = property["area"]?.Value<string>();
                if (double.TryParse(areaString, NumberStyles.Any, CultureInfo.InvariantCulture, out var area))
                {
                    searchData.Area = area;
                }

                // Private property indicator
                searchData.Private = DetermineIfPrivate(property["contact"]);

                // Location information with coordinates
                searchData.Location = ExtractLocationData(property);

                searchData.WebName = WebName.Morizon;

                return searchData;
            }
            catch (Exception ex)
            {
                // Log the error but continue processing other properties
                Console.WriteLine($"Error processing property: {ex.Message}");
                return null;
            }
        }

        private LocationPlace ExtractLocationData(JToken property)
        {
            var location = new LocationPlace();

            var locationElement = property["location"];
            if (locationElement != null && locationElement.Type == JTokenType.Object)
            {
                // Coordinates: location.map.center, the point the offer page pins on its map, sent
                // as JSON numbers ({"latitude":50.0599,"longitude":19.9383}). The Object guards
                // keep an offer that comes without them (a null map or center) instead of throwing
                // and dropping it; it keeps Lat/Lon 0, the "no coordinates" value the map skips.
                var mapElement = locationElement["map"];
                var centerElement = mapElement != null && mapElement.Type == JTokenType.Object ? mapElement["center"] : null;
                if (centerElement != null && centerElement.Type == JTokenType.Object)
                {
                    var latString = centerElement["latitude"]?.Value<string>();
                    if (double.TryParse(latString, NumberStyles.Any, CultureInfo.InvariantCulture, out var lat))
                    {
                        location.Lat = lat;
                    }

                    var lonString = centerElement["longitude"]?.Value<string>();
                    if (double.TryParse(lonString, NumberStyles.Any, CultureInfo.InvariantCulture, out var lon))
                    {
                        location.Lon = lon;
                    }
                }

                // Extract location array for city/district
                var locationArray = locationElement["location"];
                if (locationArray != null && locationArray.Type == JTokenType.Array)
                {
                    var locationParts = locationArray.Select(x => x.Value<string>()).Where(x => !string.IsNullOrEmpty(x)).ToList();

                    // Assign city and district based on location hierarchy
                    if (locationParts.Count > 0)
                        location.City = locationParts[0];
                    if (locationParts.Count > 1)
                        location.District = locationParts[1];
                }
            }

            return location;
        }

        private int ExtractFloorNumber(string floorFormatted)
        {
            if (string.IsNullOrEmpty(floorFormatted))
                return 0;

            try
            {
                if (floorFormatted.ToLower().Contains("parter"))
                    return 0;

                var match = Regex.Match(floorFormatted, @"(-?\d+)");
                if (match.Success && int.TryParse(match.Groups[1].Value, out var floor))
                {
                    return floor;
                }
            }
            catch
            {
                 return 0;
            }
            return 0;
        }    

        private bool DetermineIfPrivate(JToken contact)
        {
            if (contact == null || contact.Type != JTokenType.Object)
                return false;

            var company = contact["company"];
            var person = contact["person"];

            bool hasCompany = company != null && company.Type != JTokenType.Null && company.HasValues; 
            bool hasPerson = person != null && person.Type != JTokenType.Null && person.HasValues;

            if (hasCompany) return false;
            if (hasPerson) return true;

            return false;
        }
    }
}

   

