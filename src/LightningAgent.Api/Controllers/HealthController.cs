using LightningAgent.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace LightningAgent.Api.Controllers;

[ApiController]
[Route("api/health")]
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

    [HttpGet]
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

    [HttpGet("detailed")]
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
