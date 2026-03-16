namespace LightningAgentMarketPlace.Core.Interfaces.Services;

public interface ISpendLimitService
{
    Task<bool> CheckLimitAsync(int agentId, long amountSats, CancellationToken ct = default);
    Task RecordSpendAsync(int agentId, long amountSats, CancellationToken ct = default);
    /// <summary>
    /// Atomically checks the limit and records the spend. Returns false if the limit would be exceeded.
    /// Prevents TOCTOU race conditions between check and record.
    /// </summary>
    Task<bool> TryCheckAndRecordSpendAsync(int agentId, long amountSats, CancellationToken ct = default);
    Task<int> ResetExpiredPeriodsAsync(CancellationToken ct = default);
}
