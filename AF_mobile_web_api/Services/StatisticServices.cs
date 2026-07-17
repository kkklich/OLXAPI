using AF_mobile_web_api.Domain;
using AF_mobile_web_api.DTO;
using AF_mobile_web_api.DTO.Enums;
using AF_mobile_web_api.Repositories.Interfaces;
using AF_mobile_web_api.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using System.Reflection;

namespace AF_mobile_web_api.Services
{
    public class StatisticServices: IStatisticServices
    {
        private readonly IPropertyDataRepository _propertyDataRepository;
        private readonly IRealEstateServices _realEstate;
        private readonly IMemoryCache _cache;

        private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(120);

        // districts with fewer offers than this produce statistically meaningless medians
        private const int MinOffersPerDistrict = 5;

        // a "deal" more than this far below the district median is a scraping error
        // (swapped price/area, placeholder values), not a real offer
        private const double MaxBelowMedianPercent = 50;

        public StatisticServices(IRealEstateServices realEstate, IMemoryCache cache, IPropertyDataRepository propertyDataRepository)
        {
            _realEstate = realEstate;
            _cache = cache;
            _propertyDataRepository = propertyDataRepository;
        }

        private static CityEnum ParseCity(string cityName)
        {
            if (string.IsNullOrWhiteSpace(cityName))
                throw new ArgumentException("City name cannot be null or empty", nameof(cityName));

            if (!Enum.TryParse<CityEnum>(cityName, true, out CityEnum city))
                throw new ArgumentException($"Invalid city name: {cityName}. Valid cities: {string.Join(", ", Enum.GetNames<CityEnum>())}");

            return city;
        }

        private async Task<List<SearchData>> GetCachedRealEstateDataAsync(string city)
        {
            var cacheKey = $"RealEstateData_{city}";

            if (_cache.TryGetValue(cacheKey, out List<SearchData> cached))
            {
                return cached;
            }

            var results = (await _realEstate.GetDataAsync(city)).Data;

            var options = new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = CacheDuration
            };

            _cache.Set(cacheKey, results, options);

            return results;
        }

        public async Task<FullDashboardDTO> GetFullDashboardDataAsync(string cityName)
        {
            var city = ParseCity(cityName);

            var cacheKey = $"FullDashboard_{city}";
            if (_cache.TryGetValue(cacheKey, out FullDashboardDTO cached))
            {
                return cached;
            }

            // Sequential, not concurrent: both calls share the same scoped DbContext,
            // and EF Core throws if two operations run on it at the same time.
            var timeline = await _propertyDataRepository.GetTimelineByCityAsync(city.ToString());
            var results = await GetCachedRealEstateDataAsync(city.ToString());

            var validOffers = GetValidOffers(results);
            var validWithDistrict = validOffers
                .Where(x => !string.IsNullOrWhiteSpace(x.Location?.District))
                .ToList();

            var districtGroups = validWithDistrict
                .GroupBy(x => x.Location.District)
                .Where(g => g.Count() >= MinOffersPerDistrict)
                .ToList();

            var districtMedians = districtGroups.ToDictionary(
                g => g.Key,
                g => CalculateMedian(g.Select(x => x.PricePerMeter)));

            var charts = BuildDashboardCharts(results, validOffers, timeline);
            var insights = BuildMarketInsights(results, validOffers, validWithDistrict, districtGroups, districtMedians);
            var mapPoints = BuildMapPoints(results);

            var dto = new FullDashboardDTO
            {
                Charts = charts,
                Insights = insights,
                MapPoints = mapPoints
            };

            _cache.Set(cacheKey, dto, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = CacheDuration
            });

            return dto;
        }

        private static DashboardChartsDTO BuildDashboardCharts(List<SearchData> results, List<SearchData> validOffers, List<TimelineGroup> timeline)
        {
            var dto = new DashboardChartsDTO
            {
                Timeline = timeline
                    .Select(x => new TimelinePointDTO
                    {
                        Date = x.Date.ToString("dd-MM-yyyy"),
                        AvgPricePerMeter = Math.Round(x.AvgPricePerMeter, 1),
                        AvgPrice = Math.Round(x.AvgPrice, 1),
                        Count = x.Count
                    })
                    .ToList()
            };

            if (validOffers.Any())
            {
                dto.Summary = new DashboardSummaryDTO
                {
                    TotalOffers = results.Count,
                    MedianPrice = Math.Round(CalculateMedian(validOffers.Select(x => x.Price)), 0),
                    MedianPricePerMeter = Math.Round(CalculateMedian(validOffers.Select(x => x.PricePerMeter)), 0),
                    MedianArea = Math.Round(CalculateMedian(validOffers.Select(x => x.Area)), 1),
                    PrivateOffersPercent = Math.Round(100.0 * results.Count(x => x.Private) / results.Count, 1),
                    LastUpdated = timeline.Count > 0 ? timeline[^1].Date.ToString("dd-MM-yyyy") : string.Empty
                };

                dto.PricePerMeterHistogram = BuildPricePerMeterHistogram(validOffers);
                dto.DistrictPrices = BuildDistrictPrices(validOffers);
                dto.MarketSplit = BuildSplit(validOffers, x => x.Market);
                dto.BuildingTypeSplit = BuildSplit(validOffers, x => x.BuildingType);
            }

            return dto;
        }

        private static MarketInsightsDTO BuildMarketInsights(
            List<SearchData> results,
            List<SearchData> validOffers,
            List<SearchData> validWithDistrict,
            List<IGrouping<string, SearchData>> districtGroups,
            Dictionary<string, double> districtMedians)
        {
            // no valid offers -> Min()/Max() below would throw and results.Count would divide by zero
            if (validOffers.Count == 0)
                return new MarketInsightsDTO();

            var pricesPerMeter = validOffers.Select(x => x.PricePerMeter).ToList();

            var insights = new MarketInsightsDTO
            {
                TotalOffers = results.Count,
                MedianPrice = Math.Round(CalculateMedian(validOffers.Select(x => x.Price)), 0),
                MedianPricePerMeter = Math.Round(CalculateMedian(pricesPerMeter), 0),
                MinPricePerMeter = Math.Round(pricesPerMeter.Min(), 0),
                MaxPricePerMeter = Math.Round(pricesPerMeter.Max(), 0),
                MedianArea = Math.Round(CalculateMedian(validOffers.Select(x => x.Area)), 1),
                PrivateOffersPercent = Math.Round(100.0 * results.Count(x => x.Private) / results.Count, 1),
                OffersBySource = results
                    .GroupBy(x => x.WebName)
                    .Select(g => new SourceCountDTO { Source = g.Key.ToString(), Count = g.Count() })
                    .OrderByDescending(x => x.Count)
                    .ToList()
            };

            insights.Districts = districtGroups
                .Select(g => new DistrictPriceDTO
                {
                    District = g.Key,
                    MedianPricePerMeter = Math.Round(districtMedians[g.Key], 0),
                    Count = g.Count()
                })
                .OrderBy(x => x.MedianPricePerMeter)
                .ToList();

            insights.BestDeals = districtGroups
                .SelectMany(g => g)
                .Select(x => new BestDealDTO
                {
                    Title = x.Title,
                    Url = x.Url,
                    District = x.Location.District,
                    Price = x.Price,
                    Area = x.Area,
                    Floor = x.Floor,
                    Market = x.Market,
                    PricePerMeter = Math.Round(x.PricePerMeter, 0),
                    DistrictMedianPricePerMeter = Math.Round(districtMedians[x.Location.District], 0),
                    BelowMedianPercent = Math.Round(
                        100.0 * (districtMedians[x.Location.District] - x.PricePerMeter) / districtMedians[x.Location.District], 1),
                    Source = x.WebName.ToString()
                })
                .Where(d => d.BelowMedianPercent > 0 && d.BelowMedianPercent <= MaxBelowMedianPercent)
                .GroupBy(d => new { d.Price, d.Area, d.Floor }) // same offer posted on several portals
                .Select(g => g.First())
                .OrderByDescending(d => d.BelowMedianPercent)
                .Take(10)
                .ToList();

            return insights;
        }

        private static List<MapPointDTO> BuildMapPoints(List<SearchData> results)
        {
            var points = results
                .Where(x => x.Location != null && x.Location.Lat != 0 && x.Location.Lon != 0)
                .Select(x => new MapPointDTO
                {
                    Url = x.Url,
                    Title = x.Title,
                    Price = x.Price,
                    PricePerMeter = x.PricePerMeter,
                    Floor = x.Floor,
                    Market = x.Market,
                    BuildingType = x.BuildingType,
                    Area = x.Area,
                    Private = x.Private,
                    Location = new MapLocationDTO
                    {
                        Lat = x.Location.Lat,
                        Lon = x.Location.Lon,
                        District = x.Location.District
                    }
                })
                .ToList();

            if (points.Count == 0)
                return points;

            var values = points.Select(p => p.PricePerMeter).Where(v => v > 0).ToList();
            if (values.Count == 0)
                return points;

            var minVal = values.Min();
            var maxVal = values.Max();
            var range = maxVal - minVal;

            foreach (var p in points)
            {
                // clamp: offers with PricePerMeter <= 0 pass the coords filter but sit below minVal
                var ratio = Math.Clamp(range > 0 ? (p.PricePerMeter - minVal) / range : 0.5, 0, 1);
                var r = (int)(255 * ratio);
                var b = (int)(255 * (1 - ratio));
                p.Color = $"rgb({r},0,{b})";
            }

            return points;
        }

        public async Task<List<TimelinePriceDTO>> GetTimelinePrice(string cityName)
        {
            var city = ParseCity(cityName);

            // grouped and sorted in SQL - avoids loading every property row into memory
            var groupedData = await _propertyDataRepository.GetTimelineByCityAsync(city.ToString());

            return groupedData
                .Select(x => new TimelinePriceDTO
                {
                    AddedDate = x.Date.ToString("dd-MM-yyyy"),
                    AvgPrice = Math.Round(x.AvgPrice, 1),
                    AvgPricePerMeter = Math.Round(x.AvgPricePerMeter, 1),
                    Count = x.Count
                })
                .ToList();
        }

        // Thin wrapper kept for API compatibility - the app itself calls getFullDashboard.
        public async Task<DashboardChartsDTO> GetDashboardCharts(string cityName)
        {
            return (await GetFullDashboardDataAsync(cityName)).Charts;
        }

        private static List<HistogramBinDTO> BuildPricePerMeterHistogram(List<SearchData> validOffers)
        {
            const double binSize = 1000;

            // clip to 1st-99th percentile so scraper outliers don't stretch the axis
            var sorted = validOffers.Select(x => x.PricePerMeter).OrderBy(x => x).ToList();
            var low = sorted[(int)(sorted.Count * 0.01)];
            var high = sorted[Math.Min(sorted.Count - 1, (int)(sorted.Count * 0.99))];

            var bins = sorted
                .Where(p => p >= low && p <= high)
                .GroupBy(p => Math.Ceiling(p / binSize) * binSize)
                .OrderBy(g => g.Key)
                .Select(g => new HistogramBinDTO
                {
                    Label = $"{(g.Key - binSize) / 1000:0.#}-{g.Key / 1000:0.#}k",
                    Count = g.Count()
                })
                .ToList();

            return bins;
        }

        private static List<DistrictPriceDTO> BuildDistrictPrices(List<SearchData> validOffers)
        {
            const int maxDistricts = 10;

            return GroupByDistrict(validOffers)
                .Select(g => new DistrictPriceDTO
                {
                    District = g.Key,
                    MedianPricePerMeter = Math.Round(CalculateMedian(g.Select(x => x.PricePerMeter)), 0),
                    Count = g.Count()
                })
                .OrderByDescending(x => x.MedianPricePerMeter)
                .Take(maxDistricts)
                .ToList();
        }

        // Price/PricePerMeter/Area are all > 0: filters out scraper rows with missing or garbage values.
        private static List<SearchData> GetValidOffers(List<SearchData> results) =>
            results.Where(x => x.Price > 0 && x.PricePerMeter > 0 && x.Area > 0).ToList();

        // Groups valid offers by district, dropping districts with too few offers to trust the median.
        private static List<IGrouping<string, SearchData>> GroupByDistrict(List<SearchData> validOffers) =>
            validOffers
                .Where(x => !string.IsNullOrWhiteSpace(x.Location?.District))
                .GroupBy(x => x.Location.District)
                .Where(g => g.Count() >= MinOffersPerDistrict)
                .ToList();

        private static List<SplitSliceDTO> BuildSplit(List<SearchData> validOffers, Func<SearchData, string> keySelector)
        {
            return validOffers
                .GroupBy(x => string.IsNullOrWhiteSpace(keySelector(x)) ? "Unknown" : keySelector(x))
                .Select(g => new SplitSliceDTO
                {
                    Name = g.Key,
                    Count = g.Count(),
                    MedianPricePerMeter = Math.Round(CalculateMedian(g.Select(x => x.PricePerMeter)), 0)
                })
                .OrderByDescending(x => x.Count)
                .ToList();
        }

        // Thin wrapper kept for API compatibility - the app itself calls getFullDashboard.
        public async Task<MarketInsightsDTO> GetMarketInsights(string cityName)
        {
            return (await GetFullDashboardDataAsync(cityName)).Insights;
        }

        // Thin wrapper kept for API compatibility - the app itself calls getFullDashboard.
        public async Task<List<MapPointDTO>> GetMapPoints(string cityName)
        {
            return (await GetFullDashboardDataAsync(cityName)).MapPoints;
        }

        private static double CalculateMedian(IEnumerable<double> values)
        {
            var sorted = values.OrderBy(v => v).ToList();
            int count = sorted.Count;

            if (count == 0)
                return 0;

            if (count % 2 == 1)
                return sorted[count / 2];

            return (sorted[(count / 2) - 1] + sorted[count / 2]) / 2.0;
        }

        public async Task<RealEstateStatistics> GetDataWithStatistics()
        {
            var response = await _realEstate.GetDataAsync(CityEnum.Krakow.ToString());
            return CalculateStatistics(response.Data);
        }

        private RealEstateStatistics CalculateStatistics(List<SearchData> data)
        {
            // Average() throws InvalidOperationException on an empty list - an empty DB result
            // should surface as zeroed statistics, not HTTP 500.
            if (data == null || data.Count == 0)
                return new RealEstateStatistics();

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
            var response = await _realEstate.GetDataAsync(CityEnum.Krakow.ToString());

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
            // Average() throws InvalidOperationException on an empty list - an empty DB result
            // should surface as zeroed statistics, not HTTP 500.
            if (data == null || data.Count == 0)
                return new RealEstateStatistics();

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
            // Canonicalize before caching: the raw string becomes an IMemoryCache key, so any
            // garbage/differently-cased value would add a permanent cache entry plus a DB query.
            var parsedCity = ParseCity(city);

            var results = await GetCachedRealEstateDataAsync(parsedCity.ToString());
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
            // Canonicalize before caching: the raw string becomes an IMemoryCache key, so any
            // garbage/differently-cased value would add a permanent cache entry plus a DB query.
            var parsedCity = ParseCity(city);

            var results = await GetCachedRealEstateDataAsync(parsedCity.ToString());

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

    }
}
