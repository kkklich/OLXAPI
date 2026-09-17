using System.Collections.Concurrent;
using System.Text;
using AF_mobile_web_api.Domain;

namespace AF_mobile_web_api.Services
{
    // One offer as the paged list filters and sorts it: the newest snapshot's own columns
    // plus the history aggregates over every snapshot sharing its Url.
    //
    // Url and Title are deliberately NOT held here. They are the two widest columns and are
    // needed only for the ten rows actually rendered, which the repository fetches by Id -
    // keeping them out halves the memory the snapshot costs for ~95k offers.
    public sealed class OfferSnapshot
    {
        public Guid Id { get; init; }
        public double Price { get; init; }
        public double PricePerMeter { get; init; }
        public int Floor { get; init; }
        public double Area { get; init; }
        public bool Private { get; init; }
        public int WebName { get; init; }
        public string City { get; init; } = string.Empty;
        public string District { get; init; } = string.Empty;
        public string Market { get; init; } = string.Empty;
        public string BuildingType { get; init; } = string.Empty;
        public DateTime LastSeen { get; init; }
        public DateTime FirstSeen { get; init; }
        public int SnapshotCount { get; init; }

        // Comparison keys: see OfferText.Fold. The four short ones are shared instances
        // (there are only a handful of distinct cities, markets and building types), so
        // they cost one reference each rather than a string.
        public string TitleKey { get; init; } = string.Empty;
        public string CityKey { get; init; } = string.Empty;
        public string DistrictKey { get; init; } = string.Empty;
        public string MarketKey { get; init; } = string.Empty;
        public string BuildingTypeKey { get; init; } = string.Empty;
    }

    // Reproduces, in memory, how MariaDB compares the text columns of this table.
    //
    // They are all utf8mb4_general_ci, which is case-insensitive, accent-insensitive and
    // PAD SPACE - so 'Krakow' = 'Kraków ' there, and the list must keep matching both the
    // 'Kraków' rows older scrapes wrote and the 'Krakow' ones written since. Verified
    // against the live server: 'ó'='o' and 'ż'='z', but 'ł' is its own letter ('ł' <> 'l'),
    // which is exactly what folding the accent off a letter does - ł carries no accent to
    // fold, its stroke is part of the letter.
    //
    // One deliberate difference: the free-text search folds accents too, while the LOCATE()
    // it replaces did not (MariaDB's LOCATE is case-insensitive but accent-sensitive - the
    // same server answers 'Kraków' = 'Krakow' true, yet LOCATE('Krakow', 'Kraków') zero).
    // Searching "Krakow" now finds "Kraków", which is what a Polish keyboard-less search
    // expects, and what the map panel next to the same table already did (filterMapPoints).
    public static class OfferText
    {
        private static readonly ConcurrentDictionary<char, char> FoldedChars = new();

        /// Upper-cased, with accents dropped from the letters that carry them, without
        /// trailing spaces.
        public static string Fold(string? value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            var folded = new StringBuilder(value.Length);

            foreach (var c in value)
                folded.Append(FoldChar(c));

            while (folded.Length > 0 && folded[^1] == ' ')
                folded.Length--;

            return folded.ToString();
        }

        // Folded per character, not by decomposing the whole string: decomposing it drops
        // every combining mark, including ones that were in the text to begin with - such as
        // the variation selector in front of an emoji, which general_ci sorts by its own
        // weight. Dropping it moved those titles to the top of a title-sorted page.
        private static char FoldChar(char c)
        {
            if (c < 128)
                return char.ToUpperInvariant(c);

            return FoldedChars.GetOrAdd(c, static ch =>
            {
                // Half of a surrogate pair is not a valid string to normalize; emoji and the
                // rest of the astral planes are left exactly as they are, which is also how
                // general_ci treats what its table does not map.
                if (char.IsSurrogate(ch))
                    return ch;

                var decomposed = ch.ToString().Normalize(NormalizationForm.FormD);
                return char.ToUpperInvariant(decomposed[0]);
            });
        }
    }

    // Turns the rows streamed from the database into snapshot entries, reusing one string
    // instance per distinct value of the repeating columns.
    public sealed class OfferSnapshotBuilder
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _keys = new(StringComparer.Ordinal);

        public OfferSnapshot Build(LatestOfferRow row) => new()
        {
            Id = row.Id,
            Price = row.Price,
            PricePerMeter = row.PricePerMeter,
            Floor = row.Floor,
            Area = row.Area,
            Private = row.Private,
            WebName = row.WebName,
            City = Shared(row.City),
            District = Shared(row.District),
            Market = Shared(row.Market),
            BuildingType = Shared(row.BuildingType),
            LastSeen = row.LastSeen,
            FirstSeen = row.FirstSeen,
            SnapshotCount = row.SnapshotCount,
            TitleKey = OfferText.Fold(row.Title),
            CityKey = SharedKey(row.City),
            DistrictKey = SharedKey(row.District),
            MarketKey = SharedKey(row.Market),
            BuildingTypeKey = SharedKey(row.BuildingType)
        };

        private string Shared(string? value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            if (_values.TryGetValue(value, out var shared))
                return shared;

            _values[value] = value;
            return value;
        }

        private string SharedKey(string? value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            if (_keys.TryGetValue(value, out var key))
                return key;

            key = OfferText.Fold(value);
            _keys[value] = key;
            return key;
        }
    }
}
