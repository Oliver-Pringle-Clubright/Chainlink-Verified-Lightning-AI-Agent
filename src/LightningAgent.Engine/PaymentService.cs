using System.Security.Cryptography;
using LightningAgent.Core.Enums;
using LightningAgent.Core.Interfaces.Data;
using LightningAgent.Core.Interfaces.Services;
using LightningAgent.Core.Models;
using Microsoft.Extensions.Logging;

namespace LightningAgent.Engine;

public class PaymentService : IPaymentService
{
    private readonly IAgentRepository _agentRepo;
    private readonly IEscrowRepository _escrowRepo;
    private readonly IPaymentRepository _paymentRepo;
    private readonly IMilestoneRepository _milestoneRepo;
    private readonly ILightningClient _lightning;
    private readonly IEventPublisher _eventPublisher;
    private readonly ILogger<PaymentService> _logger;

    public PaymentService(
        IAgentRepository agentRepo,
        IEscrowRepository escrowRepo,
        IPaymentRepository paymentRepo,
        IMilestoneRepository milestoneRepo,
        ILightningClient lightning,
        IEventPublisher eventPublisher,
        ILogger<PaymentService> logger)
    {
        _agentRepo = agentRepo;
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
            "Initiating payment of {AmountSats} sats for agent {AgentId}: {Memo}",
            amountSats, agentId, memo);

        var agent = await _agentRepo.GetByIdAsync(agentId, ct)
            ?? throw new InvalidOperationException($"Agent {agentId} not found");

        var payment = new Payment
        {
            AgentId = agentId,
            AmountSats = amountSats,
            PaymentType = PaymentType.Streaming,
            Status = PaymentStatus.InFlight,
            CreatedAt = DateTime.UtcNow
        };

        payment.Id = await _paymentRepo.CreateAsync(payment, ct);

        try
        {
            // Generate a preimage/hash pair for the payment record
            var preimage = RandomNumberGenerator.GetBytes(32);
            var paymentHash = SHA256.HashData(preimage);
            var paymentHashHex = Convert.ToHexString(paymentHash).ToLowerInvariant();

            // Create a HODL invoice on our LND node for the payment
            var invoice = await _lightning.CreateHodlInvoiceAsync(
                amountSats,
                memo,
                paymentHash,
                3600,
                ct);

            payment.PaymentHash = paymentHashHex;

            // Settle the invoice — if this fails, cancel the invoice to avoid it being stuck held
            try
            {
                await _lightning.SettleInvoiceAsync(preimage, ct);
            }
            catch (Exception settleEx)
            {
                _logger.LogError(
                    settleEx,
                    "Settlement failed for payment {PaymentId} to agent {AgentId}, cancelling invoice",
                    payment.Id, agentId);

                try { await _lightning.CancelInvoiceAsync(paymentHash, ct); }
                catch (Exception cancelEx)
                {
                    _logger.LogWarning(cancelEx, "Failed to cancel stuck invoice {PaymentHash}", paymentHashHex);
                }

                payment.Status = PaymentStatus.Failed;
                await _paymentRepo.UpdateAsync(payment, ct);
                return payment;
            }

            payment.Status = PaymentStatus.Settled;
            payment.SettledAt = DateTime.UtcNow;

            if (string.IsNullOrWhiteSpace(agent.WalletPubkey))
            {
                _logger.LogWarning(
                    "Agent {AgentId} has no WalletPubkey configured — payment {PaymentId} recorded locally. " +
                    "For cross-node payments, configure the agent's Lightning node pubkey.",
                    agentId, payment.Id);
            }
            else
            {
                _logger.LogInformation(
                    "Payment {PaymentId} of {AmountSats} sats settled for agent {AgentId}",
                    payment.Id, amountSats, agentId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Payment {PaymentId} failed for agent {AgentId}: {Error}",
                payment.Id, agentId, ex.Message);

            payment.Status = PaymentStatus.Failed;
        }

        await _paymentRepo.UpdateAsync(payment, ct);
        await _eventPublisher.PublishPaymentSentAsync(payment.Id, agentId, amountSats, ct);

        return payment;
    }
}
