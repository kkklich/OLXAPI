namespace AF_mobile_web_api.DTO
{
    // One row of the paginated /properties list: the newest snapshot of an offer
    // plus history metadata aggregated over every weekly snapshot with the same Url.
    public class PropertyListItemDTO
    {
        public Guid Id { get; set; }
        public string Url { get; set; }
        public string Title { get; set; }
        public double Price { get; set; }
        public double PricePerMeter { get; set; }
        public int Floor { get; set; }
        public string Market { get; set; }
        public string BuildingType { get; set; }
        public double Area { get; set; }
        public bool Private { get; set; }
        public int WebName { get; set; }
        public string City { get; set; }
        public string District { get; set; }

        public DateTime FirstSeen { get; set; }
        public DateTime LastSeen { get; set; }
        public int SnapshotCount { get; set; }
        public double FirstPrice { get; set; }
        public double PriceChange => Price - FirstPrice;
    }
}
