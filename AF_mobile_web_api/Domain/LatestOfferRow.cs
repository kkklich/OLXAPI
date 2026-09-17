namespace AF_mobile_web_api.Domain
{
    // One row of the offers-snapshot load: the newest snapshot of a Url, joined to the
    // history aggregates of every snapshot sharing it. Kept as its own type (rather than
    // an anonymous one) so the repository can stream the projection to the cache without
    // materialising the whole result twice.
    public class LatestOfferRow
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public double Price { get; set; }
        public double PricePerMeter { get; set; }
        public int Floor { get; set; }
        public string Market { get; set; } = string.Empty;
        public string BuildingType { get; set; } = string.Empty;
        public double Area { get; set; }
        public bool Private { get; set; }
        public int WebName { get; set; }
        public string City { get; set; } = string.Empty;
        public string District { get; set; } = string.Empty;

        public DateTime LastSeen { get; set; }
        public DateTime FirstSeen { get; set; }
        public int SnapshotCount { get; set; }
    }

    // Display-only columns of one listed offer, fetched per page instead of being held in
    // memory for all ~95k offers (see OfferSnapshotCache).
    public class OfferPageDetail
    {
        public Guid Id { get; set; }
        public string Url { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public double FirstPrice { get; set; }
    }
}
