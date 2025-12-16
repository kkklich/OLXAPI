using Microsoft.AspNetCore.Mvc;
using AF_mobile_web_api.Services;
using AF_mobile_web_api.DTO;

namespace AF_mobile_web_api.Controllers
{ 
    [Route("api/[controller]")]
    public class RealEstateController : ControllerBase
    {
        private readonly RealEstateServices _realEstate;
        private readonly StatisticServices _statisticServices;
        private readonly MorizonApiService _morizonApiService;
        private readonly NieruchomosciOnlineService _nieruchomosciOnlineService;
        public RealEstateController(RealEstateServices realEstate, StatisticServices statisticServices, MorizonApiService morizonApiService, NieruchomosciOnlineService nieruchomosciOnlineService)
        {
            _realEstate = realEstate;
            _statisticServices = statisticServices;
            _morizonApiService = morizonApiService;
            _nieruchomosciOnlineService = nieruchomosciOnlineService;
        }

        [HttpGet("getNieruchomosciOnlineAPI")]
        public async Task<IActionResult> getNieruchomosciOnlineAPI()
        {
            try
            {
                var result = await _nieruchomosciOnlineService.GetAllPagesAsync();
                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }
        
        [HttpGet("getMorizonAPI")]
        public async Task<IActionResult> getMorizonAPI()
        {
            try
            {
                var result = await _morizonApiService.GetPropertyListingDataAsync();
                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpGet("loadDataMarkeplaces")]
        public async Task<IActionResult> LoadDataMarkeplaces()
        {
            try
            {
                var result = await _realEstate.LoadDataMarkeplaces();
                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }
        
        [HttpGet("getdataForManyCities")]
        public async Task<IActionResult> GetdataForManyCities()
        {
            try
            {
                var result = await _realEstate.GetdataForManyCities();
                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }
        
        [HttpGet("getRealEstate/{city}")]
        public async Task<IActionResult> GetDefaultRealEstate(string city  = "Krakow")
        {
            try
            {
                var result = await _realEstate.GetData(city);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }
        
        
        [HttpGet("getUniqueOffers")]
        public async Task<IActionResult> getUniqueOffers()
        {
            try
            {
                var result = await _realEstate.GetUniqueOfferts();
                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }
        
        
        [HttpGet("RealEstateStats")]
        public async Task<IActionResult> GetRealEstateStats()
        {
            try
            {
                var result = await _statisticServices.GetDataWithStatistics();
                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }
        
        [HttpGet("RealEstateGropuBy")]
        public async Task<IActionResult> RealEstateGropuBy([FromQuery] string groupBy)
        {
            try
            {
                var result = await _statisticServices.GetDataWithGroupStatistics(groupBy);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }
        
        
        [HttpGet("getTimelinePrice/{city}")]
        public async Task<IActionResult> GetTimelinePrice(string city)
        {
            try
            {
                var result = await _statisticServices.GetTimelinePrice(city);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpGet("getGroupedStatistics/{groupBy}/{city}")]
        public async Task<IActionResult> getGroupedStatistics(string groupBy, string city)
        {
            try
            {
                var result = await _statisticServices.GetBarChartData(city, groupBy);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpGet("filterByParameter/{groupBy}/{city}/{parameter}")]
        public async Task<IActionResult> FilterByParameter(string groupBy,  string city,  string parameter)
        {
            try
            {
                var chart = await _statisticServices.FilterByParameter(groupBy, city, parameter);
                return Ok(chart);
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

    }
}
