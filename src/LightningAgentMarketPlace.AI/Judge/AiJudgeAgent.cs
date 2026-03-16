using LightningAgentMarketPlace.AI.Prompts;
using LightningAgentMarketPlace.Core.Interfaces.Services;
using LightningAgentMarketPlace.Core.Models.AI;
using Microsoft.Extensions.Logging;

namespace LightningAgentMarketPlace.AI.Judge;

public class AiJudgeAgent
{
    private readonly IClaudeAiClient _claude;
    private readonly ILogger<AiJudgeAgent> _logger;

    public AiJudgeAgent(IClaudeAiClient claude, ILogger<AiJudgeAgent> logger)
    {
        _claude = claude;
        _logger = logger;
    }

    public async Task<JudgeVerdict> JudgeOutputAsync(
        string taskDescription,
        string verificationCriteria,
        string agentOutput,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Starting AI judge evaluation for task");

        var userMessage = $"""
            ## Task Description
            {taskDescription}

            ## Verification Criteria
            {verificationCriteria}

            ## Agent Output to Evaluate
            {agentOutput}
            """;

        var verdict = await _claude.SendStructuredRequestAsync<JudgeVerdict>(
            PromptTemplates.AiJudge,
            userMessage,
            ct);

        _logger.LogInformation(
            "AI judge verdict: Score={Score}, Passed={Passed}",
            verdict.Score,
            verdict.Passed);

        return verdict;
    }
}
