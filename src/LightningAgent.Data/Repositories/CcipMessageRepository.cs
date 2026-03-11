using LightningAgent.Core.Interfaces.Data;
using LightningAgent.Core.Models.Chainlink;

namespace LightningAgent.Data.Repositories;

public class CcipMessageRepository : ICcipMessageRepository
{
    private readonly SqliteConnectionFactory _factory;

    public CcipMessageRepository(SqliteConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task<int> CreateAsync(CcipMessage message, CancellationToken ct = default)
    {
        using var conn = _factory.CreateConnection();
        using var cmd = conn.CreateCommand();

        cmd.CommandText = @"
            INSERT INTO CcipMessages
                (MessageId, SourceChainSelector, DestinationChainSelector, SenderAddress,
                 ReceiverAddress, Payload, TokenAddress, TokenAmountWei, FeeToken,
                 Direction, Status, TxHash, ErrorDetails, TaskId, AgentId, CreatedAt, DeliveredAt)
            VALUES
                (@MessageId, @SourceChainSelector, @DestinationChainSelector, @SenderAddress,
                 @ReceiverAddress, @Payload, @TokenAddress, @TokenAmountWei, @FeeToken,
                 @Direction, @Status, @TxHash, @ErrorDetails, @TaskId, @AgentId, @CreatedAt, @DeliveredAt);
            SELECT last_insert_rowid();";

        cmd.Parameters.AddWithValue("@MessageId", message.MessageId);
        cmd.Parameters.AddWithValue("@SourceChainSelector", (long)message.SourceChainSelector);
        cmd.Parameters.AddWithValue("@DestinationChainSelector", (long)message.DestinationChainSelector);
        cmd.Parameters.AddWithValue("@SenderAddress", message.SenderAddress);
        cmd.Parameters.AddWithValue("@ReceiverAddress", message.ReceiverAddress);
        cmd.Parameters.AddWithValue("@Payload", message.Payload);
        cmd.Parameters.AddWithValue("@TokenAddress", (object?)message.TokenAddress ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@TokenAmountWei", message.TokenAmountWei);
        cmd.Parameters.AddWithValue("@FeeToken", message.FeeToken);
        cmd.Parameters.AddWithValue("@Direction", message.Direction);
        cmd.Parameters.AddWithValue("@Status", message.Status);
        cmd.Parameters.AddWithValue("@TxHash", (object?)message.TxHash ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ErrorDetails", (object?)message.ErrorDetails ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@TaskId", (object?)message.TaskId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@AgentId", (object?)message.AgentId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@CreatedAt", message.CreatedAt.ToString("o"));
        cmd.Parameters.AddWithValue("@DeliveredAt", message.DeliveredAt.HasValue ? message.DeliveredAt.Value.ToString("o") : DBNull.Value);

        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result);
    }

    public async Task<CcipMessage?> GetByMessageIdAsync(string messageId, CancellationToken ct = default)
    {
        using var conn = _factory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM CcipMessages WHERE MessageId = @MessageId";
        cmd.Parameters.AddWithValue("@MessageId", messageId);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? MapRow(reader) : null;
    }

    public async Task<List<CcipMessage>> GetPendingOutboundAsync(CancellationToken ct = default)
    {
        using var conn = _factory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM CcipMessages WHERE Direction = 'Outbound' AND Status = 'Sent' ORDER BY CreatedAt";

        using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<CcipMessage>();
        while (await reader.ReadAsync(ct))
            results.Add(MapRow(reader));
        return results;
    }

    public async Task<List<CcipMessage>> GetByTaskIdAsync(int taskId, CancellationToken ct = default)
    {
        using var conn = _factory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM CcipMessages WHERE TaskId = @TaskId ORDER BY CreatedAt DESC";
        cmd.Parameters.AddWithValue("@TaskId", taskId);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<CcipMessage>();
        while (await reader.ReadAsync(ct))
            results.Add(MapRow(reader));
        return results;
    }

    public async Task<List<CcipMessage>> GetByAgentIdAsync(int agentId, CancellationToken ct = default)
    {
        using var conn = _factory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM CcipMessages WHERE AgentId = @AgentId ORDER BY CreatedAt DESC";
        cmd.Parameters.AddWithValue("@AgentId", agentId);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<CcipMessage>();
        while (await reader.ReadAsync(ct))
            results.Add(MapRow(reader));
        return results;
    }

    public async Task<List<CcipMessage>> GetRecentAsync(int limit = 50, CancellationToken ct = default)
    {
        using var conn = _factory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM CcipMessages ORDER BY CreatedAt DESC LIMIT @Limit";
        cmd.Parameters.AddWithValue("@Limit", limit);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<CcipMessage>();
        while (await reader.ReadAsync(ct))
            results.Add(MapRow(reader));
        return results;
    }

    public async Task UpdateAsync(CcipMessage message, CancellationToken ct = default)
    {
        using var conn = _factory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE CcipMessages SET
                Status = @Status,
                TxHash = @TxHash,
                ErrorDetails = @ErrorDetails,
                DeliveredAt = @DeliveredAt
            WHERE Id = @Id";

        cmd.Parameters.AddWithValue("@Status", message.Status);
        cmd.Parameters.AddWithValue("@TxHash", (object?)message.TxHash ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ErrorDetails", (object?)message.ErrorDetails ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@DeliveredAt", message.DeliveredAt.HasValue ? message.DeliveredAt.Value.ToString("o") : DBNull.Value);
        cmd.Parameters.AddWithValue("@Id", message.Id);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static CcipMessage MapRow(Microsoft.Data.Sqlite.SqliteDataReader reader)
    {
        return new CcipMessage
        {
            Id = reader.GetInt32(reader.GetOrdinal("Id")),
            MessageId = reader.GetString(reader.GetOrdinal("MessageId")),
            SourceChainSelector = (ulong)reader.GetInt64(reader.GetOrdinal("SourceChainSelector")),
            DestinationChainSelector = (ulong)reader.GetInt64(reader.GetOrdinal("DestinationChainSelector")),
            SenderAddress = reader.GetString(reader.GetOrdinal("SenderAddress")),
            ReceiverAddress = reader.GetString(reader.GetOrdinal("ReceiverAddress")),
            Payload = reader.GetString(reader.GetOrdinal("Payload")),
            TokenAddress = reader.IsDBNull(reader.GetOrdinal("TokenAddress")) ? null : reader.GetString(reader.GetOrdinal("TokenAddress")),
            TokenAmountWei = reader.GetInt64(reader.GetOrdinal("TokenAmountWei")),
            FeeToken = reader.GetString(reader.GetOrdinal("FeeToken")),
            Direction = reader.GetString(reader.GetOrdinal("Direction")),
            Status = reader.GetString(reader.GetOrdinal("Status")),
            TxHash = reader.IsDBNull(reader.GetOrdinal("TxHash")) ? null : reader.GetString(reader.GetOrdinal("TxHash")),
            ErrorDetails = reader.IsDBNull(reader.GetOrdinal("ErrorDetails")) ? null : reader.GetString(reader.GetOrdinal("ErrorDetails")),
            TaskId = reader.IsDBNull(reader.GetOrdinal("TaskId")) ? null : reader.GetInt32(reader.GetOrdinal("TaskId")),
            AgentId = reader.IsDBNull(reader.GetOrdinal("AgentId")) ? null : reader.GetInt32(reader.GetOrdinal("AgentId")),
            CreatedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("CreatedAt"))),
            DeliveredAt = reader.IsDBNull(reader.GetOrdinal("DeliveredAt")) ? null : DateTime.Parse(reader.GetString(reader.GetOrdinal("DeliveredAt")))
        };
    }
}
