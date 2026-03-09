using LightningAgent.Core.Interfaces.Data;
using LightningAgent.Core.Models;
using Microsoft.Data.Sqlite;

namespace LightningAgent.Data.Repositories;

public class AgentReputationRepository : IAgentReputationRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public AgentReputationRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<AgentReputation?> GetByAgentIdAsync(int agentId, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Id, AgentId, TotalTasks, CompletedTasks, VerificationPasses, VerificationFails, DisputeCount, AvgResponseTimeSec, ReputationScore, LastUpdated FROM AgentReputation WHERE AgentId = @AgentId";
        cmd.Parameters.AddWithValue("@AgentId", agentId);

        using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? MapReputation(reader) : null;
    }

    public async Task<int> CreateAsync(AgentReputation reputation, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"INSERT INTO AgentReputation (AgentId, TotalTasks, CompletedTasks, VerificationPasses, VerificationFails, DisputeCount, AvgResponseTimeSec, ReputationScore, LastUpdated)
            VALUES (@AgentId, @TotalTasks, @CompletedTasks, @VerificationPasses, @VerificationFails, @DisputeCount, @AvgResponseTimeSec, @ReputationScore, @LastUpdated);
            SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@AgentId", reputation.AgentId);
        cmd.Parameters.AddWithValue("@TotalTasks", reputation.TotalTasks);
        cmd.Parameters.AddWithValue("@CompletedTasks", reputation.CompletedTasks);
        cmd.Parameters.AddWithValue("@VerificationPasses", reputation.VerificationPasses);
        cmd.Parameters.AddWithValue("@VerificationFails", reputation.VerificationFails);
        cmd.Parameters.AddWithValue("@DisputeCount", reputation.DisputeCount);
        cmd.Parameters.AddWithValue("@AvgResponseTimeSec", reputation.AvgResponseTimeSec);
        cmd.Parameters.AddWithValue("@ReputationScore", reputation.ReputationScore);
        cmd.Parameters.AddWithValue("@LastUpdated", reputation.LastUpdated.ToString("o"));

        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    public async Task UpdateAsync(AgentReputation reputation, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"UPDATE AgentReputation SET TotalTasks = @TotalTasks, CompletedTasks = @CompletedTasks,
            VerificationPasses = @VerificationPasses, VerificationFails = @VerificationFails,
            DisputeCount = @DisputeCount, AvgResponseTimeSec = @AvgResponseTimeSec,
            ReputationScore = @ReputationScore, LastUpdated = @LastUpdated
            WHERE Id = @Id";
        cmd.Parameters.AddWithValue("@Id", reputation.Id);
        cmd.Parameters.AddWithValue("@TotalTasks", reputation.TotalTasks);
        cmd.Parameters.AddWithValue("@CompletedTasks", reputation.CompletedTasks);
        cmd.Parameters.AddWithValue("@VerificationPasses", reputation.VerificationPasses);
        cmd.Parameters.AddWithValue("@VerificationFails", reputation.VerificationFails);
        cmd.Parameters.AddWithValue("@DisputeCount", reputation.DisputeCount);
        cmd.Parameters.AddWithValue("@AvgResponseTimeSec", reputation.AvgResponseTimeSec);
        cmd.Parameters.AddWithValue("@ReputationScore", reputation.ReputationScore);
        cmd.Parameters.AddWithValue("@LastUpdated", reputation.LastUpdated.ToString("o"));

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyList<AgentReputation>> GetTopByScoreAsync(int count)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Id, AgentId, TotalTasks, CompletedTasks, VerificationPasses, VerificationFails, DisputeCount, AvgResponseTimeSec, ReputationScore, LastUpdated FROM AgentReputation ORDER BY ReputationScore DESC LIMIT @Count";
        cmd.Parameters.AddWithValue("@Count", count);

        using var reader = await cmd.ExecuteReaderAsync();
        var results = new List<AgentReputation>();
        while (await reader.ReadAsync())
        {
            results.Add(MapReputation(reader));
        }
        return results;
    }

    private static AgentReputation MapReputation(SqliteDataReader reader)
    {
        return new AgentReputation
        {
            Id = reader.GetInt32(0),
            AgentId = reader.GetInt32(1),
            TotalTasks = reader.GetInt32(2),
            CompletedTasks = reader.GetInt32(3),
            VerificationPasses = reader.GetInt32(4),
            VerificationFails = reader.GetInt32(5),
            DisputeCount = reader.GetInt32(6),
            AvgResponseTimeSec = reader.GetDouble(7),
            ReputationScore = reader.GetDouble(8),
            LastUpdated = DateTime.Parse(reader.GetString(9))
        };
    }
}
