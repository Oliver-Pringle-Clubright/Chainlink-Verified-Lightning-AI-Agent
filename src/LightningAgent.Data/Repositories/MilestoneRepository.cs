using LightningAgent.Core.Enums;
using LightningAgent.Core.Interfaces.Data;
using LightningAgent.Core.Models;
using Microsoft.Data.Sqlite;

namespace LightningAgent.Data.Repositories;

public class MilestoneRepository : IMilestoneRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;

    private const string SelectColumns = "Id, TaskId, SequenceNumber, Title, Description, VerificationCriteria, PayoutSats, Status, VerificationResult, InvoicePaymentHash, CreatedAt, VerifiedAt, PaidAt, OutputData";

    public MilestoneRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Milestone?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT {SelectColumns} FROM Milestones WHERE Id = @Id";
        cmd.Parameters.AddWithValue("@Id", id);

        using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? MapMilestone(reader) : null;
    }

    public async Task<IReadOnlyList<Milestone>> GetByTaskIdAsync(int taskId, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT {SelectColumns} FROM Milestones WHERE TaskId = @TaskId ORDER BY SequenceNumber";
        cmd.Parameters.AddWithValue("@TaskId", taskId);

        using var reader = await cmd.ExecuteReaderAsync();
        var results = new List<Milestone>();
        while (await reader.ReadAsync())
        {
            results.Add(MapMilestone(reader));
        }
        return results;
    }

    public async Task<IReadOnlyList<Milestone>> GetByStatusAsync(MilestoneStatus status)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT {SelectColumns} FROM Milestones WHERE Status = @Status";
        cmd.Parameters.AddWithValue("@Status", status.ToString());

        using var reader = await cmd.ExecuteReaderAsync();
        var results = new List<Milestone>();
        while (await reader.ReadAsync())
        {
            results.Add(MapMilestone(reader));
        }
        return results;
    }

    public async Task<int> CreateAsync(Milestone milestone, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"INSERT INTO Milestones (TaskId, SequenceNumber, Title, Description, VerificationCriteria, PayoutSats, Status, VerificationResult, InvoicePaymentHash, CreatedAt, VerifiedAt, PaidAt, OutputData)
            VALUES (@TaskId, @SequenceNumber, @Title, @Description, @VerificationCriteria, @PayoutSats, @Status, @VerificationResult, @InvoicePaymentHash, @CreatedAt, @VerifiedAt, @PaidAt, @OutputData);
            SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@TaskId", milestone.TaskId);
        cmd.Parameters.AddWithValue("@SequenceNumber", milestone.SequenceNumber);
        cmd.Parameters.AddWithValue("@Title", milestone.Title);
        cmd.Parameters.AddWithValue("@Description", (object?)milestone.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@VerificationCriteria", milestone.VerificationCriteria);
        cmd.Parameters.AddWithValue("@PayoutSats", milestone.PayoutSats);
        cmd.Parameters.AddWithValue("@Status", milestone.Status.ToString());
        cmd.Parameters.AddWithValue("@VerificationResult", (object?)milestone.VerificationResult ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@InvoicePaymentHash", (object?)milestone.InvoicePaymentHash ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@CreatedAt", milestone.CreatedAt.ToString("o"));
        cmd.Parameters.AddWithValue("@VerifiedAt", milestone.VerifiedAt.HasValue ? milestone.VerifiedAt.Value.ToString("o") : DBNull.Value);
        cmd.Parameters.AddWithValue("@PaidAt", milestone.PaidAt.HasValue ? milestone.PaidAt.Value.ToString("o") : DBNull.Value);
        cmd.Parameters.AddWithValue("@OutputData", (object?)milestone.OutputData ?? DBNull.Value);

        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    public async Task UpdateAsync(Milestone milestone, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"UPDATE Milestones SET TaskId = @TaskId, SequenceNumber = @SequenceNumber, Title = @Title,
            Description = @Description, VerificationCriteria = @VerificationCriteria, PayoutSats = @PayoutSats,
            Status = @Status, VerificationResult = @VerificationResult, InvoicePaymentHash = @InvoicePaymentHash,
            VerifiedAt = @VerifiedAt, PaidAt = @PaidAt, OutputData = @OutputData
            WHERE Id = @Id";
        cmd.Parameters.AddWithValue("@Id", milestone.Id);
        cmd.Parameters.AddWithValue("@TaskId", milestone.TaskId);
        cmd.Parameters.AddWithValue("@SequenceNumber", milestone.SequenceNumber);
        cmd.Parameters.AddWithValue("@Title", milestone.Title);
        cmd.Parameters.AddWithValue("@Description", (object?)milestone.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@VerificationCriteria", milestone.VerificationCriteria);
        cmd.Parameters.AddWithValue("@PayoutSats", milestone.PayoutSats);
        cmd.Parameters.AddWithValue("@Status", milestone.Status.ToString());
        cmd.Parameters.AddWithValue("@VerificationResult", (object?)milestone.VerificationResult ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@InvoicePaymentHash", (object?)milestone.InvoicePaymentHash ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@VerifiedAt", milestone.VerifiedAt.HasValue ? milestone.VerifiedAt.Value.ToString("o") : DBNull.Value);
        cmd.Parameters.AddWithValue("@PaidAt", milestone.PaidAt.HasValue ? milestone.PaidAt.Value.ToString("o") : DBNull.Value);
        cmd.Parameters.AddWithValue("@OutputData", (object?)milestone.OutputData ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task UpdateStatusAsync(int id, MilestoneStatus status, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE Milestones SET Status = @Status WHERE Id = @Id";
        cmd.Parameters.AddWithValue("@Id", id);
        cmd.Parameters.AddWithValue("@Status", status.ToString());

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyList<Milestone>> GetCompletedByAgentAsync(int agentId, int limit = 10, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $@"
            SELECT m.Id, m.TaskId, m.SequenceNumber, m.Title, m.Description, m.VerificationCriteria,
                   m.PayoutSats, m.Status, m.VerificationResult, m.InvoicePaymentHash, m.CreatedAt, m.VerifiedAt, m.PaidAt, m.OutputData
            FROM Milestones m
            INNER JOIN Tasks t ON m.TaskId = t.Id
            WHERE t.AssignedAgentId = @AgentId AND m.Status = @Status
            ORDER BY m.VerifiedAt DESC
            LIMIT @Limit";
        cmd.Parameters.AddWithValue("@AgentId", agentId);
        cmd.Parameters.AddWithValue("@Status", MilestoneStatus.Passed.ToString());
        cmd.Parameters.AddWithValue("@Limit", limit);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<Milestone>();
        while (await reader.ReadAsync(ct))
        {
            results.Add(MapMilestone(reader));
        }
        return results;
    }

    public async Task DeleteAsync(int id)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM Milestones WHERE Id = @Id";
        cmd.Parameters.AddWithValue("@Id", id);

        await cmd.ExecuteNonQueryAsync();
    }

    private static Milestone MapMilestone(SqliteDataReader reader)
    {
        return new Milestone
        {
            Id = reader.GetInt32(0),
            TaskId = reader.GetInt32(1),
            SequenceNumber = reader.GetInt32(2),
            Title = reader.GetString(3),
            Description = reader.IsDBNull(4) ? null : reader.GetString(4),
            VerificationCriteria = reader.GetString(5),
            PayoutSats = reader.GetInt64(6),
            Status = Enum.Parse<MilestoneStatus>(reader.GetString(7)),
            VerificationResult = reader.IsDBNull(8) ? null : reader.GetString(8),
            InvoicePaymentHash = reader.IsDBNull(9) ? null : reader.GetString(9),
            CreatedAt = DateTime.Parse(reader.GetString(10)),
            VerifiedAt = reader.IsDBNull(11) ? null : DateTime.Parse(reader.GetString(11)),
            PaidAt = reader.IsDBNull(12) ? null : DateTime.Parse(reader.GetString(12)),
            OutputData = reader.IsDBNull(13) ? null : reader.GetString(13)
        };
    }
}
