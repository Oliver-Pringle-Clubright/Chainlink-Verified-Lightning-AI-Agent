using LightningAgent.Core.Interfaces.Data;
using LightningAgent.Core.Models;
using Microsoft.Data.Sqlite;

namespace LightningAgent.Data.Repositories;

public class SpendLimitRepository : ISpendLimitRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;

    private const string SelectColumns = "Id, AgentId, TaskId, LimitType, MaxSats, CurrentSpentSats, PeriodStart, PeriodEnd";

    public SpendLimitRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<SpendLimit?> GetByIdAsync(int id)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT {SelectColumns} FROM SpendLimits WHERE Id = @Id";
        cmd.Parameters.AddWithValue("@Id", id);

        using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? MapSpendLimit(reader) : null;
    }

    public async Task<SpendLimit?> GetByAgentIdAsync(int agentId, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT {SelectColumns} FROM SpendLimits WHERE AgentId = @AgentId LIMIT 1";
        cmd.Parameters.AddWithValue("@AgentId", agentId);

        using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? MapSpendLimit(reader) : null;
    }

    public async Task<SpendLimit?> GetByTaskIdAsync(int taskId, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT {SelectColumns} FROM SpendLimits WHERE TaskId = @TaskId LIMIT 1";
        cmd.Parameters.AddWithValue("@TaskId", taskId);

        using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? MapSpendLimit(reader) : null;
    }

    public async Task<IReadOnlyList<SpendLimit>> GetActiveAsync(DateTime asOf)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT {SelectColumns} FROM SpendLimits WHERE PeriodStart <= @AsOf AND PeriodEnd >= @AsOf";
        cmd.Parameters.AddWithValue("@AsOf", asOf.ToString("o"));

        using var reader = await cmd.ExecuteReaderAsync();
        var results = new List<SpendLimit>();
        while (await reader.ReadAsync())
        {
            results.Add(MapSpendLimit(reader));
        }
        return results;
    }

    public async Task<int> CreateAsync(SpendLimit spendLimit, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"INSERT INTO SpendLimits (AgentId, TaskId, LimitType, MaxSats, CurrentSpentSats, PeriodStart, PeriodEnd)
            VALUES (@AgentId, @TaskId, @LimitType, @MaxSats, @CurrentSpentSats, @PeriodStart, @PeriodEnd);
            SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@AgentId", (object?)spendLimit.AgentId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@TaskId", (object?)spendLimit.TaskId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@LimitType", spendLimit.LimitType);
        cmd.Parameters.AddWithValue("@MaxSats", spendLimit.MaxSats);
        cmd.Parameters.AddWithValue("@CurrentSpentSats", spendLimit.CurrentSpentSats);
        cmd.Parameters.AddWithValue("@PeriodStart", spendLimit.PeriodStart.ToString("o"));
        cmd.Parameters.AddWithValue("@PeriodEnd", spendLimit.PeriodEnd.ToString("o"));

        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    public async Task UpdateAsync(SpendLimit spendLimit, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"UPDATE SpendLimits SET AgentId = @AgentId, TaskId = @TaskId, LimitType = @LimitType,
            MaxSats = @MaxSats, CurrentSpentSats = @CurrentSpentSats,
            PeriodStart = @PeriodStart, PeriodEnd = @PeriodEnd
            WHERE Id = @Id";
        cmd.Parameters.AddWithValue("@Id", spendLimit.Id);
        cmd.Parameters.AddWithValue("@AgentId", (object?)spendLimit.AgentId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@TaskId", (object?)spendLimit.TaskId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@LimitType", spendLimit.LimitType);
        cmd.Parameters.AddWithValue("@MaxSats", spendLimit.MaxSats);
        cmd.Parameters.AddWithValue("@CurrentSpentSats", spendLimit.CurrentSpentSats);
        cmd.Parameters.AddWithValue("@PeriodStart", spendLimit.PeriodStart.ToString("o"));
        cmd.Parameters.AddWithValue("@PeriodEnd", spendLimit.PeriodEnd.ToString("o"));

        await cmd.ExecuteNonQueryAsync();
    }

    private static SpendLimit MapSpendLimit(SqliteDataReader reader)
    {
        return new SpendLimit
        {
            Id = reader.GetInt32(0),
            AgentId = reader.IsDBNull(1) ? null : reader.GetInt32(1),
            TaskId = reader.IsDBNull(2) ? null : reader.GetInt32(2),
            LimitType = reader.GetString(3),
            MaxSats = reader.GetInt64(4),
            CurrentSpentSats = reader.GetInt64(5),
            PeriodStart = DateTime.Parse(reader.GetString(6)),
            PeriodEnd = DateTime.Parse(reader.GetString(7))
        };
    }
}
