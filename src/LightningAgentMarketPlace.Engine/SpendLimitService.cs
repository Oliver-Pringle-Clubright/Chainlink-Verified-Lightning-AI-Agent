using LightningAgentMarketPlace.Core.Configuration;
using LightningAgentMarketPlace.Core.Interfaces.Data;
using LightningAgentMarketPlace.Core.Interfaces.Services;
using LightningAgentMarketPlace.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LightningAgentMarketPlace.Engine;

public class SpendLimitService : ISpendLimitService
{
    private readonly ISpendLimitRepository _spendLimitRepo;
    private readonly SpendLimitSettings _settings;
    private readonly ILogger<SpendLimitService> _logger;

    // Prevents concurrent check+record for the same agent
    private static readonly Dictionary<int, SemaphoreSlim> _agentLocks = new();
    private static readonly object _lockMapGuard = new();

    public SpendLimitService(
        ISpendLimitRepository spendLimitRepo,
        IOptions<SpendLimitSettings> settings,
        ILogger<SpendLimitService> logger)
    {
        _spendLimitRepo = spendLimitRepo;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<bool> CheckLimitAsync(int agentId, long amountSats, CancellationToken ct = default)
    {
        var limit = await _spendLimitRepo.GetByAgentIdAsync(agentId, ct);

        if (limit is null)
        {
            // No limit configured — check against global default daily cap
            _logger.LogDebug(
                "No spend limit for agent {AgentId}, checking against default daily cap ({DefaultCap} sats)",
                agentId, _settings.DefaultDailyCapSats);

            return amountSats <= _settings.DefaultDailyCapSats;
        }

        // Check active limit (PeriodEnd > UtcNow)
        if (limit.PeriodEnd > DateTime.UtcNow)
        {
            if (limit.CurrentSpentSats + amountSats > limit.MaxSats)
            {
                _logger.LogWarning(
                    "Spend limit exceeded for agent {AgentId}: current={Current}, requested={Requested}, max={Max} ({LimitType})",
                    agentId, limit.CurrentSpentSats, amountSats, limit.MaxSats, limit.LimitType);
                return false;
            }
        }

        return true;
    }

    public async Task RecordSpendAsync(int agentId, long amountSats, CancellationToken ct = default)
    {
        // Serialize check+record per agent to prevent TOCTOU race conditions
        var semaphore = GetAgentLock(agentId);
        await semaphore.WaitAsync(ct);
        try
        {
            await RecordSpendCoreAsync(agentId, amountSats, ct);
        }
        finally
        {
            semaphore.Release();
        }
    }

    /// <summary>
    /// Atomically checks the limit and records the spend. Returns false if the limit would be exceeded.
    /// </summary>
    public async Task<bool> TryCheckAndRecordSpendAsync(int agentId, long amountSats, CancellationToken ct = default)
    {
        var semaphore = GetAgentLock(agentId);
        await semaphore.WaitAsync(ct);
        try
        {
            if (!await CheckLimitAsync(agentId, amountSats, ct))
                return false;

            await RecordSpendCoreAsync(agentId, amountSats, ct);
            return true;
        }
        finally
        {
            semaphore.Release();
        }
    }

    private async Task RecordSpendCoreAsync(int agentId, long amountSats, CancellationToken ct)
    {
        var limit = await _spendLimitRepo.GetByAgentIdAsync(agentId, ct);

        if (limit is not null)
        {
            limit.CurrentSpentSats += amountSats;
            await _spendLimitRepo.UpdateAsync(limit, ct);

            _logger.LogInformation(
                "Recorded spend of {Amount} sats for agent {AgentId}: total={Total}/{Max} ({LimitType})",
                amountSats, agentId, limit.CurrentSpentSats, limit.MaxSats, limit.LimitType);
        }
        else
        {
            // No limits exist — create default Daily limit from settings
            var now = DateTime.UtcNow;

            var dailyLimit = new SpendLimit
            {
                AgentId = agentId,
                LimitType = "Daily",
                MaxSats = _settings.DefaultDailyCapSats,
                CurrentSpentSats = amountSats,
                PeriodStart = now,
                PeriodEnd = now.AddDays(1)
            };

            await _spendLimitRepo.CreateAsync(dailyLimit, ct);

            _logger.LogInformation(
                "Created default Daily spend limit for agent {AgentId} with initial spend of {Amount} sats (max={Max})",
                agentId, amountSats, _settings.DefaultDailyCapSats);
        }
    }

    public async Task<int> ResetExpiredPeriodsAsync(CancellationToken ct = default)
    {
        var expiredLimits = await _spendLimitRepo.GetExpiredAsync(ct);

        if (expiredLimits.Count == 0)
        {
            _logger.LogDebug("No expired spend limits found");
            return 0;
        }

        _logger.LogInformation("Found {Count} expired spend limits to reset", expiredLimits.Count);

        int resetCount = 0;
        var now = DateTime.UtcNow;

        foreach (var limit in expiredLimits)
        {
            if (ct.IsCancellationRequested) break;

            limit.CurrentSpentSats = 0;
            limit.PeriodStart = now;
            limit.PeriodEnd = limit.LimitType switch
            {
                "Daily" => now.AddDays(1),
                "Weekly" => now.AddDays(7),
                _ => now.AddDays(1) // Default to daily
            };

            await _spendLimitRepo.UpdateAsync(limit, ct);
            resetCount++;

            _logger.LogInformation(
                "Reset expired {LimitType} spend limit for agent {AgentId}: new period {Start} - {End}",
                limit.LimitType, limit.AgentId, limit.PeriodStart, limit.PeriodEnd);
        }

        return resetCount;
    }

    private static SemaphoreSlim GetAgentLock(int agentId)
    {
        lock (_lockMapGuard)
        {
            if (!_agentLocks.TryGetValue(agentId, out var semaphore))
            {
                semaphore = new SemaphoreSlim(1, 1);
                _agentLocks[agentId] = semaphore;
            }
            return semaphore;
        }
    }
}
