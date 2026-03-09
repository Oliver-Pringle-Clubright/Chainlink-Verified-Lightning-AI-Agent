namespace LightningAgent.Core.Interfaces.Services;

public interface ISpendLimitService
{
    Task<bool> CheckLimitAsync(int agentId, long amountSats, CancellationToken ct = default);
    Task RecordSpendAsync(int agentId, long amountSats, CancellationToken ct = default);
    Task<int> ResetExpiredPeriodsAsync(CancellationToken ct = default);
}
