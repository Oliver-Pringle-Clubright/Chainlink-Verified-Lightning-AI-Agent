using LightningAgent.Core.Enums;
using LightningAgent.Core.Models;

namespace LightningAgent.Core.Interfaces.Data;

public interface IEscrowRepository
{
    Task<Escrow?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<Escrow?> GetByPaymentHashAsync(string paymentHash, CancellationToken ct = default);
    Task<Escrow?> GetByMilestoneIdAsync(int milestoneId, CancellationToken ct = default);
    Task<IReadOnlyList<Escrow>> GetByStatusAsync(EscrowStatus status, CancellationToken ct = default);
    Task<int> CreateAsync(Escrow escrow, CancellationToken ct = default);
    Task UpdateAsync(Escrow escrow, CancellationToken ct = default);
    Task UpdateStatusAsync(int id, EscrowStatus status, CancellationToken ct = default);
    Task<int> GetCountByStatusAsync(EscrowStatus status, CancellationToken ct = default);
    Task<long> GetHeldAmountSatsAsync(CancellationToken ct = default);
}
