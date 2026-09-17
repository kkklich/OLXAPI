using AF_mobile_web_api.DTO;

namespace AF_mobile_web_api.Services
{
    // The page of offers a query selects, plus the size of the whole match.
    public sealed class OfferPage
    {
        public List<OfferSnapshot> Items { get; init; } = new();
        public int TotalCount { get; init; }
        public int Page { get; init; }
        public int PageSize { get; init; }
    }

    // Filters, sorts and pages the in-memory offers snapshot.
    //
    // This used to be a MySQL query. Every request re-derived "the newest snapshot of each
    // Url" with a GROUP BY over the whole table, keyed by a longtext column - ~3.6s before
    // any filtering, and with a city filter the optimizer joined through
    // (City, AddedRecordTime) instead, scanning thousands of rows per offer until it hit
    // the 30s command timeout. The dedup only changes when a scrape lands, so it is done
    // once (OfferSnapshotCache) and each request is a pass over an array.
    //
    // The comparisons mirror utf8mb4_general_ci through the folded keys - see OfferText.
    public static class OfferQuery
    {
        public static OfferPage Apply(IReadOnlyList<OfferSnapshot> offers, PropertyQueryParams query)
        {
            var matches = Filter(offers, query);

            var page = Math.Max(1, query.Page);
            var pageSize = Math.Clamp(query.PageSize, 1, 200);

            matches.Sort(Comparer(query.SortBy, IsDescending(query.SortDir)));

            var skip = (page - 1) * pageSize;
            var items = skip >= matches.Count
                ? new List<OfferSnapshot>()
                : matches.GetRange(skip, Math.Min(pageSize, matches.Count - skip));

            return new OfferPage
            {
                Items = items,
                TotalCount = matches.Count,
                Page = page,
                PageSize = pageSize
            };
        }

        private static List<OfferSnapshot> Filter(IReadOnlyList<OfferSnapshot> offers, PropertyQueryParams query)
        {
            var city = Key(query.City);
            var district = Key(query.District);
            var market = Key(query.Market);
            var buildingType = Key(query.BuildingType);
            var search = Search(query.Search);

            var matches = new List<OfferSnapshot>();

            foreach (var offer in offers)
            {
                if (city != null && !string.Equals(offer.CityKey, city, StringComparison.Ordinal)) continue;
                if (district != null && !string.Equals(offer.DistrictKey, district, StringComparison.Ordinal)) continue;
                if (market != null && !string.Equals(offer.MarketKey, market, StringComparison.Ordinal)) continue;
                if (buildingType != null && !string.Equals(offer.BuildingTypeKey, buildingType, StringComparison.Ordinal)) continue;
                if (query.WebName.HasValue && offer.WebName != query.WebName.Value) continue;
                if (query.Private.HasValue && offer.Private != query.Private.Value) continue;
                if (query.PriceMin.HasValue && offer.Price < query.PriceMin.Value) continue;
                if (query.PriceMax.HasValue && offer.Price > query.PriceMax.Value) continue;
                if (query.AreaMin.HasValue && offer.Area < query.AreaMin.Value) continue;
                if (query.AreaMax.HasValue && offer.Area > query.AreaMax.Value) continue;
                if (query.PricePerMeterMin.HasValue && offer.PricePerMeter < query.PricePerMeterMin.Value) continue;
                if (query.PricePerMeterMax.HasValue && offer.PricePerMeter > query.PricePerMeterMax.Value) continue;

                // Same as the SQL it replaces: a substring of either the title or the district.
                if (search != null
                    && !offer.TitleKey.Contains(search, StringComparison.Ordinal)
                    && !offer.DistrictKey.Contains(search, StringComparison.Ordinal)) continue;

                matches.Add(offer);
            }

            return matches;
        }

        /// A filter value, folded for comparison; null when the filter is not set.
        private static string? Key(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : OfferText.Fold(value);

        /// Trailing spaces are significant to a substring match, so the search term keeps them.
        private static string? Search(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            var folded = OfferText.Fold(value);
            return folded.Length == 0 ? null : folded;
        }

        private static bool IsDescending(string? sortDir) =>
            !string.Equals(sortDir, "asc", StringComparison.OrdinalIgnoreCase);

        // Id breaks every tie, ascending in both directions, so pages can never overlap -
        // the same guarantee the SQL's ThenBy(p => p.Id) gave.
        private static Comparison<OfferSnapshot> Comparer(string? sortBy, bool desc)
        {
            Comparison<OfferSnapshot> primary = sortBy?.ToLowerInvariant() switch
            {
                "price" => (a, b) => a.Price.CompareTo(b.Price),
                "pricepermeter" => (a, b) => a.PricePerMeter.CompareTo(b.PricePerMeter),
                "area" => (a, b) => a.Area.CompareTo(b.Area),
                "floor" => (a, b) => a.Floor.CompareTo(b.Floor),
                "title" => (a, b) => string.CompareOrdinal(a.TitleKey, b.TitleKey),
                "city" => (a, b) => string.CompareOrdinal(a.CityKey, b.CityKey),
                "district" => (a, b) => string.CompareOrdinal(a.DistrictKey, b.DistrictKey),
                "market" => (a, b) => string.CompareOrdinal(a.MarketKey, b.MarketKey),
                "buildingtype" => (a, b) => string.CompareOrdinal(a.BuildingTypeKey, b.BuildingTypeKey),
                _ => (a, b) => a.LastSeen.CompareTo(b.LastSeen)
            };

            return (a, b) =>
            {
                var result = primary(a, b);
                if (result != 0)
                    return desc ? -result : result;

                return a.Id.CompareTo(b.Id);
            };
        }
    }
}
