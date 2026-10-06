namespace AF_mobile_web_api.DTO
{
    public class PropertyQueryParams
    {
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 25;

        // Column name (case-insensitive): price, pricePerMeter, area, floor, title,
        // city, district, market, buildingType, priceChange, snapshotCount, firstSeen,
        // lastSeen (default). Every column the offers list renders is sortable.
        public string? SortBy { get; set; }
        public string? SortDir { get; set; } // "asc" | "desc" (default desc)

        public string? City { get; set; }
        // Substring match, like Search but on this column alone. It was an exact match
        // before the list grew a filter per column; nothing sent it.
        public string? District { get; set; }
        public string? Title { get; set; } // substring match on Title alone
        // "primary" | "secondary" (mapped to the stored Polish values), or a raw
        // stored value ("Pierwotny"/"Wtórny") passed through unchanged.
        public string? Market { get; set; }
        public string? BuildingType { get; set; }
        public int? WebName { get; set; }
        public bool? Private { get; set; }
        // Inclusive bounds. An offer whose price, area or price per m² is 0 - not stated by
        // the portal - matches no bound on that column, and sorts last on it either way.
        public double? PriceMin { get; set; }
        public double? PriceMax { get; set; }
        public double? AreaMin { get; set; }
        public double? AreaMax { get; set; }
        public double? PricePerMeterMin { get; set; }
        public double? PricePerMeterMax { get; set; }
        public int? FloorMin { get; set; }
        public int? FloorMax { get; set; }
        // Price now minus the first price the offer stated (its oldest non-zero snapshot) -
        // negative for an offer that has been marked down. Both bounds are inclusive, so
        // priceChangeMax=-1 selects every offer whose price has dropped. An offer whose
        // current price is 0, or that never stated one, has no known change and matches
        // neither bound.
        public double? PriceChangeMin { get; set; }
        public double? PriceChangeMax { get; set; }
        public int? SnapshotCountMin { get; set; }
        public int? SnapshotCountMax { get; set; }
        public string? Search { get; set; } // substring match on Title or District
    }
}
