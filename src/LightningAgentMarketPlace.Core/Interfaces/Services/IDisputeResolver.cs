using LightningAgentMarketPlace.Core.Models;

namespace LightningAgentMarketPlace.Core.Interfaces.Services;

public interface IDisputeResolver
{
    Task<Dispute> OpenDisputeAsync(int taskId, int? milestoneId, string initiatedBy, string initiatorId, string reason, long amountSats, CancellationToken ct = default);
    Task<int> AssignArbiterAsync(int disputeId, CancellationToken ct = default);
    Task<Dispute> ResolveDisputeAsync(int disputeId, string resolution, CancellationToken ct = default);
}
