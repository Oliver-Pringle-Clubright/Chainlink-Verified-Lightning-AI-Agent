using LightningAgent.Core.Enums;
using LightningAgent.Core.Interfaces.Data;
using LightningAgent.Core.Models;
using Microsoft.Data.Sqlite;

namespace LightningAgent.Data.Repositories;

public class AgentRepository : IAgentRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public AgentRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Agent?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Id, ExternalId, Name, WalletPubkey, Status, DailySpendCapSats, WeeklySpendCapSats, CreatedAt, UpdatedAt FROM Agents WHERE Id = @Id";
        cmd.Parameters.AddWithValue("@Id", id);

        using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? MapAgent(reader) : null;
    }

    public async Task<Agent?> GetByExternalIdAsync(string externalId, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Id, ExternalId, Name, WalletPubkey, Status, DailySpendCapSats, WeeklySpendCapSats, CreatedAt, UpdatedAt FROM Agents WHERE ExternalId = @ExternalId";
        cmd.Parameters.AddWithValue("@ExternalId", externalId);

        using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? MapAgent(reader) : null;
    }

    public async Task<IReadOnlyList<Agent>> GetAllAsync(AgentStatus? status = null, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        if (status.HasValue)
        {
            cmd.CommandText = "SELECT Id, ExternalId, Name, WalletPubkey, Status, DailySpendCapSats, WeeklySpendCapSats, CreatedAt, UpdatedAt FROM Agents WHERE Status = @Status";
            cmd.Parameters.AddWithValue("@Status", status.Value.ToString());
        }
        else
        {
            cmd.CommandText = "SELECT Id, ExternalId, Name, WalletPubkey, Status, DailySpendCapSats, WeeklySpendCapSats, CreatedAt, UpdatedAt FROM Agents";
        }

        using var reader = await cmd.ExecuteReaderAsync();
        var results = new List<Agent>();
        while (await reader.ReadAsync())
        {
            results.Add(MapAgent(reader));
        }
        return results;
    }

    public async Task<IReadOnlyList<Agent>> GetByStatusAsync(AgentStatus status)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Id, ExternalId, Name, WalletPubkey, Status, DailySpendCapSats, WeeklySpendCapSats, CreatedAt, UpdatedAt FROM Agents WHERE Status = @Status";
        cmd.Parameters.AddWithValue("@Status", status.ToString());

        using var reader = await cmd.ExecuteReaderAsync();
        var results = new List<Agent>();
        while (await reader.ReadAsync())
        {
            results.Add(MapAgent(reader));
        }
        return results;
    }

    public async Task<int> CreateAsync(Agent agent, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"INSERT INTO Agents (ExternalId, Name, WalletPubkey, Status, DailySpendCapSats, WeeklySpendCapSats, CreatedAt, UpdatedAt)
            VALUES (@ExternalId, @Name, @WalletPubkey, @Status, @DailySpendCapSats, @WeeklySpendCapSats, @CreatedAt, @UpdatedAt);
            SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@ExternalId", agent.ExternalId);
        cmd.Parameters.AddWithValue("@Name", agent.Name);
        cmd.Parameters.AddWithValue("@WalletPubkey", (object?)agent.WalletPubkey ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Status", agent.Status.ToString());
        cmd.Parameters.AddWithValue("@DailySpendCapSats", agent.DailySpendCapSats);
        cmd.Parameters.AddWithValue("@WeeklySpendCapSats", agent.WeeklySpendCapSats);
        cmd.Parameters.AddWithValue("@CreatedAt", agent.CreatedAt.ToString("o"));
        cmd.Parameters.AddWithValue("@UpdatedAt", agent.UpdatedAt.ToString("o"));

        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    public async Task UpdateAsync(Agent agent, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"UPDATE Agents SET ExternalId = @ExternalId, Name = @Name, WalletPubkey = @WalletPubkey,
            Status = @Status, DailySpendCapSats = @DailySpendCapSats, WeeklySpendCapSats = @WeeklySpendCapSats,
            UpdatedAt = @UpdatedAt WHERE Id = @Id";
        cmd.Parameters.AddWithValue("@Id", agent.Id);
        cmd.Parameters.AddWithValue("@ExternalId", agent.ExternalId);
        cmd.Parameters.AddWithValue("@Name", agent.Name);
        cmd.Parameters.AddWithValue("@WalletPubkey", (object?)agent.WalletPubkey ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Status", agent.Status.ToString());
        cmd.Parameters.AddWithValue("@DailySpendCapSats", agent.DailySpendCapSats);
        cmd.Parameters.AddWithValue("@WeeklySpendCapSats", agent.WeeklySpendCapSats);
        cmd.Parameters.AddWithValue("@UpdatedAt", agent.UpdatedAt.ToString("o"));

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task UpdateStatusAsync(int id, AgentStatus status, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE Agents SET Status = @Status, UpdatedAt = @UpdatedAt WHERE Id = @Id";
        cmd.Parameters.AddWithValue("@Id", id);
        cmd.Parameters.AddWithValue("@Status", status.ToString());
        cmd.Parameters.AddWithValue("@UpdatedAt", DateTime.UtcNow.ToString("o"));

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DeleteAsync(int id)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM Agents WHERE Id = @Id";
        cmd.Parameters.AddWithValue("@Id", id);

        await cmd.ExecuteNonQueryAsync();
    }

    private static Agent MapAgent(SqliteDataReader reader)
    {
        return new Agent
        {
            Id = reader.GetInt32(0),
            ExternalId = reader.GetString(1),
            Name = reader.GetString(2),
            WalletPubkey = reader.IsDBNull(3) ? null : reader.GetString(3),
            Status = Enum.Parse<AgentStatus>(reader.GetString(4)),
            DailySpendCapSats = reader.GetInt64(5),
            WeeklySpendCapSats = reader.GetInt64(6),
            CreatedAt = DateTime.Parse(reader.GetString(7)),
            UpdatedAt = DateTime.Parse(reader.GetString(8))
        };
    }
}
