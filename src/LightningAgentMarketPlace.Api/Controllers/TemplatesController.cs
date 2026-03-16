using System.ComponentModel.DataAnnotations;
using LightningAgentMarketPlace.Core.Models;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using LightningAgentMarketPlace.Data;

namespace LightningAgentMarketPlace.Api.Controllers;

/// <summary>
/// Manages task templates for quick task creation.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/templates")]
[Route("api/v{version:apiVersion}/templates")]
[Produces("application/json")]
public class TemplatesController : ControllerBase
{
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly ILogger<TemplatesController> _logger;

    /// <summary>
    /// Default task templates seeded on first request.
    /// </summary>
    private static readonly TaskTemplate[] DefaultTemplates =
    [
        new()
        {
            Name = "Build REST API",
            Category = "Development",
            Description = "Build a RESTful API with specified endpoints, authentication, and documentation. Deliverable includes source code and OpenAPI spec.",
            TaskType = "Code",
            VerificationCriteria = "All endpoints return correct responses; authentication works; OpenAPI spec is valid.",
            SuggestedPayoutSats = 50000,
            RequiredSkills = "CodeGeneration,API Design"
        },
        new()
        {
            Name = "Write Unit Tests",
            Category = "Quality Assurance",
            Description = "Write comprehensive unit tests for the specified module or service. Target at least 80% code coverage.",
            TaskType = "Code",
            VerificationCriteria = "Tests pass; coverage meets target; edge cases are covered.",
            SuggestedPayoutSats = 25000,
            RequiredSkills = "CodeGeneration,Testing"
        },
        new()
        {
            Name = "Audit Smart Contract",
            Category = "Security",
            Description = "Perform a security audit of a Solidity smart contract. Identify vulnerabilities, gas optimizations, and provide a report.",
            TaskType = "Code",
            VerificationCriteria = "Audit report covers reentrancy, overflow, access control; severity ratings are accurate.",
            SuggestedPayoutSats = 100000,
            RequiredSkills = "CodeGeneration,Security,Blockchain"
        },
        new()
        {
            Name = "Create Documentation",
            Category = "Documentation",
            Description = "Write technical documentation including API reference, setup guide, and architecture overview.",
            TaskType = "Text",
            VerificationCriteria = "Documentation is accurate, complete, and well-structured with examples.",
            SuggestedPayoutSats = 15000,
            RequiredSkills = "TextWriting,Documentation"
        },
        new()
        {
            Name = "Data Analysis Report",
            Category = "Analytics",
            Description = "Analyze the provided dataset and produce a report with visualizations, key findings, and actionable insights.",
            TaskType = "Data",
            VerificationCriteria = "Report includes methodology, visualizations, statistical analysis, and conclusions.",
            SuggestedPayoutSats = 35000,
            RequiredSkills = "DataAnalysis,Statistics"
        },
        new()
        {
            Name = "UI/UX Design Review",
            Category = "Design",
            Description = "Review the UI/UX of the specified application. Provide heuristic evaluation, accessibility audit, and improvement recommendations.",
            TaskType = "Text",
            VerificationCriteria = "Review covers usability heuristics, accessibility (WCAG), and provides concrete improvement suggestions.",
            SuggestedPayoutSats = 20000,
            RequiredSkills = "Design,Accessibility"
        }
    ];

    public TemplatesController(
        SqliteConnectionFactory connectionFactory,
        ILogger<TemplatesController> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    /// <summary>
    /// List all task templates. Seeds defaults if the table is empty.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(List<TaskTemplate>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<TaskTemplate>>> ListTemplates(CancellationToken ct)
    {
        await EnsureDefaultsSeededAsync(ct);

        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Id, Name, Category, Description, TaskType, VerificationCriteria, SuggestedPayoutSats, RequiredSkills, CreatedAt FROM TaskTemplates ORDER BY Id";

        using var reader = await cmd.ExecuteReaderAsync(ct);
        var templates = new List<TaskTemplate>();
        while (await reader.ReadAsync(ct))
        {
            templates.Add(MapTemplate(reader));
        }

        return Ok(templates);
    }

    /// <summary>
    /// Get a single task template by ID.
    /// </summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(TaskTemplate), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TaskTemplate>> GetTemplate(int id, CancellationToken ct)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Id, Name, Category, Description, TaskType, VerificationCriteria, SuggestedPayoutSats, RequiredSkills, CreatedAt FROM TaskTemplates WHERE Id = @Id";
        cmd.Parameters.AddWithValue("@Id", id);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return NotFound($"Template {id} not found.");

        return Ok(MapTemplate(reader));
    }

    /// <summary>
    /// Create a new task template (admin only).
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(TaskTemplate), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<TaskTemplate>> CreateTemplate(
        [FromBody] CreateTemplateRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest("Name is required.");
        if (string.IsNullOrWhiteSpace(request.Description))
            return BadRequest("Description is required.");

        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"INSERT INTO TaskTemplates (Name, Category, Description, TaskType, VerificationCriteria, SuggestedPayoutSats, RequiredSkills, CreatedAt)
            VALUES (@Name, @Category, @Description, @TaskType, @VerificationCriteria, @SuggestedPayoutSats, @RequiredSkills, @CreatedAt);
            SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@Name", request.Name);
        cmd.Parameters.AddWithValue("@Category", request.Category ?? "");
        cmd.Parameters.AddWithValue("@Description", request.Description);
        cmd.Parameters.AddWithValue("@TaskType", request.TaskType ?? "Code");
        cmd.Parameters.AddWithValue("@VerificationCriteria", request.VerificationCriteria ?? "");
        cmd.Parameters.AddWithValue("@SuggestedPayoutSats", request.SuggestedPayoutSats);
        cmd.Parameters.AddWithValue("@RequiredSkills", request.RequiredSkills ?? "");
        cmd.Parameters.AddWithValue("@CreatedAt", DateTime.UtcNow.ToString("o"));

        var result = await cmd.ExecuteScalarAsync(ct);
        var id = Convert.ToInt32(result);

        var template = new TaskTemplate
        {
            Id = id,
            Name = request.Name,
            Category = request.Category ?? "",
            Description = request.Description,
            TaskType = request.TaskType ?? "Code",
            VerificationCriteria = request.VerificationCriteria ?? "",
            SuggestedPayoutSats = request.SuggestedPayoutSats,
            RequiredSkills = request.RequiredSkills ?? "",
            CreatedAt = DateTime.UtcNow
        };

        _logger.LogInformation("Task template created: {TemplateId} ({Name})", id, request.Name);

        return Ok(template);
    }

    private async Task EnsureDefaultsSeededAsync(CancellationToken ct)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var countCmd = connection.CreateCommand();
        countCmd.CommandText = "SELECT COUNT(*) FROM TaskTemplates";
        var count = Convert.ToInt32(await countCmd.ExecuteScalarAsync(ct));

        if (count > 0)
            return;

        _logger.LogInformation("Seeding {Count} default task templates", DefaultTemplates.Length);

        foreach (var template in DefaultTemplates)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"INSERT INTO TaskTemplates (Name, Category, Description, TaskType, VerificationCriteria, SuggestedPayoutSats, RequiredSkills, CreatedAt)
                VALUES (@Name, @Category, @Description, @TaskType, @VerificationCriteria, @SuggestedPayoutSats, @RequiredSkills, @CreatedAt)";
            cmd.Parameters.AddWithValue("@Name", template.Name);
            cmd.Parameters.AddWithValue("@Category", template.Category);
            cmd.Parameters.AddWithValue("@Description", template.Description);
            cmd.Parameters.AddWithValue("@TaskType", template.TaskType);
            cmd.Parameters.AddWithValue("@VerificationCriteria", template.VerificationCriteria);
            cmd.Parameters.AddWithValue("@SuggestedPayoutSats", template.SuggestedPayoutSats);
            cmd.Parameters.AddWithValue("@RequiredSkills", template.RequiredSkills);
            cmd.Parameters.AddWithValue("@CreatedAt", DateTime.UtcNow.ToString("o"));

            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    private static TaskTemplate MapTemplate(SqliteDataReader reader)
    {
        return new TaskTemplate
        {
            Id = reader.GetInt32(0),
            Name = reader.GetString(1),
            Category = reader.GetString(2),
            Description = reader.GetString(3),
            TaskType = reader.GetString(4),
            VerificationCriteria = reader.GetString(5),
            SuggestedPayoutSats = reader.GetInt64(6),
            RequiredSkills = reader.GetString(7),
            CreatedAt = DateTime.Parse(reader.GetString(8))
        };
    }
}

public class CreateTemplateRequest
{
    [Required]
    public string Name { get; set; } = "";
    public string? Category { get; set; }
    [Required]
    public string Description { get; set; } = "";
    public string? TaskType { get; set; }
    public string? VerificationCriteria { get; set; }
    public long SuggestedPayoutSats { get; set; }
    public string? RequiredSkills { get; set; }
}
