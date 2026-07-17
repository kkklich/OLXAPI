namespace AF_mobile_web_api.DTO
{
    public class PropertyQueryParams
    {
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 25;

        // Column name (case-insensitive): price, pricePerMeter, area, floor, title,
        // city, district, market, buildingType, lastSeen (default).
        public string? SortBy { get; set; }
        public string? SortDir { get; set; } // "asc" | "desc" (default desc)

        public string? City { get; set; }
        public string? District { get; set; }
        // "primary" | "secondary" (mapped to the stored Polish values), or a raw
        // stored value ("Pierwotny"/"Wtórny") passed through unchanged.
        public string? Market { get; set; }
        public string? BuildingType { get; set; }
        public int? WebName { get; set; }
        public bool? Private { get; set; }
        public double? PriceMin { get; set; }
        public double? PriceMax { get; set; }
        public double? AreaMin { get; set; }
        public double? AreaMax { get; set; }
        public double? PricePerMeterMin { get; set; }
        public double? PricePerMeterMax { get; set; }
        public string? Search { get; set; } // substring match on Title or District
    }
}
