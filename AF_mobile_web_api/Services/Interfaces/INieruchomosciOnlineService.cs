using AF_mobile_web_api.DTO;
using AF_mobile_web_api.DTO.Enums;

namespace AF_mobile_web_api.Services.Interfaces
{
    public interface INieruchomosciOnlineService
    {
        Task<MarketplaceSearch> GetAllPagesAsync(CityEnum city = CityEnum.Krakow);
        Task<List<SearchData>> GetApartmentListingsAsync(string url);


    }
}
