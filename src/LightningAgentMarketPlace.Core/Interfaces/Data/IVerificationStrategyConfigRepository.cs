using LightningAgentMarketPlace.Core.Enums;
using LightningAgentMarketPlace.Core.Models;

namespace LightningAgentMarketPlace.Core.Interfaces.Data;

public interface IVerificationStrategyConfigRepository
{
    Task<IReadOnlyList<VerificationStrategyParam>> GetByStrategyTypeAsync(VerificationStrategyType strategyType, CancellationToken ct = default);
    Task UpsertAsync(VerificationStrategyParam param, CancellationToken ct = default);
}
