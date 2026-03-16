using LightningAgentMarketPlace.Core.Enums;
using LightningAgentMarketPlace.Core.Interfaces.Services;
using LightningAgentMarketPlace.Core.Models;
using Microsoft.Extensions.Logging;

namespace LightningAgentMarketPlace.Verification.Plugins;

/// <summary>
/// Verification plugin that assesses code output quality by checking for
/// recognizable syntax patterns, balanced delimiters, and minimum length.
/// </summary>
public class CodeQualityPlugin : IVerificationPlugin
{
    private readonly ILogger<CodeQualityPlugin> _logger;

    public CodeQualityPlugin(ILogger<CodeQualityPlugin> logger)
    {
        _logger = logger;
    }

    public string Name => "CodeQuality";

    public string[] SupportedTaskTypes => ["Code"];

    public Task<VerificationResult> VerifyAsync(
        Milestone milestone,
        string output,
        CancellationToken ct)
    {
        _logger.LogDebug("CodeQualityPlugin: verifying milestone {MilestoneId}", milestone.Id);

        var score = 0.0;
        var issues = new List<string>();

        // 1. Minimum length check
        if (output.Length >= 50)
        {
            score += 0.2;
        }
        else
        {
            issues.Add($"Output too short ({output.Length} chars, minimum 50)");
        }

        // 2. Contains recognizable code keywords
        var codeKeywords = new[]
        {
            "function ", "def ", "class ", "public ", "private ", "static ",
            "const ", "var ", "let ", "import ", "using ", "namespace ",
            "return ", "if ", "for ", "while ", "async ", "await "
        };

        var keywordCount = codeKeywords.Count(kw =>
            output.Contains(kw, StringComparison.OrdinalIgnoreCase));

        if (keywordCount >= 3)
        {
            score += 0.3;
        }
        else if (keywordCount >= 1)
        {
            score += 0.15;
            issues.Add($"Low keyword density ({keywordCount} code keywords found)");
        }
        else
        {
            issues.Add("No recognizable code keywords found");
        }

        // 3. Balanced braces / parentheses / brackets
        if (AreDelimitersBalanced(output))
        {
            score += 0.25;
        }
        else
        {
            issues.Add("Unbalanced delimiters detected");
        }

        // 4. No excessive repetition (a sign of degenerate output)
        if (!HasExcessiveRepetition(output))
        {
            score += 0.25;
        }
        else
        {
            issues.Add("Excessive repetition detected in output");
        }

        var passed = score >= 0.6;
        var details = issues.Count == 0
            ? "Code quality checks passed"
            : $"Issues: {string.Join("; ", issues)}";

        _logger.LogInformation(
            "CodeQualityPlugin result for milestone {MilestoneId}: Score={Score}, Passed={Passed}",
            milestone.Id, score, passed);

        return Task.FromResult(new VerificationResult(
            Score: score,
            Passed: passed,
            Details: details,
            StrategyType: VerificationStrategyType.CodeCompile));
    }

    private static bool AreDelimitersBalanced(string code)
    {
        var braces = 0;
        var parens = 0;
        var brackets = 0;

        foreach (var c in code)
        {
            switch (c)
            {
                case '{': braces++; break;
                case '}': braces--; break;
                case '(': parens++; break;
                case ')': parens--; break;
                case '[': brackets++; break;
                case ']': brackets--; break;
            }

            if (braces < 0 || parens < 0 || brackets < 0)
                return false;
        }

        return braces == 0 && parens == 0 && brackets == 0;
    }

    private static bool HasExcessiveRepetition(string text)
    {
        if (text.Length < 200)
            return false;

        // Check if any 20-char substring appears more than 5 times
        const int windowSize = 20;
        const int threshold = 5;
        var counts = new Dictionary<string, int>();

        for (var i = 0; i <= text.Length - windowSize; i += windowSize)
        {
            var window = text.Substring(i, windowSize);
            if (counts.TryGetValue(window, out var count))
            {
                counts[window] = count + 1;
                if (count + 1 > threshold)
                    return true;
            }
            else
            {
                counts[window] = 1;
            }
        }

        return false;
    }
}
