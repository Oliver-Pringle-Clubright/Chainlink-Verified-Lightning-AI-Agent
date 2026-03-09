using LightningAgent.Core.Enums;
using LightningAgent.Core.Models;

namespace LightningAgent.Core.Interfaces.Data;

public interface IVerificationStrategyConfigRepository
{
    Task<IReadOnlyList<VerificationStrategyParam>> GetByStrategyTypeAsync(VerificationStrategyType strategyType, CancellationToken ct = default);
    Task UpsertAsync(VerificationStrategyParam param, CancellationToken ct = default);
}
