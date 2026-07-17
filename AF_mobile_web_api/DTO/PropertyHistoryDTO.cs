namespace AF_mobile_web_api.DTO
{
    // Price history of one offer: every scrape batch in which a matching row
    // (same offer per IPropertyComparer) was found, ordered oldest first.
    // The scalar fields describe the newest snapshot of the offer; the *Seen /
    // *Count / FirstPrice fields aggregate over the whole matched history.
    public class PropertyHistoryDTO
    {
        public Guid Id { get; set; }
        public string Url { get; set; }
        public string Title { get; set; }
        public string City { get; set; }
        public string District { get; set; }
        public double Area { get; set; }

        // Newest-snapshot detail.
        public double Price { get; set; }
        public double PricePerMeter { get; set; }
        public int Floor { get; set; }
        public string Market { get; set; }
        public string BuildingType { get; set; }
        public bool Private { get; set; }
        public int WebName { get; set; }
        public double Lat { get; set; }
        public double Lon { get; set; }
        public string OffertId { get; set; }
        public string Description { get; set; }
        public DateTime CreatedTime { get; set; }

        // History aggregates over every matched snapshot.
        public DateTime FirstSeen { get; set; }
        public DateTime LastSeen { get; set; }
        public int SnapshotCount { get; set; }
        public double FirstPrice { get; set; }
        public double TotalPriceChange => Price - FirstPrice;

        public List<PropertyHistoryEntryDTO> Entries { get; set; } = new();
    }

    public class PropertyHistoryEntryDTO
    {
        public DateTime Date { get; set; }
        public double Price { get; set; }
        public double PricePerMeter { get; set; }
        public int WebName { get; set; }
        public string Url { get; set; }
        public double PriceChange { get; set; } // delta vs the previous entry, 0 for the first
    }
}
