using AF_mobile_web_api.DTO;

namespace AF_mobile_web_api.Services
{
    public class StatisticServices
    {
        private readonly RealEstateServices _realEstate;

        public StatisticServices(RealEstateServices realEstate) 
        {
            _realEstate = realEstate;
        }

        //public async Task<RealEstateStatistics> GetDataWithStatistics() 
        public async Task<Dictionary<int, RealEstateStatistics>> GetDataWithStatistics() 
        {
            var response = await _realEstate.GetMoreResponse();

            //return CalculateStatistics(response.Data);
            return CalculateStatisticsGroupBy(response.Data);
        }

        private RealEstateStatistics CalculateStatistics(List<SearchData> data)
        {
            var stats = new RealEstateStatistics
            {
                MedianPricePerMeter = CalculateMedianPricePerMeter(data),
                MedianPrice = CalculateAvaragePrice(data),
                MedianArea = CalculateMedianArea(data),
                AverageFloor = data.Average(x => x.Floor),
                Count = data.Count
            };

            return stats;
        }

        public Dictionary<int, RealEstateStatistics> CalculateStatisticsGroupBy(List<SearchData> data)
        {
            return data
                .GroupBy(x => x.Floor)
                .ToDictionary(
                    g => g.Key,
                    g => new RealEstateStatistics
                    {
                        MedianPricePerMeter = CalculateMedianPricePerMeter(g.ToList()),
                        MedianPrice = CalculateAvaragePrice(g.ToList()),
                        MedianArea = CalculateMedianArea(g.ToList()),
                        AverageFloor = g.Average(x => (double)x.Floor),
                        Count = g.Count()
                    });
        }

        private double CalculateAvaragePrice(List<SearchData> data)
        {
            return data.Average(x => x.Price);
        }
        private double CalculateAvaragePricePerMeter(List<SearchData> data)
        {
            return data.Average(x => x.PricePerMeter);
        }

        private double CalculateMedianPrice(List<SearchData> data)
        {
            var sortedPrices = data.Select(x => x.Price).OrderBy(a => a).ToList();
            int count = sortedPrices.Count;

            if (count == 0)
                return 0;

            if (count % 2 == 1)
                return sortedPrices[count / 2];
            else
                return (sortedPrices[(count / 2) - 1] + sortedPrices[count / 2]) / 2.0;
        }
        
        private double CalculateMedianPricePerMeter(List<SearchData> data)
        {
            var sortedPrices = data.Select(x => x.PricePerMeter).OrderBy(a => a).ToList();
            int count = sortedPrices.Count;

            if (count == 0)
                return 0;

            if (count % 2 == 1)
                return sortedPrices[count / 2];
            else
                return (sortedPrices[(count / 2) - 1] + sortedPrices[count / 2]) / 2.0;
        }

        private double CalculatePriceStandardDeviation(List<SearchData> data)
        {
            double avg = data.Average(x => x.Price);
            double sumSquares = data.Sum(x => Math.Pow(x.Price - avg, 2));
            return Math.Sqrt(sumSquares / data.Count);
        }

        private double CalculateMedianArea(List<SearchData> data)
        {
            var sortedAreas = data.Select(x => x.Area).OrderBy(a => a).ToList();
            int count = sortedAreas.Count;

            if (count == 0)
                return 0;

            if (count % 2 == 1)
                return sortedAreas[count / 2];
            else
                return (sortedAreas[(count / 2) - 1] + sortedAreas[count / 2]) / 2.0;
        }

    }
}
