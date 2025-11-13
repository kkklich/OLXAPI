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
        public string Id { get; set; } //  internal id 
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


    public class SearchDataComparer : IEqualityComparer<SearchData>
    {
        public bool Equals(SearchData x, SearchData y)
        {
            if (x == null && y == null) return true;
            if (x == null || y == null) return false;

            return x.Id == y.Id &&
                   //x.Url == y.Url &&
                   x.Title == y.Title &&
                   //x.CreatedTime == y.CreatedTime &&
                   x.Price == y.Price &&
                   x.PricePerMeter == y.PricePerMeter &&
                   //x.Floor == y.Floor &&
                   x.Market == y.Market &&
                   //x.BuildingType == y.BuildingType &&
                   //x.Area == y.Area &&
                   x.Description == y.Description &&
                   x.Private == y.Private &&
                   x.WebName == y.WebName;
        }

        public int GetHashCode(SearchData obj)
        {
            if (obj == null) return 0;

            return (obj.Id?.GetHashCode() ?? 0) ^
                   //(obj.Url?.GetHashCode() ?? 0) ^
                   (obj.Title?.GetHashCode() ?? 0) ^
                   //obj.CreatedTime.GetHashCode() ^
                   obj.Price.GetHashCode() ^
                   obj.PricePerMeter.GetHashCode() ^
                   //obj.Floor.GetHashCode() ^
                   (obj.Market?.GetHashCode() ?? 0) ^
                   //(obj.BuildingType?.GetHashCode() ?? 0) ^
                   //obj.Area.GetHashCode() ^
                   (obj.Description?.GetHashCode() ?? 0) ^
                   obj.Private.GetHashCode() ^
                   obj.WebName.GetHashCode();
        }
    }
}