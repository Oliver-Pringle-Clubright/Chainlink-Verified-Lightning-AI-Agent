using Microsoft.Data.Sqlite;

namespace LightningAgent.Data;

public class DatabaseInitializer
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public DatabaseInitializer(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task InitializeAsync()
    {
        using var connection = _connectionFactory.CreateConnection();

        var statements = new[]
        {
            // Agents
            @"CREATE TABLE IF NOT EXISTS Agents (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ExternalId TEXT NOT NULL UNIQUE,
                Name TEXT NOT NULL,
                WalletPubkey TEXT,
                Status TEXT NOT NULL DEFAULT 'Active',
                DailySpendCapSats INTEGER NOT NULL DEFAULT 0,
                WeeklySpendCapSats INTEGER NOT NULL DEFAULT 0,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            )",

            // AgentCapabilities
            @"CREATE TABLE IF NOT EXISTS AgentCapabilities (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                AgentId INTEGER NOT NULL REFERENCES Agents(Id),
                SkillType TEXT NOT NULL,
                TaskTypes TEXT NOT NULL,
                MaxConcurrency INTEGER NOT NULL DEFAULT 1,
                PriceSatsPerUnit INTEGER NOT NULL,
                AvgResponseSec INTEGER,
                CreatedAt TEXT NOT NULL
            )",
            @"CREATE INDEX IF NOT EXISTS IX_AgentCapabilities_SkillType ON AgentCapabilities(SkillType)",

            // AgentReputation
            @"CREATE TABLE IF NOT EXISTS AgentReputation (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                AgentId INTEGER NOT NULL UNIQUE REFERENCES Agents(Id),
                TotalTasks INTEGER NOT NULL DEFAULT 0,
                CompletedTasks INTEGER NOT NULL DEFAULT 0,
                VerificationPasses INTEGER NOT NULL DEFAULT 0,
                VerificationFails INTEGER NOT NULL DEFAULT 0,
                DisputeCount INTEGER NOT NULL DEFAULT 0,
                AvgResponseTimeSec REAL NOT NULL DEFAULT 0,
                ReputationScore REAL NOT NULL DEFAULT 0.5,
                LastUpdated TEXT NOT NULL
            )",

            // Tasks
            @"CREATE TABLE IF NOT EXISTS Tasks (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ExternalId TEXT NOT NULL UNIQUE,
                ParentTaskId INTEGER REFERENCES Tasks(Id),
                ClientId TEXT NOT NULL,
                Title TEXT NOT NULL,
                Description TEXT NOT NULL,
                TaskType TEXT NOT NULL,
                Status TEXT NOT NULL DEFAULT 'Pending',
                AcpSpec TEXT,
                VerificationCriteria TEXT,
                MaxPayoutSats INTEGER NOT NULL,
                ActualPayoutSats INTEGER DEFAULT 0,
                PriceUsd REAL,
                AssignedAgentId INTEGER REFERENCES Agents(Id),
                Priority INTEGER NOT NULL DEFAULT 0,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL,
                CompletedAt TEXT
            )",
            @"CREATE INDEX IF NOT EXISTS IX_Tasks_Status ON Tasks(Status)",
            @"CREATE INDEX IF NOT EXISTS IX_Tasks_AssignedAgentId ON Tasks(AssignedAgentId)",
            @"CREATE INDEX IF NOT EXISTS IX_Tasks_ParentTaskId ON Tasks(ParentTaskId)",

            // Milestones
            @"CREATE TABLE IF NOT EXISTS Milestones (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                TaskId INTEGER NOT NULL REFERENCES Tasks(Id),
                SequenceNumber INTEGER NOT NULL,
                Title TEXT NOT NULL,
                Description TEXT,
                VerificationCriteria TEXT NOT NULL,
                PayoutSats INTEGER NOT NULL,
                Status TEXT NOT NULL DEFAULT 'Pending',
                VerificationResult TEXT,
                InvoicePaymentHash TEXT,
                CreatedAt TEXT NOT NULL,
                VerifiedAt TEXT,
                PaidAt TEXT
            )",
            @"CREATE INDEX IF NOT EXISTS IX_Milestones_TaskId ON Milestones(TaskId)",

            // Escrows
            @"CREATE TABLE IF NOT EXISTS Escrows (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                MilestoneId INTEGER NOT NULL REFERENCES Milestones(Id),
                TaskId INTEGER NOT NULL REFERENCES Tasks(Id),
                AmountSats INTEGER NOT NULL,
                PaymentHash TEXT NOT NULL UNIQUE,
                PaymentPreimage TEXT,
                Status TEXT NOT NULL DEFAULT 'Held',
                HodlInvoice TEXT NOT NULL,
                CreatedAt TEXT NOT NULL,
                SettledAt TEXT,
                ExpiresAt TEXT NOT NULL
            )",
            @"CREATE INDEX IF NOT EXISTS IX_Escrows_PaymentHash ON Escrows(PaymentHash)",
            @"CREATE INDEX IF NOT EXISTS IX_Escrows_Status ON Escrows(Status)",

            // Payments
            @"CREATE TABLE IF NOT EXISTS Payments (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                EscrowId INTEGER REFERENCES Escrows(Id),
                TaskId INTEGER NOT NULL REFERENCES Tasks(Id),
                MilestoneId INTEGER REFERENCES Milestones(Id),
                AgentId INTEGER NOT NULL REFERENCES Agents(Id),
                AmountSats INTEGER NOT NULL,
                AmountUsd REAL,
                PaymentHash TEXT,
                PaymentType TEXT NOT NULL,
                Status TEXT NOT NULL DEFAULT 'Pending',
                CreatedAt TEXT NOT NULL,
                SettledAt TEXT
            )",

            // Verifications
            @"CREATE TABLE IF NOT EXISTS Verifications (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                MilestoneId INTEGER NOT NULL REFERENCES Milestones(Id),
                TaskId INTEGER NOT NULL REFERENCES Tasks(Id),
                StrategyType TEXT NOT NULL,
                ChainlinkRequestId TEXT,
                ChainlinkTxHash TEXT,
                InputHash TEXT,
                Score REAL,
                Passed INTEGER NOT NULL DEFAULT 0,
                Details TEXT,
                CreatedAt TEXT NOT NULL,
                CompletedAt TEXT
            )",
            @"CREATE INDEX IF NOT EXISTS IX_Verifications_MilestoneId ON Verifications(MilestoneId)",

            // Disputes
            @"CREATE TABLE IF NOT EXISTS Disputes (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                TaskId INTEGER NOT NULL REFERENCES Tasks(Id),
                MilestoneId INTEGER REFERENCES Milestones(Id),
                InitiatedBy TEXT NOT NULL,
                InitiatorId TEXT NOT NULL,
                Reason TEXT NOT NULL,
                Status TEXT NOT NULL DEFAULT 'Open',
                Resolution TEXT,
                ArbiterAgentId INTEGER REFERENCES Agents(Id),
                AmountDisputedSats INTEGER NOT NULL,
                CreatedAt TEXT NOT NULL,
                ResolvedAt TEXT
            )",

            // PriceCache
            @"CREATE TABLE IF NOT EXISTS PriceCache (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Pair TEXT NOT NULL,
                PriceUsd REAL NOT NULL,
                Source TEXT NOT NULL DEFAULT 'ChainlinkPriceFeed',
                FetchedAt TEXT NOT NULL
            )",
            @"CREATE INDEX IF NOT EXISTS IX_PriceCache_Pair_FetchedAt ON PriceCache(Pair, FetchedAt)",

            // AuditLog
            @"CREATE TABLE IF NOT EXISTS AuditLog (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                EventType TEXT NOT NULL,
                EntityType TEXT NOT NULL,
                EntityId INTEGER NOT NULL,
                Details TEXT,
                CreatedAt TEXT NOT NULL
            )",
            @"CREATE INDEX IF NOT EXISTS IX_AuditLog_EventType ON AuditLog(EventType)",
            @"CREATE INDEX IF NOT EXISTS IX_AuditLog_EntityType_EntityId ON AuditLog(EntityType, EntityId)",

            // SpendLimits
            @"CREATE TABLE IF NOT EXISTS SpendLimits (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                AgentId INTEGER REFERENCES Agents(Id),
                TaskId INTEGER REFERENCES Tasks(Id),
                LimitType TEXT NOT NULL,
                MaxSats INTEGER NOT NULL,
                CurrentSpentSats INTEGER NOT NULL DEFAULT 0,
                PeriodStart TEXT NOT NULL,
                PeriodEnd TEXT NOT NULL
            )",

            // VerificationStrategyConfig
            @"CREATE TABLE IF NOT EXISTS VerificationStrategyConfig (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                StrategyType TEXT NOT NULL,
                ParameterName TEXT NOT NULL,
                ParameterValue TEXT NOT NULL,
                LearnedWeight REAL DEFAULT 1.0,
                UpdatedAt TEXT NOT NULL
            )",
            @"CREATE UNIQUE INDEX IF NOT EXISTS IX_VerStrat_Type_Param ON VerificationStrategyConfig(StrategyType, ParameterName)"
        };

        foreach (var sql in statements)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            await cmd.ExecuteNonQueryAsync();
        }
    }
}
