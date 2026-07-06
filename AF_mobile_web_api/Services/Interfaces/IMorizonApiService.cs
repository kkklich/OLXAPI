using AF_mobile_web_api.Domain;
using AF_mobile_web_api.DTO.Enums;

namespace AF_mobile_web_api.Services.Interfaces
{
    public interface IMorizonApiService
    {
        Task<MarketplaceSearch> GetPropertyListingDataAsync(CityEnum city = CityEnum.Krakow, string? searchUrl = null);
        Task<MarketplaceSearch?> FetchSingleSearchAsync(string? searchUrl = null);
    }
}
