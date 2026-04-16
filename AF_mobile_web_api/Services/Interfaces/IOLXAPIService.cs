using AF_mobile_web_api.DTO;
using AF_mobile_web_api.DTO.Enums;

namespace AF_mobile_web_api.Services.Interfaces
{
    public interface IOLXAPIService
    {
        Task<List<SearchData>> GetAllOffersAsync(int categoryId, int? regionId, int? cityId, int priceFrom, int priceTo);
        Task<MarketplaceSearch> GetOLXResponse(CityEnum city = CityEnum.Krakow);

    }
}
