using AF_mobile_web_api.Domain;
using AF_mobile_web_api.DTO;
using AF_mobile_web_api.DTO.Enums;
using AF_mobile_web_api.Repositories.Interfaces;
using AF_mobile_web_api.Services.Interfaces;
using ApplicationDatabase;
using ApplicationDatabase.Models;
using AutoMapper;
using Microsoft.EntityFrameworkCore;

namespace AF_mobile_web_api.Services
{
    public class RealEstateServices: IRealEstateServices
    {
        private readonly IOLXAPIService _olxApiService;
        private readonly IMorizonApiService _morizonApiService;
        private readonly INieruchomosciOnlineService _nieruchomosciOnlineService;
        private readonly IMapper _mapper;
        private readonly IPropertyDataRepository _propertyDataRepository;

        public RealEstateServices(
            IOLXAPIService olxApiService,
            IMorizonApiService morizonApiService,
            INieruchomosciOnlineService nieruchomosciOnlineService,
            IMapper mapper,
            IPropertyDataRepository propertyDataRepository)
        {
            _olxApiService = olxApiService;
            _morizonApiService = morizonApiService;
            _nieruchomosciOnlineService = nieruchomosciOnlineService;
            _mapper = mapper;
            _propertyDataRepository = propertyDataRepository;
        }

        public async Task<MarketplaceSearch> GetDataAsync(string city)
        {
            var latestBatch = await _propertyDataRepository.GetLatestByCityAsync(city);
            var data = _mapper.Map<List<SearchData>>(latestBatch);

            return new MarketplaceSearch
            {
                Data = data,
                TotalCount = data.Count
            };
        }

       
        public async Task<MarketplaceSearch> GetdataForManyCitiesAsync()
        {
            foreach (CityEnum city in Enum.GetValues(typeof(CityEnum)))
            {
                await LoadDataMarkeplacesAsync(city);
            }

            return new MarketplaceSearch();
        }

        public async Task<MarketplaceSearch> LoadDataMarkeplacesAsync(CityEnum city = CityEnum.Krakow)
        {
            var nieruchomosciTask = _nieruchomosciOnlineService.GetAllPagesAsync(city);
            var morizonTask = _morizonApiService.GetPropertyListingDataAsync(city);
            var olxTask = _olxApiService.GetOLXResponse(city);

            await Task.WhenAll(nieruchomosciTask, morizonTask, olxTask);

            var combinedData = new MarketplaceSearch
            {
                Data = olxTask.Result.Data
                    .Union(morizonTask.Result.Data)
                    .Union(nieruchomosciTask.Result.Data)
                    .ToList()
            };

            var propertiesList = _mapper.Map<List<PropertyData>>(combinedData.Data);

            // Stamp every row of this scrape with the same timestamp so it forms one identifiable
            // batch: GetLatestByCityAsync selects the newest batch for the snapshot charts.
            var scrapeTime = DateTime.UtcNow;
            foreach (var property in propertiesList)
            {
                property.City = city.ToString();
                property.AddedRecordTime = scrapeTime;
            }

            await _propertyDataRepository.SaveMarketplaceDataAsync(propertiesList);

            return combinedData;
        }

        public async Task<List<SearchDataDTO>> GetUniqueOffertsAsync()
        {
            var data = await GetDataAsync(CityEnum.Krakow.ToString());
            return GetUniqueByAreaFloorMarket(data.Data);
        }

        private List<SearchDataDTO> GetUniqueByAreaFloorMarket(List<SearchData> list)
        {
            var uniqueDict = new Dictionary<(double Area, int Floor, string Market, double Price), SearchData>();

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

            return _mapper.Map<List<SearchDataDTO>>(uniqueDict.Values);
        }
    }
}
