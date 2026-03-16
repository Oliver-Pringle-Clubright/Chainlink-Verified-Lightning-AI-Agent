namespace LightningAgentMarketPlace.Data.Migrations;

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

        yield return ("1.7.0", "Add CcipMessages table for cross-chain interoperability protocol tracking", @"
            CREATE TABLE IF NOT EXISTS CcipMessages (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                MessageId TEXT NOT NULL,
                SourceChainSelector INTEGER NOT NULL,
                DestinationChainSelector INTEGER NOT NULL,
                SenderAddress TEXT NOT NULL,
                ReceiverAddress TEXT NOT NULL,
                Payload TEXT NOT NULL,
                TokenAddress TEXT,
                TokenAmountWei INTEGER NOT NULL DEFAULT 0,
                FeeToken TEXT NOT NULL,
                Direction TEXT NOT NULL DEFAULT 'Outbound',
                Status TEXT NOT NULL DEFAULT 'Pending',
                TxHash TEXT,
                ErrorDetails TEXT,
                TaskId INTEGER,
                AgentId INTEGER,
                CreatedAt TEXT NOT NULL,
                DeliveredAt TEXT
            );
            CREATE INDEX IF NOT EXISTS IX_CcipMessages_MessageId ON CcipMessages(MessageId);
            CREATE INDEX IF NOT EXISTS IX_CcipMessages_Status_Direction ON CcipMessages(Status, Direction);
            CREATE INDEX IF NOT EXISTS IX_CcipMessages_TaskId ON CcipMessages(TaskId);
            CREATE INDEX IF NOT EXISTS IX_CcipMessages_AgentId ON CcipMessages(AgentId);
            CREATE INDEX IF NOT EXISTS IX_CcipMessages_CreatedAt ON CcipMessages(CreatedAt);
        ");

        yield return ("1.8.0", "Add SpendLimits and VerificationStrategyConfig tables for VRF, Automation, and adaptive verification", @"
            CREATE TABLE IF NOT EXISTS SpendLimits (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                AgentId INTEGER REFERENCES Agents(Id),
                TaskId INTEGER REFERENCES Tasks(Id),
                LimitType TEXT NOT NULL,
                MaxSats INTEGER NOT NULL,
                CurrentSpentSats INTEGER NOT NULL DEFAULT 0,
                PeriodStart TEXT NOT NULL,
                PeriodEnd TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS VerificationStrategyConfig (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                StrategyType TEXT NOT NULL,
                ParameterName TEXT NOT NULL,
                ParameterValue TEXT NOT NULL,
                LearnedWeight REAL DEFAULT 1.0,
                UpdatedAt TEXT NOT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS IX_VerStrat_Type_Param ON VerificationStrategyConfig(StrategyType, ParameterName);
        ");

        yield return ("2.0.0", "Add unique constraint on Escrows.MilestoneId for idempotency protection", @"
            DELETE FROM Escrows WHERE rowid NOT IN (
                SELECT MIN(rowid) FROM Escrows GROUP BY MilestoneId
            );
            CREATE UNIQUE INDEX IF NOT EXISTS IX_Escrows_MilestoneId_Unique ON Escrows(MilestoneId);
        ");

        yield return ("2.3.0", "Add multi-chain payment fields to Payments table", @"
            ALTER TABLE Payments ADD COLUMN PaymentMethod TEXT NOT NULL DEFAULT 'Lightning';
            ALTER TABLE Payments ADD COLUMN ChainId INTEGER;
            ALTER TABLE Payments ADD COLUMN TokenAddress TEXT;
            ALTER TABLE Payments ADD COLUMN TransactionHash TEXT;
            ALTER TABLE Payments ADD COLUMN SenderAddress TEXT;
            ALTER TABLE Payments ADD COLUMN ReceiverAddress TEXT;
            ALTER TABLE Payments ADD COLUMN AmountWei TEXT;
        ");

        yield return ("2.3.2", "Add RecurringTasks table and DependsOnTaskId column to Tasks", @"
            CREATE TABLE IF NOT EXISTS RecurringTasks (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                TemplateTaskId INTEGER,
                CronExpression TEXT NOT NULL DEFAULT 'daily',
                Title TEXT NOT NULL,
                Description TEXT NOT NULL DEFAULT '',
                TaskType TEXT NOT NULL DEFAULT 'Code',
                MaxPayoutSats INTEGER NOT NULL DEFAULT 0,
                Active INTEGER NOT NULL DEFAULT 1,
                LastRunAt TEXT,
                NextRunAt TEXT,
                CreatedAt TEXT NOT NULL
            );
            ALTER TABLE Tasks ADD COLUMN DependsOnTaskId INTEGER;
        ");

        yield return ("2.3.1", "Add Artifacts, WebhookSubscriptions, TaskTemplates tables and wallet/payment fields", @"
            CREATE TABLE IF NOT EXISTS Artifacts (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                TaskId INTEGER NOT NULL,
                MilestoneId INTEGER,
                AgentId INTEGER,
                FileName TEXT NOT NULL,
                ContentType TEXT NOT NULL DEFAULT 'application/octet-stream',
                SizeBytes INTEGER NOT NULL,
                StoragePath TEXT NOT NULL,
                CreatedAt TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS WebhookSubscriptions (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                AgentId INTEGER,
                Url TEXT NOT NULL,
                Events TEXT NOT NULL DEFAULT '',
                Secret TEXT,
                Active INTEGER NOT NULL DEFAULT 1,
                CreatedAt TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS TaskTemplates (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                Category TEXT NOT NULL DEFAULT '',
                Description TEXT NOT NULL,
                TaskType TEXT NOT NULL DEFAULT 'Code',
                VerificationCriteria TEXT NOT NULL DEFAULT '',
                SuggestedPayoutSats INTEGER NOT NULL DEFAULT 0,
                RequiredSkills TEXT NOT NULL DEFAULT '',
                CreatedAt TEXT NOT NULL
            );
            ALTER TABLE Agents ADD COLUMN EthAddress TEXT;
            ALTER TABLE Agents ADD COLUMN PreferredChainId INTEGER;
            ALTER TABLE Agents ADD COLUMN PreferredPaymentMethod TEXT;
            ALTER TABLE Tasks ADD COLUMN PreferredPaymentMethod TEXT;
            ALTER TABLE Tasks ADD COLUMN PaymentChainId INTEGER;
            ALTER TABLE Tasks ADD COLUMN ClientWalletAddress TEXT;
        ");

        yield return ("2.5.0", "Add IsEarlyAdopter and PlatformFees table", @"
            ALTER TABLE Agents ADD COLUMN IsEarlyAdopter INTEGER NOT NULL DEFAULT 0;
            CREATE TABLE IF NOT EXISTS PlatformFees (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                FeeType TEXT NOT NULL,
                TaskId INTEGER,
                MilestoneId INTEGER,
                PaymentId INTEGER,
                AmountSats INTEGER NOT NULL,
                CreatedAt TEXT NOT NULL
            );
        ");
    }
}
