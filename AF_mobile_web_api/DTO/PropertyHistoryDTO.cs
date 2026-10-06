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
        // The oldest price the portal actually stated - snapshots scraped with a 0 are
        // skipped - or 0 when none of them had one.
        public double FirstPrice { get; set; }
        // Null when either end has no price, as on the list (OfferSnapshot.PriceChange):
        // measured against a 0, an offer first scraped without a price read as rising by
        // its whole price, and one that lost its price as dropping by all of it.
        public double? TotalPriceChange => Price > 0 && FirstPrice > 0 ? Price - FirstPrice : null;

        public List<PropertyHistoryEntryDTO> Entries { get; set; } = new();
    }

    public class PropertyHistoryEntryDTO
    {
        public DateTime Date { get; set; }
        public double Price { get; set; }
        public double PricePerMeter { get; set; }
        public int WebName { get; set; }
        public string Url { get; set; }
        // Delta vs the last earlier entry that stated a price; 0 for the first entry. Null
        // when this entry has no price (0) or no earlier one had - a gap in the history,
        // not a move to or from zero.
        public double? PriceChange { get; set; }
    }
}
