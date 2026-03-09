using LightningAgent.AI.Prompts;
using LightningAgent.Core.Interfaces.Services;
using Microsoft.Extensions.Logging;

namespace LightningAgent.AI.Orchestrator;

public class DeliverableAssembler
{
    private readonly IClaudeAiClient _claude;
    private readonly ILogger<DeliverableAssembler> _logger;

    public DeliverableAssembler(IClaudeAiClient claude, ILogger<DeliverableAssembler> logger)
    {
        _claude = claude;
        _logger = logger;
    }

    public async Task<string> AssembleAsync(
        string originalTaskDescription,
        List<string> subtaskOutputs,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Assembling deliverable from {Count} subtask outputs",
            subtaskOutputs.Count);

        var outputsSection = string.Join(
            "\n\n---\n\n",
            subtaskOutputs.Select((output, i) => $"### Subtask {i + 1} Output\n{output}"));

        var userMessage = $"""
            ## Original Task Description
            {originalTaskDescription}

            ## Subtask Outputs to Assemble
            {outputsSection}

            Combine these outputs into a single cohesive deliverable that addresses the original task.
            """;

        var result = await _claude.SendMessageAsync(
            PromptTemplates.DeliverableAssembly,
            userMessage,
            ct);

        _logger.LogInformation("Deliverable assembly completed");

        return result;
    }
}
