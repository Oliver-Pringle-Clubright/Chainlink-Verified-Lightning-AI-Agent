using LightningAgent.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace LightningAgent.Api.Controllers;

/// <summary>
/// Health check endpoints for liveness and detailed diagnostics.
/// </summary>
[ApiController]
[Route("api/health")]
[Produces("application/json")]
public class HealthController : ControllerBase
{
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly HealthCheckService _healthCheckService;

    public HealthController(
        SqliteConnectionFactory connectionFactory,
        HealthCheckService healthCheckService)
    {
        _connectionFactory = connectionFactory;
        _healthCheckService = healthCheckService;
    }

    /// <summary>
    /// Basic health check returning service status and database connectivity.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public IActionResult GetHealth()
    {
        string dbStatus;
        try
        {
            using var connection = _connectionFactory.CreateConnection();
            dbStatus = "connected";
        }
        catch
        {
            dbStatus = "disconnected";
        }

        return Ok(new
        {
            status = "healthy",
            database = dbStatus,
            timestamp = DateTime.UtcNow
        });
    }

    /// <summary>
    /// Detailed health check running all registered health checks and returning individual results.
    /// </summary>
    [HttpGet("detailed")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetDetailedHealth(CancellationToken ct)
    {
        var report = await _healthCheckService.CheckHealthAsync(ct);

        var entries = report.Entries.ToDictionary(
            e => e.Key,
            e => new
            {
                status = e.Value.Status.ToString(),
                description = e.Value.Description,
                duration = e.Value.Duration.TotalMilliseconds
            });

        string dbStatus;
        try
        {
            using var connection = _connectionFactory.CreateConnection();
            dbStatus = "connected";
        }
        catch
        {
            dbStatus = "disconnected";
        }

        var statusCode = report.Status == HealthStatus.Healthy
            ? StatusCodes.Status200OK
            : StatusCodes.Status503ServiceUnavailable;

        return StatusCode(statusCode, new
        {
            status = report.Status.ToString(),
            database = dbStatus,
            checks = entries,
            totalDuration = report.TotalDuration.TotalMilliseconds,
            timestamp = DateTime.UtcNow
        });
    }
}
