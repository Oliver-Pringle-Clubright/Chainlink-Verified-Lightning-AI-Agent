using LightningAgentMarketPlace.Core.Interfaces.Data;
using LightningAgentMarketPlace.Core.Models;
using Microsoft.Data.Sqlite;

namespace LightningAgentMarketPlace.Data.Repositories;

/// <summary>
/// SQLite-backed repository for idempotency key records.
/// </summary>
public class IdempotencyRepository : IIdempotencyRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public IdempotencyRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IdempotencyRecord?> GetAsync(string key, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Key, Method, Path, ResponseStatus, ResponseBody, CreatedAt FROM IdempotencyKeys WHERE Key = @Key";
        cmd.Parameters.AddWithValue("@Key", key);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            return new IdempotencyRecord
            {
                Key = reader.GetString(0),
                Method = reader.GetString(1),
                Path = reader.GetString(2),
                ResponseStatus = reader.GetInt32(3),
                ResponseBody = reader.GetString(4),
                CreatedAt = DateTime.Parse(reader.GetString(5))
            };
        }
        return null;
    }

    public async Task SaveAsync(string key, string method, string path, int status, string body, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"INSERT OR IGNORE INTO IdempotencyKeys (Key, Method, Path, ResponseStatus, ResponseBody, CreatedAt)
            VALUES (@Key, @Method, @Path, @ResponseStatus, @ResponseBody, @CreatedAt)";
        cmd.Parameters.AddWithValue("@Key", key);
        cmd.Parameters.AddWithValue("@Method", method);
        cmd.Parameters.AddWithValue("@Path", path);
        cmd.Parameters.AddWithValue("@ResponseStatus", status);
        cmd.Parameters.AddWithValue("@ResponseBody", body);
        cmd.Parameters.AddWithValue("@CreatedAt", DateTime.UtcNow.ToString("o"));

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<int> CleanupOlderThanAsync(DateTime cutoff, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM IdempotencyKeys WHERE CreatedAt < @Cutoff";
        cmd.Parameters.AddWithValue("@Cutoff", cutoff.ToString("o"));

        return await cmd.ExecuteNonQueryAsync(ct);
    }
}
