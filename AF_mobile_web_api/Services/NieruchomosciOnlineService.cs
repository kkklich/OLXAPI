using AF_mobile_web_api.Domain;
using AF_mobile_web_api.DTO.Enums;
using AF_mobile_web_api.Services.Interfaces;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;

namespace AF_mobile_web_api.Services
{
    public class NieruchomosciOnlineService: INieruchomosciOnlineService
    {
        private readonly JsonSerializerOptions Options = new()
        {
            PropertyNameCaseInsensitive = true
        };
        private readonly IHTTPClientServices _httpClient;
        private readonly ILogger<NieruchomosciOnlineService> _logger;
        public NieruchomosciOnlineService(IHTTPClientServices httpClient, ILogger<NieruchomosciOnlineService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        public async Task<MarketplaceSearch> GetAllPagesAsync(CityEnum city = CityEnum.Krakow)
        {
            int minPriceStart = 150000;
            int maxPriceEnd = 10000000;
            int step = 3000;
            // The portal serves each city from its own subdomain; a hardcoded "krakow." here
            // would silently return Krakow-area results for every other CityEnum value.
            var citySubdomain = city.ToString().ToLowerInvariant();
            string BaseUrlTemplate = $"https://{citySubdomain}.nieruchomosci-online.pl/szukaj.html?3,mieszkanie,sprzedaz,,{{0}},,,,{{1}}-{{2}}&ajax=1";

            var allResults = new ConcurrentBag<SearchData>();
            // Ceiling division so the last partial range (9999000-10000000) is still fetched.
            var priceRanges = Enumerable.Range(0, (maxPriceEnd - minPriceStart + step - 1) / step)
                .Select(i => (Min: minPriceStart + i * step, Max: Math.Min(minPriceStart + (i + 1) * step, maxPriceEnd)))
                .ToList();

            // Runs in parallel with a concurrency limit (e.g., 10 tasks at a time)
            var cityName = city.ToEncodedString();
            await Parallel.ForEachAsync(priceRanges, new ParallelOptions { MaxDegreeOfParallelism = 10 }, async (range, ct) =>
            {
                string url = string.Format(BaseUrlTemplate, cityName, range.Min, range.Max);
                var listings = await GetApartmentListingsAsync(url);

                if (listings != null)
                {
                    foreach (var item in listings)
                        allResults.Add(item);
                }
            });

            var uniqueItems = allResults
                .GroupBy(x => x.Id)
                .Select(g => g.First())
                .ToList();

            MarketplaceSearch searchedData = new MarketplaceSearch()
            {
                Data = uniqueItems,
                TotalCount = uniqueItems.Count
            };

            return searchedData;
        }

        public async Task<List<SearchData>> GetApartmentListingsAsync(string url)
        {
            var headers = new Dictionary<string, string>
            {
                ["X-Requested-With"] = "XMLHttpRequest"
            };

            try
            {
                // GetRaw hands the response over, so it is released here
                using var response = await _httpClient.GetRaw(url, null, headers);
                // Ensure the request was successful
                response.EnsureSuccessStatusCode();
                // Read the response content as a string (likely JSON)
                string responseBody = await response.Content.ReadAsStringAsync();

                // One parse serves both halves of the page - the listings and their record props.
                // A page is ~700 KB, most of it rendered HTML that neither half reads.
                using var doc = JsonDocument.Parse(responseBody);
                var itemsAdditional = ParseListAdditionalData(doc.RootElement);
                var itemsProps = ParseListRecordPropsData(doc.RootElement);

                var result = Convert(itemsAdditional, itemsProps);

                return result;
            }
            catch (Exception ex)
            {
                // Not just HttpRequestException: a page that does not parse (JsonException, or
                // InvalidOperationException for an unexpected JSON shape) or a timeout would
                // otherwise escape Parallel.ForEachAsync in GetAllPagesAsync and discard every
                // price range of the portal for this city. The caller skips null results.
                _logger.LogError(ex, "Nieruchomosci-online page {Url} failed, skipping price range", url);
                return null;
            }
        }
                
        private List<AdditionalData> ParseListAdditionalData(JsonElement root)
        {
            if (!root.TryGetProperty("listAdditionalData", out var lad) ||
                lad.ValueKind != JsonValueKind.Object)
                return new List<AdditionalData>();

            // No capacity guess: GetRawText() would copy the whole payload into a new string
            // just to size the list.
            var result = new List<AdditionalData>();

            foreach (var prop in lad.EnumerateObject())
            {
                var data = prop.Value.Deserialize<AdditionalData>(Options)
                           ?? new AdditionalData();

                data.Id = prop.Name;
                var floor = data.Attributes?.FirstOrDefault(x => x.Alias == "floor")?.Values?.FirstOrDefault(x => int.TryParse(x.Value, out var i))?.Value;
                data.Floor = floor != null ? int.Parse(floor) : -1;

                result.Add(data);
            }

            return result;
        }

        private List<RecordProps> ParseListRecordPropsData(JsonElement root)
        {
            // Looked up under its own name: renaming "record_props" in the raw text first copied
            // the whole payload into a new string and parsed it a second time.
            if (!(root.TryGetProperty("pb", out var pbElement) && pbElement.TryGetProperty("record_props", out var lad)))
                return new List<RecordProps>();
            
            // No capacity guess, as in ParseListAdditionalData
            var result = new List<RecordProps>();

            foreach (var prop in lad.EnumerateObject())
            {
                var data = prop.Value.Deserialize<RecordProps>(Options)
                           ?? new RecordProps();
                data.Id = prop.Name;

                result.Add(data);
            }

            return result;
        }

        private List<SearchData> Convert(List<AdditionalData> items, List<RecordProps> recordProps)
        {
            var result = new List<SearchData>();

            long runningId = 1;
            foreach (var a in items)
            {
                var b = recordProps.FirstOrDefault(x => x.Id == a.Id);
                var sd = MapOne(a, b, runningId++);
                result.Add(sd);
            }

            return result;
        }

        private SearchData MapOne(AdditionalData a, RecordProps b, long internalId)
        {
            // b comes from FirstOrDefault and can be null (and Rccata/Pbh can be null
            // even when b isn't) — a single such listing must not kill the whole scrape.
            var area = b?.Rsur ?? 1.0D;
            var price = ParseDouble(a.PrimaryPrice);
            var buildingType = b?.Rccata == null
                ? ""
                : b.Rccata.ToLower().Contains("flats") ? "Blok" : b.Rccata;
            var seller = b?.Pbh?.ToLower();
            var isPrivate = !string.IsNullOrWhiteSpace(seller)
                && !seller.Contains("agency")
                && !seller.Contains("dev");
            var sd = new SearchData
            {
                Id = a.Id,
                WebName = WebName.NieruchomsciOnline,
                Url = a.ShareUrl ?? "",
                Title = a.MetaTitle ?? "",
                Price = price,
                Market = a.Market switch
                {
                    "primary" => "Pierwotny",
                    "secondary" => "Wtórny",    
                    _ => a.Market ?? "Pierwotny"
                },
                Private = isPrivate,
                Area = area,
                PricePerMeter = area > 0 ? price / area : 0,
                Location = new LocationPlace
                {
                    Lat = ParseDouble(a.Map?.Latitude),
                    Lon = ParseDouble(a.Map?.Longitude),
                    City = a?.DlData?.CityName ?? "",
                    District = a?.DlData?.QuarterName ?? ""
                },
                Floor = a.Floor,
                BuildingType = buildingType
            };
            
            return sd;
        }

        private double ParseDouble(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return 0;
            s = s.Replace(" ", "").Replace("\u00A0", "");
            return double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0;
        }
    }
}
