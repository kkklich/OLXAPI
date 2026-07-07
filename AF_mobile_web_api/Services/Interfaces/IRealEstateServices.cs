using AF_mobile_web_api.Domain;
using AF_mobile_web_api.DTO;
using AF_mobile_web_api.DTO.Enums;

namespace AF_mobile_web_api.Services.Interfaces
{
    public interface IRealEstateServices
    {
        Task<MarketplaceSearch> GetDataAsync(string city);
        Task<MarketplaceSearch> GetdataForManyCitiesAsync();
        Task<MarketplaceSearch> LoadDataMarkeplacesAsync(CityEnum city = CityEnum.Krakow);
        Task<List<SearchDataDTO>> GetUniqueOffertsAsync();


    }
}
