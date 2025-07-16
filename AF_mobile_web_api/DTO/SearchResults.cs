namespace AF_mobile_web_api.DTO
{
    public class SearchResults
    {
        public List<DataSearch> Data { get; set; }
        public int Total_elements { get; set; }

        public SearchResults()
        {
            Data = new List<DataSearch>();
        }
    }

    public class DataSearch
    {
        public long Id { get; set; }
        public string Url { get; set; }
        public string Title { get; set; }
        public DateTime created_time { get; set; }
        public double PricePerM { get; set; }
        public double FloorSelect { get; set; }
        //public string Furniture { get; set; }
        //public string Market { get; set; }
        public double Price { get; set; }
        //public string Builttype { get; set; }
        public double Area { get; set; }
        //public string Rooms { get; set; }

    }
}
