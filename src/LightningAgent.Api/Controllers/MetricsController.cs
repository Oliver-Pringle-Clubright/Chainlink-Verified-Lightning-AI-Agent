using System.Text;
using LightningAgent.Core.Interfaces.Services;
using LightningAgent.Core.Models.Metrics;
using Microsoft.AspNetCore.Mvc;

namespace LightningAgent.Api.Controllers;

/// <summary>
/// Exposes application metrics in JSON and Prometheus formats.
/// </summary>
[ApiController]
[Route("api/metrics")]
[Produces("application/json")]
public class MetricsController : ControllerBase
{
    private readonly IMetricsCollector _metrics;

    public MetricsController(IMetricsCollector metrics)
    {
        _metrics = metrics;
    }

    /// <summary>
    /// Returns current application metrics as JSON.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(AppMetrics), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public ActionResult<AppMetrics> GetMetrics()
    {
        var snapshot = _metrics.GetSnapshot();
        return Ok(snapshot);
    }

    /// <summary>
    /// Returns current application metrics in Prometheus text exposition format.
    /// </summary>
    [HttpGet("prometheus")]
    [Produces("text/plain")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public IActionResult GetPrometheusMetrics()
    {
        var m = _metrics.GetSnapshot();

        var sb = new StringBuilder();

        // Counters
        AppendCounter(sb, "lightning_agent_tasks_created_total", "Total tasks created", m.TasksCreated);
        AppendCounter(sb, "lightning_agent_tasks_completed_total", "Total tasks completed", m.TasksCompleted);
        AppendCounter(sb, "lightning_agent_tasks_failed_total", "Total tasks failed", m.TasksFailed);
        AppendCounter(sb, "lightning_agent_milestones_verified_total", "Total milestones verified", m.MilestonesVerified);
        AppendCounter(sb, "lightning_agent_milestones_failed_total", "Total milestones failed", m.MilestonesFailed);
        AppendCounter(sb, "lightning_agent_payments_processed_total", "Total payments processed", m.PaymentsProcessed);
        AppendCounter(sb, "lightning_agent_total_sats_paid", "Total satoshis paid", m.TotalSatsPaid);
        AppendCounter(sb, "lightning_agent_api_requests_total", "Total API requests", m.ApiRequestsTotal);
        AppendCounter(sb, "lightning_agent_api_errors_5xx_total", "Total 5xx API errors", m.ApiErrors5xx);

        // Gauges
        AppendGauge(sb, "lightning_agent_active_tasks", "Number of currently active tasks", m.ActiveTasks);
        AppendGauge(sb, "lightning_agent_active_agents", "Number of currently active agents", m.ActiveAgents);
        AppendGauge(sb, "lightning_agent_queue_depth", "Current task queue depth", m.QueueDepth);
        AppendGauge(sb, "lightning_agent_btc_usd_price", "Current BTC/USD price", m.BtcUsdPrice);

        // Histograms (simplified as gauges showing averages)
        AppendGauge(sb, "lightning_agent_avg_request_duration_ms", "Average request duration in milliseconds", m.AvgRequestDurationMs);
        AppendGauge(sb, "lightning_agent_avg_verification_duration_ms", "Average verification duration in milliseconds", m.AvgVerificationDurationMs);

        return Content(sb.ToString(), "text/plain; charset=utf-8");
    }

    private static void AppendCounter(StringBuilder sb, string name, string help, long value)
    {
        sb.AppendLine($"# HELP {name} {help}");
        sb.AppendLine($"# TYPE {name} counter");
        sb.AppendLine($"{name} {value}");
    }

    private static void AppendGauge(StringBuilder sb, string name, string help, double value)
    {
        sb.AppendLine($"# HELP {name} {help}");
        sb.AppendLine($"# TYPE {name} gauge");
        sb.AppendLine($"{name} {value}");
    }

    private static void AppendGauge(StringBuilder sb, string name, string help, int value)
    {
        sb.AppendLine($"# HELP {name} {help}");
        sb.AppendLine($"# TYPE {name} gauge");
        sb.AppendLine($"{name} {value}");
    }
}
