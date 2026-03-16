using LightningAgentMarketPlace.Core.Enums;
using LightningAgentMarketPlace.Core.Interfaces.Data;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using TaskStatus = LightningAgentMarketPlace.Core.Enums.TaskStatus;

namespace LightningAgentMarketPlace.Api.Controllers;

/// <summary>
/// Provides a comprehensive system statistics dashboard.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/stats")]
[Route("api/v{version:apiVersion}/stats")]
[Produces("application/json")]
public class StatsController : ControllerBase
{
    private readonly ITaskRepository _taskRepository;
    private readonly IAgentRepository _agentRepository;
    private readonly IPaymentRepository _paymentRepository;
    private readonly IEscrowRepository _escrowRepository;
    private readonly IVerificationRepository _verificationRepository;
    private readonly IDisputeRepository _disputeRepository;
    private readonly IPriceCacheRepository _priceCacheRepository;

    public StatsController(
        ITaskRepository taskRepository,
        IAgentRepository agentRepository,
        IPaymentRepository paymentRepository,
        IEscrowRepository escrowRepository,
        IVerificationRepository verificationRepository,
        IDisputeRepository disputeRepository,
        IPriceCacheRepository priceCacheRepository)
    {
        _taskRepository = taskRepository;
        _agentRepository = agentRepository;
        _paymentRepository = paymentRepository;
        _escrowRepository = escrowRepository;
        _verificationRepository = verificationRepository;
        _disputeRepository = disputeRepository;
        _priceCacheRepository = priceCacheRepository;
    }

    /// <summary>
    /// Get aggregated system statistics including agents, tasks, payments, escrows, verifications, disputes, and pricing.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetStats(CancellationToken ct)
    {
        // Agents
        var totalAgents = await _agentRepository.GetCountAsync(null, ct);
        var activeAgents = await _agentRepository.GetCountAsync(AgentStatus.Active, ct);
        var suspendedAgents = await _agentRepository.GetCountAsync(AgentStatus.Suspended, ct);

        // Tasks
        var totalTasks = await _taskRepository.GetCountAsync(null, ct);
        var pendingTasks = await _taskRepository.GetCountAsync(TaskStatus.Pending, ct);
        var inProgressTasks = await _taskRepository.GetCountAsync(TaskStatus.InProgress, ct);
        var completedTasks = await _taskRepository.GetCountAsync(TaskStatus.Completed, ct);
        var failedTasks = await _taskRepository.GetCountAsync(TaskStatus.Failed, ct);

        // Payments
        var paymentCount = await _paymentRepository.GetCountAsync(ct);
        var totalSats = await _paymentRepository.GetTotalSatsAsync(ct);
        var totalUsd = await _paymentRepository.GetTotalUsdAsync(ct);

        // Escrows
        var heldEscrows = await _escrowRepository.GetCountByStatusAsync(EscrowStatus.Held, ct);
        var settledEscrows = await _escrowRepository.GetCountByStatusAsync(EscrowStatus.Settled, ct);
        var cancelledEscrows = await _escrowRepository.GetCountByStatusAsync(EscrowStatus.Cancelled, ct);
        var heldAmountSats = await _escrowRepository.GetHeldAmountSatsAsync(ct);

        // Verifications
        var totalVerifications = await _verificationRepository.GetTotalCountAsync(ct);
        var passedVerifications = await _verificationRepository.GetCountByPassedAsync(true, ct);
        var failedVerifications = await _verificationRepository.GetCountByPassedAsync(false, ct);
        var passRate = totalVerifications > 0 ? Math.Round((double)passedVerifications / totalVerifications, 4) : 0.0;

        // Disputes
        var openDisputes = await _disputeRepository.GetCountByStatusAsync(DisputeStatus.Open, ct);
        var resolvedDisputes = await _disputeRepository.GetCountByStatusAsync(DisputeStatus.Resolved, ct);

        // Pricing
        var latestPrice = await _priceCacheRepository.GetLatestAsync("BTC/USD", ct);

        // Uptime
        var uptime = DateTime.UtcNow - AppInfo.StartedAt;

        return Ok(new
        {
            agents = new { total = totalAgents, active = activeAgents, suspended = suspendedAgents },
            tasks = new { total = totalTasks, pending = pendingTasks, inProgress = inProgressTasks, completed = completedTasks, failed = failedTasks },
            payments = new { totalCount = paymentCount, totalSats, totalUsd },
            escrows = new { held = heldEscrows, settled = settledEscrows, cancelled = cancelledEscrows, heldAmountSats },
            verifications = new { total = totalVerifications, passed = passedVerifications, failed = failedVerifications, passRate },
            disputes = new { open = openDisputes, resolved = resolvedDisputes },
            pricing = new
            {
                btcUsd = latestPrice?.PriceUsd ?? 0.0,
                lastUpdated = latestPrice?.FetchedAt.ToString("o") ?? ""
            },
            uptime = uptime.ToString(@"dd\.hh\:mm\:ss"),
            timestamp = DateTime.UtcNow.ToString("o")
        });
    }
}
