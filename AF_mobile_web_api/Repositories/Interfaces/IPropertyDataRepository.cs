using AF_mobile_web_api.Domain;
using AF_mobile_web_api.DTO;
using ApplicationDatabase.Models;

namespace AF_mobile_web_api.Repositories.Interfaces
{
    public interface IPropertyDataRepository: IGenericRepository<PropertyData>
    {
        Task<List<PropertyData>> GetLatestByCityAsync(string city);
        Task SaveMarketplaceDataAsync(List<PropertyData> properties);
        Task SaveWeeklyCityAsync(List<PropertyData> properties, WeeklyScrapeCity result);
        Task<List<WeeklyScrapeCity>> GetWeeklyCitiesAsync(long runId);
        Task<List<TimelineGroup>> GetTimelineByCitiesAsync(IReadOnlyCollection<string> cities);
        IAsyncEnumerable<LatestOfferRow> StreamLatestOffersAsync();
        Task<List<OfferPageDetail>> GetPageDetailsAsync(IReadOnlyList<Guid> ids);
        Task<List<PropertyData>> GetHistoryCandidatesAsync(string city, HistoryCandidateFilter filter, CancellationToken cancellationToken = default);
        Task<PropertyData?> GetLatestByUrlAsync(string city, string url, CancellationToken cancellationToken = default);
        Task<List<PriceDropDTO>> GetPriceDropsAsync(string city, int limit);
        Task<DateTime?> GetLatestScrapeTimeAsync();
    }
}
