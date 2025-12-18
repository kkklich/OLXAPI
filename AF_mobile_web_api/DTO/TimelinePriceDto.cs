namespace AF_mobile_web_api.DTO
{
    public class TimelinePriceDto
    {
        public string AddedDate { get; set; } = string.Empty;
        public double AvgPrice { get; set; }
        public double AvgPricePerMeter { get; set; }
        public int Count { get; set; }
    }
}
