using LightningAgent.Api.DTOs;
using LightningAgent.Api.Helpers;
using LightningAgent.Core.Interfaces.Data;
using LightningAgent.Core.Models;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;

namespace LightningAgent.Api.Controllers;

/// <summary>
/// Admin endpoints for querying the audit log.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/admin/audit")]
[Route("api/v{version:apiVersion}/admin/audit")]
[Produces("application/json")]
public class AuditController : ControllerBase
{
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ILogger<AuditController> _logger;

    public AuditController(IAuditLogRepository auditLogRepository, ILogger<AuditController> logger)
    {
        _auditLogRepository = auditLogRepository;
        _logger = logger;
    }

    /// <summary>
    /// List audit log entries with pagination and optional filters.
    /// </summary>
    /// <param name="page">Page number (1-based).</param>
    /// <param name="pageSize">Number of items per page (max 100).</param>
    /// <param name="agentId">Optional filter by agent ID.</param>
    /// <param name="action">Optional filter by action.</param>
    /// <param name="startDate">Optional inclusive start date (ISO 8601).</param>
    /// <param name="endDate">Optional inclusive end date (ISO 8601).</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet]
    [ProducesResponseType(typeof(PaginatedResponse<AuditLogEntry>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<PaginatedResponse<AuditLogEntry>>> ListAuditEntries(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] int? agentId = null,
        [FromQuery] string? action = null,
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null,
        CancellationToken ct = default)
    {
        if (!AuthorizationHelper.IsAdminOrDevMode(HttpContext))
            return Forbid();

        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 1;
        if (pageSize > 100) pageSize = 100;

        var offset = (page - 1) * pageSize;
        var (items, totalCount) = await _auditLogRepository.GetPagedAsync(
            offset, pageSize, agentId, action, startDate, endDate, ct);

        return Ok(new PaginatedResponse<AuditLogEntry>
        {
            Items = items.ToList(),
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        });
    }

    /// <summary>
    /// Get a single audit log entry by ID.
    /// </summary>
    /// <param name="id">The audit log entry ID.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(AuditLogEntry), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<AuditLogEntry>> GetAuditEntry(int id, CancellationToken ct)
    {
        if (!AuthorizationHelper.IsAdminOrDevMode(HttpContext))
            return Forbid();

        var entry = await _auditLogRepository.GetByIdAsync(id, ct);
        if (entry is null)
            return NotFound($"Audit log entry {id} not found.");

        return Ok(entry);
    }

    /// <summary>
    /// Get audit log entries for a specific agent.
    /// </summary>
    /// <param name="agentId">The agent ID to filter by.</param>
    /// <param name="limit">Maximum number of entries to return (default 100).</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet("agent/{agentId:int}")]
    [ProducesResponseType(typeof(IReadOnlyList<AuditLogEntry>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<IReadOnlyList<AuditLogEntry>>> GetAuditEntriesByAgent(
        int agentId,
        [FromQuery] int limit = 100,
        CancellationToken ct = default)
    {
        if (!AuthorizationHelper.IsAdminOrDevMode(HttpContext))
            return Forbid();

        if (limit < 1) limit = 1;
        if (limit > 1000) limit = 1000;

        var entries = await _auditLogRepository.GetByAgentAsync(agentId, limit, ct);
        return Ok(entries);
    }
}
