using System.Globalization;
using AF_mobile_web_api.DTO;
using AF_mobile_web_api.DTO.Enums;
using AF_mobile_web_api.Helper;
using ApplicationDatabase;
using ApplicationDatabase.Models;
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
            
        public RealEstateServices(HTTPClientServices httpClient, AppDbContext dbContext, OLXAPIService olxApiService, MorizonApiService morizonApiService, NieruchomosciOnlineService nieruchomosciOnlineService)
        {
            _httpClient = httpClient;
            _dbContext = dbContext;
            _olxApiService = olxApiService;
            _morizonApiService = morizonApiService;
            _nieruchomosciOnlineService = nieruchomosciOnlineService;
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

                GetUniqueByAreaFloorMarket(result.Data);
                return result;
            }
            return new MarketplaceSearch();
        }

        private List<SearchData> GetUniqueByAreaFloorMarket(List<SearchData> list)
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

            return uniqueDict.Values.ToList();
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

            //_dbContext.WebSearchResults.Add(findings);
            //await _dbContext.SaveChangesAsync();

            var newData = CompareNewData(result.Data, combinedData.Data);
            //SavePropertyDataToDatabase(newData);

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
    }
}
