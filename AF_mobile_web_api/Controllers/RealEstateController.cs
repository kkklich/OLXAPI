using Microsoft.AspNetCore.Mvc;
using AF_mobile_web_api.Services;

namespace AF_mobile_web_api.Controllers
{ 
    [Route("api/[controller]")]
    public class RealEstateController : ControllerBase
    {
        private readonly RealEstateServices _realEstate;
        private readonly StatisticServices _statisticServices;
        public RealEstateController(RealEstateServices realEstate, StatisticServices statisticServices)
        {
            _realEstate = realEstate;
            _statisticServices = statisticServices;
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
