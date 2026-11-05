using AF_mobile_web_api.DTO;
using AF_mobile_web_api.Filters;
using AF_mobile_web_api.Services;
using Microsoft.AspNetCore.Mvc;

namespace AF_mobile_web_api.Controllers;

[ApiController]
[Route("api/RealEstate/marketplaceListings")]
[RequireScrapeApiKey]
public sealed class MarketplaceListingsController(MarketplaceListingsService listings) : ControllerBase
{
    /// <summary>Live apartments for sale from OLX and/or Otodom, without database writes.</summary>
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType(typeof(MarketplaceListingsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    [ProducesResponseType(StatusCodes.Status504GatewayTimeout)]
    public async Task<IActionResult> Get([FromQuery] string city = "Krakow",
        [FromQuery] string source = "all", [FromQuery] int priceFrom = 50_000,
        [FromQuery] int priceTo = 10_000_000, [FromQuery] int page = 1,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await listings.GetAsync(city, source, priceFrom, priceTo, page, cancellationToken);
            if (result.Pages.All(p => p.Error != null))
                return StatusCode(result.Pages.All(p => p.Error?.Code == "timeout")
                    ? StatusCodes.Status504GatewayTimeout : StatusCodes.Status502BadGateway, result);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message, savedToDatabase = false });
        }
    }
}
