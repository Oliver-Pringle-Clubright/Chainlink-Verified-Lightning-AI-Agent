using LightningAgent.Core.Models;

namespace LightningAgent.Core.Interfaces.Services;

public interface IVerificationPlugin
{
    string Name { get; }
    string[] SupportedTaskTypes { get; }
    Task<VerificationResult> VerifyAsync(Milestone milestone, string output, CancellationToken ct);
}
