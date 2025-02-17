using Microsoft.AspNetCore.Mvc;
using AF_mobile_web_api.Services;

namespace AF_mobile_web_api.Controllers
{ 
    [Route("api/[controller]")]
    public class RealEstateController : ControllerBase
    {
        private readonly RealEstateServices _realEstate;
        public RealEstateController(RealEstateServices realEstate)
        {
            _realEstate = realEstate;
        }

        [HttpGet("test")]
        public async Task<IActionResult> test()
        {
            return Ok("work");
        }


        [HttpGet("defaultRealEstate")]
        public async Task<IActionResult> GetDefaultRealEstate([FromQuery] int? floorSelect, [FromQuery] int? minPrice, [FromQuery] int? maxPrice, [FromQuery] int? minArea, [FromQuery] int? maxArea)
        {
            try
            {
                var result = await _realEstate.GetMoreResponse(floorSelect, minPrice, maxPrice, minArea, maxArea);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }
    }
}
