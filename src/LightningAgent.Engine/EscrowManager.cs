using System.Security.Cryptography;
using LightningAgent.Core.Configuration;
using LightningAgent.Core.Enums;
using LightningAgent.Core.Interfaces.Data;
using LightningAgent.Core.Interfaces.Services;
using LightningAgent.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LightningAgent.Engine;

public class EscrowManager : IEscrowManager
{
    private readonly ILightningClient _lightning;
    private readonly IEscrowRepository _escrowRepo;
    private readonly IMilestoneRepository _milestoneRepo;
    private readonly IEventPublisher _eventPublisher;
    private readonly EscrowSettings _settings;
    private readonly ILogger<EscrowManager> _logger;

    public EscrowManager(
        ILightningClient lightning,
        IEscrowRepository escrowRepo,
        IMilestoneRepository milestoneRepo,
        IEventPublisher eventPublisher,
        IOptions<EscrowSettings> settings,
        ILogger<EscrowManager> logger)
    {
        _lightning = lightning;
        _escrowRepo = escrowRepo;
        _milestoneRepo = milestoneRepo;
        _eventPublisher = eventPublisher;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<Escrow> CreateEscrowAsync(Milestone milestone, CancellationToken ct = default)
    {
        // Generate a random 32-byte preimage
        var preimage = RandomNumberGenerator.GetBytes(32);

        // Compute SHA256 hash of preimage (this is the payment hash)
        var paymentHash = SHA256.HashData(preimage);

        var paymentHashHex = Convert.ToHexString(paymentHash).ToLowerInvariant();
        var preimageHex = Convert.ToHexString(preimage).ToLowerInvariant();

        _logger.LogInformation(
            "Creating HODL invoice for milestone {MilestoneId} with {AmountSats} sats",
            milestone.Id, milestone.PayoutSats);

        // Create the HODL invoice on the Lightning node
        var hodlInvoice = await _lightning.CreateHodlInvoiceAsync(
            milestone.PayoutSats,
            $"Escrow for milestone {milestone.Id}",
            paymentHash,
            _settings.DefaultExpirySec,
            ct);

        // Build the Escrow entity
        var escrow = new Escrow
        {
            MilestoneId = milestone.Id,
            TaskId = milestone.TaskId,
            AmountSats = milestone.PayoutSats,
            PaymentHash = paymentHashHex,
            PaymentPreimage = preimageHex,
            Status = EscrowStatus.Held,
            HodlInvoice = hodlInvoice.PaymentRequest,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = hodlInvoice.ExpiresAt
        };

        // Persist escrow
        escrow.Id = await _escrowRepo.CreateAsync(escrow, ct);

        // Update milestone with the payment hash
        milestone.InvoicePaymentHash = paymentHashHex;
        await _milestoneRepo.UpdateAsync(milestone, ct);

        _logger.LogInformation(
            "Escrow {EscrowId} created for milestone {MilestoneId} (hash={PaymentHash})",
            escrow.Id, milestone.Id, paymentHashHex);

        return escrow;
    }

    public async Task<bool> SettleEscrowAsync(int escrowId, byte[] preimage, CancellationToken ct = default)
    {
        var escrow = await _escrowRepo.GetByIdAsync(escrowId, ct);
        if (escrow is null)
        {
            _logger.LogWarning("Escrow {EscrowId} not found", escrowId);
            return false;
        }

        if (escrow.Status != EscrowStatus.Held)
        {
            _logger.LogWarning(
                "Cannot settle escrow {EscrowId}: current status is {Status}, expected Held",
                escrowId, escrow.Status);
            return false;
        }

        _logger.LogInformation("Settling escrow {EscrowId}", escrowId);

        var settled = await _lightning.SettleInvoiceAsync(preimage, ct);
        if (!settled)
        {
            _logger.LogError("Lightning node failed to settle invoice for escrow {EscrowId}", escrowId);
            return false;
        }

        escrow.Status = EscrowStatus.Settled;
        escrow.SettledAt = DateTime.UtcNow;
        escrow.PaymentPreimage = Convert.ToHexString(preimage).ToLowerInvariant();

        await _escrowRepo.UpdateAsync(escrow, ct);

        _logger.LogInformation("Escrow {EscrowId} settled successfully", escrowId);

        await _eventPublisher.PublishEscrowSettledAsync(escrow.Id, escrow.MilestoneId, escrow.AmountSats, ct);

        return true;
    }

    public async Task<bool> CancelEscrowAsync(int escrowId, CancellationToken ct = default)
    {
        var escrow = await _escrowRepo.GetByIdAsync(escrowId, ct);
        if (escrow is null)
        {
            _logger.LogWarning("Escrow {EscrowId} not found", escrowId);
            return false;
        }

        if (escrow.Status != EscrowStatus.Held)
        {
            _logger.LogWarning(
                "Cannot cancel escrow {EscrowId}: current status is {Status}, expected Held",
                escrowId, escrow.Status);
            return false;
        }

        _logger.LogInformation("Cancelling escrow {EscrowId}", escrowId);

        var paymentHashBytes = Convert.FromHexString(escrow.PaymentHash);
        var cancelled = await _lightning.CancelInvoiceAsync(paymentHashBytes, ct);
        if (!cancelled)
        {
            _logger.LogError("Lightning node failed to cancel invoice for escrow {EscrowId}", escrowId);
            return false;
        }

        escrow.Status = EscrowStatus.Cancelled;
        await _escrowRepo.UpdateAsync(escrow, ct);

        _logger.LogInformation("Escrow {EscrowId} cancelled successfully", escrowId);
        return true;
    }

    public async Task<int> CheckExpiredEscrowsAsync(CancellationToken ct = default)
    {
        var heldEscrows = await _escrowRepo.GetByStatusAsync(EscrowStatus.Held, ct);

        var expired = heldEscrows.Where(e => e.ExpiresAt < DateTime.UtcNow).ToList();

        if (expired.Count == 0)
        {
            _logger.LogDebug("No expired escrows found");
            return 0;
        }

        _logger.LogInformation("Found {Count} expired escrows to cancel", expired.Count);

        var cancelledCount = 0;
        foreach (var escrow in expired)
        {
            try
            {
                var result = await CancelEscrowAsync(escrow.Id, ct);
                if (result)
                    cancelledCount++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to cancel expired escrow {EscrowId}", escrow.Id);
            }
        }

        return cancelledCount;
    }
}
