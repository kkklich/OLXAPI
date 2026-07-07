namespace AF_mobile_web_api.DTO
{
    public class DashboardChartsDTO
    {
        public DashboardSummaryDTO Summary { get; set; } = new();
        public List<TimelinePointDTO> Timeline { get; set; } = new();
        public List<HistogramBinDTO> PricePerMeterHistogram { get; set; } = new();
        public List<DistrictPriceDTO> DistrictPrices { get; set; } = new();
        public List<SplitSliceDTO> MarketSplit { get; set; } = new();
        public List<SplitSliceDTO> BuildingTypeSplit { get; set; } = new();
    }

    public class DashboardSummaryDTO
    {
        public int TotalOffers { get; set; }
        public double MedianPrice { get; set; }
        public double MedianPricePerMeter { get; set; }
        public double MedianArea { get; set; }
        public double PrivateOffersPercent { get; set; }
        public string LastUpdated { get; set; } = string.Empty;
    }

    public class TimelinePointDTO
    {
        public string Date { get; set; } = string.Empty;
        public double AvgPricePerMeter { get; set; }
        public double AvgPrice { get; set; }
        public int Count { get; set; }
    }

    public class HistogramBinDTO
    {
        public string Label { get; set; } = string.Empty;
        public int Count { get; set; }
    }

    public class SplitSliceDTO
    {
        public string Name { get; set; } = string.Empty;
        public int Count { get; set; }
        public double MedianPricePerMeter { get; set; }
    }
}
