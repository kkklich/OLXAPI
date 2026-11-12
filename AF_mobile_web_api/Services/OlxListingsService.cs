using AF_mobile_web_api.Domain;
using AF_mobile_web_api.DTO;
using AF_mobile_web_api.DTO.Enums;
using AF_mobile_web_api.Helper;
using AF_mobile_web_api.Services.Interfaces;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AF_mobile_web_api.Services;

/// <summary>A bounded, cancellable live OLX search, including syndicated Otodom listings.</summary>
public sealed class OlxListingsService(IHttpClientFactory httpClientFactory) : IOlxListingsService
{
    public async Task<MarketplaceCityPage> GetPageAsync(CityEnum city, int priceFrom, int priceTo,
        int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var cityId = city.ToEncodedOLXString();
        if (cityId == 0)
            throw new ArgumentOutOfRangeException(nameof(city));
        if (priceFrom < 0 || priceTo < priceFrom)
            throw new ArgumentException("Prices must be nonnegative and priceTo must be at least priceFrom.");
        if (pageSize is < 1 or > 40 || page < 1 || (long)page * pageSize > 1000)
            throw new ArgumentOutOfRangeException(nameof(page), "The requested page must stay within OLX's first 1000 results (page size 1–40).");

        var offset = (page - 1) * pageSize;
        var sourceUrl = $"{ConstantHelper.OLXAPI}?offset={offset}&limit={pageSize}&category_id={ConstantHelper.RealEstateCategory}"
            + $"&filter_float_price%3Afrom={priceFrom}&filter_float_price%3Ato={priceTo}"
            + $"&region_id={city.ToEncodedRegionOLXString()}&city_id={cityId}";

        using var client = httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(30);
        using var request = new HttpRequestMessage(HttpMethod.Get, sourceUrl);
        request.Headers.Accept.ParseAdd("application/json");
        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var json = JObject.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        if (json["data"] is not JArray)
            throw new JsonSerializationException("OLX response is missing its data array.");
        var result = json.ToObject<QueryData>() ?? throw new JsonSerializationException("OLX response was empty.");

        var offers = result.Data
            .Where(offer => offer != null && !string.IsNullOrWhiteSpace(offer.Id)
                && !string.IsNullOrWhiteSpace(offer.Url)
                && offer.Location?.City?.Id == cityId
                && offer.Title?.Contains("TBS", StringComparison.OrdinalIgnoreCase) != true)
            .DistinctBy(offer => offer.Id)
            .Select(MapOffer)
            .Where(offer => offer.Price >= priceFrom && offer.Price <= priceTo)
            .DistinctBy(offer => offer.Url, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var totalCount = result.metadata == null ? (int?)null
            : Math.Max(result.metadata.total_elements, result.metadata.visible_total_count);

        return new MarketplaceCityPage
        {
            Source = "olx",
            City = city.ToString(),
            SourceUrl = sourceUrl,
            TotalCount = totalCount,
            HasMore = totalCount.HasValue ? offset + pageSize < totalCount : result.Data.Count >= pageSize,
            Data = offers
        };
    }

    private static SearchData MapOffer(Data offer)
    {
        var mapped = OLXAPIService.ExtractParameter(offer);
        // Keep the legacy scraper and preview's OLX URL identity intact. Only this live API
        // uses the original Otodom URL, so cross-listed offers can be merged with Otodom results.
        if (string.Equals(offer.Partner?.Code, "otodom_pl", StringComparison.OrdinalIgnoreCase)
            && Uri.TryCreate(offer.ExternalUrl, UriKind.Absolute, out var external)
            && external.Scheme == Uri.UriSchemeHttps
            && (external.Host.Equals("www.otodom.pl", StringComparison.OrdinalIgnoreCase)
                || external.Host.Equals("otodom.pl", StringComparison.OrdinalIgnoreCase))
            && external.AbsolutePath.StartsWith("/pl/oferta/", StringComparison.Ordinal))
        {
            mapped.Url = $"https://www.otodom.pl{external.AbsolutePath.TrimEnd('/')}";
            mapped.WebName = WebName.Otodom;
        }
        return mapped;
    }
}
