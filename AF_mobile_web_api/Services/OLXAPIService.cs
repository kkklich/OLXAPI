using AF_mobile_web_api.DTO;
using AF_mobile_web_api.DTO.Enums;
using AF_mobile_web_api.Helper;
using Newtonsoft.Json;

namespace AF_mobile_web_api.Services
{
    public class OLXAPIService
    {
        private readonly HTTPClientServices _httpClient;
        public OLXAPIService(HTTPClientServices httpClient)
        {
            _httpClient = httpClient;
        }

        public  async Task<List<SearchData>> GetAllOffersAsync(
       int categoryId,
       int? regionId,
       int? cityId,
       int priceFrom,
       int priceTo
       )
        {
            int priceBucket = 50000;
            int offsetStart = 0;

            var buckets = new List<(int from, int to)>();
            for (int from = priceFrom; from <= priceTo; from += priceBucket)
            {
                int to = Math.Min(from + priceBucket - 1, priceTo);
                buckets.Add((from, to)); // one bucket per task [web:26]
            }

            var tasks = new List<Task<List<SearchData>>>(buckets.Count);
            foreach (var b in buckets)
            {
                tasks.Add(FetchRangeAllPagesAsync(
                    categoryId, regionId, cityId, b.from, b.to, offsetStart)); // one task per bucket 
            }

            var results = await Task.WhenAll(tasks).ConfigureAwait(false); // run all and wait 
            var all = new List<SearchData>();
            foreach (var chunk in results)
                all.AddRange(chunk); // flatten
            return all; // merged list
        }

        // Fetch all pages for a single price range using offset/limit
        private  async Task<List<SearchData>> FetchRangeAllPagesAsync(            
            int categoryId,
            int? regionId,
            int? cityId,
            int priceFrom,
            int priceTo,
            int offsetStart
         )
        {
            var results = new List<SearchData>();
            var offset = offsetStart;
            const int limit = 40; //olx API limit 40

            // Do-while style loop to ensure at least one request and then continue while page returns items
            while (true)
            {
                var url = BuildOffersUrl(categoryId, regionId, cityId, priceFrom, priceTo, offset, limit);

                var rawResponse = await _httpClient.GetRaw(url);
                var result = await rawResponse.Content.ReadAsStringAsync();
                var data = JsonConvert.DeserializeObject<QueryData>(result);

                var convertedData = ExtractListOfParameters(data.Data);               
                
                results.AddRange(convertedData);
                if (convertedData.Count < limit - 2) break;

                offset += limit; 
            }

            return results; // all pages for range
        }

        private  string BuildOffersUrl(
            int categoryId,
            int? regionId,
            int? cityId,
            int priceFrom,
            int priceTo,
            int offset,
            int limit)
        {
            // OLX query uses URL-encoded keys for price filters: filter_float_price:from / :to, commonly seen percent-encoded [web:12]
            // Keys are encoded as filter_float_price%3Afrom and filter_float_price%3Ato on the wire [web:12]
            var qp = new List<string>
        {
            $"offset={offset}",
            $"limit={limit}",
            $"category_id={categoryId}",
            $"filter_float_price%3Afrom={priceFrom}",
            $"filter_float_price%3Ato={priceTo}"
        }; // base params [web:12]

            if (regionId.HasValue) qp.Add($"region_id={regionId.Value}"); // location filter [web:12]
            if (cityId.HasValue) qp.Add($"city_id={cityId.Value}"); // location filter [web:12]

            var query = string.Join("&", qp); // compose query string [web:12]
            return $"{ConstantHelper.OLXAPI}?{query}"; // final URL
        }

        public async Task<MarketplaceSearch> GetOLXResponse(CityEnum city = CityEnum.Krakow)
        {
            MarketplaceSearch searchedData = new MarketplaceSearch();

            var results = await GetAllOffersAsync(
                categoryId: ConstantHelper.RealEstateCategory, 
                regionId: city.ToEncodedRegionOLXString(),
                cityId: city.ToEncodedOLXString(),
                priceFrom: 50_000,
                priceTo: 999_000              
            );
            
            searchedData.Data.AddRange(results);            

            searchedData.Data = searchedData.Data
                .DistinctBy(x => x.Id)
                .OrderBy(x => x.PricePerMeter)
                .ToList();

            searchedData.TotalCount = searchedData.Data.Count;

            return searchedData;

        }          

        private List<SearchData> ExtractListOfParameters(List<Data> responses)
        {
            List<SearchData> result = new List<SearchData>();
            foreach (var response in responses)
            {
                if (response.Title.Contains("TBS"))
                    continue;
                var extractedObject = ExtractParameter(response);
                result.Add(extractedObject);
            }

            return result;
        }

        private SearchData ExtractParameter(Data data)
        {
            var record = MapParamsToSearchData(data.Params);

            record.Id = data.Id;
            record.Url = data.Url;
            record.Title = data.Title;
            record.CreatedTime = data.created_time;
            record.Private = !data.Business;
            record.Description = data.Description;
            record.Location = new LocationPlace()
            {
                City = data?.Location?.City?.Name ?? string.Empty,
                District = data?.Location?.District?.Name ?? string.Empty,
                Lat = data?.Map?.Lat ?? 0,
                Lon = data?.Map?.Lon ?? 0
            };
            record.WebName = WebName.OLX;

            //record.Photos.AddRange(data.Photos.Select(p => new Photos
            //{
            //    Id = p.Id,
            //    Filename = p.Filename,
            //    Width = p.Width,
            //    Height = p.Height,
            //    Link = p.Link
            //}));

            return record;
        }

        private SearchData MapParamsToSearchData(List<Param> parameters)
        {
            var data = new SearchData();
            foreach (var param in parameters)
            {
                switch (param.Key)
                {
                    case ConstantHelper.Price:
                        if (param.Value?.Label != null)
                            data.Price = ParseNumberToDouble(param.Value?.Label);
                        break;
                    case ConstantHelper.PricePerMeter:
                        data.PricePerMeter = ParseNumberToDouble(param?.Value?.Key ?? string.Empty);

                        if (data.PricePerMeter == 0)
                            data.PricePerMeter = data.Price / data.Area;
                        break;
                    case ConstantHelper.FloorSelect:
                        if (int.TryParse(param.Value?.Label, out var floor))
                            data.Floor = floor;
                        break;
                    case ConstantHelper.Market:
                        data.Market = param.Value?.Label ?? string.Empty;
                        break;
                    case ConstantHelper.BuildType:
                        data.BuildingType = param.Value?.Label;
                        break;
                    case ConstantHelper.Area:
                        data.Area = ParseNumberToDouble(param?.Value?.Key ?? string.Empty);
                        break;
                }
            }

            return data;
        }

        private double ParseNumberToDouble(string priceString)
        {
            if (string.IsNullOrWhiteSpace(priceString))
                return 0;

            var cleaned = priceString.Replace("zł", "", StringComparison.OrdinalIgnoreCase)
                                     .Replace(" ", "")
                                     .Replace(",", ".", StringComparison.OrdinalIgnoreCase)
                                     .Trim();

            if (double.TryParse(cleaned, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double result))
            {
                return result;
            }

            return 0;
        }
    }
}
