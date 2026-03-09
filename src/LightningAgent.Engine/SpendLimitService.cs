using LightningAgent.Core.Configuration;
using LightningAgent.Core.Interfaces.Data;
using LightningAgent.Core.Interfaces.Services;
using LightningAgent.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LightningAgent.Engine;

public class SpendLimitService : ISpendLimitService
{
    private readonly ISpendLimitRepository _spendLimitRepo;
    private readonly SpendLimitSettings _settings;
    private readonly ILogger<SpendLimitService> _logger;

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
        // The repository does not expose a GetAll method, so we iterate
        // over agent-based limits by probing known agent IDs. In a production
        // system this would be a single SQL query. For now we rely on the
        // caller (the background job) to invoke this periodically; if no
        // agents are known we return 0.
        //
        // We use a pragmatic approach: try agent IDs 1..10000.  The repo
        // returns null for non-existent agents so this is safe, just
        // potentially slow.  A future migration should add GetAllAsync to the
        // repository interface.

        int resetCount = 0;
        var now = DateTime.UtcNow;

        // Probe a reasonable range of agent IDs.  In practice, the hosting
        // project should register a proper ISpendLimitRepository.GetAllAsync.
        for (int agentId = 1; agentId <= 10_000; agentId++)
        {
            if (ct.IsCancellationRequested) break;

            var limit = await _spendLimitRepo.GetByAgentIdAsync(agentId, ct);
            if (limit is null) continue;

            if (limit.PeriodEnd <= now)
            {
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
                    limit.LimitType, agentId, limit.PeriodStart, limit.PeriodEnd);
            }
        }

        return resetCount;
    }
}
