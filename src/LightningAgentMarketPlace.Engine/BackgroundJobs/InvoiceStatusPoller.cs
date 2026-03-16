using LightningAgentMarketPlace.Core.Enums;
using LightningAgentMarketPlace.Core.Interfaces.Data;
using LightningAgentMarketPlace.Core.Interfaces.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LightningAgentMarketPlace.Engine.BackgroundJobs;

/// <summary>
/// Background service that polls held escrows and checks their invoice status
/// via the LND API. Settles or cancels escrows based on invoice state changes.
/// </summary>
public class InvoiceStatusPoller : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<InvoiceStatusPoller> _logger;

    public InvoiceStatusPoller(
        IServiceScopeFactory scopeFactory,
        ILogger<InvoiceStatusPoller> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("InvoiceStatusPoller started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var escrowRepo = scope.ServiceProvider.GetRequiredService<IEscrowRepository>();
                var lightningClient = scope.ServiceProvider.GetRequiredService<ILightningClient>();

                var heldEscrows = await escrowRepo.GetByStatusAsync(EscrowStatus.Held, stoppingToken);

                if (heldEscrows.Count == 0)
                {
                    _logger.LogDebug("InvoiceStatusPoller found no held escrows, skipping");
                }
                else
                {
                    _logger.LogDebug(
                        "InvoiceStatusPoller checking {Count} held escrows", heldEscrows.Count);

                    foreach (var escrow in heldEscrows)
                    {
                        try
                        {
                            await CheckAndUpdateEscrowAsync(
                                escrow, escrowRepo, lightningClient, stoppingToken);
                        }
                        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                        {
                            throw; // Let the outer handler deal with shutdown
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(
                                ex,
                                "InvoiceStatusPoller failed to check escrow {EscrowId} (hash={PaymentHash})",
                                escrow.Id, escrow.PaymentHash);
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "InvoiceStatusPoller encountered an error during poll cycle");
            }

            try
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("InvoiceStatusPoller stopped");
    }

    private async Task CheckAndUpdateEscrowAsync(
        Core.Models.Escrow escrow,
        IEscrowRepository escrowRepo,
        ILightningClient lightningClient,
        CancellationToken ct)
    {
        var paymentHashBytes = Convert.FromHexString(escrow.PaymentHash);
        var invoiceState = await lightningClient.GetInvoiceStateAsync(paymentHashBytes, ct);

        if (string.Equals(invoiceState.State, "SETTLED", StringComparison.OrdinalIgnoreCase))
        {
            escrow.Status = EscrowStatus.Settled;
            escrow.SettledAt = invoiceState.SettledAt ?? DateTime.UtcNow;
            await escrowRepo.UpdateAsync(escrow, ct);

            _logger.LogInformation(
                "InvoiceStatusPoller settled escrow {EscrowId} (hash={PaymentHash}, settledAt={SettledAt})",
                escrow.Id, escrow.PaymentHash, escrow.SettledAt);
        }
        else if (string.Equals(invoiceState.State, "CANCELLED", StringComparison.OrdinalIgnoreCase)
              || string.Equals(invoiceState.State, "CANCELED", StringComparison.OrdinalIgnoreCase))
        {
            escrow.Status = EscrowStatus.Cancelled;
            await escrowRepo.UpdateAsync(escrow, ct);

            _logger.LogInformation(
                "InvoiceStatusPoller cancelled escrow {EscrowId} (hash={PaymentHash}) — invoice was cancelled",
                escrow.Id, escrow.PaymentHash);
        }
        else
        {
            _logger.LogDebug(
                "InvoiceStatusPoller escrow {EscrowId} (hash={PaymentHash}) still in state '{State}'",
                escrow.Id, escrow.PaymentHash, invoiceState.State);
        }
    }
}
