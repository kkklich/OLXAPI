using AF_mobile_web_api.DTO;
using AF_mobile_web_api.DTO.Enums;
using AF_mobile_web_api.Filters;
using AF_mobile_web_api.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;

namespace AF_mobile_web_api.Controllers;

[ApiController]
[Route("api/RealEstate/olx")]
[RequireScrapeApiKey]
public sealed class OlxPreviewController : ControllerBase
{
    private readonly IOLXAPIService _olx;
    private readonly ILogger<OlxPreviewController> _logger;

    public OlxPreviewController(IOLXAPIService olx, ILogger<OlxPreviewController> logger)
    {
        _olx = olx;
        _logger = logger;
    }

    /// <summary>Downloads one page from OLX and returns it without saving to the database.</summary>
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType(typeof(OlxPreviewResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(
        [FromQuery] string city = "Krakow",
        [FromQuery] int priceFrom = 50_000,
        [FromQuery] int priceTo = 10_000_000,
        [FromQuery] int offset = 0,
        [FromQuery] int limit = 40)
    {
        if (!CityExtensions.TryParseName(city, out var parsedCity))
            return BadRequest(new { message = "Podgląd OLX obsługuje miasta Krakow i Katowice." });

        try
        {
            return Ok(await _olx.GetPreviewAsync(parsedCity, priceFrom, priceTo, offset, limit));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "OLX preview request failed for {City}", city);
            return StatusCode(StatusCodes.Status502BadGateway, new
            {
                message = "Nie udało się pobrać danych z OLX.",
                upstreamStatusCode = (int?)ex.StatusCode,
                savedToDatabase = false
            });
        }
        catch (OperationCanceledException ex) when (!HttpContext.RequestAborted.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "OLX preview request timed out for {City}", city);
            return StatusCode(StatusCodes.Status504GatewayTimeout,
                new { message = "Przekroczono czas oczekiwania na OLX.", savedToDatabase = false });
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "OLX preview returned invalid JSON for {City}", city);
            return StatusCode(StatusCodes.Status502BadGateway,
                new { message = "OLX zwrócił nieprawidłowe dane.", savedToDatabase = false });
        }
    }
}
