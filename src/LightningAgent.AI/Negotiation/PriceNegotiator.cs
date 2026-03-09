using LightningAgent.AI.Prompts;
using LightningAgent.Core.Interfaces.Services;
using LightningAgent.Core.Models.AI;
using Microsoft.Extensions.Logging;

namespace LightningAgent.AI.Negotiation;

public class PriceNegotiator
{
    private readonly IClaudeAiClient _claude;
    private readonly ILogger<PriceNegotiator> _logger;

    public PriceNegotiator(IClaudeAiClient claude, ILogger<PriceNegotiator> logger)
    {
        _claude = claude;
        _logger = logger;
    }

    public async Task<NegotiationProposal> NegotiateAsync(
        string taskDescription,
        long requesterBudgetSats,
        long workerAskingSats,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Negotiating price: requester budget={RequesterBudget} sats, worker asking={WorkerAsking} sats",
            requesterBudgetSats,
            workerAskingSats);

        var userMessage = $"""
            ## Task Description
            {taskDescription}

            ## Negotiation Context
            - Requester's budget: {requesterBudgetSats} satoshis
            - Worker's asking price: {workerAskingSats} satoshis
            - Difference: {Math.Abs(workerAskingSats - requesterBudgetSats)} satoshis

            Analyze the task complexity and propose a fair price that balances both parties' interests.
            """;

        var proposal = await _claude.SendStructuredRequestAsync<NegotiationProposal>(
            PromptTemplates.PriceNegotiation,
            userMessage,
            ct);

        _logger.LogInformation(
            "Negotiation proposal: {ProposedPrice} sats, shouldAccept={ShouldAccept}",
            proposal.ProposedPriceSats,
            proposal.ShouldAccept);

        return proposal;
    }
}
