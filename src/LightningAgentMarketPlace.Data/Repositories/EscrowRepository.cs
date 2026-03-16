using LightningAgentMarketPlace.Core.Enums;
using LightningAgentMarketPlace.Core.Interfaces.Data;
using LightningAgentMarketPlace.Core.Models;
using Microsoft.Data.Sqlite;

namespace LightningAgentMarketPlace.Data.Repositories;

public class EscrowRepository : IEscrowRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;

    private const string SelectColumns = "Id, MilestoneId, TaskId, AmountSats, PaymentHash, PaymentPreimage, Status, HodlInvoice, CreatedAt, SettledAt, ExpiresAt";

    public EscrowRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Escrow?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT {SelectColumns} FROM Escrows WHERE Id = @Id";
        cmd.Parameters.AddWithValue("@Id", id);

        using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? MapEscrow(reader) : null;
    }

    public async Task<Escrow?> GetByPaymentHashAsync(string paymentHash, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT {SelectColumns} FROM Escrows WHERE PaymentHash = @PaymentHash";
        cmd.Parameters.AddWithValue("@PaymentHash", paymentHash);

        using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? MapEscrow(reader) : null;
    }

    public async Task<Escrow?> GetByMilestoneIdAsync(int milestoneId, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT {SelectColumns} FROM Escrows WHERE MilestoneId = @MilestoneId";
        cmd.Parameters.AddWithValue("@MilestoneId", milestoneId);

        using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? MapEscrow(reader) : null;
    }

    public async Task<IReadOnlyList<Escrow>> GetByTaskIdAsync(int taskId)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT {SelectColumns} FROM Escrows WHERE TaskId = @TaskId";
        cmd.Parameters.AddWithValue("@TaskId", taskId);

        using var reader = await cmd.ExecuteReaderAsync();
        var results = new List<Escrow>();
        while (await reader.ReadAsync())
        {
            results.Add(MapEscrow(reader));
        }
        return results;
    }

    public async Task<IReadOnlyList<Escrow>> GetByStatusAsync(EscrowStatus status, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT {SelectColumns} FROM Escrows WHERE Status = @Status";
        cmd.Parameters.AddWithValue("@Status", status.ToString());

        using var reader = await cmd.ExecuteReaderAsync();
        var results = new List<Escrow>();
        while (await reader.ReadAsync())
        {
            results.Add(MapEscrow(reader));
        }
        return results;
    }

    public async Task<IReadOnlyList<Escrow>> GetExpiredAsync(DateTime asOf)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT {SelectColumns} FROM Escrows WHERE Status = 'Held' AND ExpiresAt <= @AsOf";
        cmd.Parameters.AddWithValue("@AsOf", asOf.ToString("o"));

        using var reader = await cmd.ExecuteReaderAsync();
        var results = new List<Escrow>();
        while (await reader.ReadAsync())
        {
            results.Add(MapEscrow(reader));
        }
        return results;
    }

    public async Task<int> CreateAsync(Escrow escrow, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"INSERT INTO Escrows (MilestoneId, TaskId, AmountSats, PaymentHash, PaymentPreimage, Status, HodlInvoice, CreatedAt, SettledAt, ExpiresAt)
            VALUES (@MilestoneId, @TaskId, @AmountSats, @PaymentHash, @PaymentPreimage, @Status, @HodlInvoice, @CreatedAt, @SettledAt, @ExpiresAt);
            SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@MilestoneId", escrow.MilestoneId);
        cmd.Parameters.AddWithValue("@TaskId", escrow.TaskId);
        cmd.Parameters.AddWithValue("@AmountSats", escrow.AmountSats);
        cmd.Parameters.AddWithValue("@PaymentHash", escrow.PaymentHash);
        cmd.Parameters.AddWithValue("@PaymentPreimage", (object?)escrow.PaymentPreimage ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Status", escrow.Status.ToString());
        cmd.Parameters.AddWithValue("@HodlInvoice", escrow.HodlInvoice);
        cmd.Parameters.AddWithValue("@CreatedAt", escrow.CreatedAt.ToString("o"));
        cmd.Parameters.AddWithValue("@SettledAt", escrow.SettledAt.HasValue ? escrow.SettledAt.Value.ToString("o") : DBNull.Value);
        cmd.Parameters.AddWithValue("@ExpiresAt", escrow.ExpiresAt.ToString("o"));

        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    public async Task UpdateAsync(Escrow escrow, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"UPDATE Escrows SET MilestoneId = @MilestoneId, TaskId = @TaskId, AmountSats = @AmountSats,
            PaymentHash = @PaymentHash, PaymentPreimage = @PaymentPreimage, Status = @Status,
            HodlInvoice = @HodlInvoice, SettledAt = @SettledAt, ExpiresAt = @ExpiresAt
            WHERE Id = @Id";
        cmd.Parameters.AddWithValue("@Id", escrow.Id);
        cmd.Parameters.AddWithValue("@MilestoneId", escrow.MilestoneId);
        cmd.Parameters.AddWithValue("@TaskId", escrow.TaskId);
        cmd.Parameters.AddWithValue("@AmountSats", escrow.AmountSats);
        cmd.Parameters.AddWithValue("@PaymentHash", escrow.PaymentHash);
        cmd.Parameters.AddWithValue("@PaymentPreimage", (object?)escrow.PaymentPreimage ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Status", escrow.Status.ToString());
        cmd.Parameters.AddWithValue("@HodlInvoice", escrow.HodlInvoice);
        cmd.Parameters.AddWithValue("@SettledAt", escrow.SettledAt.HasValue ? escrow.SettledAt.Value.ToString("o") : DBNull.Value);
        cmd.Parameters.AddWithValue("@ExpiresAt", escrow.ExpiresAt.ToString("o"));

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task UpdateStatusAsync(int id, EscrowStatus status, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE Escrows SET Status = @Status WHERE Id = @Id";
        cmd.Parameters.AddWithValue("@Id", id);
        cmd.Parameters.AddWithValue("@Status", status.ToString());

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<int> GetCountByStatusAsync(EscrowStatus status, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM Escrows WHERE Status = @Status";
        cmd.Parameters.AddWithValue("@Status", status.ToString());

        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result);
    }

    public async Task<long> GetHeldAmountSatsAsync(CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COALESCE(SUM(AmountSats), 0) FROM Escrows WHERE Status = 'Held'";

        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt64(result);
    }

    private static Escrow MapEscrow(SqliteDataReader reader)
    {
        return new Escrow
        {
            Id = reader.GetInt32(0),
            MilestoneId = reader.GetInt32(1),
            TaskId = reader.GetInt32(2),
            AmountSats = reader.GetInt64(3),
            PaymentHash = reader.GetString(4),
            PaymentPreimage = reader.IsDBNull(5) ? null : reader.GetString(5),
            Status = Enum.Parse<EscrowStatus>(reader.GetString(6)),
            HodlInvoice = reader.GetString(7),
            CreatedAt = DateTime.Parse(reader.GetString(8)),
            SettledAt = reader.IsDBNull(9) ? null : DateTime.Parse(reader.GetString(9)),
            ExpiresAt = DateTime.Parse(reader.GetString(10))
        };
    }
}
