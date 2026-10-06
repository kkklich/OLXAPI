using System.Diagnostics.CodeAnalysis;
using AF_mobile_web_api.DTO.Enums;

namespace AF_mobile_web_api.Domain
{
    // What one dashboard covers: a single scraped city, or several shown as one market.
    //
    // An area only exists when data is read. Every scraped row keeps the city it was
    // scraped for, and an area's statistics are computed over its cities' rows - so
    // Katowice's history carries on inside Silesia, and a city can join an area without a
    // single row being rewritten.
    public sealed class CityArea
    {
        private static readonly CityArea[] MultiCityAreas =
        {
            new("Silesia", CityEnum.Katowice, CityEnum.Chorzow, CityEnum.Tychy,
                CityEnum.Mikolow, CityEnum.Myslowice, CityEnum.Sosnowiec)
        };

        private CityArea(string name, params CityEnum[] cities)
        {
            Name = name;
            Cities = cities;
            CityNames = cities.Select(city => city.ToString()).ToList();
        }

        // The name the API is called with; the per-area caches are keyed by it too.
        public string Name { get; }

        public IReadOnlyList<CityEnum> Cities { get; }

        // The cities as their rows' City column holds them.
        public IReadOnlyList<string> CityNames { get; }

        public bool IsMultiCity => Cities.Count > 1;

        // What the dashboard offers: every city that is in no area, and every area.
        public static IEnumerable<CityArea> DashboardAreas =>
            Enum.GetValues<CityEnum>()
                .Where(city => !MultiCityAreas.Any(area => area.Cities.Contains(city)))
                .Select(city => new CityArea(city.ToString(), city))
                .Concat(MultiCityAreas);

        // Every name whose cached statistics include this city: its own and its areas'.
        public static IEnumerable<string> NamesIncluding(CityEnum city) =>
            MultiCityAreas
                .Where(area => area.Cities.Contains(city))
                .Select(area => area.Name)
                .Prepend(city.ToString());

        // An area by its name, or a single city by its CityEnum name - both ignoring case.
        public static bool TryParse(string? name, [NotNullWhen(true)] out CityArea? area)
        {
            area = MultiCityAreas.FirstOrDefault(a =>
                string.Equals(a.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase));

            if (area == null && CityExtensions.TryParseName(name, out var city))
                area = new CityArea(city.ToString(), city);

            return area != null;
        }

        public static CityArea Parse(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("City name cannot be null or empty", nameof(name));

            if (!TryParse(name, out var area))
            {
                var valid = Enum.GetNames<CityEnum>().Concat(MultiCityAreas.Select(a => a.Name));
                throw new ArgumentException($"Invalid city name: {name}. Valid cities: {string.Join(", ", valid)}");
            }

            return area;
        }
    }
}
