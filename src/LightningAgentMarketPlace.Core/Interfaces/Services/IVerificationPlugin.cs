using LightningAgentMarketPlace.Core.Models;

namespace LightningAgentMarketPlace.Core.Interfaces.Services;

public interface IVerificationPlugin
{
    string Name { get; }
    string[] SupportedTaskTypes { get; }
    Task<VerificationResult> VerifyAsync(Milestone milestone, string output, CancellationToken ct);
}
