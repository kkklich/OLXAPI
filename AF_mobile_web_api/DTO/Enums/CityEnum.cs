namespace AF_mobile_web_api.DTO.Enums
{
    // Every city the scrapers download. The name is what a row's City column holds.
    // New cities go at the end: the numeric values of the existing ones must not move.
    public enum CityEnum
    {
        Krakow,
        Katowice,
        Chorzow,
        Tychy,
        Mikolow,
        Myslowice,
        Sosnowiec
    }

    public static class CityExtensions
    {
        // The Polish name, where it is not the enum name itself.
        private static readonly Dictionary<CityEnum, string> displayNames = new()
        {
            { CityEnum.Krakow, "Kraków" },
            { CityEnum.Chorzow, "Chorzów" },
            { CityEnum.Mikolow, "Mikołów" },
            { CityEnum.Myslowice, "Mysłowice" }
        };

        public static string ToDisplayName(this CityEnum city)
        {
            return displayNames.TryGetValue(city, out var value) ? value : city.ToString();
        }

        // The Polish name, percent-encoded - how Nieruchomosci-online takes it in a search URL.
        public static string ToEncodedString(this CityEnum city)
        {
            return Uri.EscapeDataString(city.ToDisplayName());
        }

        // A city by its enum name, ignoring case. Unlike Enum.TryParse, a number is not a name:
        // "1" or "99" would otherwise pass as a city.
        public static bool TryParseName(string? name, out CityEnum city)
        {
            var trimmed = name?.Trim();

            foreach (var value in Enum.GetValues<CityEnum>())
            {
                if (string.Equals(value.ToString(), trimmed, StringComparison.OrdinalIgnoreCase))
                {
                    city = value;
                    return true;
                }
            }

            city = default;
            return false;
        }

        // The Polish name of a City column value ("Chorzow" -> "Chorzów"); any other value as it is.
        public static string DisplayNameOf(string? city)
        {
            return TryParseName(city, out var value) ? value.ToDisplayName() : city ?? string.Empty;
        }

        // OLX's city_id. A city missing here is not scraped from OLX at all (see
        // OLXAPIService.GetOLXResponse) - the ids of the Silesian cities other than Katowice
        // are still to be looked up.
        private static readonly Dictionary<CityEnum, int> cityOLXValues = new()
        {
            { CityEnum.Krakow, 8959 },
            { CityEnum.Katowice, 7691 }
        };

        public static int ToEncodedOLXString(this CityEnum city)
        {
            return cityOLXValues.TryGetValue(city, out var value) ? value : 0;
        }
        
        // OLX's region_id: 4 is Małopolskie, 6 is Śląskie.
        private static readonly Dictionary<CityEnum, int> regionOLXValues = new()
        {
            { CityEnum.Krakow, 4 },
            { CityEnum.Katowice, 6 },
            { CityEnum.Chorzow, 6 },
            { CityEnum.Tychy, 6 },
            { CityEnum.Mikolow, 6 },
            { CityEnum.Myslowice, 6 },
            { CityEnum.Sosnowiec, 6 }
        };

        public static int ToEncodedRegionOLXString(this CityEnum city)
        {
            return regionOLXValues.TryGetValue(city, out var value) ? value : 0;
        }
    }
   
}
