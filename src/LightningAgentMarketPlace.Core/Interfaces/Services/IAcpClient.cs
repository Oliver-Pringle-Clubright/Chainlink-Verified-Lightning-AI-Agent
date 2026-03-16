using LightningAgentMarketPlace.Core.Models.Acp;

namespace LightningAgentMarketPlace.Core.Interfaces.Services;

public interface IAcpClient
{
    Task<List<AcpServiceDescriptor>> DiscoverServicesAsync(string? taskType = null, CancellationToken ct = default);
    Task<string> PostTaskAsync(AcpTaskSpec task, CancellationToken ct = default);
    Task<List<AcpAgentOffer>> ReceiveOffersAsync(string taskId, CancellationToken ct = default);
    Task<bool> AcceptOfferAsync(string offerId, CancellationToken ct = default);
    Task<bool> NotifyCompletionAsync(string taskId, string result, CancellationToken ct = default);
}
