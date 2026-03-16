namespace LightningAgentMarketPlace.Core.Interfaces.Data;

public interface IArtifactRepository
{
    Task<int> CreateAsync(LightningAgentMarketPlace.Core.Models.Artifact artifact, CancellationToken ct = default);
    Task<LightningAgentMarketPlace.Core.Models.Artifact?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<LightningAgentMarketPlace.Core.Models.Artifact>> GetByTaskIdAsync(int taskId, CancellationToken ct = default);
}
