using System.Text.Json;
using LightningAgent.Core.Enums;
using LightningAgent.Core.Interfaces.Services;
using LightningAgent.Core.Models;
using Microsoft.Extensions.Logging;

namespace LightningAgent.Verification.Plugins;

/// <summary>
/// Verification plugin that assesses data output integrity by validating JSON
/// structure, checking for expected schema elements, and verifying data completeness.
/// </summary>
public class DataIntegrityPlugin : IVerificationPlugin
{
    private readonly ILogger<DataIntegrityPlugin> _logger;

    public DataIntegrityPlugin(ILogger<DataIntegrityPlugin> logger)
    {
        _logger = logger;
    }

    public string Name => "DataIntegrity";

    public string[] SupportedTaskTypes => ["Data"];

    public Task<VerificationResult> VerifyAsync(
        Milestone milestone,
        string output,
        CancellationToken ct)
    {
        _logger.LogDebug("DataIntegrityPlugin: verifying milestone {MilestoneId}", milestone.Id);

        var score = 0.0;
        var issues = new List<string>();

        // 1. Non-empty output
        if (string.IsNullOrWhiteSpace(output))
        {
            return Task.FromResult(new VerificationResult(
                Score: 0.0,
                Passed: false,
                Details: "Output is empty",
                StrategyType: VerificationStrategyType.SchemaValidation));
        }

        score += 0.1;

        // 2. Valid JSON check
        var isValidJson = TryParseJson(output, out var jsonDoc);
        if (isValidJson && jsonDoc is not null)
        {
            score += 0.35;

            // 3. Check JSON depth and complexity (non-trivial data)
            var depth = MeasureJsonDepth(jsonDoc.RootElement);
            if (depth >= 2)
            {
                score += 0.15;
            }
            else
            {
                issues.Add($"JSON structure is shallow (depth={depth})");
            }

            // 4. Check for non-trivial element count
            var elementCount = CountJsonElements(jsonDoc.RootElement);
            if (elementCount >= 3)
            {
                score += 0.2;
            }
            else
            {
                issues.Add($"JSON has few elements ({elementCount})");
            }

            // 5. Check for no null-heavy data
            var nullCount = CountNullValues(jsonDoc.RootElement);
            var nullRatio = elementCount > 0 ? (double)nullCount / elementCount : 0;
            if (nullRatio <= 0.5)
            {
                score += 0.2;
            }
            else
            {
                issues.Add($"High null ratio ({nullRatio:P0} of values are null)");
            }

            jsonDoc.Dispose();
        }
        else
        {
            issues.Add("Output is not valid JSON");

            // Still award partial credit if output looks like structured data (CSV, etc.)
            if (LooksLikeStructuredData(output))
            {
                score += 0.2;
            }
            else
            {
                issues.Add("Output does not appear to be structured data");
            }
        }

        var passed = score >= 0.6;
        var details = issues.Count == 0
            ? "Data integrity checks passed"
            : $"Issues: {string.Join("; ", issues)}";

        _logger.LogInformation(
            "DataIntegrityPlugin result for milestone {MilestoneId}: Score={Score}, Passed={Passed}",
            milestone.Id, score, passed);

        return Task.FromResult(new VerificationResult(
            Score: score,
            Passed: passed,
            Details: details,
            StrategyType: VerificationStrategyType.SchemaValidation));
    }

    private static bool TryParseJson(string text, out JsonDocument? document)
    {
        document = null;
        try
        {
            document = JsonDocument.Parse(text);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static int MeasureJsonDepth(JsonElement element, int currentDepth = 0)
    {
        var maxDepth = currentDepth;

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in element.EnumerateObject())
                {
                    var childDepth = MeasureJsonDepth(prop.Value, currentDepth + 1);
                    if (childDepth > maxDepth)
                        maxDepth = childDepth;
                }
                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    var childDepth = MeasureJsonDepth(item, currentDepth + 1);
                    if (childDepth > maxDepth)
                        maxDepth = childDepth;
                }
                break;
        }

        return maxDepth;
    }

    private static int CountJsonElements(JsonElement element)
    {
        var count = 0;

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in element.EnumerateObject())
                {
                    count++;
                    count += CountJsonElements(prop.Value);
                }
                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    count++;
                    count += CountJsonElements(item);
                }
                break;

            default:
                count = 1;
                break;
        }

        return count;
    }

    private static int CountNullValues(JsonElement element)
    {
        var count = 0;

        switch (element.ValueKind)
        {
            case JsonValueKind.Null:
                return 1;

            case JsonValueKind.Object:
                foreach (var prop in element.EnumerateObject())
                {
                    count += CountNullValues(prop.Value);
                }
                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    count += CountNullValues(item);
                }
                break;
        }

        return count;
    }

    private static bool LooksLikeStructuredData(string text)
    {
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 2)
            return false;

        // Check for CSV-like structure (consistent delimiter counts across lines)
        var firstLineCommas = lines[0].Count(c => c == ',');
        var firstLineTabs = lines[0].Count(c => c == '\t');

        if (firstLineCommas >= 2)
        {
            var consistentLines = lines.Count(l =>
                Math.Abs(l.Count(c => c == ',') - firstLineCommas) <= 1);
            return consistentLines >= lines.Length * 0.7;
        }

        if (firstLineTabs >= 1)
        {
            var consistentLines = lines.Count(l =>
                Math.Abs(l.Count(c => c == '\t') - firstLineTabs) <= 1);
            return consistentLines >= lines.Length * 0.7;
        }

        return false;
    }
}
