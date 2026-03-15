using LightningAgent.Core.Models;
using LightningAgent.Data;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;

namespace LightningAgent.Api.Controllers;

/// <summary>
/// Manages recurring task templates.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/recurring")]
[Route("api/v{version:apiVersion}/recurring")]
[Produces("application/json")]
public class RecurringController : ControllerBase
{
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly ILogger<RecurringController> _logger;

    public RecurringController(
        SqliteConnectionFactory connectionFactory,
        ILogger<RecurringController> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    /// <summary>
    /// List all recurring tasks.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListRecurringTasks(CancellationToken ct)
    {
        var tasks = new List<RecurringTask>();

        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"SELECT Id, TemplateTaskId, CronExpression, Title, Description, TaskType,
            MaxPayoutSats, Active, LastRunAt, NextRunAt, CreatedAt
            FROM RecurringTasks ORDER BY Id DESC";

        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            tasks.Add(MapRecurringTask(reader));
        }

        return Ok(tasks);
    }

    /// <summary>
    /// Create a new recurring task.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateRecurringTask(
        [FromBody] CreateRecurringTaskRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
            return BadRequest("Title is required.");
        if (request.MaxPayoutSats <= 0)
            return BadRequest("MaxPayoutSats must be greater than zero.");

        var now = DateTime.UtcNow;
        var cronExpression = string.IsNullOrWhiteSpace(request.CronExpression) ? "daily" : request.CronExpression.Trim();

        // Calculate initial NextRunAt
        var nextRunAt = LightningAgent.Engine.BackgroundJobs.RecurringTaskService.CalculateNextRun(cronExpression, now);

        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"INSERT INTO RecurringTasks (TemplateTaskId, CronExpression, Title, Description, TaskType, MaxPayoutSats, Active, LastRunAt, NextRunAt, CreatedAt)
            VALUES (@TemplateTaskId, @CronExpression, @Title, @Description, @TaskType, @MaxPayoutSats, 1, NULL, @NextRunAt, @CreatedAt);
            SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@TemplateTaskId", request.TemplateTaskId);
        cmd.Parameters.AddWithValue("@CronExpression", cronExpression);
        cmd.Parameters.AddWithValue("@Title", request.Title);
        cmd.Parameters.AddWithValue("@Description", request.Description ?? "");
        cmd.Parameters.AddWithValue("@TaskType", request.TaskType ?? "Code");
        cmd.Parameters.AddWithValue("@MaxPayoutSats", request.MaxPayoutSats);
        cmd.Parameters.AddWithValue("@NextRunAt", nextRunAt.HasValue ? nextRunAt.Value.ToString("o") : DBNull.Value);
        cmd.Parameters.AddWithValue("@CreatedAt", now.ToString("o"));

        var result = await cmd.ExecuteScalarAsync(ct);
        var id = Convert.ToInt32(result);

        _logger.LogInformation("Created recurring task {RecurringTaskId} ('{Title}', schedule={Cron})", id, request.Title, cronExpression);

        return Ok(new
        {
            id,
            title = request.Title,
            cronExpression,
            nextRunAt,
            message = "Recurring task created successfully."
        });
    }

    /// <summary>
    /// Deactivate a recurring task.
    /// </summary>
    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeactivateRecurringTask(int id, CancellationToken ct)
    {
        using var connection = _connectionFactory.CreateConnection();

        // Check if exists
        using var checkCmd = connection.CreateCommand();
        checkCmd.CommandText = "SELECT COUNT(1) FROM RecurringTasks WHERE Id = @Id";
        checkCmd.Parameters.AddWithValue("@Id", id);
        var count = Convert.ToInt32(await checkCmd.ExecuteScalarAsync(ct));

        if (count == 0)
            return NotFound($"Recurring task {id} not found.");

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE RecurringTasks SET Active = 0 WHERE Id = @Id";
        cmd.Parameters.AddWithValue("@Id", id);
        await cmd.ExecuteNonQueryAsync(ct);

        _logger.LogInformation("Deactivated recurring task {RecurringTaskId}", id);

        return Ok(new { message = $"Recurring task {id} deactivated." });
    }

    private static RecurringTask MapRecurringTask(SqliteDataReader reader)
    {
        return new RecurringTask
        {
            Id = reader.GetInt32(0),
            TemplateTaskId = reader.GetInt32(1),
            CronExpression = reader.GetString(2),
            Title = reader.GetString(3),
            Description = reader.GetString(4),
            TaskType = reader.GetString(5),
            MaxPayoutSats = reader.GetInt64(6),
            Active = reader.GetInt32(7) == 1,
            LastRunAt = reader.IsDBNull(8) ? null : DateTime.Parse(reader.GetString(8)),
            NextRunAt = reader.IsDBNull(9) ? null : DateTime.Parse(reader.GetString(9)),
            CreatedAt = DateTime.Parse(reader.GetString(10))
        };
    }
}

public class CreateRecurringTaskRequest
{
    public int TemplateTaskId { get; set; }
    public string CronExpression { get; set; } = "daily";
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string? TaskType { get; set; }
    public long MaxPayoutSats { get; set; }
}
