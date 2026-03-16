using System.Text;
using System.Text.Json;
using LightningAgentMarketPlace.Core.Enums;
using LightningAgentMarketPlace.Core.Interfaces.Services;
using LightningAgentMarketPlace.Core.Models;
using Microsoft.Extensions.Logging;

namespace LightningAgentMarketPlace.Verification.Strategies;

public class TextSimilarityVerification : IVerificationStrategy
{
    private readonly ILogger<TextSimilarityVerification> _logger;

    public TextSimilarityVerification(ILogger<TextSimilarityVerification> logger)
    {
        _logger = logger;
    }

    public VerificationStrategyType StrategyType => VerificationStrategyType.TextSimilarity;

    public bool CanHandle(TaskType taskType) => taskType == TaskType.Text;

    public Task<VerificationResult> VerifyAsync(
        Milestone milestone,
        byte[] agentOutput,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Running text similarity verification for milestone {MilestoneId}",
            milestone.Id);

        var outputText = Encoding.UTF8.GetString(agentOutput);
        var score = 0.0;
        var issues = new List<string>();

        // Check 1: Minimum length (non-trivial output)
        if (outputText.Length >= 50)
        {
            score += 0.3;
        }
        else
        {
            issues.Add($"Output too short ({outputText.Length} characters, minimum 50 expected)");
        }

        // Check 2: Not empty or whitespace-only
        if (!string.IsNullOrWhiteSpace(outputText))
        {
            score += 0.2;
        }
        else
        {
            issues.Add("Output is empty or whitespace-only");
        }

        // Check 3: Keyword presence from verification criteria
        var keywords = ExtractKeywords(milestone.VerificationCriteria);
        if (keywords.Count > 0)
        {
            var matchedKeywords = keywords
                .Count(kw => outputText.Contains(kw, StringComparison.OrdinalIgnoreCase));
            var keywordScore = (double)matchedKeywords / keywords.Count * 0.3;
            score += keywordScore;

            if (matchedKeywords < keywords.Count)
            {
                var missing = keywords
                    .Where(kw => !outputText.Contains(kw, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                issues.Add($"Missing keywords: {string.Join(", ", missing)}");
            }
        }
        else
        {
            // No keywords to check; give partial credit
            score += 0.15;
        }

        // Check 4: Basic quality heuristics
        var sentences = outputText.Split(
            new[] { '.', '!', '?' },
            StringSplitOptions.RemoveEmptyEntries);
        if (sentences.Length >= 3)
        {
            score += 0.2;
        }
        else
        {
            issues.Add($"Low sentence count ({sentences.Length}), expected at least 3");
        }

        var passed = score >= 0.7;
        var details = issues.Count == 0
            ? "Text quality verification passed"
            : $"Text quality issues: {string.Join("; ", issues)}";

        _logger.LogInformation(
            "Text similarity verification result: Score={Score}, Passed={Passed}",
            score,
            passed);

        return Task.FromResult(new VerificationResult(
            Score: score,
            Passed: passed,
            Details: details,
            StrategyType: VerificationStrategyType.TextSimilarity));
    }

    private List<string> ExtractKeywords(string verificationCriteria)
    {
        var keywords = new List<string>();

        if (string.IsNullOrWhiteSpace(verificationCriteria))
        {
            return keywords;
        }

        try
        {
            using var doc = JsonDocument.Parse(verificationCriteria);
            if (doc.RootElement.TryGetProperty("keywords", out var keywordsElement)
                && keywordsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var kw in keywordsElement.EnumerateArray())
                {
                    var keyword = kw.GetString();
                    if (!string.IsNullOrEmpty(keyword))
                    {
                        keywords.Add(keyword);
                    }
                }
            }
        }
        catch (JsonException)
        {
            _logger.LogDebug("Could not parse verification criteria as JSON for keyword extraction");
        }

        return keywords;
    }
}
