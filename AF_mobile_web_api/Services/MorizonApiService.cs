using AF_mobile_web_api.DTO;
using System.Text.Json;
using System.Text;
using Newtonsoft.Json.Linq;
using System.Globalization;
using System.Text.RegularExpressions;
using AF_mobile_web_api.Helper;
using Microsoft.OpenApi.Services;
using AF_mobile_web_api.DTO.Enums;

namespace AF_mobile_web_api.Services
{
    public class MorizonApiService
    {
        private readonly HTTPClientServices _httpClient;

        public MorizonApiService(HTTPClientServices httpClient)
        {
            _httpClient = httpClient;           
        }

        public async Task<MarketplaceSearch> GetPropertyListingDataAsync(CityEnum city = CityEnum.Krakow, string? searchUrl = null)
        {
            try
            {
                var cityName = city.ToString().ToLower();
                var allResults = new MarketplaceSearch { Data = new List<SearchData>() };
                var tasks = new List<Task<MarketplaceSearch>>();

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

                    foreach (var result in batchResults.Where(r => r != null))
                    {
                        result.Data.ForEach(p =>
                        {
                            p.Market = batch[0].Market.ToString();
                            p.BuildingType = batch[0].Building.ToString();
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
            catch (Exception ex)
            {
                throw new Exception($"Error fetching comprehensive property data: {ex.Message}", ex);
            }
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
                throw new Exception($"Error fetching data from Morizon API: {ex.Message}", ex);
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

                // Parse creation date
                var addedAtString = property["addedAt"]?.Value<string>();
                if (!string.IsNullOrEmpty(addedAtString))
                {
                    if (DateTime.TryParseExact(addedAtString, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var parsedDate))
                    {
                        searchData.CreatedTime = parsedDate;
                    }
                    else if (DateTime.TryParse(addedAtString, out parsedDate))
                    {
                        searchData.CreatedTime = parsedDate;
                    }
                }

                // Price information
                var priceElement = property["price"];
                if (priceElement != null)
                {
                    var amountString = priceElement["amount"]?.Value<string>();
                    if (double.TryParse(amountString, NumberStyles.Any, CultureInfo.InvariantCulture, out var price))
                    {
                        searchData.Price = price;
                    }
                }

                var priceM2Element = property["priceM2"];
                if (priceM2Element != null)
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

                // Description
                searchData.Description = property["description"]?.Value<string>();         

                // Private property indicator
                searchData.Private = DetermineIfPrivate(property["contact"]);

                // Location information with coordinates
                searchData.Location = ExtractLocationData(property);

                // Photos with enhanced information
                searchData.Photos = ExtractPhotosData(property, searchData.Id);
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
            if (locationElement != null)
            {
                // Extract coordinates
                var coordinatesElement = locationElement["coordinates"];
                if (coordinatesElement != null)
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

                // Extract street and number directly from the API response
                location.Street = locationElement["street"]?.Value<string>();
                location.Number = locationElement["number"]?.Value<string>();

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

        private List<Photos> ExtractPhotosData(JToken property, string propertyId)
        {
            var photos = new List<Photos>();

            var photosElement = property["photos"];
            var longId = long.Parse(propertyId);
            if (photosElement != null && photosElement.Type == JTokenType.Array)
            {
                foreach (var photo in photosElement)
                {
                    var photoObj = new Photos();

                    // Extract photo ID
                    var photoIdString = photo["id"]?.Value<string>();
                    if (long.TryParse(photoIdString, out var photoId))
                    {
                        photoObj.Id = photoId;
                    }

                    // Extract filename from name
                    photoObj.Filename = photo["name"]?.Value<string>() ?? $"property_{propertyId}_{photoObj.Id}.jpg";

                    // Extract dimensions
                    var widthString = photo["width"]?.Value<string>();
                    if (int.TryParse(widthString, out var width))
                    {
                        photoObj.Width = width;
                    }

                    var heightString = photo["height"]?.Value<string>();
                    if (int.TryParse(heightString, out var height))
                    {
                        photoObj.Height = height;
                    }

                    // Construct photo URL
                    photoObj.Link = ConstructPhotoUrl(photoObj.Filename, photoObj.Id, longId);

                    photos.Add(photoObj);
                }
            }
            else
            {
                var photosNumber = property["photosNumber"]?.Value<int>() ?? 0;
                if (photosNumber > 0)
                {
                    // If no photos array but photosNumber exists, create placeholder photos
                    for (int i = 1; i <= photosNumber; i++)
                    {                      
                        photos.Add(new Photos
                        {
                            Id = longId * 1000 + i,
                            Filename = $"property_{propertyId}_{i}.jpg",
                            Width = 800,
                            Height = 600,
                            Link = ConstructPhotoUrl($"property_{propertyId}_{i}.jpg", longId * 1000 + i, longId)
                        });
                    }
                }
            }

            return photos;
        }

        private string ConstructPhotoUrl(string filename, long photoId, long propertyId)
        {
            return $"{ConstantHelper.BasePhotoUrl}800x600/{photoId}/{filename}";
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
            if (contact == null || contact.Type == JTokenType.Null || contact.Type == JTokenType.Undefined)
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

   

