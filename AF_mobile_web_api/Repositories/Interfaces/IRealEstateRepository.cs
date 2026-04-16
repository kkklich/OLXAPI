using ApplicationDatabase.Models;

namespace AF_mobile_web_api.Repositories.Interfaces
{
    public interface IRealEstateRepository : IGenericRepository<WebSearchResults>
    {
        Task<WebSearchResults?> GetLatestSearchByCityAsync(string city);
        Task SaveWebSearchResultAsync(WebSearchResults searchResult);
    }
}
