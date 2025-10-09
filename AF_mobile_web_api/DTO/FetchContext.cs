namespace AF_mobile_web_api.DTO
{
    public enum MarketKind { Pierwotny = 1, Wtórny = 2 }
    public enum BuildingType { Blok = 1, Apartamentowiec = 2, Kamienica = 3 }

    public sealed class FetchContext
    {
        public string Url { get; init; }
        public MarketKind Market { get; init; }
        public BuildingType Building { get; init; }
        public int? Page { get; init; }
        public int? PriceFrom { get; init; }
        public int? PriceTo { get; init; }
    }
}
