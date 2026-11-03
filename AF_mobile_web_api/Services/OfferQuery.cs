using AF_mobile_web_api.Domain;
using AF_mobile_web_api.DTO;
using System.Collections.Concurrent;
using System.Buffers;
using System.Runtime.CompilerServices;

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
        // Orders belong to one immutable snapshot and disappear when it is replaced.
        // Store indices (four bytes each), and only build columns actually requested.
        private static readonly ConditionalWeakTable<IReadOnlyList<OfferSnapshot>,
            ConcurrentDictionary<(string, bool), Lazy<int[]>>> Orders = new();

        private static string SortKey(string? key) => key?.ToLowerInvariant() switch
        {
            "price" or "pricepermeter" or "area" or "floor" or "title" or "city"
                or "district" or "market" or "buildingtype" or "pricechange"
                or "snapshotcount" or "firstseen" => key.ToLowerInvariant(),
            _ => "lastseen"
        };

        public static OfferPage Apply(IReadOnlyList<OfferSnapshot> offers, PropertyQueryParams query)
        {
            var page = Math.Max(1, query.Page);
            var pageSize = Math.Clamp(query.PageSize, 1, 200);
            var skip = (long)(page - 1) * pageSize;
            var items = new List<OfferSnapshot>(pageSize);
            var matches = ArrayPool<int>.Shared.Rent(offers.Count);
            byte[]? included = null;
            var total = 0;
            try
            {
                // Filter in storage order for locality. A very selective filter is cheaper
                // to sort directly than to walk an order for the entire snapshot.
                var predicate = Predicate(query);
                for (var i = 0; i < offers.Count; i++)
                    if (predicate(offers[i])) matches[total++] = i;

                if (skip < total)
                {
                    var key = (SortKey(query.SortBy), IsDescending(query.SortDir));
                    if (total <= 2048)
                    {
                        var compare = Comparer(key.Item1, key.Item2);
                        Array.Sort(matches, 0, total, System.Collections.Generic.Comparer<int>.Create((a, b) => compare(offers[a], offers[b])));
                        for (var i = (int)skip; i < total && items.Count < pageSize; i++) items.Add(offers[matches[i]]);
                    }
                    else
                    {
                        var orders = Orders.GetValue(offers, _ => new());
                        var order = orders.GetOrAdd(key, k => new Lazy<int[]>(() =>
                        {
                            var indices = Enumerable.Range(0, offers.Count).ToArray();
                            var compare = Comparer(k.Item1, k.Item2);
                            Array.Sort(indices, (a, b) => compare(offers[a], offers[b]));
                            return indices;
                        })).Value;
                        if (total < offers.Count)
                        {
                            included = ArrayPool<byte>.Shared.Rent(offers.Count);
                            Array.Clear(included, 0, offers.Count);
                            for (var i = 0; i < total; i++) included[matches[i]] = 1;
                        }
                        var matched = 0;
                        foreach (var index in order)
                        {
                            if (included != null && included[index] == 0) continue;
                            if (matched++ < skip) continue;
                            items.Add(offers[index]);
                            if (items.Count == pageSize) break;
                        }
                    }
                }
                return new OfferPage { Items = items, TotalCount = total, Page = page, PageSize = pageSize };
            }
            finally
            {
                ArrayPool<int>.Shared.Return(matches);
                if (included != null) ArrayPool<byte>.Shared.Return(included);
            }
        }

        private static Func<OfferSnapshot, bool> Predicate(PropertyQueryParams query)
        {
            var cities = CityKeys(query.City);
            var market = Key(query.Market);
            var buildingType = Key(query.BuildingType);
            // The per-column text filters match the way Search does - a substring of the
            // folded value - so typing "krak" into the District column behaves like the
            // free-text box the page already had.
            var district = Search(query.District);
            var title = Search(query.Title);
            var url = string.IsNullOrWhiteSpace(query.Url) ? null : query.Url.Trim();
            var search = Search(query.Search);

            return offer =>
            {
                if (cities != null && !cities.Contains(offer.CityKey)) return false;
                if (market != null && !string.Equals(offer.MarketKey, market, StringComparison.Ordinal)) return false;
                if (buildingType != null && !string.Equals(offer.BuildingTypeKey, buildingType, StringComparison.Ordinal)) return false;
                if (district != null && !offer.DistrictKey.Contains(district, StringComparison.Ordinal)) return false;
                if (title != null && !offer.TitleKey.Contains(title, StringComparison.Ordinal)) return false;
                if (url != null && !offer.Url.Contains(url, StringComparison.OrdinalIgnoreCase)) return false;
                if (query.WebName.HasValue && offer.WebName != query.WebName.Value) return false;
                if (query.Private.HasValue && offer.Private != query.Private.Value) return false;
                // A 0 price, price per m² or area is one the portal did not state, so it is
                // within no bound - measured as a real 0 it passed every maximum, and a list
                // filtered to "under 300k" filled up with offers of unknown price.
                if (!Within(offer.Price, query.PriceMin, query.PriceMax)) return false;
                if (!Within(offer.Area, query.AreaMin, query.AreaMax)) return false;
                if (!Within(offer.PricePerMeter, query.PricePerMeterMin, query.PricePerMeterMax)) return false;
                // Floor 0 is the ground floor, a real value, so it is bounded as it stands.
                if (query.FloorMin.HasValue && offer.Floor < query.FloorMin.Value) return false;
                if (query.FloorMax.HasValue && offer.Floor > query.FloorMax.Value) return false;
                // An unknown change is within no bound - see OfferSnapshot.PriceChange.
                if ((query.PriceChangeMin.HasValue || query.PriceChangeMax.HasValue) && offer.PriceChange is null) return false;
                if (query.PriceChangeMin.HasValue && offer.PriceChange < query.PriceChangeMin.Value) return false;
                if (query.PriceChangeMax.HasValue && offer.PriceChange > query.PriceChangeMax.Value) return false;
                if (query.SnapshotCountMin.HasValue && offer.SnapshotCount < query.SnapshotCountMin.Value) return false;
                if (query.SnapshotCountMax.HasValue && offer.SnapshotCount > query.SnapshotCountMax.Value) return false;

                // Same as the SQL it replaces: a substring of either the title or the district.
                if (search != null
                    && !offer.TitleKey.Contains(search, StringComparison.Ordinal)
                    && !offer.DistrictKey.Contains(search, StringComparison.Ordinal)) return false;

                return true;
            };
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

        /// The city keys a city filter selects: every city of an area such as "Silesia" (its
        // offers keep their own city), otherwise just the value; null when the filter is not set.
        private static HashSet<string>? CityKeys(string? value)
        {
            var key = Key(value);
            if (key == null)
                return null;

            return CityArea.TryParse(value, out var area) && area.IsMultiCity
                ? area.CityNames.Select(OfferText.Fold).ToHashSet(StringComparer.Ordinal)
                : new HashSet<string>(StringComparer.Ordinal) { key };
        }

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
