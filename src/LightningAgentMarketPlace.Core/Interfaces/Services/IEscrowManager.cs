using LightningAgentMarketPlace.Core.Models;

namespace LightningAgentMarketPlace.Core.Interfaces.Services;

public interface IEscrowManager
{
    Task<Escrow> CreateEscrowAsync(Milestone milestone, CancellationToken ct = default);
    Task<bool> SettleEscrowAsync(int escrowId, byte[] preimage, CancellationToken ct = default);
    Task<bool> CancelEscrowAsync(int escrowId, CancellationToken ct = default);
    Task<int> CheckExpiredEscrowsAsync(CancellationToken ct = default);
}
