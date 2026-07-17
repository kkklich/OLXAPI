using AF_mobile_web_api.Domain;
using AF_mobile_web_api.DTO;
using AF_mobile_web_api.DTO.Enums;
using AF_mobile_web_api.Repositories.Interfaces;
using AF_mobile_web_api.Services.Interfaces;
using ApplicationDatabase;
using ApplicationDatabase.Models;
using AutoMapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace AF_mobile_web_api.Services
{
    public class RealEstateServices: IRealEstateServices
    {
        private readonly IOLXAPIService _olxApiService;
        private readonly IMorizonApiService _morizonApiService;
        private readonly INieruchomosciOnlineService _nieruchomosciOnlineService;
        private readonly IMapper _mapper;
        private readonly IPropertyDataRepository _propertyDataRepository;
        private readonly IMemoryCache _cache;
        private readonly ILogger<RealEstateServices> _logger;

        public RealEstateServices(
            IOLXAPIService olxApiService,
            IMorizonApiService morizonApiService,
            INieruchomosciOnlineService nieruchomosciOnlineService,
            IMapper mapper,
            IPropertyDataRepository propertyDataRepository,
            IMemoryCache cache,
            ILogger<RealEstateServices> logger)
        {
            _olxApiService = olxApiService;
            _morizonApiService = morizonApiService;
            _nieruchomosciOnlineService = nieruchomosciOnlineService;
            _mapper = mapper;
            _propertyDataRepository = propertyDataRepository;
            _cache = cache;
            _logger = logger;
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
            // Start all three scrapes concurrently, but await each one separately so a single
            // failing source does not discard the results of the healthy ones.
            var nieruchomosciTask = _nieruchomosciOnlineService.GetAllPagesAsync(city);
            var morizonTask = _morizonApiService.GetPropertyListingDataAsync(city);
            var olxTask = _olxApiService.GetOLXResponse(city);

            var olxData = await GetSourceDataSafeAsync(olxTask, "OLX", city);
            var morizonData = await GetSourceDataSafeAsync(morizonTask, "Morizon", city);
            var nieruchomosciData = await GetSourceDataSafeAsync(nieruchomosciTask, "NieruchomosciOnline", city);

            var combinedData = new MarketplaceSearch
            {
                Data = olxData
                    .Union(morizonData)
                    .Union(nieruchomosciData)
                    .ToList()
            };

            if (combinedData.Data.Count == 0)
            {
                // Saving an empty batch would become the "latest" snapshot downstream
                // (GetLatestByCityAsync) and wipe the dashboard until the next scrape.
                _logger.LogError("All marketplace sources returned no data for {City}; skipping save", city);
                return combinedData;
            }

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

            // StatisticServices caches these per-city entries for 120 minutes; evict them so
            // dashboards pick up the freshly scraped batch instead of serving stale data.
            _cache.Remove($"RealEstateData_{city}");
            _cache.Remove($"FullDashboard_{city}");

            return combinedData;
        }

        private async Task<List<SearchData>> GetSourceDataSafeAsync(Task<MarketplaceSearch> sourceTask, string sourceName, CityEnum city)
        {
            try
            {
                var result = await sourceTask;
                return result?.Data ?? new List<SearchData>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Marketplace source {Source} failed for {City}; continuing with the remaining sources", sourceName, city);
                return new List<SearchData>();
            }
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
