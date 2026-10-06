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
            var market = Key(query.Market);
            var buildingType = Key(query.BuildingType);
            // The per-column text filters match the way Search does - a substring of the
            // folded value - so typing "krak" into the District column behaves like the
            // free-text box the page already had.
            var district = Search(query.District);
            var title = Search(query.Title);
            var search = Search(query.Search);

            var matches = new List<OfferSnapshot>();

            foreach (var offer in offers)
            {
                if (city != null && !string.Equals(offer.CityKey, city, StringComparison.Ordinal)) continue;
                if (market != null && !string.Equals(offer.MarketKey, market, StringComparison.Ordinal)) continue;
                if (buildingType != null && !string.Equals(offer.BuildingTypeKey, buildingType, StringComparison.Ordinal)) continue;
                if (district != null && !offer.DistrictKey.Contains(district, StringComparison.Ordinal)) continue;
                if (title != null && !offer.TitleKey.Contains(title, StringComparison.Ordinal)) continue;
                if (query.WebName.HasValue && offer.WebName != query.WebName.Value) continue;
                if (query.Private.HasValue && offer.Private != query.Private.Value) continue;
                // A 0 price, price per m² or area is one the portal did not state, so it is
                // within no bound - measured as a real 0 it passed every maximum, and a list
                // filtered to "under 300k" filled up with offers of unknown price.
                if (!Within(offer.Price, query.PriceMin, query.PriceMax)) continue;
                if (!Within(offer.Area, query.AreaMin, query.AreaMax)) continue;
                if (!Within(offer.PricePerMeter, query.PricePerMeterMin, query.PricePerMeterMax)) continue;
                // Floor 0 is the ground floor, a real value, so it is bounded as it stands.
                if (query.FloorMin.HasValue && offer.Floor < query.FloorMin.Value) continue;
                if (query.FloorMax.HasValue && offer.Floor > query.FloorMax.Value) continue;
                // An unknown change is within no bound - see OfferSnapshot.PriceChange.
                if ((query.PriceChangeMin.HasValue || query.PriceChangeMax.HasValue) && offer.PriceChange is null) continue;
                if (query.PriceChangeMin.HasValue && offer.PriceChange < query.PriceChangeMin.Value) continue;
                if (query.PriceChangeMax.HasValue && offer.PriceChange > query.PriceChangeMax.Value) continue;
                if (query.SnapshotCountMin.HasValue && offer.SnapshotCount < query.SnapshotCountMin.Value) continue;
                if (query.SnapshotCountMax.HasValue && offer.SnapshotCount > query.SnapshotCountMax.Value) continue;

                // Same as the SQL it replaces: a substring of either the title or the district.
                if (search != null
                    && !offer.TitleKey.Contains(search, StringComparison.Ordinal)
                    && !offer.DistrictKey.Contains(search, StringComparison.Ordinal)) continue;

                matches.Add(offer);
            }

            return matches;
        }

        /// Inclusive bounds on a value where 0 means "not stated": with either bound set, an
        /// unstated value is out; with neither, everything passes.
        private static bool Within(double value, double? min, double? max)
        {
            if (!min.HasValue && !max.HasValue)
                return true;

            return value > 0
                && (!min.HasValue || value >= min.Value)
                && (!max.HasValue || value <= max.Value);
        }

        /// A filter value, folded for comparison; null when the filter is not set.
        private static string? Key(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : OfferText.Fold(value);

        /// A substring filter value, folded like the keys it is matched against: trailing
        /// spaces are dropped, leading ones kept. filterMapPoints trims the same way, so the
        /// table and the map beside it agree on what "krak " matches.
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
            var key = sortBy?.ToLowerInvariant();

            Comparison<OfferSnapshot> primary = key switch
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
                "pricechange" => (a, b) => Nullable.Compare(a.PriceChange, b.PriceChange),
                "snapshotcount" => (a, b) => a.SnapshotCount.CompareTo(b.SnapshotCount),
                "firstseen" => (a, b) => a.FirstSeen.CompareTo(b.FirstSeen),
                _ => (a, b) => a.LastSeen.CompareTo(b.LastSeen)
            };

            // An unknown value is neither the smallest nor the largest one, so it goes last in
            // both directions; ranked as either, it would lead one of the two sorts - an
            // ascending price sort opened on a page of offers whose price nobody stated. Unknown
            // is a null change, or a 0 price, price per m² or area (floor 0 is the ground floor).
            Func<OfferSnapshot, bool>? unknown = key switch
            {
                "pricechange" => offer => offer.PriceChange is null,
                "price" => offer => offer.Price <= 0,
                "pricepermeter" => offer => offer.PricePerMeter <= 0,
                "area" => offer => offer.Area <= 0,
                _ => null
            };

            return (a, b) =>
            {
                if (unknown != null)
                {
                    var byKnown = unknown(a).CompareTo(unknown(b));
                    if (byKnown != 0)
                        return byKnown;
                }

                var result = primary(a, b);
                if (result != 0)
                    return desc ? -result : result;

                return a.Id.CompareTo(b.Id);
            };
        }
    }
}
