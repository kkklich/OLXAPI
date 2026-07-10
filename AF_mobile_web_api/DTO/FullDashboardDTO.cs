namespace AF_mobile_web_api.DTO
{
    public class FullDashboardDTO
    {
        public DashboardChartsDTO Charts { get; set; } = new();
        public MarketInsightsDTO Insights { get; set; } = new();
        public List<MapPointDTO> MapPoints { get; set; } = new();
    }
}
