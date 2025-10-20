using Microsoft.AspNetCore.Mvc;
using AF_mobile_web_api.Services;

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
        
        [HttpGet("getRealEstate")]
        public async Task<IActionResult> GetDefaultRealEstate()
        {
            try
            {
                var result = await _realEstate.GetDataSave();
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
    }
}
