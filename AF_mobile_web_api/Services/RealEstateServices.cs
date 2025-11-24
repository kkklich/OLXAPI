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
                Name = combinedData.Data.Count.ToString(),
                Content = JsonConvert.SerializeObject(combinedData.Data),
                CreationDate = DateTime.UtcNow,
                City = city.ToString()
            };

            _dbContext.WebSearchResults.Add(findings); 
             SavePropertyDataToDatabase(combinedData.Data);
            await _dbContext.SaveChangesAsync();

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
                    AddedRecordTime = DateTime.UtcNow,
                    WebName = (int)item.WebName
                };

                propertiesList.Add(property);
            }

            _dbContext.PropertyData.AddRange(propertiesList);
            //await _dbContext.SaveChangesAsync();
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

        private List<SearchDataDTO> GetUniqueByAreaFloorMarket2(List<SearchData> list)
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
                        if (item.Value[i].WebName == item.Value[j].WebName)
                            continue;

                        var firstItem = item.Value[i];
                        var secondItem = item.Value[j];
                        double priceDiff = Math.Abs(item.Value[i].Price - item.Value[j].Price) / Math.Max(item.Value[i].Price, item.Value[j].Price);
                        if (priceDiff < 0.05)
                        {
                            item.Value[i].Url += "," + item.Value[j].Url;
                            if (search?.Url == null)
                                search = item.Value[i];
                            else
                                search.Url += "," + item.Value[j].Url;
                        }
                    }
                }
                final.Add(search);

            }

            return _mapper.Map<List<SearchDataDTO>>(final);
        }
    }
}
