using AF_mobile_web_api.DTO;
using AF_mobile_web_api.DTO.Enums;
using ApplicationDatabase;
using Microsoft.EntityFrameworkCore;
using System.Reflection;

namespace AF_mobile_web_api.Services
{
    public class StatisticServices
    {
        private readonly RealEstateServices _realEstate;
        private readonly AppDbContext _dbContext;

        public StatisticServices(RealEstateServices realEstate, AppDbContext dbContext)
        {
            _realEstate = realEstate;
            _dbContext = dbContext;
        }

        public async Task<List<TimelinePriceDto>> GetTimelinePrice(string cityName)
        {
            if (string.IsNullOrWhiteSpace(cityName))
                throw new ArgumentException("City name cannot be null or empty", nameof(cityName));

            if (!Enum.TryParse<CityEnum>(cityName, true, out CityEnum city))
                throw new ArgumentException($"Invalid city name: {cityName}. Valid cities: {string.Join(", ", Enum.GetNames<CityEnum>())}");


            // Server-side: filter, group, aggregate (all translatable)
            var groupedData = await _dbContext.PropertyData
                .Where(p => p.City == city.ToString() && p.WebName == 0)
                .GroupBy(p => p.AddedRecordTime.Date)
                .Select(g => new
                {
                    Date = g.Key,
                    AvgPrice = Math.Round(g.Average(x => x.Price), 1),
                    AvgPricePerMeter = Math.Round(g.Average(x => x.PricePerMeter), 1)
                })
                .ToListAsync();

            // Client-side: format and sort
            var result = groupedData
                .Select(x => new TimelinePriceDto
                {
                    AddedDate = x.Date.ToString("dd-MM-yyyy"),
                    AvgPrice = x.AvgPrice,
                    AvgPricePerMeter = x.AvgPricePerMeter
                })
                .OrderBy(x => DateTime.ParseExact(x.AddedDate, "dd-MM-yyyy", null))
                .ToList();

            return result;
        }

        public async Task<RealEstateStatistics> GetDataWithStatistics()
        {
            var response = await _realEstate.GetData(CityEnum.Krakow.ToString());
            return CalculateStatistics(response.Data);
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

        public async Task<Dictionary<object, RealEstateStatistics>> GetDataWithGroupStatistics(string groupByProperty)
        {
            var response = await _realEstate.GetData(CityEnum.Krakow.ToString());

            var propertyParts = groupByProperty.Split('.');
            var type = typeof(SearchData);
            PropertyInfo propertyInfo = null;

            Func<SearchData, object> keySelector = x =>
            {
                object value = x;
                foreach (var part in propertyParts)
                {
                    if (value == null) return null;
                    propertyInfo = value.GetType().GetProperty(part);
                    if (propertyInfo == null) return null;
                    value = propertyInfo.GetValue(value, null);
                }
                return value;
            };

            var groupedByData = CalculateStatisticsGroupBy(response.Data, keySelector);
            
            return groupedByData;

        }

        public Dictionary<TKey, RealEstateStatistics> CalculateStatisticsGroupBy<TKey>(
           List<SearchData> data,
           Func<SearchData, TKey> keySelector)
        {
            return data
                .GroupBy(keySelector)
                .Select(g => new
                {
                    Key = g.Key,
                    Stats = DisplayStatistics(g.ToList())
                })
                .OrderBy(x => x.Stats.MedianPricePerMeter)
                .ToDictionary(x => x.Key, x => x.Stats);
        }

        private RealEstateStatistics DisplayStatistics(List<SearchData> data)
        {
            return new RealEstateStatistics
            {
                MedianPricePerMeter = CalculateMedianPricePerMeter(data),
                MedianPrice = CalculateAvaragePrice(data),
                MedianArea = CalculateMedianArea(data),
                AverageFloor = data.Average(x => x.Floor),
                Count = data.Count
            };
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
