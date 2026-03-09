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

        var spec = await _claude.SendStructuredRequestAsync<AcpTaskSpec>(
            PromptTemplates.NaturalLanguageParser,
            userMessage,
            ct);

        _logger.LogInformation(
            "Parsed task: '{Title}' of type {TaskType}",
            spec.Title,
            spec.TaskType);

        return spec;
    }
}
