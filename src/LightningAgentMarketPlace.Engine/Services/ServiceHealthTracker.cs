using System.Collections.Concurrent;
using LightningAgentMarketPlace.Core.Interfaces.Services;
using Microsoft.Extensions.Logging;

namespace LightningAgentMarketPlace.Engine.Services;

public class ServiceHealthTracker : IServiceHealthTracker
{
    private readonly ConcurrentDictionary<string, MutableHealthState> _states = new();
    private readonly ILogger<ServiceHealthTracker> _logger;
    private const int AlertThreshold = 3; // consecutive failures before alerting

    public ServiceHealthTracker(ILogger<ServiceHealthTracker> logger)
    {
        _logger = logger;
    }

    public void RecordSuccess(string serviceName)
    {
        var state = _states.GetOrAdd(serviceName, _ => new MutableHealthState());
        state.TotalSuccesses++;
        state.ConsecutiveFailures = 0;
        state.LastSuccessAt = DateTime.UtcNow;
    }

    public void RecordFailure(string serviceName, string errorMessage)
    {
        var state = _states.GetOrAdd(serviceName, _ => new MutableHealthState());
        state.TotalFailures++;
        state.ConsecutiveFailures++;
        state.LastError = errorMessage;
        state.LastFailureAt = DateTime.UtcNow;

        if (state.ConsecutiveFailures == AlertThreshold)
        {
            _logger.LogCritical(
                "ALERT: Service {ServiceName} has failed {Count} consecutive times. Last error: {Error}",
                serviceName, state.ConsecutiveFailures, errorMessage);
        }
        else if (state.ConsecutiveFailures > AlertThreshold && state.ConsecutiveFailures % 10 == 0)
        {
            _logger.LogCritical(
                "ALERT: Service {ServiceName} has now failed {Count} consecutive times",
                serviceName, state.ConsecutiveFailures);
        }
    }

    public ServiceHealthStatus GetStatus(string serviceName)
    {
        if (!_states.TryGetValue(serviceName, out var state))
            return new ServiceHealthStatus(serviceName, true, 0, 0, 0, null, null, null);

        return new ServiceHealthStatus(
            serviceName,
            state.ConsecutiveFailures < AlertThreshold,
            state.ConsecutiveFailures,
            state.TotalFailures,
            state.TotalSuccesses,
            state.LastError,
            state.LastSuccessAt,
            state.LastFailureAt);
    }

    public IReadOnlyDictionary<string, ServiceHealthStatus> GetAllStatuses()
    {
        return _states.ToDictionary(
            kvp => kvp.Key,
            kvp => new ServiceHealthStatus(
                kvp.Key,
                kvp.Value.ConsecutiveFailures < AlertThreshold,
                kvp.Value.ConsecutiveFailures,
                kvp.Value.TotalFailures,
                kvp.Value.TotalSuccesses,
                kvp.Value.LastError,
                kvp.Value.LastSuccessAt,
                kvp.Value.LastFailureAt));
    }

    private class MutableHealthState
    {
        public int ConsecutiveFailures;
        public int TotalFailures;
        public int TotalSuccesses;
        public string? LastError;
        public DateTime? LastSuccessAt;
        public DateTime? LastFailureAt;
    }
}
