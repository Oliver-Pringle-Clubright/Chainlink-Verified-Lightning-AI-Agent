using LightningAgent.Core.Enums;
using LightningAgent.Core.Interfaces.Data;
using LightningAgent.Core.Models;
using Microsoft.Data.Sqlite;

namespace LightningAgent.Data.Repositories;

public class DisputeRepository : IDisputeRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;

    private const string SelectColumns = "Id, TaskId, MilestoneId, InitiatedBy, InitiatorId, Reason, Status, Resolution, ArbiterAgentId, AmountDisputedSats, CreatedAt, ResolvedAt";

    public DisputeRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Dispute?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT {SelectColumns} FROM Disputes WHERE Id = @Id";
        cmd.Parameters.AddWithValue("@Id", id);

        using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? MapDispute(reader) : null;
    }

    public async Task<IReadOnlyList<Dispute>> GetByTaskIdAsync(int taskId, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT {SelectColumns} FROM Disputes WHERE TaskId = @TaskId";
        cmd.Parameters.AddWithValue("@TaskId", taskId);

        using var reader = await cmd.ExecuteReaderAsync();
        var results = new List<Dispute>();
        while (await reader.ReadAsync())
        {
            results.Add(MapDispute(reader));
        }
        return results;
    }

    public async Task<IReadOnlyList<Dispute>> GetOpenAsync(CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT {SelectColumns} FROM Disputes WHERE Status = 'Open'";

        using var reader = await cmd.ExecuteReaderAsync();
        var results = new List<Dispute>();
        while (await reader.ReadAsync())
        {
            results.Add(MapDispute(reader));
        }
        return results;
    }

    public async Task<IReadOnlyList<Dispute>> GetByStatusAsync(DisputeStatus status)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT {SelectColumns} FROM Disputes WHERE Status = @Status";
        cmd.Parameters.AddWithValue("@Status", status.ToString());

        using var reader = await cmd.ExecuteReaderAsync();
        var results = new List<Dispute>();
        while (await reader.ReadAsync())
        {
            results.Add(MapDispute(reader));
        }
        return results;
    }

    public async Task<int> CreateAsync(Dispute dispute, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"INSERT INTO Disputes (TaskId, MilestoneId, InitiatedBy, InitiatorId, Reason, Status, Resolution, ArbiterAgentId, AmountDisputedSats, CreatedAt, ResolvedAt)
            VALUES (@TaskId, @MilestoneId, @InitiatedBy, @InitiatorId, @Reason, @Status, @Resolution, @ArbiterAgentId, @AmountDisputedSats, @CreatedAt, @ResolvedAt);
            SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@TaskId", dispute.TaskId);
        cmd.Parameters.AddWithValue("@MilestoneId", (object?)dispute.MilestoneId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@InitiatedBy", dispute.InitiatedBy);
        cmd.Parameters.AddWithValue("@InitiatorId", dispute.InitiatorId);
        cmd.Parameters.AddWithValue("@Reason", dispute.Reason);
        cmd.Parameters.AddWithValue("@Status", dispute.Status.ToString());
        cmd.Parameters.AddWithValue("@Resolution", (object?)dispute.Resolution ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ArbiterAgentId", (object?)dispute.ArbiterAgentId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@AmountDisputedSats", dispute.AmountDisputedSats);
        cmd.Parameters.AddWithValue("@CreatedAt", dispute.CreatedAt.ToString("o"));
        cmd.Parameters.AddWithValue("@ResolvedAt", dispute.ResolvedAt.HasValue ? dispute.ResolvedAt.Value.ToString("o") : DBNull.Value);

        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    public async Task UpdateAsync(Dispute dispute, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"UPDATE Disputes SET TaskId = @TaskId, MilestoneId = @MilestoneId,
            InitiatedBy = @InitiatedBy, InitiatorId = @InitiatorId, Reason = @Reason,
            Status = @Status, Resolution = @Resolution, ArbiterAgentId = @ArbiterAgentId,
            AmountDisputedSats = @AmountDisputedSats, ResolvedAt = @ResolvedAt
            WHERE Id = @Id";
        cmd.Parameters.AddWithValue("@Id", dispute.Id);
        cmd.Parameters.AddWithValue("@TaskId", dispute.TaskId);
        cmd.Parameters.AddWithValue("@MilestoneId", (object?)dispute.MilestoneId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@InitiatedBy", dispute.InitiatedBy);
        cmd.Parameters.AddWithValue("@InitiatorId", dispute.InitiatorId);
        cmd.Parameters.AddWithValue("@Reason", dispute.Reason);
        cmd.Parameters.AddWithValue("@Status", dispute.Status.ToString());
        cmd.Parameters.AddWithValue("@Resolution", (object?)dispute.Resolution ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ArbiterAgentId", (object?)dispute.ArbiterAgentId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@AmountDisputedSats", dispute.AmountDisputedSats);
        cmd.Parameters.AddWithValue("@ResolvedAt", dispute.ResolvedAt.HasValue ? dispute.ResolvedAt.Value.ToString("o") : DBNull.Value);

        await cmd.ExecuteNonQueryAsync();
    }

    private static Dispute MapDispute(SqliteDataReader reader)
    {
        return new Dispute
        {
            Id = reader.GetInt32(0),
            TaskId = reader.GetInt32(1),
            MilestoneId = reader.IsDBNull(2) ? null : reader.GetInt32(2),
            InitiatedBy = reader.GetString(3),
            InitiatorId = reader.GetString(4),
            Reason = reader.GetString(5),
            Status = Enum.Parse<DisputeStatus>(reader.GetString(6)),
            Resolution = reader.IsDBNull(7) ? null : reader.GetString(7),
            ArbiterAgentId = reader.IsDBNull(8) ? null : reader.GetInt32(8),
            AmountDisputedSats = reader.GetInt64(9),
            CreatedAt = DateTime.Parse(reader.GetString(10)),
            ResolvedAt = reader.IsDBNull(11) ? null : DateTime.Parse(reader.GetString(11))
        };
    }
}
