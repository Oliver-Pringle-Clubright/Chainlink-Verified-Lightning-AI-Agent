namespace LightningAgent.Core.Interfaces.Data;

public interface IArtifactRepository
{
    Task<int> CreateAsync(LightningAgent.Core.Models.Artifact artifact, CancellationToken ct = default);
    Task<LightningAgent.Core.Models.Artifact?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<LightningAgent.Core.Models.Artifact>> GetByTaskIdAsync(int taskId, CancellationToken ct = default);
}
