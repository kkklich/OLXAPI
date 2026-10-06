namespace AF_mobile_web_api.DTO
{
    // Slim projection for the map: only the fields the map plots or groups by.
    // Deliberately omits Description, Photos, Id, WebName etc. to keep the payload small -
    // and a colour: the map colours every dot itself, by whichever field it groups by, so a
    // server-side "rgb(r,0,b)" per point was 6% of the payload that nothing read.
    public class MapPointDTO
    {
        public string Url { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public double Price { get; set; }
        public double PricePerMeter { get; set; }
        public int Floor { get; set; }
        public string Market { get; set; } = string.Empty;
        public string BuildingType { get; set; } = string.Empty;
        public double Area { get; set; }
        public bool Private { get; set; }
        public MapLocationDTO Location { get; set; } = new();
    }

    public class MapLocationDTO
    {
        public double Lat { get; set; }
        public double Lon { get; set; }
        public string District { get; set; } = string.Empty;
    }
}
