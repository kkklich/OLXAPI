using AF_mobile_web_api.Domain;

namespace AF_mobile_web_api.DTO;

public sealed class OlxPreviewResponse
{
    public string City { get; init; } = string.Empty;
    public string SourceUrl { get; init; } = string.Empty;
    public bool SavedToDatabase => false;
    public int Offset { get; init; }
    public int Limit { get; init; }
    public int? TotalCount { get; init; }
    public int ReturnedCount => Data.Count;
    public List<SearchData> Data { get; init; } = new();
}
