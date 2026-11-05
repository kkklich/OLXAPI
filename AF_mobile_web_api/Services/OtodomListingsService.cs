using System.Globalization;
using System.Text.RegularExpressions;
using AF_mobile_web_api.Domain;
using AF_mobile_web_api.DTO;
using AF_mobile_web_api.DTO.Enums;
using AF_mobile_web_api.Services.Interfaces;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AF_mobile_web_api.Services;

/// <summary>Reads one public Otodom apartment-sale search page. Does not access the database.</summary>
public sealed class OtodomListingsService(IHttpClientFactory httpClientFactory) : IOtodomListingsService
{
    private static readonly Regex NextDataScript = new(
        "<script\\b[^>]*\\bid\\s*=\\s*[\"']__NEXT_DATA__[\"'][^>]*>(?<json>.*?)</script\\s*>",
        RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(2));

    private static readonly IReadOnlyDictionary<CityEnum, string> Locations = new Dictionary<CityEnum, string>
    {
        [CityEnum.Krakow] = "malopolskie/krakow/krakow/krakow",
        [CityEnum.Katowice] = "slaskie/katowice/katowice/katowice",
        [CityEnum.Chorzow] = "slaskie/chorzow/chorzow/chorzow",
        [CityEnum.Tychy] = "slaskie/tychy/tychy/tychy",
        [CityEnum.Mikolow] = "slaskie/mikolowski/mikolow/mikolow",
        [CityEnum.Myslowice] = "slaskie/myslowice/myslowice/myslowice",
        [CityEnum.Sosnowiec] = "slaskie/sosnowiec/sosnowiec/sosnowiec"
    };

    public async Task<MarketplaceCityPage> GetPageAsync(CityEnum city, int priceFrom, int priceTo,
        int page, int pageSize, CancellationToken cancellationToken = default)
    {
        if (!Locations.TryGetValue(city, out var location))
            throw new ArgumentException("Unsupported Otodom city.", nameof(city));
        if (priceFrom < 0 || priceTo < priceFrom)
            throw new ArgumentException("Prices must be nonnegative and priceTo must be at least priceFrom.");
        if (page is < 1 or > 27)
            throw new ArgumentOutOfRangeException(nameof(page), "Page must be between 1 and 27.");
        // Otodom normalizes unsupported limits to 36. Using another size would skip/repeat offers.
        if (pageSize != 36)
            throw new ArgumentOutOfRangeException(nameof(pageSize), "Otodom search pages contain 36 slots.");

        var url = FormattableString.Invariant(
            $"https://www.otodom.pl/pl/wyniki/sprzedaz/mieszkanie/{location}?page={page}&limit={pageSize}&priceMin={priceFrom}&priceMax={priceTo}");
        using var client = httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(30);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.ParseAdd("text/html");
        // The public search rejects clients without a User-Agent. Identify this
        // application explicitly; no browser impersonation or session cookies needed.
        request.Headers.UserAgent.ParseAdd("RealEstateApp/1.0");
        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync(cancellationToken);

        Match script;
        try { script = NextDataScript.Match(html); }
        catch (RegexMatchTimeoutException ex)
        {
            throw new JsonSerializationException("Otodom search page could not be parsed.", ex);
        }
        if (!script.Success)
            throw new JsonSerializationException("Otodom search page is missing listing data.");

        var root = JObject.Parse(script.Groups["json"].Value);
        if (root.SelectToken("props.pageProps.data.searchAds") is not JObject search
            || search["items"] is not JArray items || search["pagination"] is not JObject pagination)
            throw new JsonSerializationException("Otodom search response has an unsupported structure.");

        var total = RequiredInteger(pagination, "totalItems", 0);
        var totalPages = RequiredInteger(pagination, "totalPages", 0);
        var currentPage = RequiredInteger(pagination, "currentPage", 1);
        var actualSize = RequiredInteger(pagination, "itemsPerPage", 1);
        if (actualSize != pageSize || (currentPage != page && page <= totalPages))
            throw new JsonSerializationException("Otodom returned a different search page than requested.");

        var offers = new List<SearchData>();
        if (page <= totalPages)
        {
            foreach (var token in items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (token is not JObject item)
                    throw new JsonSerializationException("Otodom returned an invalid listing.");
                // Development cards and recommendations outside the selected city are not offers
                // for this query. Never label an unrelated city's offer with the requested city.
                if (Text(item["estate"]) != "FLAT" || Text(item["transaction"]) != "SELL")
                    continue;
                var places = item.SelectToken("location.reverseGeocoding.locations") as JArray;
                var cityName = FindPlace(places, "city_or_village");
                if (!string.Equals(cityName, city.ToDisplayName(), StringComparison.OrdinalIgnoreCase))
                    continue;

                var id = Text(item["id"]);
                var slug = Text(item["slug"]);
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(slug))
                    throw new JsonSerializationException("Otodom listing is missing its id or URL slug.");
                var price = Number(item.SelectToken("totalPrice.value"));
                if (price < priceFrom || price > priceTo) continue;
                var area = Number(item["areaInSquareMeters"]);
                var pricePerMeter = Number(item.SelectToken("pricePerSquareMeter.value"));
                if (pricePerMeter <= 0 && area > 0 && price > 0) pricePerMeter = price / area;
                if (!double.IsFinite(pricePerMeter)) pricePerMeter = 0;

                offers.Add(new SearchData
                {
                    Id = id,
                    Url = $"https://www.otodom.pl/pl/oferta/{Uri.EscapeDataString(slug)}",
                    Title = Text(item["title"]) ?? string.Empty,
                    Price = price,
                    PricePerMeter = pricePerMeter,
                    Area = area,
                    Floor = Floor(Text(item["floorNumber"])),
                    Private = item["isPrivateOwner"]?.Type == JTokenType.Boolean && item.Value<bool>("isPrivateOwner"),
                    WebName = WebName.Otodom,
                    // These fields are not present in the public search-page payload.
                    Market = string.Empty,
                    BuildingType = string.Empty,
                    Location = new LocationPlace
                    {
                        City = cityName!, District = FindPlace(places, "district") ?? string.Empty
                    }
                });
            }
        }

        return new MarketplaceCityPage
        {
            Source = "otodom", City = city.ToString(), SourceUrl = url,
            TotalCount = total, HasMore = page < totalPages,
            Data = offers.DistinctBy(offer => offer.Id).ToList()
        };
    }

    private static int RequiredInteger(JObject value, string name, int minimum)
    {
        var token = value[name];
        if (token?.Type != JTokenType.Integer
            || !int.TryParse(token.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            || number < minimum)
            throw new JsonSerializationException($"Otodom pagination is missing a valid {name}.");
        return number;
    }

    private static string? FindPlace(JArray? places, string level) => Text(places?.OfType<JObject>()
        .FirstOrDefault(place => Text(place["locationLevel"]) == level)?["name"]);

    private static string? Text(JToken? token) => token?.Type switch
    {
        null or JTokenType.Null => null,
        JTokenType.String or JTokenType.Integer => token.ToString(),
        _ => throw new JsonSerializationException("Otodom listing contains an invalid text field.")
    };

    private static double Number(JToken? value) => double.TryParse(value?.ToString(), NumberStyles.Float,
        CultureInfo.InvariantCulture, out var number) && double.IsFinite(number) && number > 0 ? number : 0;

    private static int Floor(string? value) => value switch
    {
        "BASEMENT" => -1, "GROUND" => 0, "FIRST" => 1, "SECOND" => 2, "THIRD" => 3,
        "FOURTH" => 4, "FIFTH" => 5, "SIXTH" => 6, "SEVENTH" => 7, "EIGHTH" => 8,
        "NINTH" => 9, "TENTH" => 10,
        _ => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var floor) ? floor : 0
    };
}
