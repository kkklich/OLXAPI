using System.Globalization;
using System.Text.RegularExpressions;
using AF_mobile_web_api.DTO;
using AF_mobile_web_api.DTO.Enums;
using AF_mobile_web_api.Helper;
using ApplicationDatabase;
using ApplicationDatabase.Models;
using AutoMapper;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace AF_mobile_web_api.Services
{
    public class RealEstateServices
    {
        private readonly HTTPClientServices _httpClient;
        private readonly AppDbContext _dbContext;
        private readonly OLXAPIService _olxApiService;
        private readonly MorizonApiService _morizonApiService;
        private readonly NieruchomosciOnlineService _nieruchomosciOnlineService;
        private readonly IMapper _mapper;

        public RealEstateServices(HTTPClientServices httpClient,
            AppDbContext dbContext,
            OLXAPIService olxApiService,
            MorizonApiService morizonApiService, 
            NieruchomosciOnlineService nieruchomosciOnlineService,
            IMapper mapper)            
        {
            _httpClient = httpClient;
            _dbContext = dbContext;
            _olxApiService = olxApiService;
            _morizonApiService = morizonApiService;
            _nieruchomosciOnlineService = nieruchomosciOnlineService;
            _mapper = mapper;
        }        

        public async Task<MarketplaceSearch> GetDataSave()
        {
            var recentEntry = await _dbContext.WebSearchResults
                .OrderByDescending(w => w.CreationDate)
                .FirstOrDefaultAsync();

            if (recentEntry != null)
            {
                var deserializedData = JsonConvert.DeserializeObject<List<SearchData>>(recentEntry.Content);
                MarketplaceSearch result = new MarketplaceSearch()
                {
                    Data = deserializedData ?? new List<SearchData>(),
                    TotalCount = recentEntry.Name != null ? int.Parse(recentEntry.Name) : 0,
                };

                return result;
            }
            return new MarketplaceSearch();
        }


        public async Task<List<SearchDataDTO>> GetUniqueOfferts()
        {
            var data = await GetDataSave();
            return GetUniqueByAreaFloorMarket(data.Data);
        }

        private List<SearchDataDTO> GetUniqueByAreaFloorMarket(List<SearchData> list)
        {
            var uniqueDict = new Dictionary<(double Area, int Floor, string Market, double Price), SearchData>();
            List<SearchData> uniqueList = new List<SearchData>();

            foreach (var item in list)
            {
                var key = (item.Area, item.Floor, item.Market, item.Price);
                if (!uniqueDict.ContainsKey(key))
                {
                    uniqueDict[key] = item;
                }
                else
                {
                    uniqueDict[key].Url += ", " + item.Url;
                }
            }

            var xdd = uniqueDict.Values
        .Where(sd => sd.Url.Contains(","))
        .ToList();

            return _mapper.Map<List<SearchDataDTO>>(uniqueDict.Values);
        }

        private  List<SearchDataDTO> GetUniqueByAreaFloorMarket2(List<SearchData> list)
        {

            //todo check Description and Title similarity and Price 15% diffrent
            //var titleSim = GetSimilarity(Title1, Title2);, if titleSim > 0.5 then it means that descriptions are similar
            var uniqueDict = new Dictionary<(double Area, int Floor, string Market), SearchData>();
            var duplicatesDict = new Dictionary<(double Area, int Floor, string Market), List<SearchData>>();
            var finalList = new Dictionary<(double Area, int Floor, string Market), List<SearchData>>();
            List<SearchData> uniqueList = new List<SearchData>();

            foreach (var item in list)
            {
                var key = (item.Area, item.Floor, item.Market);
                if (!uniqueDict.ContainsKey(key))
                {
                    uniqueDict[key] = item;
                    duplicatesDict[key] = new List<SearchData>() { item };
                }
                else
                {
                    duplicatesDict[key].Add(item);

                }
            }

            var ssss = duplicatesDict.Where(kv => kv.Value.Count > 1).ToList();
            List<SearchData> final = new List<SearchData>();

            foreach (var item in ssss)
            {
                var search = new SearchData();

                for (int i = 0; i < item.Value.Count; i++)
                {
                    for (int j = i + 1; j < item.Value.Count; j++)
                    {
                        if(item.Value[i].WebName == item.Value[j].WebName) 
                            continue;

                        var firstItem = item.Value[i];
                        var secondItem = item.Value[j];
                        double priceDiff = Math.Abs(item.Value[i].Price - item.Value[j].Price) / Math.Max(item.Value[i].Price, item.Value[j].Price);
                        if (priceDiff < 0.05)                       
                        {
                            item.Value[i].Url += "," + item.Value[j].Url;
                            if(search?.Url == null)
                                search = item.Value[i];
                            else
                                search.Url += "," + item.Value[j].Url;
                        }
                    }
                }
                final.Add(search);
                
            }

            var xdd = uniqueDict.Values
                .Where(sd => sd.Url.Contains(","))
                .ToList();
            return _mapper.Map<List<SearchDataDTO>>(final);
        }

        public async Task<MarketplaceSearch> GetdataForManyCities()
        {
            foreach (CityEnum city in Enum.GetValues(typeof(CityEnum)))
            {
                await LoadDataMarkeplaces(city);
            }

            return new MarketplaceSearch();
        }

        public async Task<MarketplaceSearch> LoadDataMarkeplaces(CityEnum city = CityEnum.Krakow)
        {
            var recentEntry = await _dbContext.WebSearchResults
                .Where(w => w.City == city.ToString())
                .OrderByDescending(w => w.CreationDate)
                .FirstOrDefaultAsync();

            MarketplaceSearch result = new MarketplaceSearch();
            if (recentEntry != null)
            {
                var deserializedData = JsonConvert.DeserializeObject<List<SearchData>>(recentEntry.Content);
                result.Data = deserializedData ?? new List<SearchData>();
            }

            // Proceed with fetching and saving data
            var nieruchomosciTask = _nieruchomosciOnlineService.GetAllPagesAsync(city);
            var morizonTask = _morizonApiService.GetPropertyListingDataAsync(city);
            var olxTask = _olxApiService.GetOLXResponse(city);

            await Task.WhenAll(nieruchomosciTask, morizonTask, olxTask);

            var responseNieruchomosci = await nieruchomosciTask;
            var responsemorizon = await morizonTask;
            var responseOLX = await olxTask;

            MarketplaceSearch combinedData = new MarketplaceSearch();
            combinedData.Data = responseOLX.Data.Union(responsemorizon.Data).ToList();
            combinedData.Data = combinedData.Data.Union(responseNieruchomosci.Data).ToList();            

            WebSearchResults findings = new WebSearchResults()
            {
                Name = combinedData.TotalCount.ToString(),
                Content = JsonConvert.SerializeObject(combinedData.Data),
                CreationDate = DateTime.UtcNow,
                City = city.ToString()
            };

            _dbContext.WebSearchResults.Add(findings);
            await _dbContext.SaveChangesAsync();

            var newData = CompareNewData(result.Data, combinedData.Data);
            SavePropertyDataToDatabase(newData);

            return combinedData;
        }

        private List<SearchData> CompareNewData(List<SearchData> DbList, List<SearchData> NewList)
        {
            var comparer = new SearchDataComparer();
            return NewList.Except(DbList, comparer).ToList();          
        }

        private void SavePropertyDataToDatabase(List<SearchData> data)
        {
            List<PropertyData> propertiesList = new List<PropertyData>();
            foreach (var item in data)
            {
                var property = new PropertyData
                {
                    OffertId = item.Id,
                    Url = item.Url,
                    Title = item.Title,
                    CreatedTime = item.CreatedTime,
                    Private = item.Private,
                    Price = item.Price,
                    PricePerMeter = item.PricePerMeter,
                    Floor = item.Floor,
                    Market = item.Market,
                    BuildingType = item.BuildingType,
                    Area = item.Area,
                    City = item.Location.City,
                    AddedRecordTime = DateTime.UtcNow
                };

                propertiesList.Add(property);
            }

            _dbContext.PropertyData.AddRange(propertiesList);
            _dbContext.SaveChanges();
        }



        public List<SearchData> FindAndCombineSimilarRealEstates(List<SearchData> listings, double titleThreshold = 0.8, double descriptionThreshold = 0.7)
        {
            var combined = new List<SearchData>();
            var visited = new bool[listings.Count];
            double titleTokenThreshold = 0.4;
            double descriptionTokenThreshold = 0.4;

            for (int i = 0; i < listings.Count; i++)
            {
                if (visited[i]) continue;

                var baseItem = listings[i];
                //var combinedItem = new SearchData
                //{
                //    Id = baseItem.Id,
                //    Url = baseItem.Url,
                //    Title = baseItem.Title,
                //    Price = baseItem.Price,
                //    PricePerMeter = baseItem.PricePerMeter,
                //    Floor = baseItem.Floor,
                //    Market = baseItem.Market,
                //    BuildingType = baseItem.BuildingType,
                //    Area = baseItem.Area,
                //    Description = baseItem.Description,
                //    Private = baseItem.Private,
                //    WebName = baseItem.WebName
                //};

                visited[i] = true;

                for (int j = i + 1; j < listings.Count; j++)
                {
                    if (visited[j]) continue;
                    //if (visited[j] || baseItem.Area != listings[j].Area || baseItem.Floor != listings[j].Floor || baseItem.WebName == listings[j].WebName) continue;
                    if (baseItem.WebName == listings[j].WebName) continue;
                    if (baseItem.Area != listings[j].Area || baseItem.Floor != listings[j].Floor || baseItem.Private != listings[j].Private) continue;
                    var xd = listings[j];

                    //var titleSim = TitleTokenSimilarity(baseItem.Title, listings[j].Title);
                    //var descSim = TitleTokenSimilarity(baseItem.Description ?? "", listings[j].Description ?? "");

                    //var titleSim2 = GetSimilarity(baseItem.Title, listings[j].Title);
                    //var descSim2 = GetSimilarity(baseItem.Description ?? "", listings[j].Description ?? "");

                    //if (titleSim2 >= titleTokenThreshold && descSim2 >= descriptionTokenThreshold)
                    //{

                    //}

                    //todo if baseItem.url contains url to the same website than skip                        
                        //baseItem.Url += ", " + listings[j].Url;

                    baseItem.Url = CombineUrls(baseItem.Url, listings[j].Url);
                    visited[j] = true;

                    //}
                    combined.Add(baseItem);
                }

                //combined.Add(combinedItem);
            }

            var xdd = combined
               .Where(sd => sd.Url.Contains(","))
               .ToList();

            return xdd;
            //return combined;
        }


        private static string CombineUrls(string baseUrl, string url)
        {
            Uri baseUri = new Uri(baseUrl);
            string baseDomain = baseUri.Host;

            string combinedUrl = baseUrl;

            Uri uri;
            // Try to build an absolute or relative Uri from url string
            if (Uri.TryCreate(url, UriKind.Absolute, out uri))
            {
                // It's an absolute url, check domain
                if (uri.Host != baseDomain)
                {
                    // Different domain, append full url
                    combinedUrl += ", " + url;
                }
                // else skip because same domain absolute url already covered by base
            }
            else
            {
                // Relative url, belongs to base domain, so append it
                combinedUrl += ", " + url;
            }
            

            return combinedUrl;
        }

        static readonly Regex wordSplitter = new Regex(@"\w+", RegexOptions.Compiled);


        public static double TitleTokenSimilarity(string s1, string s2)
        {
            var set1 = new HashSet<string>(wordSplitter.Matches(s1.ToLower()).Select(m => m.Value));
            var set2 = new HashSet<string>(wordSplitter.Matches(s2.ToLower()).Select(m => m.Value));

            if (set1.Count == 0 || set2.Count == 0)
                return 0;

            var intersection = set1.Intersect(set2).Count();
            var union = set1.Union(set2).Count();

            return (double)intersection / union;
        }


        private static double GetSimilarity(string s1, string s2)
        {
            if (string.IsNullOrEmpty(s1) || string.IsNullOrEmpty(s2))
                return 0;

            int len1 = s1.Length;
            int len2 = s2.Length;
            int[,] matrix = new int[len1 + 1, len2 + 1];

            for (int i = 0; i <= len1; i++)
                matrix[i, 0] = i;

            for (int j = 0; j <= len2; j++)
                matrix[0, j] = j;

            for (int i = 1; i <= len1; i++)
            {
                for (int j = 1; j <= len2; j++)
                {
                    int cost = s1[i - 1] == s2[j - 1] ? 0 : 1;
                    matrix[i, j] = Math.Min(Math.Min(matrix[i - 1, j] + 1, matrix[i, j - 1] + 1),
                                            matrix[i - 1, j - 1] + cost);
                }
            }

            int maxLen = Math.Max(len1, len2);
            int distance = matrix[len1, len2];
            return 1.0 - (double)distance / maxLen; // similarity ratio from edit distance
        }


    }
}
