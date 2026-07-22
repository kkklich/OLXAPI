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

        public async Task<MarketplaceSearch> GetPropertyListingDataAsync(CityEnum city = CityEnum.Krakow, string? searchUrl = null)
        {
            var cityName = city.ToString().ToLower();
            var allResults = new MarketplaceSearch { Data = new List<SearchData>() };

            // Define price ranges to cover 100,000 to 1,000,000 PLN
            int minPrice = 100000;
            int maxPrice = 1000000;
            int priceStep = 5000;

            var contexts = new List<FetchContext>();

            if (!string.IsNullOrWhiteSpace(searchUrl))
            {
                contexts.Add(new FetchContext { Url = searchUrl});
            }
            else
            {
                foreach (var buildingType in Enum.GetValues<BuildingType>())
                {
                    foreach (var kind in Enum.GetValues<MarketKind>())
                    {
                        for (int priceFrom = minPrice; priceFrom < maxPrice; priceFrom += priceStep)
                        {
                            int priceTo = Math.Min(priceFrom + priceStep, maxPrice);
                            var building = buildingType.ToString().ToLower() ?? "";
                            var kindNumber = (int)kind;
                            var url = $"/mieszkania/{building}/{cityName}/?ps%5Bprice_from%5D={priceFrom}&ps%5Bprice_to%5D={priceTo}&ps%5Bmarket_type%5D={kindNumber}";
                            contexts.Add(new FetchContext
                            {
                                Url = url,
                                Market = kind,
                                Building = buildingType,
                                PriceFrom = priceFrom,
                                PriceTo = priceTo
                            });
                        }
                    }
                }
            }

            // Add pagination per context (pages 2-5)
            var expandedUrls = new List<FetchContext>();
            foreach (var ctx in contexts)
            {
                expandedUrls.Add(ctx);
                for (int page = 2; page <= 5; page++)
                {
                    expandedUrls.Add(new FetchContext
                    {
                        Url = $"{ctx.Url}&page={page}",
                        Market = ctx.Market,
                        Building = ctx.Building,
                        PriceFrom = ctx.PriceFrom,
                        PriceTo = ctx.PriceTo,
                        Page = page
                    });
                }
            }

            // Process URLs in batches to avoid overwhelming the API
            var batchSize = 10; // Adjust based on rate limits
            var batches = expandedUrls
                .Select((url, index) => new { url, index })
                .GroupBy(x => x.index / batchSize)
                .Select(g => g.Select(x => x.url).ToList())
                .ToList();

            foreach (var batch in batches)
            {
                var batchTasks = batch.Select(c => FetchSingleSearchAsync(c.Url)).ToArray();
                var batchResults = await Task.WhenAll(batchTasks);

                // batchResults is index-aligned with batch, so each result
                // is stamped with its own context, not the first one's
                for (int i = 0; i < batchResults.Length; i++)
                {
                    var result = batchResults[i];
                    if (result == null)
                        continue;

                    var context = batch[i];
                    result.Data.ForEach(p =>
                    {
                        p.Market = context.Market.ToString();
                        p.BuildingType = context.Building.ToString();
                    });
                    allResults.Data.AddRange(result.Data);
                    allResults.TotalCount += result.TotalCount;
                }

                // Add delay between batches to respect rate limits
                await Task.Delay(100); // 100 ms delay between batches
            }

            // Remove duplicates based on property ID
            var uniqueProperties = allResults.Data
                .GroupBy(p => p.Id)
                .Select(g => g.First())
                .ToList();

            return new MarketplaceSearch
            {
                Data = uniqueProperties,
                TotalCount = uniqueProperties.Count
            };
        }

        public async Task<MarketplaceSearch?> FetchSingleSearchAsync(string? searchUrl = null)
        {
            searchUrl ??= "/mieszkania/najtansze/krakow/?ps%5Bprice_from%5D=100000&ps%5Bprice_to%5D=750000";
            try
            {              
                var requestBody = new
                {
                    query = ConstantHelper.GraphqlQuery ,
                    variables = new { url = searchUrl }
                };

                // Use your HTTPClientServices to make the POST request
                var response = await _httpClient.PostAsync<object, JObject>(ConstantHelper.MorizonAPI, requestBody, false, 30.0);

                return TransformToMarketplaceSearch(response); //function to transform json to MarketplaceSearch object
            }
            catch (Exception ex)
            {
                // Callers filter out null results, so a single failed URL is logged
                // and skipped instead of failing the whole scrape via Task.WhenAll.
                _logger.LogError(ex, "Error fetching data from Morizon API for search URL {SearchUrl}", searchUrl);
                return null;
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
                // throws on child access - hence the JTokenType.Object guards below.
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
                // Extract coordinates
                var coordinatesElement = locationElement["coordinates"];
                if (coordinatesElement != null && coordinatesElement.Type == JTokenType.Object)
                {
                    var latString = coordinatesElement["lat"]?.Value<string>();
                    if (double.TryParse(latString, NumberStyles.Any, CultureInfo.InvariantCulture, out var lat))
                    {
                        location.Lat = lat;
                    }

                    var lngString = coordinatesElement["lng"]?.Value<string>();
                    if (double.TryParse(lngString, NumberStyles.Any, CultureInfo.InvariantCulture, out var lng))
                    {
                        location.Lon = lng;
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

   

