using LightningAgent.Core.Interfaces.Data;
using LightningAgent.Core.Models;
using Microsoft.Data.Sqlite;

namespace LightningAgent.Data.Repositories;

public class PriceCacheRepository : IPriceCacheRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;

    private const string SelectColumns = "Id, Pair, PriceUsd, Source, FetchedAt";

    public PriceCacheRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<PriceQuote?> GetLatestAsync(string pair, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT {SelectColumns} FROM PriceCache WHERE Pair = @Pair ORDER BY FetchedAt DESC LIMIT 1";
        cmd.Parameters.AddWithValue("@Pair", pair);

        using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? MapPriceQuote(reader) : null;
    }

    public async Task<IReadOnlyList<PriceQuote>> GetHistoryAsync(string pair, DateTime from, DateTime to)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT {SelectColumns} FROM PriceCache WHERE Pair = @Pair AND FetchedAt >= @From AND FetchedAt <= @To ORDER BY FetchedAt DESC";
        cmd.Parameters.AddWithValue("@Pair", pair);
        cmd.Parameters.AddWithValue("@From", from.ToString("o"));
        cmd.Parameters.AddWithValue("@To", to.ToString("o"));

        using var reader = await cmd.ExecuteReaderAsync();
        var results = new List<PriceQuote>();
        while (await reader.ReadAsync())
        {
            results.Add(MapPriceQuote(reader));
        }
        return results;
    }

    public async Task<int> CreateAsync(PriceQuote priceQuote, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"INSERT INTO PriceCache (Pair, PriceUsd, Source, FetchedAt)
            VALUES (@Pair, @PriceUsd, @Source, @FetchedAt);
            SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@Pair", priceQuote.Pair);
        cmd.Parameters.AddWithValue("@PriceUsd", priceQuote.PriceUsd);
        cmd.Parameters.AddWithValue("@Source", priceQuote.Source);
        cmd.Parameters.AddWithValue("@FetchedAt", priceQuote.FetchedAt.ToString("o"));

        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    public async Task PruneOlderThanAsync(DateTime cutoff)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM PriceCache WHERE FetchedAt < @Cutoff";
        cmd.Parameters.AddWithValue("@Cutoff", cutoff.ToString("o"));

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<int> DeleteOlderThanAsync(DateTime cutoff, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM PriceCache WHERE FetchedAt < @Cutoff";
        cmd.Parameters.AddWithValue("@Cutoff", cutoff.ToString("o"));

        return await cmd.ExecuteNonQueryAsync(ct);
    }

    private static PriceQuote MapPriceQuote(SqliteDataReader reader)
    {
        return new PriceQuote
        {
            Id = reader.GetInt32(0),
            Pair = reader.GetString(1),
            PriceUsd = reader.GetDouble(2),
            Source = reader.GetString(3),
            FetchedAt = DateTime.Parse(reader.GetString(4))
        };
    }
}
