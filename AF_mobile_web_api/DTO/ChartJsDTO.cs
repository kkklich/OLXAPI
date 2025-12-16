namespace AF_mobile_web_api.DTO
{
    public class ChartData
    {
        public List<Dataset> Datasets { get; set; } = new();
        public List<string> Labels { get; set; } = new();
    }

    public class Dataset
    {
        public List<double> Data { get; set; } = new();
        public string Label { get; set; } = string.Empty;
    }
}
