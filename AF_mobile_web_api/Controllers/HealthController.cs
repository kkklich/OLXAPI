using ApplicationDatabase;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;

namespace AF_mobile_web_api.Controllers
{
    /// <summary>
    /// Deploy-time smoke checks. EF opens its connection lazily on the first
    /// query, so a container with a missing or wrong connection string starts
    /// cleanly, passes a port-based health probe, and only fails once a real
    /// request reaches a DB-backed endpoint. These two endpoints make that
    /// failure visible immediately after a deploy instead.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class HealthController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly ILogger<HealthController> _logger;

        public HealthController(AppDbContext db, ILogger<HealthController> logger)
        {
            _db = db;
            _logger = logger;
        }

        /// <summary>Process liveness only - says nothing about the database.</summary>
        [HttpGet]
        public IActionResult Get() => Ok(new { status = "ok" });

        /// <summary>
        /// Opens a real connection to the database. The response deliberately
        /// carries no host, credentials or driver message: this endpoint is
        /// public whenever ScrapeApiKey is unset. The details go to the log,
        /// where "which host did it even try" is the question worth answering.
        /// </summary>
        [HttpGet("db")]
        public async Task<IActionResult> Db(CancellationToken ct)
        {
            var conn = _db.Database.GetDbConnection();
            // DataSource/Database are parsed out of the connection string and
            // hold no secret - unlike ConnectionString itself, which does.
            var host = conn.DataSource;
            var database = conn.Database;

            var sw = Stopwatch.StartNew();
            try
            {
                await _db.Database.OpenConnectionAsync(ct);
                sw.Stop();

                _logger.LogInformation(
                    "Health: database reachable at {Host}/{Database} ({ServerVersion}) in {ElapsedMs} ms",
                    host, database, conn.ServerVersion, sw.ElapsedMilliseconds);

                return Ok(new { status = "ok", database = "reachable", elapsedMs = sw.ElapsedMilliseconds });
            }
            catch (Exception ex)
            {
                sw.Stop();

                _logger.LogError(ex,
                    "Health: database UNREACHABLE. Tried host {Host}, database {Database}, failed after {ElapsedMs} ms. " +
                    "An empty or unset ConnectionStrings__ConnectionString makes this fall back to localhost.",
                    string.IsNullOrEmpty(host) ? "(none - empty connection string)" : host,
                    string.IsNullOrEmpty(database) ? "(none)" : database,
                    sw.ElapsedMilliseconds);

                return StatusCode(StatusCodes.Status503ServiceUnavailable,
                    new { status = "error", database = "unreachable", elapsedMs = sw.ElapsedMilliseconds });
            }
            finally
            {
                await _db.Database.CloseConnectionAsync();
            }
        }
    }
}
