using LightningAgentMarketPlace.Core.Enums;
using LightningAgentMarketPlace.Core.Interfaces.Data;
using LightningAgentMarketPlace.Core.Models;
using Microsoft.Data.Sqlite;

namespace LightningAgentMarketPlace.Data.Repositories;

public class VerificationRepository : IVerificationRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;

    private const string SelectColumns = "Id, MilestoneId, TaskId, StrategyType, ChainlinkRequestId, ChainlinkTxHash, InputHash, Score, Passed, Details, CreatedAt, CompletedAt";

    public VerificationRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Verification?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT {SelectColumns} FROM Verifications WHERE Id = @Id";
        cmd.Parameters.AddWithValue("@Id", id);

        using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? MapVerification(reader) : null;
    }

    public async Task<IReadOnlyList<Verification>> GetByMilestoneIdAsync(int milestoneId, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT {SelectColumns} FROM Verifications WHERE MilestoneId = @MilestoneId";
        cmd.Parameters.AddWithValue("@MilestoneId", milestoneId);

        using var reader = await cmd.ExecuteReaderAsync();
        var results = new List<Verification>();
        while (await reader.ReadAsync())
        {
            results.Add(MapVerification(reader));
        }
        return results;
    }

    public async Task<IReadOnlyList<Verification>> GetByTaskIdAsync(int taskId)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT {SelectColumns} FROM Verifications WHERE TaskId = @TaskId";
        cmd.Parameters.AddWithValue("@TaskId", taskId);

        using var reader = await cmd.ExecuteReaderAsync();
        var results = new List<Verification>();
        while (await reader.ReadAsync())
        {
            results.Add(MapVerification(reader));
        }
        return results;
    }

    public async Task<IReadOnlyList<Verification>> GetPendingChainlinkAsync(CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT {SelectColumns} FROM Verifications WHERE CompletedAt IS NULL AND ChainlinkRequestId IS NOT NULL";

        using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<Verification>();
        while (await reader.ReadAsync(ct))
        {
            results.Add(MapVerification(reader));
        }
        return results;
    }

    public async Task<int> GetCountByPassedAsync(bool passed, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM Verifications WHERE Passed = @Passed";
        cmd.Parameters.AddWithValue("@Passed", passed ? 1 : 0);

        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result);
    }

    public async Task<int> GetTotalCountAsync(CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM Verifications";

        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result);
    }

    public async Task<int> CreateAsync(Verification verification, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"INSERT INTO Verifications (MilestoneId, TaskId, StrategyType, ChainlinkRequestId, ChainlinkTxHash, InputHash, Score, Passed, Details, CreatedAt, CompletedAt)
            VALUES (@MilestoneId, @TaskId, @StrategyType, @ChainlinkRequestId, @ChainlinkTxHash, @InputHash, @Score, @Passed, @Details, @CreatedAt, @CompletedAt);
            SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@MilestoneId", verification.MilestoneId);
        cmd.Parameters.AddWithValue("@TaskId", verification.TaskId);
        cmd.Parameters.AddWithValue("@StrategyType", verification.StrategyType.ToString());
        cmd.Parameters.AddWithValue("@ChainlinkRequestId", (object?)verification.ChainlinkRequestId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ChainlinkTxHash", (object?)verification.ChainlinkTxHash ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@InputHash", (object?)verification.InputHash ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Score", (object?)verification.Score ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Passed", verification.Passed ? 1 : 0);
        cmd.Parameters.AddWithValue("@Details", (object?)verification.Details ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@CreatedAt", verification.CreatedAt.ToString("o"));
        cmd.Parameters.AddWithValue("@CompletedAt", verification.CompletedAt.HasValue ? verification.CompletedAt.Value.ToString("o") : DBNull.Value);

        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    public async Task UpdateAsync(Verification verification, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"UPDATE Verifications SET MilestoneId = @MilestoneId, TaskId = @TaskId,
            StrategyType = @StrategyType, ChainlinkRequestId = @ChainlinkRequestId,
            ChainlinkTxHash = @ChainlinkTxHash, InputHash = @InputHash, Score = @Score,
            Passed = @Passed, Details = @Details, CompletedAt = @CompletedAt
            WHERE Id = @Id";
        cmd.Parameters.AddWithValue("@Id", verification.Id);
        cmd.Parameters.AddWithValue("@MilestoneId", verification.MilestoneId);
        cmd.Parameters.AddWithValue("@TaskId", verification.TaskId);
        cmd.Parameters.AddWithValue("@StrategyType", verification.StrategyType.ToString());
        cmd.Parameters.AddWithValue("@ChainlinkRequestId", (object?)verification.ChainlinkRequestId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ChainlinkTxHash", (object?)verification.ChainlinkTxHash ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@InputHash", (object?)verification.InputHash ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Score", (object?)verification.Score ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Passed", verification.Passed ? 1 : 0);
        cmd.Parameters.AddWithValue("@Details", (object?)verification.Details ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@CompletedAt", verification.CompletedAt.HasValue ? verification.CompletedAt.Value.ToString("o") : DBNull.Value);

        await cmd.ExecuteNonQueryAsync();
    }

    private static Verification MapVerification(SqliteDataReader reader)
    {
        return new Verification
        {
            Id = reader.GetInt32(0),
            MilestoneId = reader.GetInt32(1),
            TaskId = reader.GetInt32(2),
            StrategyType = Enum.Parse<VerificationStrategyType>(reader.GetString(3)),
            ChainlinkRequestId = reader.IsDBNull(4) ? null : reader.GetString(4),
            ChainlinkTxHash = reader.IsDBNull(5) ? null : reader.GetString(5),
            InputHash = reader.IsDBNull(6) ? null : reader.GetString(6),
            Score = reader.IsDBNull(7) ? null : reader.GetDouble(7),
            Passed = reader.GetInt32(8) == 1,
            Details = reader.IsDBNull(9) ? null : reader.GetString(9),
            CreatedAt = DateTime.Parse(reader.GetString(10)),
            CompletedAt = reader.IsDBNull(11) ? null : DateTime.Parse(reader.GetString(11))
        };
    }
}
