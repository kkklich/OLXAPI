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

        public async Task<QueryData> GetDefaultResponse()
        {
            var realEstateQuery = ConstantHelper.OLXAPI + ConstantHelper.OLXRealEstate;


            var rawResponse = await _httpClient.GetRaw(realEstateQuery);

            var result = await rawResponse.Content.ReadAsStringAsync();
            //var queryParams = $"?paramsJSON={paramsJSON}&applicationInstanceId={applicationInstanceId}";

            var data = JsonConvert.DeserializeObject<QueryData>(result);


            return data;
        }
    }
}
