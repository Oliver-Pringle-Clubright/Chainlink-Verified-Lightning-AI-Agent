namespace LightningAgentMarketPlace.Core.Interfaces.Services;

public interface IServiceHealthTracker
{
    void RecordSuccess(string serviceName);
    void RecordFailure(string serviceName, string errorMessage);
    ServiceHealthStatus GetStatus(string serviceName);
    IReadOnlyDictionary<string, ServiceHealthStatus> GetAllStatuses();
}

public record ServiceHealthStatus(
    string ServiceName,
    bool IsHealthy,
    int ConsecutiveFailures,
    int TotalFailures,
    int TotalSuccesses,
    string? LastError,
    DateTime? LastSuccessAt,
    DateTime? LastFailureAt);
