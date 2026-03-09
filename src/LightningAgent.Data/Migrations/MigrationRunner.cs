namespace LightningAgent.Data.Migrations;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

public class MigrationRunner
{
    private readonly SqliteConnectionFactory _factory;
    private readonly ILogger<MigrationRunner> _logger;

    public MigrationRunner(SqliteConnectionFactory factory, ILogger<MigrationRunner> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    public async Task RunMigrationsAsync()
    {
        using var connection = _factory.CreateConnection();

        // Create migrations table if not exists
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS __Migrations (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Version TEXT NOT NULL UNIQUE,
                Description TEXT NOT NULL,
                AppliedAt TEXT NOT NULL
            );";
        await cmd.ExecuteNonQueryAsync();

        // Get applied migrations
        var applied = new HashSet<string>();
        using var readCmd = connection.CreateCommand();
        readCmd.CommandText = "SELECT Version FROM __Migrations";
        using var reader = await readCmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            applied.Add(reader.GetString(0));

        // Run pending migrations
        foreach (var migration in GetMigrations())
        {
            if (applied.Contains(migration.Version))
                continue;

            _logger.LogInformation("Applying migration {Version}: {Description}", migration.Version, migration.Description);

            using var migrationCmd = connection.CreateCommand();
            migrationCmd.CommandText = migration.Sql;
            await migrationCmd.ExecuteNonQueryAsync();

            using var recordCmd = connection.CreateCommand();
            recordCmd.CommandText = "INSERT INTO __Migrations (Version, Description, AppliedAt) VALUES (@Version, @Description, @AppliedAt)";
            recordCmd.Parameters.AddWithValue("@Version", migration.Version);
            recordCmd.Parameters.AddWithValue("@Description", migration.Description);
            recordCmd.Parameters.AddWithValue("@AppliedAt", DateTime.UtcNow.ToString("o"));
            await recordCmd.ExecuteNonQueryAsync();
        }
    }

    private static IEnumerable<(string Version, string Description, string Sql)> GetMigrations()
    {
        yield return ("1.0.0", "Initial schema", "SELECT 1;"); // Schema created by DatabaseInitializer

        yield return ("1.1.0", "Add webhook support and rate limit per agent", @"
            ALTER TABLE Agents ADD COLUMN WebhookUrl TEXT;
            ALTER TABLE Agents ADD COLUMN ApiKeyHash TEXT;
            ALTER TABLE Agents ADD COLUMN RateLimitPerMinute INTEGER NOT NULL DEFAULT 100;
        ");
    }
}
