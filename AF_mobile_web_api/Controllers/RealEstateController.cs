using Microsoft.AspNetCore.Mvc;
using AF_mobile_web_api.Services.Interfaces;

namespace AF_mobile_web_api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class RealEstateController : ControllerBase
    {
        private readonly IRealEstateServices _realEstate;
        private readonly IStatisticServices _statisticServices;
        private readonly IMorizonApiService _morizonApiService;
        private readonly INieruchomosciOnlineService _nieruchomosciOnlineService;
        public RealEstateController(IRealEstateServices realEstate, IStatisticServices statisticServices, IMorizonApiService morizonApiService, INieruchomosciOnlineService nieruchomosciOnlineService)
        {
            _realEstate = realEstate;
            _statisticServices = statisticServices;
            _morizonApiService = morizonApiService;
            _nieruchomosciOnlineService = nieruchomosciOnlineService;
        }

        [HttpGet("nieruchomosciOnline")]
        public async Task<IActionResult> getNieruchomosciOnlineAPI()
        {
            var result = await _nieruchomosciOnlineService.GetAllPagesAsync();
            return Ok(result);           
        }
        
        [HttpGet("morizon")]
        public async Task<IActionResult> getMorizonAPI()
        {
            var result = await _morizonApiService.GetPropertyListingDataAsync();
            return Ok(result);           
        }

        [HttpGet("loadDataMarkeplaces")]
        public async Task<IActionResult> LoadDataMarkeplaces()
        {
            var result = await _realEstate.LoadDataMarkeplacesAsync();
            return Ok(result);            
        }
        
        [HttpGet("getdataForManyCities")]
        public async Task<IActionResult> GetdataForManyCities()
        {
            var result = await _realEstate.GetdataForManyCitiesAsync();
            return Ok(result);          
        }
        
        [HttpGet("getRealEstate/{city}")]
        public async Task<IActionResult> GetDefaultRealEstate(string city  = "Krakow")
        {
            var result = await _realEstate.GetDataAsync(city);
            return Ok(result);            
        }
                
        [HttpGet("getUniqueOffers")]
        public async Task<IActionResult> getUniqueOffers()
        {
            var result = await _realEstate.GetUniqueOffertsAsync();
            return Ok(result);         
        }
                
        [HttpGet("RealEstateStats")]
        public async Task<IActionResult> GetRealEstateStats()
        {
            var result = await _statisticServices.GetDataWithStatistics();
            return Ok(result);         
        }
        
        [HttpGet("RealEstateGropuBy")]
        public async Task<IActionResult> RealEstateGropuBy([FromQuery] string groupBy)
        {
            var result = await _statisticServices.GetDataWithGroupStatistics(groupBy);
            return Ok(result);            
        }
        
        
        [HttpGet("getTimelinePrice/{city}")]
        public async Task<IActionResult> GetTimelinePrice(string city)
        {
            var result = await _statisticServices.GetTimelinePrice(city);
            return Ok(result);            
        }

        [HttpGet("getGroupedStatistics/{groupBy}/{city}")]
        public async Task<IActionResult> getGroupedStatistics(string groupBy, string city)
        {
            var result = await _statisticServices.GetBarChartData(city, groupBy);
            return Ok(result);         
        }

        [HttpGet("filterByParameter/{groupBy}/{city}/{parameter}")]
        public async Task<IActionResult> FilterByParameter(string groupBy,  string city,  string parameter)
        {
            var chart = await _statisticServices.FilterByParameter(groupBy, city, parameter);
            return Ok(chart);            
        }
    }
}
