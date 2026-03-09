using LightningAgent.Core.Enums;
using LightningAgent.Core.Interfaces.Data;
using LightningAgent.Core.Interfaces.Services;
using LightningAgent.Core.Models;
using Microsoft.Extensions.Logging;

namespace LightningAgent.Engine;

public class PaymentService : IPaymentService
{
    private readonly IEscrowRepository _escrowRepo;
    private readonly IPaymentRepository _paymentRepo;
    private readonly IMilestoneRepository _milestoneRepo;
    private readonly ILightningClient _lightning;
    private readonly IEventPublisher _eventPublisher;
    private readonly ILogger<PaymentService> _logger;

    public PaymentService(
        IEscrowRepository escrowRepo,
        IPaymentRepository paymentRepo,
        IMilestoneRepository milestoneRepo,
        ILightningClient lightning,
        IEventPublisher eventPublisher,
        ILogger<PaymentService> logger)
    {
        _escrowRepo = escrowRepo;
        _paymentRepo = paymentRepo;
        _milestoneRepo = milestoneRepo;
        _lightning = lightning;
        _eventPublisher = eventPublisher;
        _logger = logger;
    }

    public async Task<Payment> ProcessMilestonePaymentAsync(int milestoneId, CancellationToken ct = default)
    {
        var milestone = await _milestoneRepo.GetByIdAsync(milestoneId, ct)
            ?? throw new InvalidOperationException($"Milestone {milestoneId} not found");

        var escrow = await _escrowRepo.GetByMilestoneIdAsync(milestoneId, ct);

        Payment payment;

        if (escrow is not null && escrow.Status == EscrowStatus.Settled)
        {
            _logger.LogInformation(
                "Processing escrow-based payment for milestone {MilestoneId} (escrow {EscrowId})",
                milestoneId, escrow.Id);

            payment = new Payment
            {
                EscrowId = escrow.Id,
                TaskId = milestone.TaskId,
                MilestoneId = milestone.Id,
                AgentId = 0, // To be set by caller or resolved upstream
                AmountSats = escrow.AmountSats,
                PaymentHash = escrow.PaymentHash,
                PaymentType = PaymentType.Escrow,
                Status = PaymentStatus.Settled,
                CreatedAt = DateTime.UtcNow,
                SettledAt = DateTime.UtcNow
            };
        }
        else
        {
            _logger.LogInformation(
                "Processing direct payment for milestone {MilestoneId} (no settled escrow)",
                milestoneId);

            payment = new Payment
            {
                TaskId = milestone.TaskId,
                MilestoneId = milestone.Id,
                AgentId = 0,
                AmountSats = milestone.PayoutSats,
                PaymentType = PaymentType.Direct,
                Status = PaymentStatus.Pending,
                CreatedAt = DateTime.UtcNow
            };
        }

        payment.Id = await _paymentRepo.CreateAsync(payment, ct);

        // Mark the milestone as paid
        milestone.PaidAt = DateTime.UtcNow;
        await _milestoneRepo.UpdateAsync(milestone, ct);

        _logger.LogInformation(
            "Payment {PaymentId} ({PaymentType}) created for milestone {MilestoneId}",
            payment.Id, payment.PaymentType, milestoneId);

        await _eventPublisher.PublishPaymentSentAsync(payment.Id, payment.AgentId, payment.AmountSats, ct);

        return payment;
    }

    public async Task<Payment> StreamPaymentAsync(int agentId, long amountSats, string memo, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Initiating streaming payment of {AmountSats} sats for agent {AgentId}: {Memo}",
            amountSats, agentId, memo);

        var payment = new Payment
        {
            AgentId = agentId,
            AmountSats = amountSats,
            PaymentType = PaymentType.Streaming,
            Status = PaymentStatus.InFlight,
            CreatedAt = DateTime.UtcNow
        };

        payment.Id = await _paymentRepo.CreateAsync(payment, ct);

        // ──────────────────────────────────────────────────────────────
        // PRODUCTION FLOW (not yet wired):
        //
        // 1. Look up the agent's Lightning node pubkey / wallet address
        // 2. Request a BOLT11 invoice from the agent's node:
        //      var invoice = await agentNode.CreateInvoiceAsync(amountSats, memo, ct);
        // 3. Pay the invoice via our Lightning node:
        //      var result = await _lightning.SendPaymentAsync(invoice.PaymentRequest, ct);
        // 4. On success, update payment with the real PaymentHash from result:
        //      payment.PaymentHash = result.PaymentHash;
        //      payment.Status = PaymentStatus.Settled;
        //      payment.SettledAt = DateTime.UtcNow;
        // 5. On failure, mark payment as Failed and log the error.
        //
        // The simulation below stands in for steps 2-4 until a real
        // Lightning integration is configured.
        // ──────────────────────────────────────────────────────────────

        _logger.LogWarning(
            "Streaming payment {PaymentId}: SIMULATION MODE — in production this would generate " +
            "and pay a real BOLT11 invoice for {AmountSats} sats to agent {AgentId}",
            payment.Id, amountSats, agentId);

        // Simulate settlement
        payment.Status = PaymentStatus.Settled;
        payment.SettledAt = DateTime.UtcNow;
        await _paymentRepo.UpdateAsync(payment, ct);

        _logger.LogInformation(
            "Streaming payment {PaymentId} settled (simulated) for agent {AgentId}",
            payment.Id, agentId);

        await _eventPublisher.PublishPaymentSentAsync(payment.Id, agentId, amountSats, ct);

        return payment;
    }
}
