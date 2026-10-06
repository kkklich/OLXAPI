using AF_mobile_web_api.Domain;
using AF_mobile_web_api.DTO;
using AF_mobile_web_api.DTO.Enums;

namespace AF_mobile_web_api.Services.Interfaces
{
    public interface IRealEstateServices
    {
        /// <summary>
        /// The latest scrape of the named city, from the cache. Throws ArgumentException for
        /// a name that is not a <see cref="CityEnum"/> value.
        /// </summary>
        Task<MarketplaceSearch> GetDataAsync(string city);

        /// <summary>
        /// The latest scrape of one city, cached as RealEstateData_{city} until a scrape of that
        /// city evicts it. The list is shared by every caller: read it, never modify it.
        /// </summary>
        Task<List<SearchData>> GetLatestBatchAsync(CityEnum city);

        Task<MarketplaceSearch> GetdataForManyCitiesAsync();
        Task<MarketplaceSearch> LoadDataMarkeplacesAsync(CityEnum city = CityEnum.Krakow);
        Task<List<SearchDataDTO>> GetUniqueOffertsAsync();
    }
}
