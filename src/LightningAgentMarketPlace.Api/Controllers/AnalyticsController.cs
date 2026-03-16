using LightningAgentMarketPlace.Core.Interfaces.Data;
using LightningAgentMarketPlace.Core.Models.Analytics;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;

namespace LightningAgentMarketPlace.Api.Controllers;

/// <summary>
/// Provides system analytics: summary, per-agent stats, and timeline data.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/analytics")]
[Route("api/v{version:apiVersion}/analytics")]
[Produces("application/json")]
public class AnalyticsController : ControllerBase
{
    private readonly IAnalyticsRepository _analyticsRepository;
    private readonly ILogger<AnalyticsController> _logger;

    public AnalyticsController(
        IAnalyticsRepository analyticsRepository,
        ILogger<AnalyticsController> logger)
    {
        _analyticsRepository = analyticsRepository;
        _logger = logger;
    }

    /// <summary>
    /// Task counts by status, average completion time, total sats spent.
    /// </summary>
    [HttpGet("summary")]
    [ProducesResponseType(typeof(SystemSummary), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<SystemSummary>> GetSummary(CancellationToken ct)
    {
        _logger.LogDebug("Fetching analytics summary");
        var summary = await _analyticsRepository.GetSystemSummaryAsync(ct);
        return Ok(summary);
    }

    /// <summary>
    /// Per-agent statistics: tasks completed, avg score, total earned.
    /// </summary>
    [HttpGet("agents")]
    [ProducesResponseType(typeof(IReadOnlyList<AgentStats>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<IReadOnlyList<AgentStats>>> GetAgentStats(CancellationToken ct)
    {
        _logger.LogDebug("Fetching agent analytics");
        var stats = await _analyticsRepository.GetAgentStatsAsync(ct);
        return Ok(stats);
    }

    /// <summary>
    /// Daily task counts for the specified number of days (default 30).
    /// </summary>
    [HttpGet("timeline")]
    [ProducesResponseType(typeof(IReadOnlyList<TimelineEntry>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<IReadOnlyList<TimelineEntry>>> GetTimeline(
        [FromQuery] int days = 30,
        CancellationToken ct = default)
    {
        if (days < 1) days = 1;
        if (days > 365) days = 365;

        _logger.LogDebug("Fetching analytics timeline for {Days} days", days);
        var timeline = await _analyticsRepository.GetTimelineAsync(days, ct);
        return Ok(timeline);
    }
}
