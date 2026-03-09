using LightningAgent.Core.Interfaces.Data;
using LightningAgent.Core.Models;
using Microsoft.Data.Sqlite;

namespace LightningAgent.Data.Repositories;

public class AuditLogRepository : IAuditLogRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;

    private const string SelectColumns = "Id, EventType, EntityType, EntityId, Details, CreatedAt";

    public AuditLogRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
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

    public async Task<IReadOnlyList<AuditLogEntry>> GetByEntityAsync(string entityType, int entityId, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT {SelectColumns} FROM AuditLog WHERE EntityType = @EntityType AND EntityId = @EntityId ORDER BY CreatedAt DESC";
        cmd.Parameters.AddWithValue("@EntityType", entityType);
        cmd.Parameters.AddWithValue("@EntityId", entityId);

        using var reader = await cmd.ExecuteReaderAsync();
        var results = new List<AuditLogEntry>();
        while (await reader.ReadAsync())
        {
            results.Add(MapAuditLogEntry(reader));
        }
        return results;
    }

    public async Task<IReadOnlyList<AuditLogEntry>> GetRecentAsync(int count)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT {SelectColumns} FROM AuditLog ORDER BY CreatedAt DESC LIMIT @Count";
        cmd.Parameters.AddWithValue("@Count", count);

        using var reader = await cmd.ExecuteReaderAsync();
        var results = new List<AuditLogEntry>();
        while (await reader.ReadAsync())
        {
            results.Add(MapAuditLogEntry(reader));
        }
        return results;
    }

    public async Task<int> CreateAsync(AuditLogEntry entry, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"INSERT INTO AuditLog (EventType, EntityType, EntityId, Details, CreatedAt)
            VALUES (@EventType, @EntityType, @EntityId, @Details, @CreatedAt);
            SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@EventType", entry.EventType);
        cmd.Parameters.AddWithValue("@EntityType", entry.EntityType);
        cmd.Parameters.AddWithValue("@EntityId", entry.EntityId);
        cmd.Parameters.AddWithValue("@Details", (object?)entry.Details ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@CreatedAt", entry.CreatedAt.ToString("o"));

        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    private static AuditLogEntry MapAuditLogEntry(SqliteDataReader reader)
    {
        return new AuditLogEntry
        {
            Id = reader.GetInt32(0),
            EventType = reader.GetString(1),
            EntityType = reader.GetString(2),
            EntityId = reader.GetInt32(3),
            Details = reader.IsDBNull(4) ? null : reader.GetString(4),
            CreatedAt = DateTime.Parse(reader.GetString(5))
        };
    }
}
