using LightningAgentMarketPlace.AI.Prompts;
using LightningAgentMarketPlace.Core.Interfaces.Services;
using LightningAgentMarketPlace.Core.Models.AI;
using Microsoft.Extensions.Logging;

namespace LightningAgentMarketPlace.AI.Fraud;

public class RecycledOutputDetector
{
    private readonly IClaudeAiClient _claude;
    private readonly ILogger<RecycledOutputDetector> _logger;

    public RecycledOutputDetector(IClaudeAiClient claude, ILogger<RecycledOutputDetector> logger)
    {
        _claude = claude;
        _logger = logger;
    }

    public async Task<FraudReport?> DetectAsync(
        string currentOutput,
        List<string> previousOutputs,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Running recycled output detection against {Count} previous outputs",
            previousOutputs.Count);

        var previousSection = string.Join(
            "\n\n---\n\n",
            previousOutputs.Select((output, i) => $"### Previous Output {i + 1}\n{output}"));

        var userMessage = $"""
            ## Current Output Under Review
            {currentOutput}

            ## Previous Outputs for Comparison
            {previousSection}

            Analyze whether the current output appears to be recycled, plagiarized, or substantially
            copied from any of the previous outputs. Consider:
            - Direct text reuse
            - Structural similarity with minor word changes
            - Suspiciously similar formatting or organization
            - Identical errors or unique phrasings appearing in both
            """;

        var report = await _claude.SendStructuredRequestAsync<FraudReport>(
            PromptTemplates.FraudDetection,
            userMessage,
            ct);

        if (report.Confidence > 0.5)
        {
            _logger.LogWarning(
                "Recycled output detected with confidence {Confidence}: {FraudType}",
                report.Confidence,
                report.FraudType);
            return report;
        }

        _logger.LogInformation(
            "No output recycling detected (confidence {Confidence})",
            report.Confidence);
        return null;
    }
}
