namespace LightningAgent.Core.Interfaces.Services;

public interface IChainlinkAutomationClient
{
    Task<string> RegisterUpkeepAsync(string target, byte[] checkData, int gasLimit, CancellationToken ct = default);
    Task<bool> CheckUpkeepAsync(string upkeepId, CancellationToken ct = default);
    Task<bool> CancelUpkeepAsync(string upkeepId, CancellationToken ct = default);
}
