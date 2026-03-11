using System.Text.Json;
using LightningAgent.AI.Prompts;
using LightningAgent.Core.Interfaces.Services;
using LightningAgent.Core.Models.Acp;
using Microsoft.Extensions.Logging;

namespace LightningAgent.AI.TaskParser;

public class NaturalLanguageTaskParser : INaturalLanguageTaskParser
{
    private readonly IClaudeAiClient _claude;
    private readonly ILogger<NaturalLanguageTaskParser> _logger;

    public NaturalLanguageTaskParser(IClaudeAiClient claude, ILogger<NaturalLanguageTaskParser> logger)
    {
        _claude = claude;
        _logger = logger;
    }

    public async Task<AcpTaskSpec> ParseDescriptionAsync(
        string naturalLanguageDescription,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Parsing natural language task description");

        var userMessage = $"""
            ## Natural Language Task Description
            {naturalLanguageDescription}

            Parse this into a structured task specification with title, description, taskType,
            requiredSkills, budget estimation, verification requirements, and deadline if mentioned.
            """;

        AcpTaskSpec spec;
        try
        {
            spec = await _claude.SendStructuredRequestAsync<AcpTaskSpec>(
                PromptTemplates.NaturalLanguageParser,
                userMessage,
                ct);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(
                ex,
                "Structured deserialization failed for task description, attempting lenient parse");

            // Fall back: get raw JSON string and manually extract fields
            var rawJson = await _claude.SendMessageAsync(
                PromptTemplates.NaturalLanguageParser,
                userMessage,
                ct);

            spec = LenientParseTaskSpec(rawJson);
        }

        _logger.LogInformation(
            "Parsed task: '{Title}' of type {TaskType}",
            spec.Title,
            spec.TaskType);

        return spec;
    }

    /// <summary>
    /// Lenient parser that handles cases where Claude returns verificationRequirements
    /// as a JSON object/array instead of a plain string. We normalize it to a string
    /// before deserializing into AcpTaskSpec.
    /// </summary>
    private AcpTaskSpec LenientParseTaskSpec(string rawJson)
    {
        // Strip markdown code fences if present
        var json = rawJson.Trim();
        if (json.StartsWith("```"))
        {
            var firstNewline = json.IndexOf('\n');
            if (firstNewline >= 0)
                json = json[(firstNewline + 1)..];
            if (json.EndsWith("```"))
                json = json[..^3];
            json = json.Trim();
        }

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var spec = new AcpTaskSpec();

        if (root.TryGetProperty("title", out var titleEl))
            spec.Title = titleEl.GetString() ?? string.Empty;

        if (root.TryGetProperty("description", out var descEl))
            spec.Description = descEl.GetString() ?? string.Empty;

        if (root.TryGetProperty("taskType", out var taskTypeEl))
            spec.TaskType = taskTypeEl.GetString() ?? string.Empty;

        if (root.TryGetProperty("requiredSkills", out var skillsEl) && skillsEl.ValueKind == JsonValueKind.Array)
        {
            spec.RequiredSkills = skillsEl.EnumerateArray()
                .Select(e => e.GetString() ?? string.Empty)
                .Where(s => !string.IsNullOrEmpty(s))
                .ToList();
        }

        if (root.TryGetProperty("budget", out var budgetEl) && budgetEl.ValueKind == JsonValueKind.Object)
        {
            spec.Budget = new AcpBudget();
            if (budgetEl.TryGetProperty("maxSats", out var maxSatsEl))
                spec.Budget.MaxSats = maxSatsEl.TryGetInt64(out var ms) ? ms : 0;
            if (budgetEl.TryGetProperty("preferredCurrency", out var currEl))
                spec.Budget.PreferredCurrency = currEl.GetString() ?? string.Empty;
            if (budgetEl.TryGetProperty("usdEquivalent", out var usdEl) && usdEl.TryGetDouble(out var usd))
                spec.Budget.UsdEquivalent = usd;
        }

        // Handle verificationRequirements: convert object/array to string
        if (root.TryGetProperty("verificationRequirements", out var vrEl))
        {
            spec.VerificationRequirements = vrEl.ValueKind switch
            {
                JsonValueKind.String => vrEl.GetString(),
                _ => vrEl.GetRawText() // serialize object/array back to a JSON string
            };
        }

        if (root.TryGetProperty("deadline", out var deadlineEl) && deadlineEl.ValueKind == JsonValueKind.String)
        {
            if (DateTime.TryParse(deadlineEl.GetString(), out var dl))
                spec.Deadline = dl;
        }

        if (root.TryGetProperty("taskId", out var taskIdEl))
            spec.TaskId = taskIdEl.ValueKind == JsonValueKind.String
                ? taskIdEl.GetString() ?? string.Empty
                : taskIdEl.GetRawText();

        return spec;
    }
}
