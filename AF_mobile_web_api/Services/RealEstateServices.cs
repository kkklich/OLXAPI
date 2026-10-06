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
        private readonly IOfferSnapshotCache _offers;
        private readonly ILogger<RealEstateServices> _logger;

        public RealEstateServices(
            IOLXAPIService olxApiService,
            IMorizonApiService morizonApiService,
            INieruchomosciOnlineService nieruchomosciOnlineService,
            IMapper mapper,
            IPropertyDataRepository propertyDataRepository,
            IMemoryCache cache,
            IOfferSnapshotCache offers,
            ILogger<RealEstateServices> logger)
        {
            _olxApiService = olxApiService;
            _morizonApiService = morizonApiService;
            _nieruchomosciOnlineService = nieruchomosciOnlineService;
            _mapper = mapper;
            _propertyDataRepository = propertyDataRepository;
            _cache = cache;
            _offers = offers;
            _logger = logger;
        }

        // Public getRealEstate/{city}: the city is parsed rather than passed through, so an
        // unknown name is a 400 instead of a database query (and a cache entry) per request.
        public async Task<MarketplaceSearch> GetDataAsync(string city)
        {
            var data = await GetLatestBatchAsync(StatisticServices.ParseCity(city));

            return new MarketplaceSearch
            {
                Data = data,
                TotalCount = data.Count
            };
        }

        // The latest scrape of one city - the dashboard's input and every legacy read endpoint's.
        // Cached here rather than in StatisticServices: this class evicts the entry after a
        // scrape, and its own getRealEstate/getUniqueOffers read it too, which they could not do
        // through StatisticServices without a DI cycle (StatisticServices depends on this class).
        // The list is shared by every caller until evicted: read it, never modify it.
        public async Task<List<SearchData>> GetLatestBatchAsync(CityEnum city)
        {
            var cacheKey = $"RealEstateData_{city}";

            if (_cache.TryGetValue(cacheKey, out List<SearchData>? cached) && cached is not null)
            {
                return cached;
            }

            var latestBatch = await _propertyDataRepository.GetLatestByCityAsync(city.ToString());
            var data = _mapper.Map<List<SearchData>>(latestBatch);

            _cache.Set(cacheKey, data, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = StatisticServices.CacheDuration
            });

            return data;
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

            // Every row of this run gets one AddedRecordTime, and the offers list joins each Url's
            // newest snapshot back on exactly (Url, AddedRecordTime), so two rows with one Url
            // would list that offer twice. Keep one row per exact Url: the first with a stated
            // price, or simply the first when none states one.
            var uniqueByUrl = combinedData.Data
                .GroupBy(p => p.Url, StringComparer.Ordinal)
                .Select(g => g.FirstOrDefault(p => p.Price > 0) ?? g.First())
                .ToList();

            var duplicateUrls = combinedData.Data.Count - uniqueByUrl.Count;
            if (duplicateUrls > 0)
            {
                _logger.LogWarning("Dropped {Count} rows of {City} repeating a Url of this scrape", duplicateUrls, city);
            }
            combinedData.Data = uniqueByUrl;

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

            // These per-city entries (the latest batch, cached by GetLatestBatchAsync, the
            // dashboard slices StatisticServices builds from it and the compressed map points)
            // live for 120 minutes; evict them so dashboards pick up the freshly scraped batch
            // instead of serving stale data.
            _cache.Remove($"RealEstateData_{city}");
            _cache.Remove($"FullDashboard_{city}");
            _cache.Remove($"PriceDrops_{city}");
            _cache.Remove(MapPointsPayloadProvider.CacheKey(city));

            // The offers list is served from a deduplicated snapshot of every city at once,
            // so it is rebuilt as a whole - the rows just saved are new newest-snapshots.
            _offers.Invalidate();

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
            var data = await GetLatestBatchAsync(CityEnum.Krakow);
            return GetUniqueByAreaFloorMarket(data);
        }

        private List<SearchDataDTO> GetUniqueByAreaFloorMarket(List<SearchData> list)
        {
            var uniqueDict = new Dictionary<(double Area, int Floor, string Market, double Price), SearchDataDTO>();

            foreach (var item in list)
            {
                var key = (item.Area, item.Floor, item.Market, item.Price);
                if (!uniqueDict.TryGetValue(key, out var unique))
                {
                    uniqueDict[key] = _mapper.Map<SearchDataDTO>(item);
                }
                else
                {
                    // Appended on the DTO, not on the offer: the offers are the cached batch, and
                    // writing to them would grow their Urls with every call until the next scrape.
                    unique.Url += ", " + item.Url;
                }
            }

            return uniqueDict.Values.ToList();
        }
    }
}
