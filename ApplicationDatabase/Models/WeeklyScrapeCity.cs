namespace ApplicationDatabase.Models;

public class WeeklyScrapeCity
{
    public long RunId { get; set; }
    public string City { get; set; } = "";
    public string? Error { get; set; }
}
