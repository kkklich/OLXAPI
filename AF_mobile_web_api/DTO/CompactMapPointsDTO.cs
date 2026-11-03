namespace AF_mobile_web_api.DTO;

// Version 1 row: url, title, price, price/m², floor, market index, building index,
// area, private, latitude, longitude, district index. No numeric rounding.
public sealed class CompactMapPointsDTO
{
    public int Version => 1;
    public List<string> Markets { get; } = new();
    public List<string> BuildingTypes { get; } = new();
    public List<string> Districts { get; } = new();
    public List<object[]> Points { get; } = new();

    public static CompactMapPointsDTO From(IReadOnlyList<MapPointDTO> points)
    {
        var result = new CompactMapPointsDTO();
        var markets = new Dictionary<string, int>(StringComparer.Ordinal);
        var buildings = new Dictionary<string, int>(StringComparer.Ordinal);
        var districts = new Dictionary<string, int>(StringComparer.Ordinal);
        static int Index(string value, Dictionary<string, int> index, List<string> values)
        {
            if (index.TryGetValue(value, out var id)) return id;
            id = values.Count;
            values.Add(value);
            index[value] = id;
            return id;
        }
        foreach (var point in points)
            result.Points.Add(new object[]
            {
                point.Url, point.Title, point.Price, point.PricePerMeter, point.Floor,
                Index(point.Market, markets, result.Markets),
                Index(point.BuildingType, buildings, result.BuildingTypes), point.Area, point.Private,
                point.Location.Lat, point.Location.Lon,
                Index(point.Location.District, districts, result.Districts)
            });
        return result;
    }
}
