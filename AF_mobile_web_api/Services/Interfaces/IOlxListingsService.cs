using AF_mobile_web_api.DTO;
using AF_mobile_web_api.DTO.Enums;

namespace AF_mobile_web_api.Services.Interfaces;

public interface IOlxListingsService
{
    Task<MarketplaceCityPage> GetPageAsync(CityEnum city, int priceFrom, int priceTo,
        int page, int pageSize, CancellationToken cancellationToken = default);
}
