using LightningAgent.Core.Interfaces.Data;
using LightningAgent.Core.Models.Analytics;
using Microsoft.AspNetCore.Mvc;

namespace LightningAgent.Api.Controllers;

[ApiController]
[Route("api/analytics")]
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
