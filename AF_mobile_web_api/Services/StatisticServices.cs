using AF_mobile_web_api.DTO;
using AF_mobile_web_api.DTO.Enums;
using ApplicationDatabase;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using System.Reflection;

namespace AF_mobile_web_api.Services
{
    public class StatisticServices
    {
        private readonly RealEstateServices _realEstate;
        private readonly AppDbContext _dbContext;

        private readonly IMemoryCache _cache;

        // np. 10 minut cache
        private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(120);

        public StatisticServices(RealEstateServices realEstate, AppDbContext dbContext, IMemoryCache cache)
        {
            _realEstate = realEstate;
            _dbContext = dbContext;
            _cache = cache;
        }

        private async Task<List<SearchData>> GetCachedRealEstateDataAsync(string city)
        {
            var cacheKey = $"RealEstateData_{city}";

            if (_cache.TryGetValue(cacheKey, out List<SearchData> cached))
            {
                return cached;
            }

            var results = (await _realEstate.GetData(city)).Data;

            var options = new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = CacheDuration
            };

            _cache.Set(cacheKey, results, options);

            return results;
        }

        public async Task<List<TimelinePriceDto>> GetTimelinePrice(string cityName)
        {
            if (string.IsNullOrWhiteSpace(cityName))
                throw new ArgumentException("City name cannot be null or empty", nameof(cityName));

            if (!Enum.TryParse<CityEnum>(cityName, true, out CityEnum city))
                throw new ArgumentException($"Invalid city name: {cityName}. Valid cities: {string.Join(", ", Enum.GetNames<CityEnum>())}");

            // Server-side: filter, group, aggregate (all translatable)
            var groupedData = await _dbContext.PropertyData
                .Where(p => p.City == city.ToString())
                .GroupBy(p => p.AddedRecordTime.Date)
                .Select(g => new
                {
                    Date = g.Key,
                    AvgPrice = Math.Round(g.Average(x => x.Price), 1),
                    AvgPricePerMeter = Math.Round(g.Average(x => x.PricePerMeter), 1),
                    Count = g.Count()
                })
                .ToListAsync();

            // Client-side: format and sort
            var result = groupedData
                .Select(x => new TimelinePriceDto
                {
                    AddedDate = x.Date.ToString("dd-MM-yyyy"),
                    AvgPrice = x.AvgPrice,
                    AvgPricePerMeter = x.AvgPricePerMeter,
                    Count = x.Count
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

        public async Task<ChartData> GetBarChartData(string city, string groupedBy)
        {
            var results = await GetCachedRealEstateDataAsync(city);
            return GetBarChartDataByBuildingType(results, groupedBy);

        }
        private ChartData GetBarChartDataByBuildingType(List<SearchData> results, string groupedBy)
        {
            // Input validation
            if (results == null || !results.Any())
            {
                return new ChartData
                {
                    Datasets = new List<Dataset> { new Dataset { Data = new List<double>(), Label = "Liczba ofert" } },
                    Labels = new List<string>()
                };
            }

            // Group data by specified property path
            var groupMap = new Dictionary<string, double>();
            foreach (var item in results)
            {
                var key = GetNestedProperty(item, groupedBy);

                // Handle different data types consistently
                if (key is double numericKey)
                {
                    key = Math.Round(numericKey).ToString();
                }
                else if (key is bool boolKey)
                {
                    key = boolKey.ToString();
                }
                else if (key == null)
                {
                    key = "Unknown";
                }

                var keyString = key.ToString();
                if (groupMap.ContainsKey(keyString))
                    groupMap[keyString]++;
                else
                    groupMap[keyString] = 1.0;
            }

            var keys = groupMap.Keys.ToList();
            var allNumeric = keys.All(k => double.TryParse(k, out _));

            List<string> labels;
            List<double> counts;

            if (allNumeric)
            {
                var binSize = GetBinSizeValue(groupedBy);
                var bins = BinNumericKeys(keys, groupMap, binSize);
                var pairs = bins.Select(kvp => new { key = kvp.Key, value = kvp.Value })
                                .OrderBy(p => double.Parse(p.key))
                                .ToList();

                labels = pairs.Select(p => p.key).ToList();
                counts = pairs.Select(p => p.value).ToList();
            }
            else
            {
                labels = keys;
                counts = keys.Select(k => groupMap[k]).ToList();
            }

            return new ChartData
            {
                Datasets = new List<Dataset>
        {
            new Dataset
            {
                Data = counts,
                Label = "Liczba ofert"
            }
        },
                Labels = labels
            };
        }

        private double GetBinSizeValue(string key)
        {
            return key switch
            {
                "pricePerMeter" => 200,
                "price" => 5000,
                _ => 1
            };
        }

        private Dictionary<string, double> BinNumericKeys(List<string> keys, Dictionary<string, double> groupMap, double binSize)
        {
            var bins = new Dictionary<string, double>();

            foreach (var key in keys)
            {
                if (double.TryParse(key, out var num))
                {
                    var binEnd = Math.Ceiling(num / binSize) * binSize;
                    var binLabel = binEnd.ToString();

                    if (bins.ContainsKey(binLabel))
                        bins[binLabel] += groupMap[key];
                    else
                        bins[binLabel] = groupMap[key];
                }
            }

            return bins;
        }

        private object? GetNestedProperty(object obj, string path)
        {
            if (obj == null || string.IsNullOrEmpty(path))
                return null;

            var parts = path.Split('.');
            var current = obj;

            foreach (var part in parts)
            {
                if (current == null) return null;

                // Handle both dynamic objects and strongly-typed properties with case-insensitive lookup
                var property = current.GetType().GetProperty(part,
                    BindingFlags.IgnoreCase |
                    BindingFlags.Public |
                    BindingFlags.Instance);

                if (property != null)
                {
                    current = property.GetValue(current);
                }
                else
                {
                    // Fallback for dynamic properties or dictionaries (try exact match first, then case-insensitive)
                    if (current is Dictionary<string, object> dict)
                    {
                        if (dict.TryGetValue(part, out var value))
                        {
                            current = value;
                        }
                        else
                        {
                            // Try case-insensitive lookup for dictionary
                            var key = dict.Keys.FirstOrDefault(k => k.Equals(part, StringComparison.OrdinalIgnoreCase));
                            if (key != null)
                                current = dict[key];
                            else
                                return null;
                        }
                    }
                    else
                    {
                        return null;
                    }
                }
            }

            return current;
        }






        public async Task<ChartData> FilterByParameter(string groupBy, string city, string parameter)
        {
            var results = await GetCachedRealEstateDataAsync(city);

            var groupedStats = GetDataWithGroupStatistics(results, groupBy);
            var fullChart = BuildChartData(groupedStats);
            var filtered = FilterChartDataByParameter(fullChart, parameter);

            return filtered;
        }


        private Dictionary<string, RealEstateStatistics> GetDataWithGroupStatistics(
    List<SearchData> data,
    string groupByProperty
)
        {
            var groups = new Dictionary<string, List<SearchData>>();

            foreach (var item in data)
            {
                var keyObj = GetNestedProperty(item, groupByProperty);
                var key = keyObj?.ToString() ?? "Unknown";

                if (!groups.TryGetValue(key, out var list))
                {
                    list = new List<SearchData>();
                    groups[key] = list;
                }

                list.Add(item);
            }

            var result = new Dictionary<string, RealEstateStatistics>();

            foreach (var kvp in groups)
            {
                var stats = DisplayStatistics(kvp.Value);
                result[kvp.Key] = stats;
            }

            // sortowanie po MedianPricePerMeter
            var sorted = result
                .OrderBy(x => x.Value.MedianPricePerMeter)
                .ToDictionary(x => x.Key, x => x.Value);

            return sorted;
        }



        // ====== 3. buildChartData (C#) ======
        private ChartData BuildChartData(Dictionary<string, RealEstateStatistics> input)
        {
            // input: key => RealEstateStatistics (medianPricePerMeter, medianPrice, medianArea, averageFloor, count)
            // labels: medianPricePerMeter, medianPrice, medianArea, averageFloor, count
            var labels = new List<string> { "medianPricePerMeter", "medianPrice", "medianArea", "averageFloor", "count" };

            var datasets = new List<Dataset>();
            int idx = 0;

            foreach (var kvp in input)
            {
                var groupName = kvp.Key;
                var v = kvp.Value;

                var ds = new Dataset
                {
                    Label = groupName,
                    Data = new List<double>
                {
                    v.MedianPricePerMeter,
                    v.MedianPrice,
                    v.MedianArea,
                    v.AverageFloor,
                    v.Count
                }
                };

                datasets.Add(ds);
                idx++;
            }

            return new ChartData
            {
                Labels = labels,
                Datasets = datasets
            };
        }

        // ====== 4. odpowiednik TS: filterByParameter(parameter) ======
        private ChartData FilterChartDataByParameter(ChartData datasets, string parameter)
        {
            if (datasets?.Labels == null || datasets.Datasets == null)
            {
                return new ChartData
                {
                    Labels = new List<string>(),
                    Datasets = new List<Dataset>()
                };
            }

            var index = datasets.Labels.IndexOf(parameter);
            if (index == -1)
            {
                return new ChartData
                {
                    Labels = new List<string>(),
                    Datasets = new List<Dataset>()
                };
            }

            // newDatasets: każdy dataset ma tylko 1 value (z indexu)
            var newDatasets = datasets.Datasets.Select(ds => new Dataset
            {
                Label = ds.Label,
                Data = (index < ds.Data.Count)
                    ? new List<double> { ds.Data[index] }
                    : new List<double> { 0 }
            }).ToList();

            // datas: flatten
            var datas = newDatasets.SelectMany(ds => ds.Data).ToList();
            // labels: nazwy grup (Label z datasetów)
            var labels = newDatasets.Select(ds => ds.Label).ToList();

            return new ChartData
            {
                Labels = labels,
                Datasets = new List<Dataset>
            {
                new Dataset
                {
                    Data = datas,
                    Label = "Liczba ofert"
                }
            }
            };
        }

        private double CalculateAverage(List<double> values)
        {
            if (values.Count == 0) return 0;
            return values.Average();
        }

        private double CalculateMedian(List<double> values)
        {
            if (values.Count == 0) return 0;
            values.Sort();
            int count = values.Count;
            if (count % 2 == 1)
            {
                return values[count / 2];
            }
            return (values[count / 2 - 1] + values[count / 2]) / 2.0;
        }






    }
}
