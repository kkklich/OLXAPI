using AF_mobile_web_api.DTO;
using AF_mobile_web_api.DTO.Enums;
using AF_mobile_web_api.Services.Interfaces;
using ApplicationDatabase;
using ApplicationDatabase.Models;
using AutoMapper;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace AF_mobile_web_api.Services
{
    public class RealEstateServices: IRealEstateServices
    {
        private readonly AppDbContext _dbContext;
        private readonly OLXAPIService _olxApiService;
        private readonly MorizonApiService _morizonApiService;
        private readonly NieruchomosciOnlineService _nieruchomosciOnlineService;
        private readonly IMapper _mapper;

        public RealEstateServices(AppDbContext dbContext,
            OLXAPIService olxApiService,
            MorizonApiService morizonApiService, 
            NieruchomosciOnlineService nieruchomosciOnlineService,
            IMapper mapper)            
        {
            _dbContext = dbContext;
            _olxApiService = olxApiService;
            _morizonApiService = morizonApiService;
            _nieruchomosciOnlineService = nieruchomosciOnlineService;
            _mapper = mapper;
        }        

        public async Task<MarketplaceSearch> GetData(string city)
        {
            var recentEntry = await _dbContext.WebSearchResults
                .Where(w => w.City.ToLower() == city.ToLower())
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

        private void SavePropertyDataToDatabase(List<SearchData> data)
        {
            List<PropertyData> propertiesList = new List<PropertyData>();
            foreach (var item in data)
            {
                var property = new PropertyData  ///TODO add automapper
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
        }

        public async Task<List<SearchDataDTO>> GetUniqueOfferts()
        {
            var data = await GetData(CityEnum.Krakow.ToString());
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
    }
}
