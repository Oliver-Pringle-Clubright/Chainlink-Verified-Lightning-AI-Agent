using System.Text;
using LightningAgent.AI.Judge;
using LightningAgent.Core.Enums;
using LightningAgent.Core.Interfaces.Services;
using LightningAgent.Core.Models;
using Microsoft.Extensions.Logging;

namespace LightningAgent.Verification.Strategies;

public class AiJudgeVerification : IVerificationStrategy
{
    private readonly AiJudgeAgent _judge;
    private readonly ILogger<AiJudgeVerification> _logger;

    public AiJudgeVerification(AiJudgeAgent judge, ILogger<AiJudgeVerification> logger)
    {
        _judge = judge;
        _logger = logger;
    }

    public VerificationStrategyType StrategyType => VerificationStrategyType.AiJudge;

    public bool CanHandle(TaskType taskType) => true;

    public async Task<VerificationResult> VerifyAsync(
        Milestone milestone,
        byte[] agentOutput,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Running AI judge verification for milestone {MilestoneId}",
            milestone.Id);

        var outputText = Encoding.UTF8.GetString(agentOutput);
        var taskDescription = milestone.Description ?? milestone.Title;

        var verdict = await _judge.JudgeOutputAsync(
            taskDescription,
            milestone.VerificationCriteria,
            outputText,
            ct);

        return new VerificationResult(
            Score: verdict.Score,
            Passed: verdict.Passed,
            Details: verdict.Reasoning,
            StrategyType: VerificationStrategyType.AiJudge);
    }
}
