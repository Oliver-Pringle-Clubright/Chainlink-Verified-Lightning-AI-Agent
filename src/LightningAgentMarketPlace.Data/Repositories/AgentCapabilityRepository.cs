using LightningAgentMarketPlace.Core.Enums;
using LightningAgentMarketPlace.Core.Interfaces.Data;
using LightningAgentMarketPlace.Core.Models;
using Microsoft.Data.Sqlite;

namespace LightningAgentMarketPlace.Data.Repositories;

public class AgentCapabilityRepository : IAgentCapabilityRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public AgentCapabilityRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<AgentCapability?> GetByIdAsync(int id)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Id, AgentId, SkillType, TaskTypes, MaxConcurrency, PriceSatsPerUnit, AvgResponseSec, CreatedAt FROM AgentCapabilities WHERE Id = @Id";
        cmd.Parameters.AddWithValue("@Id", id);

        using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? MapCapability(reader) : null;
    }

    public async Task<IReadOnlyList<AgentCapability>> GetByAgentIdAsync(int agentId, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Id, AgentId, SkillType, TaskTypes, MaxConcurrency, PriceSatsPerUnit, AvgResponseSec, CreatedAt FROM AgentCapabilities WHERE AgentId = @AgentId";
        cmd.Parameters.AddWithValue("@AgentId", agentId);

        using var reader = await cmd.ExecuteReaderAsync();
        var results = new List<AgentCapability>();
        while (await reader.ReadAsync())
        {
            results.Add(MapCapability(reader));
        }
        return results;
    }

    public async Task<IReadOnlyList<AgentCapability>> GetBySkillTypeAsync(SkillType skillType, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Id, AgentId, SkillType, TaskTypes, MaxConcurrency, PriceSatsPerUnit, AvgResponseSec, CreatedAt FROM AgentCapabilities WHERE SkillType = @SkillType";
        cmd.Parameters.AddWithValue("@SkillType", skillType.ToString());

        using var reader = await cmd.ExecuteReaderAsync();
        var results = new List<AgentCapability>();
        while (await reader.ReadAsync())
        {
            results.Add(MapCapability(reader));
        }
        return results;
    }

    public async Task<int> CreateAsync(AgentCapability capability, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"INSERT INTO AgentCapabilities (AgentId, SkillType, TaskTypes, MaxConcurrency, PriceSatsPerUnit, AvgResponseSec, CreatedAt)
            VALUES (@AgentId, @SkillType, @TaskTypes, @MaxConcurrency, @PriceSatsPerUnit, @AvgResponseSec, @CreatedAt);
            SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@AgentId", capability.AgentId);
        cmd.Parameters.AddWithValue("@SkillType", capability.SkillType.ToString());
        cmd.Parameters.AddWithValue("@TaskTypes", capability.TaskTypes);
        cmd.Parameters.AddWithValue("@MaxConcurrency", capability.MaxConcurrency);
        cmd.Parameters.AddWithValue("@PriceSatsPerUnit", capability.PriceSatsPerUnit);
        cmd.Parameters.AddWithValue("@AvgResponseSec", (object?)capability.AvgResponseSec ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@CreatedAt", capability.CreatedAt.ToString("o"));

        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    public async Task UpdateAsync(AgentCapability capability)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"UPDATE AgentCapabilities SET AgentId = @AgentId, SkillType = @SkillType, TaskTypes = @TaskTypes,
            MaxConcurrency = @MaxConcurrency, PriceSatsPerUnit = @PriceSatsPerUnit, AvgResponseSec = @AvgResponseSec
            WHERE Id = @Id";
        cmd.Parameters.AddWithValue("@Id", capability.Id);
        cmd.Parameters.AddWithValue("@AgentId", capability.AgentId);
        cmd.Parameters.AddWithValue("@SkillType", capability.SkillType.ToString());
        cmd.Parameters.AddWithValue("@TaskTypes", capability.TaskTypes);
        cmd.Parameters.AddWithValue("@MaxConcurrency", capability.MaxConcurrency);
        cmd.Parameters.AddWithValue("@PriceSatsPerUnit", capability.PriceSatsPerUnit);
        cmd.Parameters.AddWithValue("@AvgResponseSec", (object?)capability.AvgResponseSec ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DeleteAsync(int id)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM AgentCapabilities WHERE Id = @Id";
        cmd.Parameters.AddWithValue("@Id", id);

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DeleteByAgentIdAsync(int agentId, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM AgentCapabilities WHERE AgentId = @AgentId";
        cmd.Parameters.AddWithValue("@AgentId", agentId);

        await cmd.ExecuteNonQueryAsync();
    }

    private static AgentCapability MapCapability(SqliteDataReader reader)
    {
        return new AgentCapability
        {
            Id = reader.GetInt32(0),
            AgentId = reader.GetInt32(1),
            SkillType = Enum.Parse<SkillType>(reader.GetString(2)),
            TaskTypes = reader.GetString(3),
            MaxConcurrency = reader.GetInt32(4),
            PriceSatsPerUnit = reader.GetInt64(5),
            AvgResponseSec = reader.IsDBNull(6) ? null : reader.GetInt32(6),
            CreatedAt = DateTime.Parse(reader.GetString(7))
        };
    }
}
