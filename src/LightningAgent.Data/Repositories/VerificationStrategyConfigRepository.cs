using LightningAgent.Core.Enums;
using LightningAgent.Core.Interfaces.Data;
using LightningAgent.Core.Models;
using Microsoft.Data.Sqlite;

namespace LightningAgent.Data.Repositories;

public class VerificationStrategyConfigRepository : IVerificationStrategyConfigRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;

    private const string SelectColumns = "Id, StrategyType, ParameterName, ParameterValue, LearnedWeight, UpdatedAt";

    public VerificationStrategyConfigRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<VerificationStrategyParam>> GetByStrategyTypeAsync(VerificationStrategyType strategyType, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT {SelectColumns} FROM VerificationStrategyConfig WHERE StrategyType = @StrategyType";
        cmd.Parameters.AddWithValue("@StrategyType", strategyType.ToString());

        using var reader = await cmd.ExecuteReaderAsync();
        var results = new List<VerificationStrategyParam>();
        while (await reader.ReadAsync())
        {
            results.Add(MapParam(reader));
        }
        return results;
    }

    public async Task<VerificationStrategyConfig?> GetByTypeAndParameterAsync(VerificationStrategyType strategyType, string parameterName)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT {SelectColumns} FROM VerificationStrategyConfig WHERE StrategyType = @StrategyType AND ParameterName = @ParameterName";
        cmd.Parameters.AddWithValue("@StrategyType", strategyType.ToString());
        cmd.Parameters.AddWithValue("@ParameterName", parameterName);

        using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? MapConfig(reader) : null;
    }

    public async Task<int> CreateAsync(VerificationStrategyConfig config)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"INSERT INTO VerificationStrategyConfig (StrategyType, ParameterName, ParameterValue, LearnedWeight, UpdatedAt)
            VALUES (@StrategyType, @ParameterName, @ParameterValue, @LearnedWeight, @UpdatedAt);
            SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@StrategyType", config.StrategyType.ToString());
        cmd.Parameters.AddWithValue("@ParameterName", config.ParameterName);
        cmd.Parameters.AddWithValue("@ParameterValue", config.ParameterValue);
        cmd.Parameters.AddWithValue("@LearnedWeight", config.LearnedWeight);
        cmd.Parameters.AddWithValue("@UpdatedAt", config.UpdatedAt.ToString("o"));

        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    public async Task UpdateAsync(VerificationStrategyConfig config)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"UPDATE VerificationStrategyConfig SET StrategyType = @StrategyType,
            ParameterName = @ParameterName, ParameterValue = @ParameterValue,
            LearnedWeight = @LearnedWeight, UpdatedAt = @UpdatedAt
            WHERE Id = @Id";
        cmd.Parameters.AddWithValue("@Id", config.Id);
        cmd.Parameters.AddWithValue("@StrategyType", config.StrategyType.ToString());
        cmd.Parameters.AddWithValue("@ParameterName", config.ParameterName);
        cmd.Parameters.AddWithValue("@ParameterValue", config.ParameterValue);
        cmd.Parameters.AddWithValue("@LearnedWeight", config.LearnedWeight);
        cmd.Parameters.AddWithValue("@UpdatedAt", config.UpdatedAt.ToString("o"));

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task UpsertAsync(VerificationStrategyParam param, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"INSERT INTO VerificationStrategyConfig (StrategyType, ParameterName, ParameterValue, LearnedWeight, UpdatedAt)
            VALUES (@StrategyType, @ParameterName, @ParameterValue, @LearnedWeight, @UpdatedAt)
            ON CONFLICT(StrategyType, ParameterName) DO UPDATE SET
                ParameterValue = excluded.ParameterValue,
                LearnedWeight = excluded.LearnedWeight,
                UpdatedAt = excluded.UpdatedAt";
        cmd.Parameters.AddWithValue("@StrategyType", param.StrategyType.ToString());
        cmd.Parameters.AddWithValue("@ParameterName", param.ParameterName);
        cmd.Parameters.AddWithValue("@ParameterValue", param.ParameterValue);
        cmd.Parameters.AddWithValue("@LearnedWeight", param.LearnedWeight);
        cmd.Parameters.AddWithValue("@UpdatedAt", param.UpdatedAt.ToString("o"));

        await cmd.ExecuteNonQueryAsync();
    }

    private static VerificationStrategyParam MapParam(SqliteDataReader reader)
    {
        return new VerificationStrategyParam
        {
            Id = reader.GetInt32(0),
            StrategyType = Enum.Parse<VerificationStrategyType>(reader.GetString(1)),
            ParameterName = reader.GetString(2),
            ParameterValue = reader.GetString(3),
            LearnedWeight = reader.IsDBNull(4) ? 1.0 : reader.GetDouble(4),
            UpdatedAt = DateTime.Parse(reader.GetString(5))
        };
    }

    private static VerificationStrategyConfig MapConfig(SqliteDataReader reader)
    {
        return new VerificationStrategyConfig
        {
            Id = reader.GetInt32(0),
            StrategyType = Enum.Parse<VerificationStrategyType>(reader.GetString(1)),
            ParameterName = reader.GetString(2),
            ParameterValue = reader.GetString(3),
            LearnedWeight = reader.IsDBNull(4) ? 1.0 : reader.GetDouble(4),
            UpdatedAt = DateTime.Parse(reader.GetString(5))
        };
    }
}
