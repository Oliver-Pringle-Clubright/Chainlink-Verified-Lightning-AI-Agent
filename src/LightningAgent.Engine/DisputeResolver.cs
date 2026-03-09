using LightningAgent.Core.Enums;
using LightningAgent.Core.Interfaces.Data;
using LightningAgent.Core.Interfaces.Services;
using LightningAgent.Core.Models;
using Microsoft.Extensions.Logging;

namespace LightningAgent.Engine;

/// <summary>
/// Handles opening, arbitrating, and resolving disputes between task clients and agents.
/// </summary>
public class DisputeResolver : IDisputeResolver
{
    private readonly IDisputeRepository _disputeRepo;
    private readonly IAgentMatcher _agentMatcher;
    private readonly IEscrowManager _escrowManager;
    private readonly IReputationService _reputationService;
    private readonly IEventPublisher _eventPublisher;
    private readonly ILogger<DisputeResolver> _logger;

    public DisputeResolver(
        IDisputeRepository disputeRepo,
        IAgentMatcher agentMatcher,
        IEscrowManager escrowManager,
        IReputationService reputationService,
        IEventPublisher eventPublisher,
        ILogger<DisputeResolver> logger)
    {
        _disputeRepo = disputeRepo;
        _agentMatcher = agentMatcher;
        _escrowManager = escrowManager;
        _reputationService = reputationService;
        _eventPublisher = eventPublisher;
        _logger = logger;
    }

    public async Task<Dispute> OpenDisputeAsync(
        int taskId,
        int? milestoneId,
        string initiatedBy,
        string initiatorId,
        string reason,
        long amountSats,
        CancellationToken ct = default)
    {
        var dispute = new Dispute
        {
            TaskId = taskId,
            MilestoneId = milestoneId,
            InitiatedBy = initiatedBy,
            InitiatorId = initiatorId,
            Reason = reason,
            AmountDisputedSats = amountSats,
            Status = DisputeStatus.Open,
            CreatedAt = DateTime.UtcNow
        };

        dispute.Id = await _disputeRepo.CreateAsync(dispute, ct);

        _logger.LogInformation(
            "Dispute {DisputeId} opened for task {TaskId} (milestone={MilestoneId}) by {InitiatedBy} {InitiatorId}: {Reason}. Amount: {AmountSats} sats",
            dispute.Id, taskId, milestoneId, initiatedBy, initiatorId, reason, amountSats);

        await _eventPublisher.PublishDisputeOpenedAsync(dispute.Id, taskId, reason, ct);

        return dispute;
    }

    public async Task<int> AssignArbiterAsync(int disputeId, CancellationToken ct = default)
    {
        var dispute = await _disputeRepo.GetByIdAsync(disputeId, ct)
            ?? throw new InvalidOperationException($"Dispute {disputeId} not found");

        // Find top agents with highest reputation using Text skill type
        // (as a proxy for analysis / arbitration capability)
        var topAgents = await _reputationService.GetTopAgentsAsync(10, SkillType.TextWriting, ct);

        // Pick the first agent that isn't involved in the dispute
        var arbiter = topAgents.FirstOrDefault(a =>
            a.AgentId.ToString() != dispute.InitiatorId);

        if (arbiter is null)
        {
            _logger.LogWarning(
                "No suitable arbiter found for dispute {DisputeId}. Falling back to top-ranked agent",
                disputeId);

            // Fall back to any top agent
            var allTopAgents = await _reputationService.GetTopAgentsAsync(10, ct: ct);
            arbiter = allTopAgents.FirstOrDefault(a =>
                a.AgentId.ToString() != dispute.InitiatorId);

            if (arbiter is null)
                throw new InvalidOperationException($"No available arbiter agents for dispute {disputeId}");
        }

        dispute.ArbiterAgentId = arbiter.AgentId;
        dispute.Status = DisputeStatus.UnderReview;

        await _disputeRepo.UpdateAsync(dispute, ct);

        _logger.LogInformation(
            "Arbiter agent {ArbiterId} assigned to dispute {DisputeId}. Status changed to UnderReview",
            arbiter.AgentId, disputeId);

        return arbiter.AgentId;
    }

    public async Task<Dispute> ResolveDisputeAsync(
        int disputeId,
        string resolution,
        CancellationToken ct = default)
    {
        var dispute = await _disputeRepo.GetByIdAsync(disputeId, ct)
            ?? throw new InvalidOperationException($"Dispute {disputeId} not found");

        dispute.Status = DisputeStatus.Resolved;
        dispute.Resolution = resolution;
        dispute.ResolvedAt = DateTime.UtcNow;

        await _disputeRepo.UpdateAsync(dispute, ct);

        _logger.LogInformation(
            "Dispute {DisputeId} resolved: {Resolution}",
            disputeId, resolution);

        // Determine escrow action based on the resolution text
        var lowerResolution = resolution.ToLowerInvariant();

        if (dispute.MilestoneId.HasValue)
        {
            if (lowerResolution.Contains("refund") || lowerResolution.Contains("cancel"))
            {
                _logger.LogInformation(
                    "Resolution indicates refund/cancel. Cancelling escrow for milestone {MilestoneId}",
                    dispute.MilestoneId.Value);

                // Find the escrow by milestone and cancel it
                // CancelEscrowAsync expects the escrow ID; we use milestone-based lookup
                // The escrow ID for a milestone typically matches the milestone ID in the repository
                await _escrowManager.CancelEscrowAsync(dispute.MilestoneId.Value, ct);
            }
            else if (lowerResolution.Contains("pay") || lowerResolution.Contains("settle"))
            {
                _logger.LogInformation(
                    "Resolution indicates payment/settlement. Settling escrow for milestone {MilestoneId}",
                    dispute.MilestoneId.Value);

                // Settle with empty preimage; the EscrowManager will handle
                // looking up the actual preimage from the stored escrow record
                await _escrowManager.SettleEscrowAsync(dispute.MilestoneId.Value, Array.Empty<byte>(), ct);
            }
        }

        // Update reputation for the losing party (increment dispute count)
        if (!string.IsNullOrEmpty(dispute.InitiatorId) && int.TryParse(dispute.InitiatorId, out var losingAgentId))
        {
            _logger.LogInformation(
                "Updating reputation for agent {AgentId} due to dispute resolution",
                losingAgentId);

            await _reputationService.UpdateReputationAsync(
                losingAgentId,
                taskCompleted: false,
                verificationPassed: false,
                responseTimeSec: 0,
                ct);
        }

        return dispute;
    }
}
