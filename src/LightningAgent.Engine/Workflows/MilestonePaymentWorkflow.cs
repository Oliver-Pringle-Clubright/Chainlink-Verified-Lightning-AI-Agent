using LightningAgent.Core.Interfaces.Data;
using LightningAgent.Core.Interfaces.Services;
using LightningAgent.Core.Models;
using Microsoft.Extensions.Logging;

namespace LightningAgent.Engine.Workflows;

public class MilestonePaymentWorkflow
{
    private readonly IEscrowManager _escrowManager;
    private readonly IEscrowRepository _escrowRepo;
    private readonly IPaymentService _paymentService;
    private readonly IMilestoneRepository _milestoneRepo;
    private readonly ILogger<MilestonePaymentWorkflow> _logger;

    public MilestonePaymentWorkflow(
        IEscrowManager escrowManager,
        IEscrowRepository escrowRepo,
        IPaymentService paymentService,
        IMilestoneRepository milestoneRepo,
        ILogger<MilestonePaymentWorkflow> logger)
    {
        _escrowManager = escrowManager;
        _escrowRepo = escrowRepo;
        _paymentService = paymentService;
        _milestoneRepo = milestoneRepo;
        _logger = logger;
    }

    /// <summary>
    /// Creates a HODL-invoice-based escrow for the given milestone.
    /// </summary>
    public async Task<Escrow> CreateEscrowForMilestoneAsync(
        Milestone milestone,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Creating escrow for milestone {MilestoneId} ({Sats} sats)",
            milestone.Id, milestone.PayoutSats);

        var escrow = await _escrowManager.CreateEscrowAsync(milestone, ct);

        _logger.LogInformation(
            "Escrow {EscrowId} created for milestone {MilestoneId}",
            escrow.Id, milestone.Id);

        return escrow;
    }

    /// <summary>
    /// Settles the escrow for a milestone using the stored preimage,
    /// processes the payment, and updates the milestone PaidAt timestamp.
    /// </summary>
    public async Task<Payment> SettleAndPayAsync(int milestoneId, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Settling and paying milestone {MilestoneId}", milestoneId);

        // 1. Get milestone
        var milestone = await _milestoneRepo.GetByIdAsync(milestoneId, ct)
            ?? throw new InvalidOperationException($"Milestone {milestoneId} not found");

        // 2. Get escrow by milestoneId
        var escrow = await _escrowRepo.GetByMilestoneIdAsync(milestoneId, ct)
            ?? throw new InvalidOperationException(
                $"No escrow found for milestone {milestoneId}");

        // 3. Settle escrow with stored preimage
        var preimage = Convert.FromHexString(escrow.PaymentPreimage ?? string.Empty);
        var settled = await _escrowManager.SettleEscrowAsync(escrow.Id, preimage, ct);
        if (!settled)
        {
            throw new InvalidOperationException(
                $"Failed to settle escrow {escrow.Id} for milestone {milestoneId}");
        }

        _logger.LogInformation(
            "Escrow {EscrowId} settled for milestone {MilestoneId}",
            escrow.Id, milestoneId);

        // 4. Process payment
        var payment = await _paymentService.ProcessMilestonePaymentAsync(milestoneId, ct);

        // 5. Update milestone PaidAt
        milestone.PaidAt = DateTime.UtcNow;
        await _milestoneRepo.UpdateAsync(milestone, ct);

        _logger.LogInformation(
            "Payment {PaymentId} completed for milestone {MilestoneId} ({Sats} sats)",
            payment.Id, milestoneId, payment.AmountSats);

        return payment;
    }
}
