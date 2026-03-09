using LightningAgent.Core.Models.Acp;

namespace LightningAgent.Core.Interfaces.Services;

public interface INaturalLanguageTaskParser
{
    Task<AcpTaskSpec> ParseDescriptionAsync(string naturalLanguageDescription, CancellationToken ct = default);
}
