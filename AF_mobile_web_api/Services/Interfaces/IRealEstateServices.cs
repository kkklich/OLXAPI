using AF_mobile_web_api.DTO;
using AF_mobile_web_api.DTO.Enums;

namespace AF_mobile_web_api.Services.Interfaces
{
    public interface IRealEstateServices
    {
        Task<MarketplaceSearch> GetData(string city);
        Task<MarketplaceSearch> GetdataForManyCities();
        Task<MarketplaceSearch> LoadDataMarkeplaces(CityEnum city = CityEnum.Krakow);
        Task<List<SearchDataDTO>> GetUniqueOfferts();


    }
}
