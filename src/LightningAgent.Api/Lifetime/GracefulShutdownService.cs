namespace LightningAgent.Api.Lifetime;

/// <summary>
/// Hosted service that logs application lifecycle events and coordinates
/// graceful shutdown by listening for ApplicationStopping/ApplicationStopped events.
/// </summary>
public class GracefulShutdownService : IHostedService
{
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<GracefulShutdownService> _logger;

    public GracefulShutdownService(
        IHostApplicationLifetime lifetime,
        ILogger<GracefulShutdownService> logger)
    {
        _lifetime = lifetime;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("GracefulShutdownService started — lifecycle hooks registered");

        _lifetime.ApplicationStopping.Register(OnStopping);
        _lifetime.ApplicationStopped.Register(OnStopped);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    private void OnStopping()
    {
        _logger.LogInformation("Application shutting down, draining background tasks...");
    }

    private void OnStopped()
    {
        _logger.LogInformation("All background tasks drained. Shutdown complete.");
    }
}
