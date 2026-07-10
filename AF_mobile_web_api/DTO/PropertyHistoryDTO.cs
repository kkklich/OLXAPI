namespace AF_mobile_web_api.DTO
{
    // Price history of one offer: every scrape batch in which a matching row
    // (same offer per IPropertyComparer) was found, ordered oldest first.
    public class PropertyHistoryDTO
    {
        public Guid Id { get; set; }
        public string Url { get; set; }
        public string Title { get; set; }
        public string City { get; set; }
        public string District { get; set; }
        public double Area { get; set; }
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
