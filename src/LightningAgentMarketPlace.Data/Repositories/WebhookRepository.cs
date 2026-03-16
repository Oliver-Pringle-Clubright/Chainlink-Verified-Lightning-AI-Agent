using LightningAgentMarketPlace.Core.Interfaces.Data;
using LightningAgentMarketPlace.Core.Models;
using Microsoft.Data.Sqlite;

namespace LightningAgentMarketPlace.Data.Repositories;

public class WebhookRepository : IWebhookRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;

    private const string SelectColumns = "Id, AgentId, Url, Events, Secret, Active, CreatedAt";

    public WebhookRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<int> CreateAsync(WebhookSubscription subscription, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"INSERT INTO WebhookSubscriptions (AgentId, Url, Events, Secret, Active, CreatedAt)
            VALUES (@AgentId, @Url, @Events, @Secret, @Active, @CreatedAt);
            SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@AgentId", (object?)subscription.AgentId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Url", subscription.Url);
        cmd.Parameters.AddWithValue("@Events", subscription.Events);
        cmd.Parameters.AddWithValue("@Secret", (object?)subscription.Secret ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Active", subscription.Active ? 1 : 0);
        cmd.Parameters.AddWithValue("@CreatedAt", subscription.CreatedAt.ToString("o"));

        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result);
    }

    public async Task<WebhookSubscription?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT {SelectColumns} FROM WebhookSubscriptions WHERE Id = @Id";
        cmd.Parameters.AddWithValue("@Id", id);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? MapSubscription(reader) : null;
    }

    public async Task<IReadOnlyList<WebhookSubscription>> GetByAgentIdAsync(int agentId, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT {SelectColumns} FROM WebhookSubscriptions WHERE AgentId = @AgentId";
        cmd.Parameters.AddWithValue("@AgentId", agentId);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<WebhookSubscription>();
        while (await reader.ReadAsync(ct))
        {
            results.Add(MapSubscription(reader));
        }
        return results;
    }

    public async Task<IReadOnlyList<WebhookSubscription>> GetActiveByEventAsync(string eventType, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        // Match subscriptions where the Events column contains the event type
        cmd.CommandText = $"SELECT {SelectColumns} FROM WebhookSubscriptions WHERE Active = 1 AND (',' || Events || ',' LIKE '%,' || @EventType || ',%')";
        cmd.Parameters.AddWithValue("@EventType", eventType);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<WebhookSubscription>();
        while (await reader.ReadAsync(ct))
        {
            results.Add(MapSubscription(reader));
        }
        return results;
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM WebhookSubscriptions WHERE Id = @Id";
        cmd.Parameters.AddWithValue("@Id", id);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static WebhookSubscription MapSubscription(SqliteDataReader reader)
    {
        return new WebhookSubscription
        {
            Id = reader.GetInt32(0),
            AgentId = reader.IsDBNull(1) ? null : reader.GetInt32(1),
            Url = reader.GetString(2),
            Events = reader.GetString(3),
            Secret = reader.IsDBNull(4) ? null : reader.GetString(4),
            Active = reader.GetInt32(5) == 1,
            CreatedAt = DateTime.Parse(reader.GetString(6))
        };
    }
}
