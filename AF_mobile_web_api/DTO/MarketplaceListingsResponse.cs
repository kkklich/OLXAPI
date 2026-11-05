using AF_mobile_web_api.Domain;

namespace AF_mobile_web_api.DTO;

public sealed class MarketplaceListingsResponse
{
    public string City { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public bool SavedToDatabase => false;
    public DateTimeOffset FetchedAt { get; init; } = DateTimeOffset.UtcNow;
    public int Page { get; init; }
    // Pagination applies separately to each city and source, before deduplication.
    public int PageSize => 36;
    public bool IsPartial => Pages.Any(p => p.Error != null);
    public bool HasMore => Pages.Any(p => p.Error == null && p.HasMore);
    public int? NextPage => HasMore && Page < 27 ? Page + 1 : null;
    public bool PaginationLimitReached => HasMore && Page == 27;
    public int ReturnedCount => Data.Count;
    public List<MarketplaceCityPage> Pages { get; init; } = new();
    public List<SearchData> Data { get; init; } = new();
}

public sealed class MarketplaceCityPage
{
    public string Source { get; init; } = string.Empty;
    public string City { get; init; } = string.Empty;
    public string SourceUrl { get; init; } = string.Empty;
    public int? TotalCount { get; init; }
    public bool HasMore { get; init; }
    public int ReturnedCount => Data.Count;
    public MarketplaceFetchError? Error { get; init; }
    // Items are returned once in the top-level data array, even across portals.
    [System.Text.Json.Serialization.JsonIgnore]
    public List<SearchData> Data { get; init; } = new();
}

public sealed record MarketplaceFetchError(string Code, string Message, int? UpstreamStatusCode = null);
