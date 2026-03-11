using LightningAgent.Core.Interfaces.Data;
using LightningAgent.Core.Models;
using Microsoft.Data.Sqlite;

namespace LightningAgent.Data.Repositories;

public class WebhookLogRepository : IWebhookLogRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;

    private const string SelectColumns =
        "Id, WebhookUrl, EventType, Payload, Attempts, LastAttemptAt, Status, ErrorMessage, CreatedAt";

    public WebhookLogRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<int> LogAsync(WebhookDeliveryLog entry, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"INSERT INTO WebhookDeliveryLog
            (WebhookUrl, EventType, Payload, Attempts, LastAttemptAt, Status, ErrorMessage, CreatedAt)
            VALUES
            (@WebhookUrl, @EventType, @Payload, @Attempts, @LastAttemptAt, @Status, @ErrorMessage, @CreatedAt);
            SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@WebhookUrl", entry.WebhookUrl);
        cmd.Parameters.AddWithValue("@EventType", entry.EventType);
        cmd.Parameters.AddWithValue("@Payload", entry.Payload);
        cmd.Parameters.AddWithValue("@Attempts", entry.Attempts);
        cmd.Parameters.AddWithValue("@LastAttemptAt", entry.LastAttemptAt.ToString("o"));
        cmd.Parameters.AddWithValue("@Status", entry.Status.ToString());
        cmd.Parameters.AddWithValue("@ErrorMessage", (object?)entry.ErrorMessage ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@CreatedAt", entry.CreatedAt.ToString("o"));

        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result);
    }

    public async Task UpdateAsync(WebhookDeliveryLog entry, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"UPDATE WebhookDeliveryLog
            SET Attempts = @Attempts,
                LastAttemptAt = @LastAttemptAt,
                Status = @Status,
                ErrorMessage = @ErrorMessage
            WHERE Id = @Id";
        cmd.Parameters.AddWithValue("@Id", entry.Id);
        cmd.Parameters.AddWithValue("@Attempts", entry.Attempts);
        cmd.Parameters.AddWithValue("@LastAttemptAt", entry.LastAttemptAt.ToString("o"));
        cmd.Parameters.AddWithValue("@Status", entry.Status.ToString());
        cmd.Parameters.AddWithValue("@ErrorMessage", (object?)entry.ErrorMessage ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<WebhookDeliveryLog>> GetFailedAsync(int limit = 100, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT {SelectColumns} FROM WebhookDeliveryLog WHERE Status = 'Failed' ORDER BY LastAttemptAt DESC LIMIT @Limit";
        cmd.Parameters.AddWithValue("@Limit", limit);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<WebhookDeliveryLog>();
        while (await reader.ReadAsync(ct))
        {
            results.Add(MapWebhookDeliveryLog(reader));
        }
        return results;
    }

    public async Task<IReadOnlyList<WebhookDeliveryLog>> GetRecentAsync(int count, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT {SelectColumns} FROM WebhookDeliveryLog ORDER BY CreatedAt DESC LIMIT @Count";
        cmd.Parameters.AddWithValue("@Count", count);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<WebhookDeliveryLog>();
        while (await reader.ReadAsync(ct))
        {
            results.Add(MapWebhookDeliveryLog(reader));
        }
        return results;
    }

    public async Task<int> DeleteOlderThanAsync(DateTime cutoff, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM WebhookDeliveryLog WHERE Status = 'Failed' AND CreatedAt < @Cutoff";
        cmd.Parameters.AddWithValue("@Cutoff", cutoff.ToString("o"));

        return await cmd.ExecuteNonQueryAsync(ct);
    }

    private static WebhookDeliveryLog MapWebhookDeliveryLog(SqliteDataReader reader)
    {
        return new WebhookDeliveryLog
        {
            Id = reader.GetInt32(0),
            WebhookUrl = reader.GetString(1),
            EventType = reader.GetString(2),
            Payload = reader.GetString(3),
            Attempts = reader.GetInt32(4),
            LastAttemptAt = DateTime.Parse(reader.GetString(5)),
            Status = Enum.Parse<WebhookDeliveryStatus>(reader.GetString(6)),
            ErrorMessage = reader.IsDBNull(7) ? null : reader.GetString(7),
            CreatedAt = DateTime.Parse(reader.GetString(8))
        };
    }
}
