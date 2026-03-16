using System.Text;
using System.Text.Json;
using LightningAgentMarketPlace.Core.Enums;
using LightningAgentMarketPlace.Core.Interfaces.Services;
using LightningAgentMarketPlace.Core.Models;
using Microsoft.Extensions.Logging;

namespace LightningAgentMarketPlace.Verification.Strategies;

public class SchemaValidationVerification : IVerificationStrategy
{
    private readonly ILogger<SchemaValidationVerification> _logger;

    public SchemaValidationVerification(ILogger<SchemaValidationVerification> logger)
    {
        _logger = logger;
    }

    public VerificationStrategyType StrategyType => VerificationStrategyType.SchemaValidation;

    public bool CanHandle(TaskType taskType) => taskType == TaskType.Data;

    public Task<VerificationResult> VerifyAsync(
        Milestone milestone,
        byte[] agentOutput,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Running schema validation verification for milestone {MilestoneId}",
            milestone.Id);

        var outputText = Encoding.UTF8.GetString(agentOutput);
        var score = 0.0;
        var issues = new List<string>();

        // Step 1: Check if output is valid JSON
        JsonDocument? parsedOutput = null;
        try
        {
            parsedOutput = JsonDocument.Parse(outputText);
            score += 0.5;
        }
        catch (JsonException ex)
        {
            issues.Add($"Invalid JSON: {ex.Message}");

            var result = new VerificationResult(
                Score: 0.0,
                Passed: false,
                Details: $"Output is not valid JSON: {ex.Message}",
                StrategyType: VerificationStrategyType.SchemaValidation);

            return Task.FromResult(result);
        }

        using (parsedOutput)
        {
            // Step 2: Check for expected fields from verification criteria
            var expectedFields = ExtractExpectedFields(milestone.VerificationCriteria);

            if (expectedFields.Count > 0)
            {
                var foundFields = 0;
                foreach (var field in expectedFields)
                {
                    if (parsedOutput.RootElement.TryGetProperty(field, out _))
                    {
                        foundFields++;
                    }
                    else
                    {
                        issues.Add($"Missing expected field: {field}");
                    }
                }

                var fieldScore = expectedFields.Count > 0
                    ? (double)foundFields / expectedFields.Count * 0.5
                    : 0.5;
                score += fieldScore;
            }
            else
            {
                // No schema expectations; valid JSON is enough
                score += 0.5;
            }
        }

        var passed = score >= 0.7;
        var details = issues.Count == 0
            ? "Schema validation passed"
            : $"Schema issues: {string.Join("; ", issues)}";

        _logger.LogInformation(
            "Schema validation result: Score={Score}, Passed={Passed}",
            score,
            passed);

        return Task.FromResult(new VerificationResult(
            Score: score,
            Passed: passed,
            Details: details,
            StrategyType: VerificationStrategyType.SchemaValidation));
    }

    private List<string> ExtractExpectedFields(string verificationCriteria)
    {
        var fields = new List<string>();

        if (string.IsNullOrWhiteSpace(verificationCriteria))
        {
            return fields;
        }

        try
        {
            using var doc = JsonDocument.Parse(verificationCriteria);
            if (doc.RootElement.TryGetProperty("expectedFields", out var fieldsElement)
                && fieldsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var field in fieldsElement.EnumerateArray())
                {
                    var fieldName = field.GetString();
                    if (!string.IsNullOrEmpty(fieldName))
                    {
                        fields.Add(fieldName);
                    }
                }
            }
        }
        catch (JsonException)
        {
            _logger.LogDebug("Could not parse verification criteria as JSON for schema extraction");
        }

        return fields;
    }
}
