using LightningAgentMarketPlace.Core.Enums;
using LightningAgentMarketPlace.Core.Interfaces.Data;
using LightningAgentMarketPlace.Core.Interfaces.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LightningAgentMarketPlace.Engine.BackgroundJobs;

/// <summary>
/// Background service that periodically retries escrows stuck in PendingChannel status
/// by attempting to create HODL invoices via the Lightning client.
/// </summary>
public class EscrowRetryService : BackgroundService
{
    private static readonly TimeSpan RetryInterval = TimeSpan.FromMinutes(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EscrowRetryService> _logger;

    public EscrowRetryService(
        IServiceScopeFactory scopeFactory,
        ILogger<EscrowRetryService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("EscrowRetryService started (interval: {Interval})", RetryInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var escrowRepo = scope.ServiceProvider.GetRequiredService<IEscrowRepository>();
                var milestoneRepo = scope.ServiceProvider.GetRequiredService<IMilestoneRepository>();
                var lightning = scope.ServiceProvider.GetRequiredService<ILightningClient>();

                var pendingEscrows = await escrowRepo.GetByStatusAsync(
                    EscrowStatus.PendingChannel, stoppingToken);

                if (pendingEscrows.Count > 0)
                {
                    _logger.LogInformation(
                        "EscrowRetryService found {Count} escrows with PendingChannel status",
                        pendingEscrows.Count);
                }

                foreach (var escrow in pendingEscrows)
                {
                    stoppingToken.ThrowIfCancellationRequested();

                    try
                    {
                        var paymentHash = Convert.FromHexString(escrow.PaymentHash);

                        var milestone = await milestoneRepo.GetByIdAsync(escrow.MilestoneId, stoppingToken);
                        if (milestone is null)
                        {
                            _logger.LogWarning(
                                "Milestone {MilestoneId} not found for escrow {EscrowId}, skipping retry",
                                escrow.MilestoneId, escrow.Id);
                            continue;
                        }

                        var hodlInvoice = await lightning.CreateHodlInvoiceAsync(
                            escrow.AmountSats,
                            $"Escrow for milestone {escrow.MilestoneId}",
                            paymentHash,
                            (int)(escrow.ExpiresAt - DateTime.UtcNow).TotalSeconds,
                            stoppingToken);

                        // Success: update escrow to Held with the invoice
                        escrow.Status = EscrowStatus.Held;
                        escrow.HodlInvoice = hodlInvoice.PaymentRequest;
                        escrow.ExpiresAt = hodlInvoice.ExpiresAt;
                        await escrowRepo.UpdateAsync(escrow, stoppingToken);

                        _logger.LogInformation(
                            "Escrow {EscrowId} for milestone {MilestoneId} successfully upgraded from PendingChannel to Held",
                            escrow.Id, escrow.MilestoneId);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(
                            ex,
                            "HODL invoice creation still failing for escrow {EscrowId} (milestone {MilestoneId}), will retry next cycle",
                            escrow.Id, escrow.MilestoneId);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "EscrowRetryService encountered an error during retry cycle");
            }

            try
            {
                await Task.Delay(RetryInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("EscrowRetryService stopped");
    }
}
