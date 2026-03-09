using LightningAgent.Core.Models;

namespace LightningAgent.Core.Interfaces.Data;

public interface IPaymentRepository
{
    Task<Payment?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<Payment>> GetByTaskIdAsync(int taskId, CancellationToken ct = default);
    Task<IReadOnlyList<Payment>> GetByAgentIdAsync(int agentId, CancellationToken ct = default);
    Task<int> GetCountAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Payment>> GetPagedAsync(int offset, int limit, CancellationToken ct = default);
    Task<long> GetTotalSatsAsync(CancellationToken ct = default);
    Task<double> GetTotalUsdAsync(CancellationToken ct = default);
    Task<int> CreateAsync(Payment payment, CancellationToken ct = default);
    Task UpdateAsync(Payment payment, CancellationToken ct = default);
}
