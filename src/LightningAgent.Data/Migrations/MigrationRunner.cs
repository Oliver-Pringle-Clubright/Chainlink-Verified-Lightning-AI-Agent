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

            foreach (var statement in SplitStatements(migration.Sql))
            {
                try
                {
                    using var migrationCmd = connection.CreateCommand();
                    migrationCmd.CommandText = statement;
                    await migrationCmd.ExecuteNonQueryAsync();
                }
                catch (SqliteException ex) when (ex.SqliteErrorCode == 1 && ex.Message.Contains("duplicate column"))
                {
                    _logger.LogDebug("Column already exists, skipping: {Statement}", statement.Trim());
                }
            }

            using var recordCmd = connection.CreateCommand();
            recordCmd.CommandText = "INSERT INTO __Migrations (Version, Description, AppliedAt) VALUES (@Version, @Description, @AppliedAt)";
            recordCmd.Parameters.AddWithValue("@Version", migration.Version);
            recordCmd.Parameters.AddWithValue("@Description", migration.Description);
            recordCmd.Parameters.AddWithValue("@AppliedAt", DateTime.UtcNow.ToString("o"));
            await recordCmd.ExecuteNonQueryAsync();
        }
    }

    private static IEnumerable<string> SplitStatements(string sql)
    {
        return sql.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                  .Where(s => !string.IsNullOrWhiteSpace(s))
                  .Select(s => s + ";");
    }

    private static IEnumerable<(string Version, string Description, string Sql)> GetMigrations()
    {
        yield return ("1.0.0", "Initial schema", "SELECT 1;"); // Schema created by DatabaseInitializer

        yield return ("1.1.0", "Add webhook support and rate limit per agent", @"
            ALTER TABLE Agents ADD COLUMN WebhookUrl TEXT;
            ALTER TABLE Agents ADD COLUMN ApiKeyHash TEXT;
            ALTER TABLE Agents ADD COLUMN RateLimitPerMinute INTEGER NOT NULL DEFAULT 100;
        ");

        yield return ("1.2.0", "Add OutputData column to Milestones for storing agent output", @"
            ALTER TABLE Milestones ADD COLUMN OutputData TEXT;
        ");

        yield return ("1.3.0", "Enrich AuditLog with AgentId, Action, IpAddress, UserAgent columns", @"
            ALTER TABLE AuditLog ADD COLUMN AgentId INTEGER;
            ALTER TABLE AuditLog ADD COLUMN Action TEXT;
            ALTER TABLE AuditLog ADD COLUMN IpAddress TEXT;
            ALTER TABLE AuditLog ADD COLUMN UserAgent TEXT;
            CREATE INDEX IF NOT EXISTS IX_AuditLog_AgentId ON AuditLog(AgentId);
        ");

        yield return ("1.4.0", "Add composite indexes for common multi-column query patterns", @"
            CREATE INDEX IF NOT EXISTS IX_Tasks_Status_ClientId ON Tasks(Status, ClientId);
            CREATE INDEX IF NOT EXISTS IX_Tasks_Status_AssignedAgentId ON Tasks(Status, AssignedAgentId);
            CREATE INDEX IF NOT EXISTS IX_Payments_TaskId_Status ON Payments(TaskId, Status);
            CREATE INDEX IF NOT EXISTS IX_Payments_AgentId_Status ON Payments(AgentId, Status);
            CREATE INDEX IF NOT EXISTS IX_Escrows_MilestoneId_Status ON Escrows(MilestoneId, Status);
            CREATE INDEX IF NOT EXISTS IX_Verifications_TaskId_MilestoneId ON Verifications(TaskId, MilestoneId);
            CREATE INDEX IF NOT EXISTS IX_AuditLog_CreatedAt ON AuditLog(CreatedAt);
        ");

        yield return ("1.5.0", "Add WebhookDeliveryLog table for webhook retry and dead letter tracking", @"
            CREATE TABLE IF NOT EXISTS WebhookDeliveryLog (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                WebhookUrl TEXT NOT NULL,
                EventType TEXT NOT NULL,
                Payload TEXT NOT NULL,
                Attempts INTEGER NOT NULL DEFAULT 0,
                LastAttemptAt TEXT NOT NULL,
                Status TEXT NOT NULL DEFAULT 'Pending',
                ErrorMessage TEXT,
                CreatedAt TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_WebhookDeliveryLog_Status ON WebhookDeliveryLog(Status);
            CREATE INDEX IF NOT EXISTS IX_WebhookDeliveryLog_CreatedAt ON WebhookDeliveryLog(CreatedAt);
        ");

        yield return ("1.6.0", "Add IdempotencyKeys table for deduplicating mutating HTTP requests", @"
            CREATE TABLE IF NOT EXISTS IdempotencyKeys (
                Key TEXT NOT NULL UNIQUE,
                Method TEXT NOT NULL,
                Path TEXT NOT NULL,
                ResponseStatus INTEGER NOT NULL,
                ResponseBody TEXT NOT NULL,
                CreatedAt TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_IdempotencyKeys_CreatedAt ON IdempotencyKeys(CreatedAt);
        ");
    }
}
