using AF_mobile_web_api.DTO;

namespace AF_mobile_web_api.Services.Interfaces
{
    public interface IStatisticServices
    {
        Task<List<TimelinePriceDto>> GetTimelinePrice(string cityName);
        Task<RealEstateStatistics> GetDataWithStatistics();
        Task<Dictionary<object, RealEstateStatistics>> GetDataWithGroupStatistics(string groupByProperty);
        Dictionary<TKey, RealEstateStatistics> CalculateStatisticsGroupBy<TKey>(
          List<SearchData> data,
          Func<SearchData, TKey> keySelector);
        Task<ChartData> GetBarChartData(string city, string groupedBy);
        Task<ChartData> FilterByParameter(string groupBy, string city, string parameter);
    }
}
