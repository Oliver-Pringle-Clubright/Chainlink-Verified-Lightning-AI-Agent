using LightningAgentMarketPlace.Core.Interfaces.Data;
using LightningAgentMarketPlace.Core.Models;
using Microsoft.Data.Sqlite;

namespace LightningAgentMarketPlace.Data.Repositories;

public class ArtifactRepository : IArtifactRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;

    private const string SelectColumns = "Id, TaskId, MilestoneId, AgentId, FileName, ContentType, SizeBytes, StoragePath, CreatedAt";

    public ArtifactRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<int> CreateAsync(Artifact artifact, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"INSERT INTO Artifacts (TaskId, MilestoneId, AgentId, FileName, ContentType, SizeBytes, StoragePath, CreatedAt)
            VALUES (@TaskId, @MilestoneId, @AgentId, @FileName, @ContentType, @SizeBytes, @StoragePath, @CreatedAt);
            SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@TaskId", artifact.TaskId);
        cmd.Parameters.AddWithValue("@MilestoneId", (object?)artifact.MilestoneId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@AgentId", (object?)artifact.AgentId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@FileName", artifact.FileName);
        cmd.Parameters.AddWithValue("@ContentType", artifact.ContentType);
        cmd.Parameters.AddWithValue("@SizeBytes", artifact.SizeBytes);
        cmd.Parameters.AddWithValue("@StoragePath", artifact.StoragePath);
        cmd.Parameters.AddWithValue("@CreatedAt", artifact.CreatedAt.ToString("o"));

        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result);
    }

    public async Task<Artifact?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT {SelectColumns} FROM Artifacts WHERE Id = @Id";
        cmd.Parameters.AddWithValue("@Id", id);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? MapArtifact(reader) : null;
    }

    public async Task<IReadOnlyList<Artifact>> GetByTaskIdAsync(int taskId, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT {SelectColumns} FROM Artifacts WHERE TaskId = @TaskId ORDER BY CreatedAt DESC";
        cmd.Parameters.AddWithValue("@TaskId", taskId);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<Artifact>();
        while (await reader.ReadAsync(ct))
        {
            results.Add(MapArtifact(reader));
        }
        return results;
    }

    private static Artifact MapArtifact(SqliteDataReader reader)
    {
        return new Artifact
        {
            Id = reader.GetInt32(0),
            TaskId = reader.GetInt32(1),
            MilestoneId = reader.IsDBNull(2) ? null : reader.GetInt32(2),
            AgentId = reader.IsDBNull(3) ? null : reader.GetInt32(3),
            FileName = reader.GetString(4),
            ContentType = reader.GetString(5),
            SizeBytes = reader.GetInt64(6),
            StoragePath = reader.GetString(7),
            CreatedAt = DateTime.Parse(reader.GetString(8))
        };
    }
}
