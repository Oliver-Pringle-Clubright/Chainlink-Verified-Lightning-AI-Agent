using System.Collections.Concurrent;
using System.Numerics;
using LightningAgentMarketPlace.Core.Interfaces.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LightningAgentMarketPlace.Engine.BackgroundJobs;

/// <summary>
/// Polls for VRF randomness fulfillment. When a VRF request is pending,
/// this service checks the consumer contract for fulfilled random words
/// and invokes the registered callback.
/// </summary>
public class VrfResponsePoller : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(15);
    private const int MaxPollAttempts = 40; // 40 * 15s = 10 minutes

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<VrfResponsePoller> _logger;

    /// <summary>
    /// Pending VRF requests: requestId → (numWords, attempt count, callback).
    /// </summary>
    private static readonly ConcurrentDictionary<string, VrfPendingRequest> PendingRequests = new();

    public VrfResponsePoller(
        IServiceScopeFactory scopeFactory,
        ILogger<VrfResponsePoller> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>
    /// Registers a VRF request for polling. The callback is invoked with the random words
    /// when fulfillment is detected.
    /// </summary>
    public static void TrackRequest(string requestId, int numWords, Action<List<string>> onFulfilled)
    {
        PendingRequests.TryAdd(requestId, new VrfPendingRequest
        {
            RequestId = requestId,
            NumWords = numWords,
            OnFulfilled = onFulfilled,
            Attempts = 0,
            SubmittedAt = DateTime.UtcNow
        });
    }

    /// <summary>Returns the number of pending VRF requests.</summary>
    public static int PendingCount => PendingRequests.Count;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("VrfResponsePoller started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (PendingRequests.IsEmpty)
                {
                    await SafeDelay(PollInterval, stoppingToken);
                    continue;
                }

                using var scope = _scopeFactory.CreateScope();
                var vrfClient = scope.ServiceProvider.GetRequiredService<IChainlinkVrfClient>();

                foreach (var (requestId, pending) in PendingRequests)
                {
                    try
                    {
                        pending.Attempts++;

                        if (pending.Attempts > MaxPollAttempts)
                        {
                            _logger.LogWarning(
                                "VRF request {RequestId} exceeded max poll attempts ({Max}), giving up",
                                requestId, MaxPollAttempts);
                            PendingRequests.TryRemove(requestId, out _);
                            continue;
                        }

                        var result = await vrfClient.GetFulfillmentAsync(requestId, stoppingToken);
                        if (result?.Randomness is not null && result.Randomness.Count > 0)
                        {
                            _logger.LogInformation(
                                "VRF request {RequestId} fulfilled with {Count} random words (attempt {Attempt})",
                                requestId, result.Randomness.Count, pending.Attempts);

                            // Invoke callback
                            try
                            {
                                pending.OnFulfilled?.Invoke(result.Randomness);
                            }
                            catch (Exception cbEx)
                            {
                                _logger.LogError(cbEx, "VRF fulfillment callback failed for {RequestId}", requestId);
                            }

                            PendingRequests.TryRemove(requestId, out _);
                        }
                        else
                        {
                            _logger.LogDebug(
                                "VRF request {RequestId} not yet fulfilled (attempt {Attempt}/{Max})",
                                requestId, pending.Attempts, MaxPollAttempts);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "VrfResponsePoller error checking {RequestId}", requestId);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "VrfResponsePoller encountered an error during poll cycle");
            }

            await SafeDelay(PollInterval, stoppingToken);
        }

        _logger.LogInformation("VrfResponsePoller stopped");
    }

    private static async Task SafeDelay(TimeSpan delay, CancellationToken ct)
    {
        try { await Task.Delay(delay, ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }

    private class VrfPendingRequest
    {
        public string RequestId { get; set; } = "";
        public int NumWords { get; set; }
        public Action<List<string>>? OnFulfilled { get; set; }
        public int Attempts { get; set; }
        public DateTime SubmittedAt { get; set; }
    }
}
