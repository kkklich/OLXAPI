namespace AF_mobile_web_api.DTO
{
    public class MarketplaceSearch
    {
        public int TotalCount { get; set; }
        public List<SearchData> Data { get; set; }

        public MarketplaceSearch()
        {
            Data = new List<SearchData>();
        }
    }


    public class SearchData
    {
        public long Id { get; set; }
        public string Url { get; set; }
        public string Title { get; set; }
        public DateTime CreatedTime { get; set; }
        public double Price { get; set; }
        public double PricePerMeter { get; set; }
        public int Floor { get; set; }
        public string Market{ get; set; }
        public string BuildingType { get; set; }
        public double Area { get; set; }
        public string Description { get; set; }
        public bool Private { get; set; }
        public LocationPlace Location { get; set; }
        public List<Photos> Photos { get; set; }

        public SearchData()
        {
            Photos = new List<Photos>();
        }
    }

    public class LocationPlace
    {
        public double Lat { get; set; }
        public double Lon { get; set; }
        public string City { get; set; }
        public string District { get; set; }
    }

    public class Photos
    {
        public long Id { get; set; }
        public string Filename { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public string Link { get; set; }
    }
}