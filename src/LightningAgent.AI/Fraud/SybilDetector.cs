using LightningAgent.AI.Prompts;
using LightningAgent.Core.Interfaces.Services;
using LightningAgent.Core.Models.AI;
using Microsoft.Extensions.Logging;

namespace LightningAgent.AI.Fraud;

public class SybilDetector
{
    private readonly IClaudeAiClient _claude;
    private readonly ILogger<SybilDetector> _logger;

    public SybilDetector(IClaudeAiClient claude, ILogger<SybilDetector> logger)
    {
        _claude = claude;
        _logger = logger;
    }

    public async Task<FraudReport?> DetectAsync(
        string agentBehaviorData,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Running sybil detection analysis");

        var userMessage = $"""
            ## Agent Behavior Data to Analyze
            {agentBehaviorData}

            Analyze this behavior data for signs of sybil attacks or coordinated fake agent activity.
            Look for: similar response patterns, shared infrastructure indicators, timing correlations,
            and identity reuse patterns.
            """;

        var report = await _claude.SendStructuredRequestAsync<FraudReport>(
            PromptTemplates.FraudDetection,
            userMessage,
            ct);

        if (report.Confidence > 0.5)
        {
            _logger.LogWarning(
                "Sybil attack detected with confidence {Confidence}: {FraudType}",
                report.Confidence,
                report.FraudType);
            return report;
        }

        _logger.LogInformation(
            "No sybil attack detected (confidence {Confidence})",
            report.Confidence);
        return null;
    }
}
