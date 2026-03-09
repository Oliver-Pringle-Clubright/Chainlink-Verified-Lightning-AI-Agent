using LightningAgent.Core.Enums;
using LightningAgent.Core.Interfaces.Data;
using LightningAgent.Core.Models;
using Microsoft.Data.Sqlite;

namespace LightningAgent.Data.Repositories;

public class PaymentRepository : IPaymentRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;

    private const string SelectColumns = "Id, EscrowId, TaskId, MilestoneId, AgentId, AmountSats, AmountUsd, PaymentHash, PaymentType, Status, CreatedAt, SettledAt";

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

    public async Task<int> CreateAsync(Payment payment, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"INSERT INTO Payments (EscrowId, TaskId, MilestoneId, AgentId, AmountSats, AmountUsd, PaymentHash, PaymentType, Status, CreatedAt, SettledAt)
            VALUES (@EscrowId, @TaskId, @MilestoneId, @AgentId, @AmountSats, @AmountUsd, @PaymentHash, @PaymentType, @Status, @CreatedAt, @SettledAt);
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

        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    public async Task UpdateAsync(Payment payment, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"UPDATE Payments SET EscrowId = @EscrowId, TaskId = @TaskId, MilestoneId = @MilestoneId,
            AgentId = @AgentId, AmountSats = @AmountSats, AmountUsd = @AmountUsd, PaymentHash = @PaymentHash,
            PaymentType = @PaymentType, Status = @Status, SettledAt = @SettledAt
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
            SettledAt = reader.IsDBNull(11) ? null : DateTime.Parse(reader.GetString(11))
        };
    }
}
