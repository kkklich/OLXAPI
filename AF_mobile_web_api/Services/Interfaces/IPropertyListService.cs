using AF_mobile_web_api.DTO;

namespace AF_mobile_web_api.Services.Interfaces
{
    // Read side of the offers list: the paginated table and a single offer's price history.
    public interface IPropertyListService
    {
        Task<PagedResultDTO<PropertyListItemDTO>> GetPagedAsync(PropertyQueryParams query);
        Task<PropertyHistoryDTO?> GetHistoryAsync(string city, string url);
    }
}
