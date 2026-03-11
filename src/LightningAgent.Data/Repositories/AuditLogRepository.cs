using LightningAgent.Core.Interfaces.Data;
using LightningAgent.Core.Models;
using Microsoft.Data.Sqlite;

namespace LightningAgent.Data.Repositories;

public class AuditLogRepository : IAuditLogRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;

    private const string SelectColumns =
        "Id, EventType, EntityType, EntityId, AgentId, Action, Details, IpAddress, UserAgent, CreatedAt";

    public AuditLogRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<int> CreateAsync(AuditLogEntry entry, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"INSERT INTO AuditLog
            (EventType, EntityType, EntityId, AgentId, Action, Details, IpAddress, UserAgent, CreatedAt)
            VALUES
            (@EventType, @EntityType, @EntityId, @AgentId, @Action, @Details, @IpAddress, @UserAgent, @CreatedAt);
            SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@EventType", entry.EventType);
        cmd.Parameters.AddWithValue("@EntityType", entry.EntityType);
        cmd.Parameters.AddWithValue("@EntityId", entry.EntityId);
        cmd.Parameters.AddWithValue("@AgentId", (object?)entry.AgentId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Action", (object?)entry.Action ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Details", (object?)entry.Details ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@IpAddress", (object?)entry.IpAddress ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@UserAgent", (object?)entry.UserAgent ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@CreatedAt", entry.CreatedAt.ToString("o"));

        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result);
    }

    public async Task<IReadOnlyList<AuditLogEntry>> GetByAgentAsync(int agentId, int limit = 100, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT {SelectColumns} FROM AuditLog WHERE AgentId = @AgentId ORDER BY CreatedAt DESC LIMIT @Limit";
        cmd.Parameters.AddWithValue("@AgentId", agentId);
        cmd.Parameters.AddWithValue("@Limit", limit);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<AuditLogEntry>();
        while (await reader.ReadAsync(ct))
        {
            results.Add(MapAuditLogEntry(reader));
        }
        return results;
    }

    public async Task<IReadOnlyList<AuditLogEntry>> GetByEntityAsync(string entityType, int entityId, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT {SelectColumns} FROM AuditLog WHERE EntityType = @EntityType AND EntityId = @EntityId ORDER BY CreatedAt DESC";
        cmd.Parameters.AddWithValue("@EntityType", entityType);
        cmd.Parameters.AddWithValue("@EntityId", entityId);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<AuditLogEntry>();
        while (await reader.ReadAsync(ct))
        {
            results.Add(MapAuditLogEntry(reader));
        }
        return results;
    }

    public async Task<IReadOnlyList<AuditLogEntry>> GetRecentAsync(int count, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT {SelectColumns} FROM AuditLog ORDER BY CreatedAt DESC LIMIT @Count";
        cmd.Parameters.AddWithValue("@Count", count);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<AuditLogEntry>();
        while (await reader.ReadAsync(ct))
        {
            results.Add(MapAuditLogEntry(reader));
        }
        return results;
    }

    public async Task<IReadOnlyList<AuditLogEntry>> GetByEventTypeAsync(string eventType)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT {SelectColumns} FROM AuditLog WHERE EventType = @EventType ORDER BY CreatedAt DESC";
        cmd.Parameters.AddWithValue("@EventType", eventType);

        using var reader = await cmd.ExecuteReaderAsync();
        var results = new List<AuditLogEntry>();
        while (await reader.ReadAsync())
        {
            results.Add(MapAuditLogEntry(reader));
        }
        return results;
    }

    private static AuditLogEntry MapAuditLogEntry(SqliteDataReader reader)
    {
        return new AuditLogEntry
        {
            Id = reader.GetInt32(0),
            EventType = reader.GetString(1),
            EntityType = reader.GetString(2),
            EntityId = reader.GetInt32(3),
            AgentId = reader.IsDBNull(4) ? null : reader.GetInt32(4),
            Action = reader.IsDBNull(5) ? null : reader.GetString(5),
            Details = reader.IsDBNull(6) ? null : reader.GetString(6),
            IpAddress = reader.IsDBNull(7) ? null : reader.GetString(7),
            UserAgent = reader.IsDBNull(8) ? null : reader.GetString(8),
            CreatedAt = DateTime.Parse(reader.GetString(9))
        };
    }
}
