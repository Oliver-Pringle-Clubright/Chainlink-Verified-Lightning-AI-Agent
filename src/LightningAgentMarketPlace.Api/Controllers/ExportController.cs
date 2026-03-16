using System.Text;
using System.Text.Json;
using LightningAgentMarketPlace.Api.Helpers;
using LightningAgentMarketPlace.Core.Interfaces.Data;
using LightningAgentMarketPlace.Core.Models;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;

namespace LightningAgentMarketPlace.Api.Controllers;

/// <summary>
/// Admin-only endpoints for exporting data as JSON or CSV.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/admin/export")]
[Route("api/v{version:apiVersion}/admin/export")]
[Produces("application/json")]
public class ExportController : ControllerBase
{
    private readonly ITaskRepository _taskRepository;
    private readonly IPaymentRepository _paymentRepository;
    private readonly IAgentRepository _agentRepository;
    private readonly IAgentReputationRepository _reputationRepository;
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ILogger<ExportController> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public ExportController(
        ITaskRepository taskRepository,
        IPaymentRepository paymentRepository,
        IAgentRepository agentRepository,
        IAgentReputationRepository reputationRepository,
        IAuditLogRepository auditLogRepository,
        ILogger<ExportController> logger)
    {
        _taskRepository = taskRepository;
        _paymentRepository = paymentRepository;
        _agentRepository = agentRepository;
        _reputationRepository = reputationRepository;
        _auditLogRepository = auditLogRepository;
        _logger = logger;
    }

    /// <summary>
    /// Export all tasks as JSON or CSV.
    /// </summary>
    [HttpGet("tasks")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ExportTasks(
        [FromQuery] string format = "json",
        CancellationToken ct = default)
    {
        if (!AuthorizationHelper.IsAdminOrDevMode(HttpContext))
            return Forbid();

        var tasks = await _taskRepository.GetPagedAsync(0, int.MaxValue, null, ct);

        if (string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase))
        {
            var csv = BuildTasksCsv(tasks);
            return File(Encoding.UTF8.GetBytes(csv), "text/csv", "tasks_export.csv");
        }

        if (string.Equals(format, "json", StringComparison.OrdinalIgnoreCase))
        {
            var json = JsonSerializer.Serialize(tasks, JsonOptions);
            var bytes = Encoding.UTF8.GetBytes(json);
            return File(bytes, "application/json", "tasks_export.json");
        }

        return BadRequest("Invalid format. Supported values: json, csv.");
    }

    /// <summary>
    /// Export payment ledger as JSON or CSV.
    /// </summary>
    [HttpGet("payments")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ExportPayments(
        [FromQuery] string format = "json",
        CancellationToken ct = default)
    {
        if (!AuthorizationHelper.IsAdminOrDevMode(HttpContext))
            return Forbid();

        var payments = await _paymentRepository.GetPagedAsync(0, int.MaxValue, ct);

        if (string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase))
        {
            var csv = BuildPaymentsCsv(payments);
            return File(Encoding.UTF8.GetBytes(csv), "text/csv", "payments_export.csv");
        }

        if (string.Equals(format, "json", StringComparison.OrdinalIgnoreCase))
        {
            var maskedPayments = payments.Select(p => new
            {
                p.Id, p.EscrowId, p.TaskId, p.MilestoneId, p.AgentId,
                p.AmountSats, p.AmountUsd,
                PaymentHash = MaskSensitive(p.PaymentHash),
                p.PaymentType, p.Status, p.CreatedAt, p.SettledAt
            }).ToList();

            var json = JsonSerializer.Serialize(maskedPayments, JsonOptions);
            var bytes = Encoding.UTF8.GetBytes(json);
            return File(bytes, "application/json", "payments_export.json");
        }

        return BadRequest("Invalid format. Supported values: json, csv.");
    }

    /// <summary>
    /// Export agents and their reputation as JSON.
    /// </summary>
    [HttpGet("agents")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ExportAgents(
        [FromQuery] string format = "json",
        CancellationToken ct = default)
    {
        if (!AuthorizationHelper.IsAdminOrDevMode(HttpContext))
            return Forbid();

        var agents = await _agentRepository.GetAllAsync(null, ct);

        var exportItems = new List<AgentExportItem>();
        foreach (var agent in agents)
        {
            var reputation = await _reputationRepository.GetByAgentIdAsync(agent.Id, ct);
            exportItems.Add(new AgentExportItem
            {
                Id = agent.Id,
                ExternalId = agent.ExternalId,
                Name = agent.Name,
                WalletPubkey = agent.WalletPubkey,
                Status = agent.Status.ToString(),
                DailySpendCapSats = agent.DailySpendCapSats,
                WeeklySpendCapSats = agent.WeeklySpendCapSats,
                RateLimitPerMinute = agent.RateLimitPerMinute,
                CreatedAt = agent.CreatedAt,
                UpdatedAt = agent.UpdatedAt,
                ReputationScore = reputation?.ReputationScore,
                TotalTasks = reputation?.TotalTasks,
                CompletedTasks = reputation?.CompletedTasks,
                VerificationPasses = reputation?.VerificationPasses,
                VerificationFails = reputation?.VerificationFails,
                DisputeCount = reputation?.DisputeCount
            });
        }

        if (string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase))
        {
            var csv = BuildAgentsCsv(exportItems);
            return File(Encoding.UTF8.GetBytes(csv), "text/csv", "agents_export.csv");
        }

        var json = JsonSerializer.Serialize(exportItems, JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        return File(bytes, "application/json", "agents_export.json");
    }

    /// <summary>
    /// Export recent audit log entries as JSON.
    /// </summary>
    [HttpGet("audit")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ExportAuditLog(
        [FromQuery] string format = "json",
        [FromQuery] int days = 30,
        CancellationToken ct = default)
    {
        if (!AuthorizationHelper.IsAdminOrDevMode(HttpContext))
            return Forbid();

        if (days < 1) days = 1;
        if (days > 365) days = 365;

        // GetRecentAsync returns entries ordered by most recent; we use a large count
        // and rely on the days parameter for filtering conceptually.
        // Since the repository only supports count-based retrieval, we fetch a generous amount
        // and filter by date client-side.
        var allRecent = await _auditLogRepository.GetRecentAsync(100_000, ct);
        var cutoff = DateTime.UtcNow.AddDays(-days);
        var filtered = allRecent.Where(e => e.CreatedAt >= cutoff).ToList();

        if (string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase))
        {
            var csv = BuildAuditCsv(filtered);
            return File(Encoding.UTF8.GetBytes(csv), "text/csv", "audit_export.csv");
        }

        var json = JsonSerializer.Serialize(filtered, JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        return File(bytes, "application/json", "audit_export.json");
    }

    // ── CSV Builders ────────────────────────────────────────────────────

    private static string BuildTasksCsv(IReadOnlyList<TaskItem> tasks)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Id,ExternalId,ParentTaskId,ClientId,Title,Description,TaskType,Status,MaxPayoutSats,ActualPayoutSats,PriceUsd,AssignedAgentId,Priority,CreatedAt,UpdatedAt,CompletedAt");

        foreach (var t in tasks)
        {
            sb.AppendLine(string.Join(",",
                t.Id,
                CsvEscape(t.ExternalId),
                t.ParentTaskId?.ToString() ?? "",
                CsvEscape(t.ClientId),
                CsvEscape(t.Title),
                CsvEscape(t.Description),
                t.TaskType,
                t.Status,
                t.MaxPayoutSats,
                t.ActualPayoutSats,
                t.PriceUsd?.ToString() ?? "",
                t.AssignedAgentId?.ToString() ?? "",
                t.Priority,
                t.CreatedAt.ToString("o"),
                t.UpdatedAt.ToString("o"),
                t.CompletedAt?.ToString("o") ?? ""));
        }

        return sb.ToString();
    }

    private static string BuildPaymentsCsv(IReadOnlyList<Payment> payments)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Id,EscrowId,TaskId,MilestoneId,AgentId,AmountSats,AmountUsd,PaymentHash (masked),PaymentType,Status,CreatedAt,SettledAt");

        foreach (var p in payments)
        {
            sb.AppendLine(string.Join(",",
                p.Id,
                p.EscrowId?.ToString() ?? "",
                p.TaskId,
                p.MilestoneId?.ToString() ?? "",
                p.AgentId,
                p.AmountSats,
                p.AmountUsd?.ToString() ?? "",
                MaskSensitive(p.PaymentHash),
                p.PaymentType,
                p.Status,
                p.CreatedAt.ToString("o"),
                p.SettledAt?.ToString("o") ?? ""));
        }

        return sb.ToString();
    }

    private static string BuildAgentsCsv(List<AgentExportItem> agents)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Id,ExternalId,Name,WalletPubkey,Status,DailySpendCapSats,WeeklySpendCapSats,RateLimitPerMinute,CreatedAt,UpdatedAt,ReputationScore,TotalTasks,CompletedTasks,VerificationPasses,VerificationFails,DisputeCount");

        foreach (var a in agents)
        {
            sb.AppendLine(string.Join(",",
                a.Id,
                CsvEscape(a.ExternalId),
                CsvEscape(a.Name),
                CsvEscape(a.WalletPubkey ?? ""),
                a.Status,
                a.DailySpendCapSats,
                a.WeeklySpendCapSats,
                a.RateLimitPerMinute,
                a.CreatedAt.ToString("o"),
                a.UpdatedAt.ToString("o"),
                a.ReputationScore?.ToString() ?? "",
                a.TotalTasks?.ToString() ?? "",
                a.CompletedTasks?.ToString() ?? "",
                a.VerificationPasses?.ToString() ?? "",
                a.VerificationFails?.ToString() ?? "",
                a.DisputeCount?.ToString() ?? ""));
        }

        return sb.ToString();
    }

    private static string BuildAuditCsv(List<AuditLogEntry> entries)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Id,EventType,EntityType,EntityId,AgentId,Action,Details,IpAddress,UserAgent,CreatedAt");

        foreach (var e in entries)
        {
            sb.AppendLine(string.Join(",",
                e.Id,
                CsvEscape(e.EventType),
                CsvEscape(e.EntityType),
                e.EntityId,
                e.AgentId?.ToString() ?? "",
                CsvEscape(e.Action ?? ""),
                CsvEscape(e.Details ?? ""),
                CsvEscape(e.IpAddress ?? ""),
                CsvEscape(e.UserAgent ?? ""),
                e.CreatedAt.ToString("o")));
        }

        return sb.ToString();
    }

    private static string MaskSensitive(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (value.Length <= 8) return "****";
        return value[..4] + "****" + value[^4..];
    }

    private static string CsvEscape(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "";

        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        return value;
    }
}

internal class AgentExportItem
{
    public int Id { get; set; }
    public string ExternalId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? WalletPubkey { get; set; }
    public string Status { get; set; } = string.Empty;
    public long DailySpendCapSats { get; set; }
    public long WeeklySpendCapSats { get; set; }
    public int RateLimitPerMinute { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public double? ReputationScore { get; set; }
    public int? TotalTasks { get; set; }
    public int? CompletedTasks { get; set; }
    public int? VerificationPasses { get; set; }
    public int? VerificationFails { get; set; }
    public int? DisputeCount { get; set; }
}
