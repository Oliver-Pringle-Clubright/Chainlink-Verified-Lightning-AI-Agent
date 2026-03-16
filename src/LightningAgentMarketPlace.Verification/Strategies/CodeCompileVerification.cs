using System.Text;
using LightningAgentMarketPlace.Core.Enums;
using LightningAgentMarketPlace.Core.Interfaces.Services;
using LightningAgentMarketPlace.Core.Models;
using Microsoft.Extensions.Logging;

namespace LightningAgentMarketPlace.Verification.Strategies;

public class CodeCompileVerification : IVerificationStrategy
{
    private readonly ILogger<CodeCompileVerification> _logger;

    public CodeCompileVerification(ILogger<CodeCompileVerification> logger)
    {
        _logger = logger;
    }

    public VerificationStrategyType StrategyType => VerificationStrategyType.CodeCompile;

    public bool CanHandle(TaskType taskType) => taskType == TaskType.Code;

    public Task<VerificationResult> VerifyAsync(
        Milestone milestone,
        byte[] agentOutput,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Running code compile verification for milestone {MilestoneId}",
            milestone.Id);

        var code = Encoding.UTF8.GetString(agentOutput);

        var score = 0.0;
        var issues = new List<string>();

        // Check for basic code structure indicators
        var hasCodeStructure = ContainsCodeStructure(code);
        if (hasCodeStructure)
        {
            score += 0.4;
        }
        else
        {
            issues.Add("No recognizable code structure found");
        }

        // Check for balanced braces
        var bracesBalanced = AreBracesBalanced(code);
        if (bracesBalanced)
        {
            score += 0.3;
        }
        else
        {
            issues.Add("Unbalanced braces detected");
        }

        // Check for syntax errors (basic heuristics)
        var noObviousErrors = !HasObviousSyntaxErrors(code);
        if (noObviousErrors)
        {
            score += 0.3;
        }
        else
        {
            issues.Add("Potential syntax errors detected");
        }

        var passed = score >= 0.7;
        var details = issues.Count == 0
            ? "Code structure validation passed"
            : $"Issues found: {string.Join("; ", issues)}";

        _logger.LogInformation(
            "Code compile verification result: Score={Score}, Passed={Passed}",
            score,
            passed);

        return Task.FromResult(new VerificationResult(
            Score: score,
            Passed: passed,
            Details: details,
            StrategyType: VerificationStrategyType.CodeCompile));
    }

    private static bool ContainsCodeStructure(string code)
    {
        var codeIndicators = new[]
        {
            "function ", "def ", "class ", "public ", "private ", "static ",
            "const ", "var ", "let ", "import ", "using ", "namespace ",
            "return ", "if (", "for (", "while ("
        };

        return codeIndicators.Any(indicator =>
            code.Contains(indicator, StringComparison.OrdinalIgnoreCase));
    }

    private static bool AreBracesBalanced(string code)
    {
        var braceCount = 0;
        var parenCount = 0;
        var bracketCount = 0;

        foreach (var c in code)
        {
            switch (c)
            {
                case '{': braceCount++; break;
                case '}': braceCount--; break;
                case '(': parenCount++; break;
                case ')': parenCount--; break;
                case '[': bracketCount++; break;
                case ']': bracketCount--; break;
            }

            if (braceCount < 0 || parenCount < 0 || bracketCount < 0)
            {
                return false;
            }
        }

        return braceCount == 0 && parenCount == 0 && bracketCount == 0;
    }

    private static bool HasObviousSyntaxErrors(string code)
    {
        // Check for common syntax error patterns
        var errorPatterns = new[]
        {
            ";;",           // double semicolons (outside for loops)
            "{{{{",         // excessive braces
            "}}}}",
            "())",          // mismatched parens
            "(()"
        };

        return errorPatterns.Any(pattern => code.Contains(pattern));
    }
}
