using LightningAgent.Core.Enums;
using LightningAgent.Core.Interfaces.Data;
using LightningAgent.Core.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TaskStatus = LightningAgent.Core.Enums.TaskStatus;

namespace LightningAgent.Engine.BackgroundJobs;

/// <summary>
/// Polls for active recurring tasks and creates new task instances when due.
/// Supports simple schedule expressions: "daily", "weekly", "hourly".
/// </summary>
public class RecurringTaskService : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(60);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RecurringTaskService> _logger;

    public RecurringTaskService(
        IServiceScopeFactory scopeFactory,
        ILogger<RecurringTaskService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("RecurringTaskService started (poll interval={Interval}s)", PollInterval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessDueRecurringTasksAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RecurringTaskService encountered an error");
            }

            try
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("RecurringTaskService stopped");
    }

    private async Task ProcessDueRecurringTasksAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var taskRepo = scope.ServiceProvider.GetRequiredService<ITaskRepository>();

        // Get the SQLite connection factory to query recurring tasks directly
        var connectionFactory = scope.ServiceProvider.GetRequiredService<LightningAgent.Data.SqliteConnectionFactory>();

        var dueRecurringTasks = await GetDueRecurringTasksAsync(connectionFactory, ct);

        foreach (var recurring in dueRecurringTasks)
        {
            try
            {
                // Parse TaskType with fallback
                if (!Enum.TryParse<TaskType>(recurring.TaskType, ignoreCase: true, out var taskType))
                    taskType = TaskType.Code;

                var now = DateTime.UtcNow;

                var newTask = new TaskItem
                {
                    ExternalId = Guid.NewGuid().ToString("N"),
                    ClientId = "recurring",
                    Title = recurring.Title,
                    Description = recurring.Description,
                    TaskType = taskType,
                    Status = TaskStatus.Pending,
                    MaxPayoutSats = recurring.MaxPayoutSats,
                    Priority = 0,
                    CreatedAt = now,
                    UpdatedAt = now
                };

                var taskId = await taskRepo.CreateAsync(newTask, ct);

                _logger.LogInformation(
                    "RecurringTask {RecurringId} created new task {TaskId} ('{Title}')",
                    recurring.Id, taskId, recurring.Title);

                // Update LastRunAt and NextRunAt
                var nextRun = CalculateNextRun(recurring.CronExpression, now);
                await UpdateRecurringTaskRunAsync(connectionFactory, recurring.Id, now, nextRun, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to create task from recurring template {RecurringId}",
                    recurring.Id);
            }
        }
    }

    private static async Task<List<RecurringTask>> GetDueRecurringTasksAsync(
        LightningAgent.Data.SqliteConnectionFactory factory, CancellationToken ct)
    {
        var results = new List<RecurringTask>();

        try
        {
            using var connection = factory.CreateConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"SELECT Id, TemplateTaskId, CronExpression, Title, Description, TaskType,
                MaxPayoutSats, Active, LastRunAt, NextRunAt, CreatedAt
                FROM RecurringTasks
                WHERE Active = 1 AND (NextRunAt IS NULL OR NextRunAt <= @Now)";
            cmd.Parameters.AddWithValue("@Now", DateTime.UtcNow.ToString("o"));

            using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                results.Add(new RecurringTask
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
                });
            }
        }
        catch (SqliteException ex) when (ex.Message.Contains("no such table"))
        {
            // Table hasn't been created yet by migration — skip silently
        }

        return results;
    }

    private static async Task UpdateRecurringTaskRunAsync(
        LightningAgent.Data.SqliteConnectionFactory factory, int id, DateTime lastRunAt, DateTime? nextRunAt, CancellationToken ct)
    {
        using var connection = factory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE RecurringTasks SET LastRunAt = @LastRunAt, NextRunAt = @NextRunAt WHERE Id = @Id";
        cmd.Parameters.AddWithValue("@Id", id);
        cmd.Parameters.AddWithValue("@LastRunAt", lastRunAt.ToString("o"));
        cmd.Parameters.AddWithValue("@NextRunAt", nextRunAt.HasValue ? nextRunAt.Value.ToString("o") : DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Simple cron parser: supports "daily", "weekly", "hourly" keywords.
    /// </summary>
    public static DateTime? CalculateNextRun(string cronExpression, DateTime from)
    {
        return cronExpression.Trim().ToLowerInvariant() switch
        {
            "hourly" => from.AddHours(1),
            "daily" => from.AddDays(1),
            "weekly" => from.AddDays(7),
            _ => from.AddDays(1) // default to daily
        };
    }
}
