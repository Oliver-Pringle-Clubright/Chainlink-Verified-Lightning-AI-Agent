using LightningAgentMarketPlace.Core.Interfaces.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LightningAgentMarketPlace.Engine.BackgroundJobs;

public class DataCleanupService : BackgroundService
{
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(6);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DataCleanupService> _logger;

    public DataCleanupService(
        IServiceScopeFactory scopeFactory,
        ILogger<DataCleanupService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("DataCleanupService started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunCleanupAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DataCleanupService encountered an error during cleanup cycle");
            }

            try
            {
                await Task.Delay(CleanupInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("DataCleanupService stopped");
    }

    private async Task RunCleanupAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();

        var priceCacheRepo = scope.ServiceProvider.GetRequiredService<IPriceCacheRepository>();
        var auditLogRepo = scope.ServiceProvider.GetRequiredService<IAuditLogRepository>();
        var webhookLogRepo = scope.ServiceProvider.GetRequiredService<IWebhookLogRepository>();
        var idempotencyRepo = scope.ServiceProvider.GetRequiredService<IIdempotencyRepository>();

        // Delete price cache entries older than 24 hours
        var priceCutoff = DateTime.UtcNow.AddHours(-24);
        var deletedPriceEntries = await priceCacheRepo.DeleteOlderThanAsync(priceCutoff, ct);

        // Delete audit log entries older than 90 days
        var auditCutoff = DateTime.UtcNow.AddDays(-90);
        var deletedAuditEntries = await auditLogRepo.DeleteOlderThanAsync(auditCutoff, ct);

        // Delete failed webhook delivery logs older than 30 days
        var webhookCutoff = DateTime.UtcNow.AddDays(-30);
        var deletedWebhookEntries = await webhookLogRepo.DeleteOlderThanAsync(webhookCutoff, ct);

        // Delete idempotency keys older than 24 hours
        var idempotencyCutoff = DateTime.UtcNow.AddHours(-24);
        var deletedIdempotencyKeys = await idempotencyRepo.CleanupOlderThanAsync(idempotencyCutoff, ct);

        _logger.LogInformation(
            "DataCleanupService completed: deleted {PriceCount} price cache entries, {AuditCount} audit log entries, {WebhookCount} failed webhook logs, {IdempotencyCount} idempotency keys",
            deletedPriceEntries, deletedAuditEntries, deletedWebhookEntries, deletedIdempotencyKeys);
    }
}
