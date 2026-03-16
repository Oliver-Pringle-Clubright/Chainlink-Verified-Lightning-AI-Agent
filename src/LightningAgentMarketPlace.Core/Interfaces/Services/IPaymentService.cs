using LightningAgentMarketPlace.Core.Models;

namespace LightningAgentMarketPlace.Core.Interfaces.Services;

public interface IPaymentService
{
    Task<Payment> ProcessMilestonePaymentAsync(int milestoneId, CancellationToken ct = default);
    Task<Payment> StreamPaymentAsync(int agentId, long amountSats, string memo, CancellationToken ct = default);
}
