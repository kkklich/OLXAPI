using Newtonsoft.Json;
using OLX_web_api.DTO;
using OLX_web_api.Helper;

namespace OLX_web_api.Services
{
    public class RealEstateServices
    {
        private readonly HTTPClientServices _httpClient;
        //public RealEstateServices()
        //{
        public RealEstateServices(HTTPClientServices httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<QueryData> GetDefaultResponse(int offset = 0, int limit = 40)
        {
            var realEstateQuery = ConstantHelper.OLXAPI;
            
            var queryParams = $"?offset={offset}&limit={limit}&category_id={ConstantHelper.RealEstateCategory}";
            realEstateQuery += queryParams;

            var rawResponse = await _httpClient.GetRaw(realEstateQuery);

            var result = await rawResponse.Content.ReadAsStringAsync();

            var data = JsonConvert.DeserializeObject<QueryData>(result);


            return data;
        }

        public async Task<QueryData> GetMoreResponse()
        {
            QueryData query = new QueryData();
            for (int i = 0; i < 2; i++)
            {
                var response = await GetDefaultResponse(i * 40, 40);
                query.Data.AddRange(response.Data);
            }

            return query;
        }
    }
}
