namespace AF_mobile_web_api.DTO
{
    public class SearchDataDTO
    {
            public string Id { get; set; } //  internal id 
            public string Url { get; set; } //url of the offer
            public string Title { get; set; } //title of the offer
            public DateTime CreatedTime { get; set; } //date of creation
            public double Price { get; set; } // total price
            public double PricePerMeter { get; set; } // price per m2
            public int Floor { get; set; }// number of floor
            public string Market { get; set; } // primary = Pierwotny, Wtorny
            public string BuildingType { get; set; } //Blok, Apartamentowiec, Kamienica 
            public double Area { get; set; } //area in m2
            public bool Private { get; set; } //private = true, agency = false
            public WebName WebName { get; set; }  //OLX, Morizon, NieruchomsciOnline       
    }    
}
