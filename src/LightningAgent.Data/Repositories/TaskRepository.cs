using LightningAgent.Core.Enums;
using LightningAgent.Core.Interfaces.Data;
using LightningAgent.Core.Models;
using Microsoft.Data.Sqlite;
using TaskStatus = LightningAgent.Core.Enums.TaskStatus;

namespace LightningAgent.Data.Repositories;

public class TaskRepository : ITaskRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;

    private const string SelectColumns = "Id, ExternalId, ParentTaskId, ClientId, Title, Description, TaskType, Status, AcpSpec, VerificationCriteria, MaxPayoutSats, ActualPayoutSats, PriceUsd, AssignedAgentId, Priority, CreatedAt, UpdatedAt, CompletedAt";

    public TaskRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<TaskItem?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT {SelectColumns} FROM Tasks WHERE Id = @Id";
        cmd.Parameters.AddWithValue("@Id", id);

        using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? MapTask(reader) : null;
    }

    public async Task<TaskItem?> GetByExternalIdAsync(string externalId, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT {SelectColumns} FROM Tasks WHERE ExternalId = @ExternalId";
        cmd.Parameters.AddWithValue("@ExternalId", externalId);

        using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? MapTask(reader) : null;
    }

    public async Task<IReadOnlyList<TaskItem>> GetByStatusAsync(TaskStatus status, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT {SelectColumns} FROM Tasks WHERE Status = @Status";
        cmd.Parameters.AddWithValue("@Status", status.ToString());

        using var reader = await cmd.ExecuteReaderAsync();
        var results = new List<TaskItem>();
        while (await reader.ReadAsync())
        {
            results.Add(MapTask(reader));
        }
        return results;
    }

    public async Task<IReadOnlyList<TaskItem>> GetByAssignedAgentAsync(int agentId, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT {SelectColumns} FROM Tasks WHERE AssignedAgentId = @AgentId";
        cmd.Parameters.AddWithValue("@AgentId", agentId);

        using var reader = await cmd.ExecuteReaderAsync();
        var results = new List<TaskItem>();
        while (await reader.ReadAsync())
        {
            results.Add(MapTask(reader));
        }
        return results;
    }

    public async Task<IReadOnlyList<TaskItem>> GetByClientIdAsync(string clientId)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT {SelectColumns} FROM Tasks WHERE ClientId = @ClientId";
        cmd.Parameters.AddWithValue("@ClientId", clientId);

        using var reader = await cmd.ExecuteReaderAsync();
        var results = new List<TaskItem>();
        while (await reader.ReadAsync())
        {
            results.Add(MapTask(reader));
        }
        return results;
    }

    public async Task<IReadOnlyList<TaskItem>> GetSubtasksAsync(int parentTaskId, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT {SelectColumns} FROM Tasks WHERE ParentTaskId = @ParentTaskId";
        cmd.Parameters.AddWithValue("@ParentTaskId", parentTaskId);

        using var reader = await cmd.ExecuteReaderAsync();
        var results = new List<TaskItem>();
        while (await reader.ReadAsync())
        {
            results.Add(MapTask(reader));
        }
        return results;
    }

    public async Task<IReadOnlyList<TaskItem>> GetCompletedSinceAsync(DateTime since, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT {SelectColumns} FROM Tasks WHERE Status = @Status AND CompletedAt IS NOT NULL AND CompletedAt >= @Since";
        cmd.Parameters.AddWithValue("@Status", TaskStatus.Completed.ToString());
        cmd.Parameters.AddWithValue("@Since", since.ToString("o"));

        using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<TaskItem>();
        while (await reader.ReadAsync(ct))
        {
            results.Add(MapTask(reader));
        }
        return results;
    }

    public async Task<int> CreateAsync(TaskItem task, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"INSERT INTO Tasks (ExternalId, ParentTaskId, ClientId, Title, Description, TaskType, Status, AcpSpec, VerificationCriteria, MaxPayoutSats, ActualPayoutSats, PriceUsd, AssignedAgentId, Priority, CreatedAt, UpdatedAt, CompletedAt)
            VALUES (@ExternalId, @ParentTaskId, @ClientId, @Title, @Description, @TaskType, @Status, @AcpSpec, @VerificationCriteria, @MaxPayoutSats, @ActualPayoutSats, @PriceUsd, @AssignedAgentId, @Priority, @CreatedAt, @UpdatedAt, @CompletedAt);
            SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@ExternalId", task.ExternalId);
        cmd.Parameters.AddWithValue("@ParentTaskId", (object?)task.ParentTaskId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ClientId", task.ClientId);
        cmd.Parameters.AddWithValue("@Title", task.Title);
        cmd.Parameters.AddWithValue("@Description", task.Description);
        cmd.Parameters.AddWithValue("@TaskType", task.TaskType.ToString());
        cmd.Parameters.AddWithValue("@Status", task.Status.ToString());
        cmd.Parameters.AddWithValue("@AcpSpec", (object?)task.AcpSpec ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@VerificationCriteria", (object?)task.VerificationCriteria ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@MaxPayoutSats", task.MaxPayoutSats);
        cmd.Parameters.AddWithValue("@ActualPayoutSats", task.ActualPayoutSats);
        cmd.Parameters.AddWithValue("@PriceUsd", (object?)task.PriceUsd ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@AssignedAgentId", (object?)task.AssignedAgentId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Priority", task.Priority);
        cmd.Parameters.AddWithValue("@CreatedAt", task.CreatedAt.ToString("o"));
        cmd.Parameters.AddWithValue("@UpdatedAt", task.UpdatedAt.ToString("o"));
        cmd.Parameters.AddWithValue("@CompletedAt", task.CompletedAt.HasValue ? task.CompletedAt.Value.ToString("o") : DBNull.Value);

        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    public async Task UpdateAsync(TaskItem task, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"UPDATE Tasks SET ExternalId = @ExternalId, ParentTaskId = @ParentTaskId, ClientId = @ClientId,
            Title = @Title, Description = @Description, TaskType = @TaskType, Status = @Status,
            AcpSpec = @AcpSpec, VerificationCriteria = @VerificationCriteria, MaxPayoutSats = @MaxPayoutSats,
            ActualPayoutSats = @ActualPayoutSats, PriceUsd = @PriceUsd, AssignedAgentId = @AssignedAgentId,
            Priority = @Priority, UpdatedAt = @UpdatedAt, CompletedAt = @CompletedAt
            WHERE Id = @Id";
        cmd.Parameters.AddWithValue("@Id", task.Id);
        cmd.Parameters.AddWithValue("@ExternalId", task.ExternalId);
        cmd.Parameters.AddWithValue("@ParentTaskId", (object?)task.ParentTaskId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ClientId", task.ClientId);
        cmd.Parameters.AddWithValue("@Title", task.Title);
        cmd.Parameters.AddWithValue("@Description", task.Description);
        cmd.Parameters.AddWithValue("@TaskType", task.TaskType.ToString());
        cmd.Parameters.AddWithValue("@Status", task.Status.ToString());
        cmd.Parameters.AddWithValue("@AcpSpec", (object?)task.AcpSpec ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@VerificationCriteria", (object?)task.VerificationCriteria ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@MaxPayoutSats", task.MaxPayoutSats);
        cmd.Parameters.AddWithValue("@ActualPayoutSats", task.ActualPayoutSats);
        cmd.Parameters.AddWithValue("@PriceUsd", (object?)task.PriceUsd ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@AssignedAgentId", (object?)task.AssignedAgentId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Priority", task.Priority);
        cmd.Parameters.AddWithValue("@UpdatedAt", task.UpdatedAt.ToString("o"));
        cmd.Parameters.AddWithValue("@CompletedAt", task.CompletedAt.HasValue ? task.CompletedAt.Value.ToString("o") : DBNull.Value);

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task UpdateStatusAsync(int id, TaskStatus status, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE Tasks SET Status = @Status, UpdatedAt = @UpdatedAt WHERE Id = @Id";
        cmd.Parameters.AddWithValue("@Id", id);
        cmd.Parameters.AddWithValue("@Status", status.ToString());
        cmd.Parameters.AddWithValue("@UpdatedAt", DateTime.UtcNow.ToString("o"));

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DeleteAsync(int id)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM Tasks WHERE Id = @Id";
        cmd.Parameters.AddWithValue("@Id", id);

        await cmd.ExecuteNonQueryAsync();
    }

    private static TaskItem MapTask(SqliteDataReader reader)
    {
        return new TaskItem
        {
            Id = reader.GetInt32(0),
            ExternalId = reader.GetString(1),
            ParentTaskId = reader.IsDBNull(2) ? null : reader.GetInt32(2),
            ClientId = reader.GetString(3),
            Title = reader.GetString(4),
            Description = reader.GetString(5),
            TaskType = Enum.Parse<TaskType>(reader.GetString(6)),
            Status = Enum.Parse<TaskStatus>(reader.GetString(7)),
            AcpSpec = reader.IsDBNull(8) ? null : reader.GetString(8),
            VerificationCriteria = reader.IsDBNull(9) ? null : reader.GetString(9),
            MaxPayoutSats = reader.GetInt64(10),
            ActualPayoutSats = reader.GetInt64(11),
            PriceUsd = reader.IsDBNull(12) ? null : reader.GetDouble(12),
            AssignedAgentId = reader.IsDBNull(13) ? null : reader.GetInt32(13),
            Priority = reader.GetInt32(14),
            CreatedAt = DateTime.Parse(reader.GetString(15)),
            UpdatedAt = DateTime.Parse(reader.GetString(16)),
            CompletedAt = reader.IsDBNull(17) ? null : DateTime.Parse(reader.GetString(17))
        };
    }
}
