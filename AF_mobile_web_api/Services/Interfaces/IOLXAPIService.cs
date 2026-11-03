using AF_mobile_web_api.Domain;
using AF_mobile_web_api.DTO.Enums;
using AF_mobile_web_api.DTO;

namespace AF_mobile_web_api.Services.Interfaces
{
    public interface IOLXAPIService
    {
        Task<OlxPreviewResponse> GetPreviewAsync(CityEnum city, int priceFrom, int priceTo, int offset, int limit);
        Task<List<SearchData>> GetAllOffersAsync(int categoryId, int? regionId, int? cityId, int priceFrom, int priceTo);
        Task<MarketplaceSearch> GetOLXResponse(CityEnum city = CityEnum.Krakow);

    }
}
