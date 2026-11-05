using AF_mobile_web_api.Domain;
using AF_mobile_web_api.DTO;
using AF_mobile_web_api.DTO.Enums;
using AF_mobile_web_api.Services.Interfaces;
using Newtonsoft.Json;

namespace AF_mobile_web_api.Services;

/// <summary>Bounded live search; deliberately has no repository or scrape-job dependency.</summary>
public sealed class MarketplaceListingsService(
    IOlxListingsService olx,
    IOtodomListingsService otodom,
    ILogger<MarketplaceListingsService> logger)
{
    public async Task<MarketplaceListingsResponse> GetAsync(string city = "Krakow", string source = "all",
        int priceFrom = 50_000, int priceTo = 10_000_000, int page = 1,
        CancellationToken cancellationToken = default)
    {
        // Keep aliases local: existing database/cache city keys are unchanged.
        var cityName = Enum.GetValues<CityEnum>().Where(c =>
            string.Equals(c.ToDisplayName(), city?.Trim(), StringComparison.OrdinalIgnoreCase))
            .Select(c => c.ToString()).FirstOrDefault();
        var area = CityArea.Parse(cityName ?? city);
        var selectedSource = source?.Trim().ToLowerInvariant();
        if (selectedSource is not ("all" or "olx" or "otodom"))
            throw new ArgumentException("Source must be all, olx or otodom.", nameof(source));
        if (priceFrom < 0 || priceTo < priceFrom)
            throw new ArgumentException("Prices must be nonnegative and priceTo must be at least priceFrom.");
        if (page is < 1 or > 27)
            throw new ArgumentOutOfRangeException(nameof(page), "Page must be between 1 and 27 (36 offers per city and source).");

        var sources = selectedSource == "all" ? new[] { "olx", "otodom" } : new[] { selectedSource };
        using var slots = new SemaphoreSlim(3);
        var tasks = area.Cities.SelectMany(c => sources.Select(s => FetchAsync(c, s))).ToArray();
        var pages = await Task.WhenAll(tasks);
        cancellationToken.ThrowIfCancellationRequested();

        return new MarketplaceListingsResponse
        {
            City = area.Name, Source = selectedSource, Page = page, Pages = pages.ToList(),
            // OLX partner URLs are canonicalized by its adapter. Prefer the original
            // Otodom item when both portals return that URL; IDs alone are portal-local.
            Data = pages.OrderBy(p => p.Source == "otodom" ? 0 : 1)
                .SelectMany(p => p.Data)
                .GroupBy(p => CanonicalUrl(p.Url), StringComparer.Ordinal)
                .Select(MergeOffer)
                .ToList()
        };

        async Task<MarketplaceCityPage> FetchAsync(CityEnum selectedCity, string portal)
        {
            await slots.WaitAsync(cancellationToken);
            try
            {
                return portal == "olx"
                    ? await olx.GetPageAsync(selectedCity, priceFrom, priceTo, page, 36, cancellationToken)
                    : await otodom.GetPageAsync(selectedCity, priceFrom, priceTo, page, 36, cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                logger.LogWarning(ex, "Live {Source} search failed for {City}", portal, selectedCity);
                return Failed("upstream_error", "The portal could not return listings.", (int?)ex.StatusCode);
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Live {Source} search timed out for {City}", portal, selectedCity);
                return Failed("timeout", "The portal did not respond in time.");
            }
            catch (JsonException ex)
            {
                logger.LogWarning(ex, "Live {Source} search returned malformed data for {City}", portal, selectedCity);
                return Failed("invalid_response", "The portal returned an unrecognized response.");
            }
            finally { slots.Release(); }

            MarketplaceCityPage Failed(string code, string message, int? status = null) => new()
            {
                City = selectedCity.ToString(), Source = portal,
                Error = new MarketplaceFetchError(code, message, status)
            };
        }
    }

    private static SearchData MergeOffer(IEnumerable<SearchData> copies)
    {
        var preferred = copies.First();
        // Otodom's search cards omit some details supplied by OLX partner cards.
        // These objects belong only to this request; no cached/persisted rows are mutated.
        foreach (var copy in copies.Skip(1))
        {
            if (string.IsNullOrWhiteSpace(preferred.Market)) preferred.Market = copy.Market;
            if (string.IsNullOrWhiteSpace(preferred.BuildingType)) preferred.BuildingType = copy.BuildingType;
            if (copy.Location == null) continue;
            if (preferred.Location == null) preferred.Location = copy.Location;
            else
            {
                if (string.IsNullOrWhiteSpace(preferred.Location.District))
                    preferred.Location.District = copy.Location.District;
                if (preferred.Location.Lat == 0 && preferred.Location.Lon == 0)
                {
                    preferred.Location.Lat = copy.Location.Lat;
                    preferred.Location.Lon = copy.Location.Lon;
                }
            }
        }
        return preferred;
    }

    private static string CanonicalUrl(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri)
        ? uri.GetLeftPart(UriPartial.Path).TrimEnd('/') : url;
}
