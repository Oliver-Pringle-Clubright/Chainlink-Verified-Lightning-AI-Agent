using LightningAgent.Core.Enums;
using LightningAgent.Core.Interfaces.Data;
using LightningAgent.Core.Models;
using Microsoft.Data.Sqlite;

namespace LightningAgent.Data.Repositories;

public class PaymentRepository : IPaymentRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;

    private const string SelectColumns = "Id, EscrowId, TaskId, MilestoneId, AgentId, AmountSats, AmountUsd, PaymentHash, PaymentType, Status, CreatedAt, SettledAt, PaymentMethod, ChainId, TokenAddress, TransactionHash, SenderAddress, ReceiverAddress, AmountWei";

    public PaymentRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Payment?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT {SelectColumns} FROM Payments WHERE Id = @Id";
        cmd.Parameters.AddWithValue("@Id", id);

        using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? MapPayment(reader) : null;
    }

    public async Task<IReadOnlyList<Payment>> GetByTaskIdAsync(int taskId, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT {SelectColumns} FROM Payments WHERE TaskId = @TaskId";
        cmd.Parameters.AddWithValue("@TaskId", taskId);

        using var reader = await cmd.ExecuteReaderAsync();
        var results = new List<Payment>();
        while (await reader.ReadAsync())
        {
            results.Add(MapPayment(reader));
        }
        return results;
    }

    public async Task<IReadOnlyList<Payment>> GetByAgentIdAsync(int agentId, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT {SelectColumns} FROM Payments WHERE AgentId = @AgentId";
        cmd.Parameters.AddWithValue("@AgentId", agentId);

        using var reader = await cmd.ExecuteReaderAsync();
        var results = new List<Payment>();
        while (await reader.ReadAsync())
        {
            results.Add(MapPayment(reader));
        }
        return results;
    }

    public async Task<IReadOnlyList<Payment>> GetByEscrowIdAsync(int escrowId)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT {SelectColumns} FROM Payments WHERE EscrowId = @EscrowId";
        cmd.Parameters.AddWithValue("@EscrowId", escrowId);

        using var reader = await cmd.ExecuteReaderAsync();
        var results = new List<Payment>();
        while (await reader.ReadAsync())
        {
            results.Add(MapPayment(reader));
        }
        return results;
    }

    public async Task<int> GetCountAsync(CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM Payments";

        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result);
    }

    public async Task<IReadOnlyList<Payment>> GetPagedAsync(int offset, int limit, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT {SelectColumns} FROM Payments ORDER BY Id DESC LIMIT @Limit OFFSET @Offset";
        cmd.Parameters.AddWithValue("@Limit", limit);
        cmd.Parameters.AddWithValue("@Offset", offset);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<Payment>();
        while (await reader.ReadAsync(ct))
        {
            results.Add(MapPayment(reader));
        }
        return results;
    }

    public async Task<int> GetFilteredCountAsync(int? taskId = null, int? agentId = null, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();

        var where = BuildWhereClause(cmd, taskId, agentId, cursor: null);
        cmd.CommandText = $"SELECT COUNT(*) FROM Payments{where}";

        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result);
    }

    public async Task<IReadOnlyList<Payment>> GetFilteredPagedAsync(
        int offset,
        int limit,
        int? taskId = null,
        int? agentId = null,
        int? cursor = null,
        CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();

        var where = BuildWhereClause(cmd, taskId, agentId, cursor);

        if (cursor.HasValue)
        {
            // Keyset pagination: no OFFSET needed, cursor condition is in WHERE
            cmd.CommandText = $"SELECT {SelectColumns} FROM Payments{where} ORDER BY Id DESC LIMIT @Limit";
        }
        else
        {
            // Classic offset pagination
            cmd.CommandText = $"SELECT {SelectColumns} FROM Payments{where} ORDER BY Id DESC LIMIT @Limit OFFSET @Offset";
            cmd.Parameters.AddWithValue("@Offset", offset);
        }

        cmd.Parameters.AddWithValue("@Limit", limit);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<Payment>();
        while (await reader.ReadAsync(ct))
        {
            results.Add(MapPayment(reader));
        }
        return results;
    }

    /// <summary>
    /// Builds a WHERE clause string and adds corresponding parameters to the command.
    /// Returns a string like " WHERE cond1 AND cond2" or empty string if no conditions.
    /// </summary>
    private static string BuildWhereClause(SqliteCommand cmd, int? taskId, int? agentId, int? cursor)
    {
        var conditions = new List<string>();

        if (taskId.HasValue)
        {
            conditions.Add("TaskId = @TaskId");
            cmd.Parameters.AddWithValue("@TaskId", taskId.Value);
        }

        if (agentId.HasValue)
        {
            conditions.Add("AgentId = @AgentId");
            cmd.Parameters.AddWithValue("@AgentId", agentId.Value);
        }

        if (cursor.HasValue)
        {
            conditions.Add("Id < @Cursor");
            cmd.Parameters.AddWithValue("@Cursor", cursor.Value);
        }

        return conditions.Count > 0
            ? " WHERE " + string.Join(" AND ", conditions)
            : "";
    }

    public async Task<long> GetTotalSatsAsync(CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COALESCE(SUM(AmountSats), 0) FROM Payments";

        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt64(result);
    }

    public async Task<double> GetTotalUsdAsync(CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COALESCE(SUM(AmountUsd), 0.0) FROM Payments WHERE AmountUsd IS NOT NULL";

        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToDouble(result);
    }

    public async Task<int> CreateAsync(Payment payment, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"INSERT INTO Payments (EscrowId, TaskId, MilestoneId, AgentId, AmountSats, AmountUsd, PaymentHash, PaymentType, Status, CreatedAt, SettledAt, PaymentMethod, ChainId, TokenAddress, TransactionHash, SenderAddress, ReceiverAddress, AmountWei)
            VALUES (@EscrowId, @TaskId, @MilestoneId, @AgentId, @AmountSats, @AmountUsd, @PaymentHash, @PaymentType, @Status, @CreatedAt, @SettledAt, @PaymentMethod, @ChainId, @TokenAddress, @TransactionHash, @SenderAddress, @ReceiverAddress, @AmountWei);
            SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@EscrowId", (object?)payment.EscrowId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@TaskId", payment.TaskId);
        cmd.Parameters.AddWithValue("@MilestoneId", (object?)payment.MilestoneId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@AgentId", payment.AgentId);
        cmd.Parameters.AddWithValue("@AmountSats", payment.AmountSats);
        cmd.Parameters.AddWithValue("@AmountUsd", (object?)payment.AmountUsd ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@PaymentHash", (object?)payment.PaymentHash ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@PaymentType", payment.PaymentType.ToString());
        cmd.Parameters.AddWithValue("@Status", payment.Status.ToString());
        cmd.Parameters.AddWithValue("@CreatedAt", payment.CreatedAt.ToString("o"));
        cmd.Parameters.AddWithValue("@SettledAt", payment.SettledAt.HasValue ? payment.SettledAt.Value.ToString("o") : DBNull.Value);
        cmd.Parameters.AddWithValue("@PaymentMethod", payment.PaymentMethod.ToString());
        cmd.Parameters.AddWithValue("@ChainId", (object?)payment.ChainId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@TokenAddress", (object?)payment.TokenAddress ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@TransactionHash", (object?)payment.TransactionHash ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@SenderAddress", (object?)payment.SenderAddress ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ReceiverAddress", (object?)payment.ReceiverAddress ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@AmountWei", (object?)payment.AmountWei ?? DBNull.Value);

        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    public async Task UpdateAsync(Payment payment, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"UPDATE Payments SET EscrowId = @EscrowId, TaskId = @TaskId, MilestoneId = @MilestoneId,
            AgentId = @AgentId, AmountSats = @AmountSats, AmountUsd = @AmountUsd, PaymentHash = @PaymentHash,
            PaymentType = @PaymentType, Status = @Status, SettledAt = @SettledAt,
            PaymentMethod = @PaymentMethod, ChainId = @ChainId, TokenAddress = @TokenAddress,
            TransactionHash = @TransactionHash, SenderAddress = @SenderAddress, ReceiverAddress = @ReceiverAddress, AmountWei = @AmountWei
            WHERE Id = @Id";
        cmd.Parameters.AddWithValue("@Id", payment.Id);
        cmd.Parameters.AddWithValue("@EscrowId", (object?)payment.EscrowId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@TaskId", payment.TaskId);
        cmd.Parameters.AddWithValue("@MilestoneId", (object?)payment.MilestoneId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@AgentId", payment.AgentId);
        cmd.Parameters.AddWithValue("@AmountSats", payment.AmountSats);
        cmd.Parameters.AddWithValue("@AmountUsd", (object?)payment.AmountUsd ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@PaymentHash", (object?)payment.PaymentHash ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@PaymentType", payment.PaymentType.ToString());
        cmd.Parameters.AddWithValue("@Status", payment.Status.ToString());
        cmd.Parameters.AddWithValue("@SettledAt", payment.SettledAt.HasValue ? payment.SettledAt.Value.ToString("o") : DBNull.Value);
        cmd.Parameters.AddWithValue("@PaymentMethod", payment.PaymentMethod.ToString());
        cmd.Parameters.AddWithValue("@ChainId", (object?)payment.ChainId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@TokenAddress", (object?)payment.TokenAddress ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@TransactionHash", (object?)payment.TransactionHash ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@SenderAddress", (object?)payment.SenderAddress ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ReceiverAddress", (object?)payment.ReceiverAddress ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@AmountWei", (object?)payment.AmountWei ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync();
    }

    private static Payment MapPayment(SqliteDataReader reader)
    {
        return new Payment
        {
            Id = reader.GetInt32(0),
            EscrowId = reader.IsDBNull(1) ? null : reader.GetInt32(1),
            TaskId = reader.GetInt32(2),
            MilestoneId = reader.IsDBNull(3) ? null : reader.GetInt32(3),
            AgentId = reader.GetInt32(4),
            AmountSats = reader.GetInt64(5),
            AmountUsd = reader.IsDBNull(6) ? null : reader.GetDouble(6),
            PaymentHash = reader.IsDBNull(7) ? null : reader.GetString(7),
            PaymentType = Enum.Parse<PaymentType>(reader.GetString(8)),
            Status = Enum.Parse<PaymentStatus>(reader.GetString(9)),
            CreatedAt = DateTime.Parse(reader.GetString(10)),
            SettledAt = reader.IsDBNull(11) ? null : DateTime.Parse(reader.GetString(11)),
            PaymentMethod = reader.FieldCount > 12 && !reader.IsDBNull(12) ? Enum.Parse<PaymentMethod>(reader.GetString(12)) : PaymentMethod.Lightning,
            ChainId = reader.FieldCount > 13 && !reader.IsDBNull(13) ? reader.GetInt64(13) : null,
            TokenAddress = reader.FieldCount > 14 && !reader.IsDBNull(14) ? reader.GetString(14) : null,
            TransactionHash = reader.FieldCount > 15 && !reader.IsDBNull(15) ? reader.GetString(15) : null,
            SenderAddress = reader.FieldCount > 16 && !reader.IsDBNull(16) ? reader.GetString(16) : null,
            ReceiverAddress = reader.FieldCount > 17 && !reader.IsDBNull(17) ? reader.GetString(17) : null,
            AmountWei = reader.FieldCount > 18 && !reader.IsDBNull(18) ? reader.GetString(18) : null
        };
    }
}
