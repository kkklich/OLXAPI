namespace AF_mobile_web_api.DTO
{
    public class RealEstateStatistics
    {
        public double MedianPrice { get; set; }
        public double MedianPricePerMeter { get; set; }
        public double MedianArea { get; set; }
        public double AverageFloor { get; set; }
        public string District { get; set; }
        public int Count { get; set; }
    }
}
