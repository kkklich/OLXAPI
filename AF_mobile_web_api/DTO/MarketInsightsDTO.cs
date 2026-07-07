namespace AF_mobile_web_api.DTO
{
    public class MarketInsightsDTO
    {
        public int TotalOffers { get; set; }
        public double MedianPrice { get; set; }
        public double MedianPricePerMeter { get; set; }
        public double MinPricePerMeter { get; set; }
        public double MaxPricePerMeter { get; set; }
        public double MedianArea { get; set; }
        public double PrivateOffersPercent { get; set; }
        public List<SourceCountDTO> OffersBySource { get; set; } = new();
        public List<DistrictPriceDTO> Districts { get; set; } = new();
        public List<BestDealDTO> BestDeals { get; set; } = new();
    }

    public class SourceCountDTO
    {
        public string Source { get; set; } = string.Empty;
        public int Count { get; set; }
    }

    public class DistrictPriceDTO
    {
        public string District { get; set; } = string.Empty;
        public double MedianPricePerMeter { get; set; }
        public int Count { get; set; }
    }

    public class BestDealDTO
    {
        public string Title { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string District { get; set; } = string.Empty;
        public double Price { get; set; }
        public double Area { get; set; }
        public int Floor { get; set; }
        public string Market { get; set; } = string.Empty;
        public double PricePerMeter { get; set; }
        public double DistrictMedianPricePerMeter { get; set; }
        public double BelowMedianPercent { get; set; }
        public string Source { get; set; } = string.Empty;
    }
}
