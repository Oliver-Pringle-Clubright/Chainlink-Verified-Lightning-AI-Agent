using LightningAgentMarketPlace.Core.Models.Acp;

namespace LightningAgentMarketPlace.Core.Interfaces.Services;

public interface INaturalLanguageTaskParser
{
    Task<AcpTaskSpec> ParseDescriptionAsync(string naturalLanguageDescription, CancellationToken ct = default);
}
