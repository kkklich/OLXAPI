using AF_mobile_web_api.Domain;
using AF_mobile_web_api.DTO;
using ApplicationDatabase.Models;

namespace AF_mobile_web_api.Repositories.Interfaces
{
    public interface IPropertyDataRepository: IGenericRepository<PropertyData>
    {
        Task<List<PropertyData>> GetLatestByCityAsync(string city);
        Task SaveMarketplaceDataAsync(List<PropertyData> properties);
        Task<List<TimelineGroup>> GetTimelineByCityAsync(string city);
        Task<PagedResultDTO<PropertyListItemDTO>> GetPagedAsync(PropertyQueryParams query);
        Task<List<PropertyData>> GetHistoryCandidatesAsync(string city, string url, double areaMin, double areaMax);
        Task<PropertyData?> GetLatestByUrlAsync(string city, string url);
    }
}
