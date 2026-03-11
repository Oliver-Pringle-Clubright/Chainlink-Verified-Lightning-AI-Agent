using LightningAgent.Core.Enums;
using LightningAgent.Core.Interfaces.Services;
using LightningAgent.Core.Models;
using Microsoft.Extensions.Logging;

namespace LightningAgent.Verification.Plugins;

/// <summary>
/// Verification plugin that assesses text output quality using readability
/// heuristics such as sentence count, average word length, and vocabulary diversity.
/// </summary>
public class TextQualityPlugin : IVerificationPlugin
{
    private readonly ILogger<TextQualityPlugin> _logger;

    public TextQualityPlugin(ILogger<TextQualityPlugin> logger)
    {
        _logger = logger;
    }

    public string Name => "TextQuality";

    public string[] SupportedTaskTypes => ["Text"];

    public Task<VerificationResult> VerifyAsync(
        Milestone milestone,
        string output,
        CancellationToken ct)
    {
        _logger.LogDebug("TextQualityPlugin: verifying milestone {MilestoneId}", milestone.Id);

        var score = 0.0;
        var issues = new List<string>();

        // 1. Minimum length check
        if (output.Length >= 100)
        {
            score += 0.15;
        }
        else if (output.Length >= 20)
        {
            score += 0.05;
            issues.Add($"Output is short ({output.Length} chars)");
        }
        else
        {
            issues.Add($"Output is very short ({output.Length} chars, minimum 20)");
        }

        // 2. Sentence structure
        var sentences = CountSentences(output);
        if (sentences >= 3)
        {
            score += 0.2;
        }
        else if (sentences >= 1)
        {
            score += 0.1;
            issues.Add($"Few sentences found ({sentences})");
        }
        else
        {
            issues.Add("No sentence boundaries detected");
        }

        // 3. Word count and average word length
        var words = output.Split(
            new[] { ' ', '\t', '\n', '\r' },
            StringSplitOptions.RemoveEmptyEntries);

        if (words.Length >= 20)
        {
            score += 0.15;
        }
        else
        {
            issues.Add($"Low word count ({words.Length})");
        }

        if (words.Length > 0)
        {
            var avgWordLen = words.Average(w => w.Length);
            if (avgWordLen is >= 3 and <= 12)
            {
                score += 0.15;
            }
            else
            {
                issues.Add($"Unusual average word length ({avgWordLen:F1})");
            }
        }

        // 4. Vocabulary diversity (type-token ratio)
        if (words.Length >= 10)
        {
            var uniqueWords = words
                .Select(w => w.ToLowerInvariant().Trim('.', ',', '!', '?', ';', ':', '"', '\''))
                .Where(w => w.Length > 0)
                .Distinct()
                .Count();

            var ttr = (double)uniqueWords / words.Length;
            if (ttr >= 0.3)
            {
                score += 0.2;
            }
            else
            {
                issues.Add($"Low vocabulary diversity (TTR={ttr:F2})");
            }
        }

        // 5. No excessive special characters (sign of garbled output)
        var alphanumericRatio = output.Count(char.IsLetterOrDigit) / (double)Math.Max(output.Length, 1);
        if (alphanumericRatio >= 0.5)
        {
            score += 0.15;
        }
        else
        {
            issues.Add($"Low alphanumeric ratio ({alphanumericRatio:P0})");
        }

        var passed = score >= 0.6;
        var details = issues.Count == 0
            ? "Text quality checks passed"
            : $"Issues: {string.Join("; ", issues)}";

        _logger.LogInformation(
            "TextQualityPlugin result for milestone {MilestoneId}: Score={Score}, Passed={Passed}",
            milestone.Id, score, passed);

        return Task.FromResult(new VerificationResult(
            Score: score,
            Passed: passed,
            Details: details,
            StrategyType: VerificationStrategyType.TextSimilarity));
    }

    private static int CountSentences(string text)
    {
        var count = 0;
        var sentenceEnders = new[] { '.', '!', '?' };

        for (var i = 0; i < text.Length; i++)
        {
            if (sentenceEnders.Contains(text[i]))
            {
                // Avoid counting abbreviations like "e.g." or "Dr."
                // by requiring whitespace or end after the punctuation
                if (i + 1 >= text.Length || char.IsWhiteSpace(text[i + 1]) || char.IsUpper(text[i + 1]))
                {
                    count++;
                }
            }
        }

        return count;
    }
}
