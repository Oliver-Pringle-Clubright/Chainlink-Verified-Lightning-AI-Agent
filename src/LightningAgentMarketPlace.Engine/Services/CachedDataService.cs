using LightningAgentMarketPlace.Core.Enums;
using LightningAgentMarketPlace.Core.Interfaces.Data;
using LightningAgentMarketPlace.Core.Interfaces.Services;
using LightningAgentMarketPlace.Core.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using TaskStatus = LightningAgentMarketPlace.Core.Enums.TaskStatus;

namespace LightningAgentMarketPlace.Engine.Services;

/// <summary>
/// Wraps common repository reads with an in-memory cache to reduce
/// database round-trips for frequently-accessed data.
/// </summary>
public class CachedDataService : ICachedDataService
{
    private readonly IAgentRepository _agentRepository;
    private readonly IAgentReputationRepository _reputationRepository;
    private readonly IAgentCapabilityRepository _capabilityRepository;
    private readonly IPriceCacheRepository _priceCacheRepository;
    private readonly ITaskRepository _taskRepository;
    private readonly IPaymentRepository _paymentRepository;
    private readonly IEscrowRepository _escrowRepository;
    private readonly IVerificationRepository _verificationRepository;
    private readonly IDisputeRepository _disputeRepository;
    private readonly IMemoryCache _cache;
    private readonly ILogger<CachedDataService> _logger;

    private static string AgentKey(int id) => $"agent:{id}";
    private static string ReputationKey(int agentId) => $"agent-reputation:{agentId}";
    private static string CapabilitiesKey(int agentId) => $"agent-capabilities:{agentId}";
    private const string BtcUsdPriceKey = "btcusd-price";
    private const string SystemStatsKey = "system-stats";

    public CachedDataService(
        IAgentRepository agentRepository,
        IAgentReputationRepository reputationRepository,
        IAgentCapabilityRepository capabilityRepository,
        IPriceCacheRepository priceCacheRepository,
        ITaskRepository taskRepository,
        IPaymentRepository paymentRepository,
        IEscrowRepository escrowRepository,
        IVerificationRepository verificationRepository,
        IDisputeRepository disputeRepository,
        IMemoryCache cache,
        ILogger<CachedDataService> logger)
    {
        _agentRepository = agentRepository;
        _reputationRepository = reputationRepository;
        _capabilityRepository = capabilityRepository;
        _priceCacheRepository = priceCacheRepository;
        _taskRepository = taskRepository;
        _paymentRepository = paymentRepository;
        _escrowRepository = escrowRepository;
        _verificationRepository = verificationRepository;
        _disputeRepository = disputeRepository;
        _cache = cache;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Agent?> GetAgentAsync(int id, CancellationToken ct)
    {
        var key = AgentKey(id);
        if (_cache.TryGetValue(key, out Agent? cached))
            return cached;

        var agent = await _agentRepository.GetByIdAsync(id, ct);
        if (agent is not null)
        {
            _cache.Set(key, agent, TimeSpan.FromMinutes(2));
        }

        return agent;
    }

    /// <inheritdoc />
    public async Task<AgentReputation?> GetAgentReputationAsync(int agentId, CancellationToken ct)
    {
        var key = ReputationKey(agentId);
        if (_cache.TryGetValue(key, out AgentReputation? cached))
            return cached;

        var reputation = await _reputationRepository.GetByAgentIdAsync(agentId, ct);
        if (reputation is not null)
        {
            _cache.Set(key, reputation, TimeSpan.FromMinutes(1));
        }

        return reputation;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AgentCapability>> GetAgentCapabilitiesAsync(int agentId, CancellationToken ct)
    {
        var key = CapabilitiesKey(agentId);
        if (_cache.TryGetValue(key, out IReadOnlyList<AgentCapability>? cached))
            return cached!;

        var capabilities = await _capabilityRepository.GetByAgentIdAsync(agentId, ct);
        _cache.Set(key, capabilities, TimeSpan.FromMinutes(5));

        return capabilities;
    }

    /// <inheritdoc />
    public async Task<PriceQuote?> GetBtcUsdPriceAsync(CancellationToken ct)
    {
        if (_cache.TryGetValue(BtcUsdPriceKey, out PriceQuote? cached))
            return cached;

        var price = await _priceCacheRepository.GetLatestAsync("BTC/USD", ct);
        if (price is not null)
        {
            _cache.Set(BtcUsdPriceKey, price, TimeSpan.FromSeconds(30));
        }

        return price;
    }

    /// <inheritdoc />
    public async Task<SystemStats> GetSystemStatsAsync(CancellationToken ct)
    {
        if (_cache.TryGetValue(SystemStatsKey, out SystemStats? cached))
            return cached!;

        _logger.LogDebug("Building system stats (cache miss)");

        var stats = new SystemStats
        {
            // Agents
            TotalAgents = await _agentRepository.GetCountAsync(null, ct),
            ActiveAgents = await _agentRepository.GetCountAsync(AgentStatus.Active, ct),
            SuspendedAgents = await _agentRepository.GetCountAsync(AgentStatus.Suspended, ct),

            // Tasks
            TotalTasks = await _taskRepository.GetCountAsync(null, ct),
            PendingTasks = await _taskRepository.GetCountAsync(TaskStatus.Pending, ct),
            InProgressTasks = await _taskRepository.GetCountAsync(TaskStatus.InProgress, ct),
            CompletedTasks = await _taskRepository.GetCountAsync(TaskStatus.Completed, ct),
            FailedTasks = await _taskRepository.GetCountAsync(TaskStatus.Failed, ct),

            // Payments
            PaymentCount = await _paymentRepository.GetCountAsync(ct),
            TotalSats = await _paymentRepository.GetTotalSatsAsync(ct),
            TotalUsd = await _paymentRepository.GetTotalUsdAsync(ct),

            // Escrows
            HeldEscrows = await _escrowRepository.GetCountByStatusAsync(EscrowStatus.Held, ct),
            SettledEscrows = await _escrowRepository.GetCountByStatusAsync(EscrowStatus.Settled, ct),
            CancelledEscrows = await _escrowRepository.GetCountByStatusAsync(EscrowStatus.Cancelled, ct),
            HeldAmountSats = await _escrowRepository.GetHeldAmountSatsAsync(ct),

            // Verifications
            TotalVerifications = await _verificationRepository.GetTotalCountAsync(ct),
            PassedVerifications = await _verificationRepository.GetCountByPassedAsync(true, ct),
            FailedVerifications = await _verificationRepository.GetCountByPassedAsync(false, ct),

            // Disputes
            OpenDisputes = await _disputeRepository.GetCountByStatusAsync(DisputeStatus.Open, ct),
            ResolvedDisputes = await _disputeRepository.GetCountByStatusAsync(DisputeStatus.Resolved, ct),
        };

        // Verification pass rate
        stats.VerificationPassRate = stats.TotalVerifications > 0
            ? Math.Round((double)stats.PassedVerifications / stats.TotalVerifications, 4)
            : 0.0;

        // Pricing
        var latestPrice = await _priceCacheRepository.GetLatestAsync("BTC/USD", ct);
        stats.BtcUsdPrice = latestPrice?.PriceUsd ?? 0.0;
        stats.PriceLastUpdated = latestPrice?.FetchedAt.ToString("o") ?? "";

        stats.Timestamp = DateTime.UtcNow.ToString("o");

        _cache.Set(SystemStatsKey, stats, TimeSpan.FromSeconds(30));

        return stats;
    }

    /// <inheritdoc />
    public void InvalidateAgent(int agentId)
    {
        _cache.Remove(AgentKey(agentId));
        _cache.Remove(ReputationKey(agentId));
        _cache.Remove(CapabilitiesKey(agentId));

        _logger.LogDebug("Invalidated cache entries for agent {AgentId}", agentId);
    }
}
