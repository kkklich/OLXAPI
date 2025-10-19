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
        public long Id { get; set; } //  internal id  //todo change to string DB
        public string Url { get; set; } //url of the offer
        public string Title { get; set; } //title of the offer
        public DateTime CreatedTime { get; set; } //date of creation
        public double Price { get; set; } // total price
        public double PricePerMeter { get; set; } // price per m2
        public int Floor { get; set; }// number of floor
        public string Market{ get; set; } // primary = Pierwotny, Wtorny
        public string BuildingType { get; set; } //Blok, Apartamentowiec, Kamienica 
        public double Area { get; set; } //area in m2
        public string Description { get; set; } //description
        public bool Private { get; set; } //private = true, agency = false
        public WebName WebName { get; set; }  //OLX, Morizon, NieruchomsciOnline
        public LocationPlace Location { get; set; } //location details
        public List<Photos> Photos { get; set; }//list of photos

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
        public string Street { get; set; }
        public string Number { get; set; }
    }

    public class Photos
    {
        public long Id { get; set; }
        public string Filename { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public string Link { get; set; }
    }

    public enum WebName
    {
        OLX,
        Morizon,
        NieruchomsciOnline
    }
}