using LightningAgent.Core.Models;

namespace LightningAgent.Core.Interfaces.Data;

public interface IVerificationRepository
{
    Task<Verification?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<Verification>> GetByMilestoneIdAsync(int milestoneId, CancellationToken ct = default);
    Task<IReadOnlyList<Verification>> GetPendingChainlinkAsync(CancellationToken ct = default);
    Task<int> GetCountByPassedAsync(bool passed, CancellationToken ct = default);
    Task<int> GetTotalCountAsync(CancellationToken ct = default);
    Task<int> CreateAsync(Verification verification, CancellationToken ct = default);
    Task UpdateAsync(Verification verification, CancellationToken ct = default);
}
