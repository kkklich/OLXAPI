using AF_mobile_web_api.Services.Interfaces;
using ApplicationDatabase.Models;

namespace AF_mobile_web_api.Services
{
    /// <summary>
    /// Decides whether two scraped rows describe the same real-estate offer so that
    /// weekly snapshots can be grouped into a per-offer price history.
    /// Strong signal: the same normalized Url (the marketplace's natural key, stable
    /// across batches). Fallback: a fuzzy profile match that catches re-posts and
    /// cross-marketplace duplicates while deliberately ignoring price drift.
    /// Over a set of rows, two Urls one marketplace listed in the same scrape are two
    /// offers outright, whatever their other rows look like (FindMatches, GroupMatches).
    /// </summary>
    public class PropertyComparer : IPropertyComparer
    {
        // Fuzzy-match thresholds.
        public const double AreaEqualityToleranceMeters = 0.005;   // areas must be equal; epsilon only absorbs float round-trip
        private const double MaxCoordinateDistanceMeters = 150;    // "same building" radius
        private const double MaxPricePerMeterRelativeGap = 0.35;   // larger gap => different property, not price drift

        private const double MetersPerDegreeLatitude = 111_320;    // approximation, plenty accurate at city scale

        // Market is stored as scraped, in Polish; this is the primary market (new developments).
        private const string PrimaryMarket = "Pierwotny";

        public bool AreSameProperty(PropertyData a, PropertyData b)
        {
            if (a == null || b == null)
            {
                return false;
            }

            if (ReferenceEquals(a, b))
            {
                return true;
            }

            // Strong match: the offer Url survives across batches unchanged, so equal
            // normalized Urls always mean the same offer (price may still differ).
            var urlA = NormalizeUrl(a.Url);
            var urlB = NormalizeUrl(b.Url);
            if (urlA.Length > 0 && urlA == urlB)
            {
                return true;
            }

            return IsFuzzyMatch(a, b);
        }

        public List<PropertyData> FindMatches(PropertyData target, IEnumerable<PropertyData> candidates)
        {
            var matches = new List<PropertyData>();
            if (target == null || candidates == null)
            {
                return matches;
            }

            var rows = candidates.Where(row => row != null).ToList();

            // A marketplace lists each offer once per scrape, so another Url it listed in a
            // scrape that also listed the target's Url is another offer - in every scrape, not
            // only that one. Its rows from the other scrapes pass the pairwise rules as easily
            // as a re-post would, so the Url is ruled out as a whole. The target's scrapes
            // come from its own Url's rows among the candidates.
            var listings = ListingDays(rows.Append(target));
            var targetUrl = NormalizeUrl(target.Url);

            foreach (var candidate in rows)
            {
                if (ListedBeside(candidate, NormalizeUrl(candidate.Url), targetUrl, listings))
                {
                    continue;
                }

                if (AreSameProperty(target, candidate))
                {
                    matches.Add(candidate);
                }
            }

            return matches;
        }

        public List<List<PropertyData>> GroupMatches(IEnumerable<PropertyData> rows)
        {
            var items = (rows ?? Enumerable.Empty<PropertyData>())
                .Where(row => row != null)
                .ToList();

            // Union-find over every matching pair makes the partition a transitive
            // closure, so the result is deterministic and independent of input order.
            var parent = new int[items.Count];
            for (var i = 0; i < parent.Length; i++)
            {
                parent[i] = i;
            }

            // As in FindMatches, two Urls one marketplace listed in the same scrape never link,
            // whichever scrapes the two rows compared come from. The closure can still join them
            // through a third row that matches both (a duplicate on another marketplace);
            // FindMatches compares every row with the target alone, so it cannot.
            var urls = items.Select(row => NormalizeUrl(row.Url)).ToArray();
            var listings = ListingDays(items);

            for (var i = 0; i < items.Count; i++)
            {
                for (var j = i + 1; j < items.Count; j++)
                {
                    if (AreSameProperty(items[i], items[j])
                        && !ListedBeside(items[i], urls[i], urls[j], listings)
                        && !ListedBeside(items[j], urls[j], urls[i], listings))
                    {
                        Union(parent, i, j);
                    }
                }
            }

            var groupsByRoot = new Dictionary<int, List<PropertyData>>();
            for (var i = 0; i < items.Count; i++)
            {
                var root = Find(parent, i);
                if (!groupsByRoot.TryGetValue(root, out var group))
                {
                    group = new List<PropertyData>();
                    groupsByRoot[root] = group;
                }

                group.Add(items[i]);
            }

            // One group per offer: rows ordered by scrape time (price history order),
            // groups ordered by their earliest appearance for a stable output.
            return groupsByRoot.Values
                .Select(group => group
                    .OrderBy(row => row.AddedRecordTime)
                    .ThenBy(row => row.Url ?? string.Empty, StringComparer.Ordinal)
                    .ToList())
                .OrderBy(group => group[0].AddedRecordTime)
                .ThenBy(group => group[0].Url ?? string.Empty, StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>
        /// The scrape a row belongs to. A run was meant to share one AddedRecordTime, but
        /// historical rows were stamped per row, microseconds apart, so an exact comparison
        /// never saw two rows of one old run as the same scrape. Scrapes run days apart, so
        /// the calendar day identifies one, as everywhere else in the app
        /// (PropertyDataRepository.GetLatestByCityAsync).
        /// </summary>
        private static DateTime ScrapeDay(PropertyData row) => row.AddedRecordTime.Date;

        /// <summary>
        /// The scrape days on which each marketplace listed each normalized Url. Rows with no
        /// Url are left out: nothing tells them apart, so they prove nothing about another Url.
        /// </summary>
        /// <remarks>
        /// WebName was 0 for every marketplace before 2025-11-24, so a scrape of that era can
        /// look shared by two marketplaces' Urls. That can only rule a Url out, never merge one.
        /// </remarks>
        private static Dictionary<(int WebName, string Url), HashSet<DateTime>> ListingDays(IEnumerable<PropertyData> rows)
        {
            var days = new Dictionary<(int WebName, string Url), HashSet<DateTime>>();
            foreach (var row in rows)
            {
                var url = NormalizeUrl(row.Url);
                if (url.Length == 0)
                {
                    continue;
                }

                if (!days.TryGetValue((row.WebName, url), out var rowDays))
                {
                    rowDays = new HashSet<DateTime>();
                    days[(row.WebName, url)] = rowDays;
                }

                rowDays.Add(ScrapeDay(row));
            }

            return days;
        }

        /// <summary>
        /// True when the row's marketplace listed its Url and a different one in the same
        /// scrape. It lists each offer once per scrape, so that makes them two offers.
        /// </summary>
        private static bool ListedBeside(
            PropertyData row,
            string rowUrl,
            string otherUrl,
            Dictionary<(int WebName, string Url), HashSet<DateTime>> listingDays)
        {
            return rowUrl.Length > 0
                && otherUrl.Length > 0
                && rowUrl != otherUrl
                && listingDays.TryGetValue((row.WebName, rowUrl), out var rowDays)
                && listingDays.TryGetValue((row.WebName, otherUrl), out var otherDays)
                && rowDays.Overlaps(otherDays);
        }

        /// <summary>
        /// Fuzzy identity for re-posted offers (new Url, same marketplace, another scrape;
        /// secondary market only) and cross-marketplace duplicates (different WebName, any scrape).
        /// </summary>
        private static bool IsFuzzyMatch(PropertyData a, PropertyData b)
        {
            // Within one scrape a marketplace lists each offer once, so two different Urls
            // on the same marketplace in the same scrape are distinct offers; only
            // cross-marketplace duplicates may fuzzy-match inside a scrape.
            if (a.WebName == b.WebName && ScrapeDay(a) == ScrapeDay(b))
            {
                return false;
            }

            // Hard requirements: same city, identical area, same floor, same market.
            if (string.IsNullOrWhiteSpace(a.City) || !TextEquals(a.City, b.City))
            {
                return false;
            }

            if (!AreasEqual(a.Area, b.Area) || a.Floor != b.Floor || !TextEquals(a.Market, b.Market))
            {
                return false;
            }

            // A development sells many units of one size on each floor, in one district and
            // building, through one seller - every field these rules compare - so on one
            // marketplace a new Url is as likely a sibling unit as a re-post of this one.
            // Siblings listed in the same scrape are ruled out above and in FindMatches; this
            // catches the ones that never were (a 41 m² unit at Centralna 51D in Krakow took the
            // prices of two 41 m² units at 51C, listed before it). A sibling taken for a re-post
            // invents a price change between two flats, while a missed re-post only shortens a
            // history, so in the primary market only the same Url, or a duplicate on another
            // marketplace, is the same offer.
            if (a.WebName == b.WebName && TextEquals(a.Market, PrimaryMarket))
            {
                return false;
            }

            // Price changes over time, so it never confirms identity — but a huge
            // price-per-meter gap means a different property rather than price drift.
            if (a.PricePerMeter > 0 && b.PricePerMeter > 0)
            {
                var gap = Math.Abs(a.PricePerMeter - b.PricePerMeter)
                    / Math.Max(a.PricePerMeter, b.PricePerMeter);
                if (gap > MaxPricePerMeterRelativeGap)
                {
                    return false;
                }
            }

            // At least one corroborating signal beyond the shared profile.
            return CoordinatesClose(a, b)
                || SameNeighbourhoodProfile(a, b);
        }

        /// <summary>Signal (a): both rows carry coordinates and they sit within ~150 m.</summary>
        private static bool CoordinatesClose(PropertyData a, PropertyData b)
        {
            if (!HasCoordinates(a) || !HasCoordinates(b))
            {
                return false;
            }

            // Equirectangular approximation — more than accurate enough for 150 m.
            var meanLatitudeRadians = (a.Lat + b.Lat) / 2 * Math.PI / 180;
            var northSouthMeters = (a.Lat - b.Lat) * MetersPerDegreeLatitude;
            var eastWestMeters = (a.Lon - b.Lon) * MetersPerDegreeLatitude * Math.Cos(meanLatitudeRadians);
            var distanceMeters = Math.Sqrt(northSouthMeters * northSouthMeters + eastWestMeters * eastWestMeters);

            return distanceMeters <= MaxCoordinateDistanceMeters;
        }

        private static bool HasCoordinates(PropertyData row)
        {
            // Scrapers leave 0/0 when the marketplace exposes no coordinates.
            return !double.IsNaN(row.Lat) && !double.IsNaN(row.Lon) && (row.Lat != 0 || row.Lon != 0);
        }

        /// <summary>Signal (b): same district and building type plus the same seller kind.</summary>
        private static bool SameNeighbourhoodProfile(PropertyData a, PropertyData b)
        {
            return !string.IsNullOrWhiteSpace(a.District)
                && !string.IsNullOrWhiteSpace(b.District)
                && TextEquals(a.District, b.District)
                && !string.IsNullOrWhiteSpace(a.BuildingType)
                && !string.IsNullOrWhiteSpace(b.BuildingType)
                && TextEquals(a.BuildingType, b.BuildingType)
                && a.Private == b.Private;
        }

        /// <summary>
        /// Hard requirement: both rows state the same floor area. Portals round area to whole
        /// or half meters, so a tolerance band spans neighbouring sizes instead of narrowing
        /// the match — at 50 m² the old ±2% admitted 49, 50 and 51 alike. Only a sub-square-
        /// centimeter epsilon is allowed here, to absorb the float round-trip through the database.
        /// </summary>
        private static bool AreasEqual(double areaA, double areaB)
        {
            if (areaA <= 0 || areaB <= 0)
            {
                return false; // a missing area cannot corroborate a fuzzy match
            }

            return Math.Abs(areaA - areaB) <= AreaEqualityToleranceMeters;
        }

        private static bool TextEquals(string? x, string? y)
        {
            return string.Equals(
                x?.Trim() ?? string.Empty,
                y?.Trim() ?? string.Empty,
                StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Trim, lowercase, strip query string, fragment and trailing slash.</summary>
        /// <remarks>
        /// Public because PropertyListService pre-filters history candidates on it - as it
        /// does on the fuzzy rules' floor, market and area requirements. Loosening either
        /// rule here means widening that pre-filter too, or the new matches never arrive.
        /// </remarks>
        public static string NormalizeUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return string.Empty;
            }

            var normalized = url.Trim().ToLowerInvariant();

            var queryStart = normalized.IndexOf('?');
            if (queryStart >= 0)
            {
                normalized = normalized.Substring(0, queryStart);
            }

            var fragmentStart = normalized.IndexOf('#');
            if (fragmentStart >= 0)
            {
                normalized = normalized.Substring(0, fragmentStart);
            }

            return normalized.TrimEnd('/');
        }

        private static int Find(int[] parent, int index)
        {
            while (parent[index] != index)
            {
                parent[index] = parent[parent[index]]; // path halving
                index = parent[index];
            }

            return index;
        }

        private static void Union(int[] parent, int left, int right)
        {
            var rootLeft = Find(parent, left);
            var rootRight = Find(parent, right);
            if (rootLeft == rootRight)
            {
                return;
            }

            // The smaller index always becomes the root, keeping unions deterministic.
            if (rootLeft < rootRight)
            {
                parent[rootRight] = rootLeft;
            }
            else
            {
                parent[rootLeft] = rootRight;
            }
        }
    }
}
