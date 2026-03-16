using LightningAgentMarketPlace.Core.Interfaces.Data;
using LightningAgentMarketPlace.Core.Models.Analytics;

namespace LightningAgentMarketPlace.Data.Repositories;

public class AnalyticsRepository : IAnalyticsRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public AnalyticsRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<SystemSummary> GetSystemSummaryAsync(CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var summary = new SystemSummary { GeneratedAt = DateTime.UtcNow };

        // Task counts by status
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT
                    COUNT(*) AS Total,
                    SUM(CASE WHEN Status = 'Pending' THEN 1 ELSE 0 END) AS Pending,
                    SUM(CASE WHEN Status IN ('Assigned','InProgress','Verifying') THEN 1 ELSE 0 END) AS InProgress,
                    SUM(CASE WHEN Status = 'Completed' THEN 1 ELSE 0 END) AS Completed,
                    SUM(CASE WHEN Status = 'Failed' THEN 1 ELSE 0 END) AS Failed,
                    SUM(CASE WHEN Status = 'Disputed' THEN 1 ELSE 0 END) AS Disputed
                FROM Tasks";

            using var reader = await cmd.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                summary.TotalTasks = reader.GetInt32(0);
                summary.PendingTasks = reader.GetInt32(1);
                summary.InProgressTasks = reader.GetInt32(2);
                summary.CompletedTasks = reader.GetInt32(3);
                summary.FailedTasks = reader.GetInt32(4);
                summary.DisputedTasks = reader.GetInt32(5);
            }
        }

        // Avg completion time for completed tasks
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT AVG(
                    (julianday(CompletedAt) - julianday(CreatedAt)) * 86400.0
                )
                FROM Tasks
                WHERE Status = 'Completed' AND CompletedAt IS NOT NULL";

            var result = await cmd.ExecuteScalarAsync(ct);
            summary.AvgCompletionTimeSec = result is DBNull || result is null ? 0.0 : Convert.ToDouble(result);
        }

        // Total sats and USD spent
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT
                    COALESCE(SUM(AmountSats), 0),
                    COALESCE(SUM(AmountUsd), 0.0)
                FROM Payments
                WHERE Status = 'Settled'";

            using var reader = await cmd.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                summary.TotalSatsSpent = reader.GetInt64(0);
                summary.TotalUsdSpent = reader.GetDouble(1);
            }
        }

        // Agent counts
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT
                    COUNT(*) AS Total,
                    SUM(CASE WHEN Status = 'Active' THEN 1 ELSE 0 END) AS Active
                FROM Agents";

            using var reader = await cmd.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                summary.TotalAgents = reader.GetInt32(0);
                summary.ActiveAgents = reader.GetInt32(1);
            }
        }

        // Held escrow amount
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT COALESCE(SUM(AmountSats), 0) FROM Escrows WHERE Status = 'Held'";
            var result = await cmd.ExecuteScalarAsync(ct);
            summary.HeldEscrowSats = result is DBNull || result is null ? 0 : Convert.ToInt64(result);
        }

        return summary;
    }

    public async Task<IReadOnlyList<AgentStats>> GetAgentStatsAsync(CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();

        cmd.CommandText = @"
            SELECT
                a.Id,
                a.Name,
                a.Status,
                COALESCE(r.CompletedTasks, 0) AS TasksCompleted,
                COALESCE(r.VerificationFails, 0) AS TasksFailed,
                COALESCE(r.TotalTasks, 0) AS TotalTasks,
                COALESCE(r.ReputationScore, 0.0) AS ReputationScore,
                COALESCE(p.TotalEarned, 0) AS TotalEarnedSats,
                CASE WHEN (COALESCE(r.VerificationPasses, 0) + COALESCE(r.VerificationFails, 0)) > 0
                    THEN CAST(COALESCE(r.VerificationPasses, 0) AS REAL) / (COALESCE(r.VerificationPasses, 0) + COALESCE(r.VerificationFails, 0))
                    ELSE 0.0
                END AS AvgScore
            FROM Agents a
            LEFT JOIN AgentReputation r ON r.AgentId = a.Id
            LEFT JOIN (
                SELECT AgentId, SUM(AmountSats) AS TotalEarned
                FROM Payments
                WHERE Status = 'Settled'
                GROUP BY AgentId
            ) p ON p.AgentId = a.Id
            ORDER BY COALESCE(r.ReputationScore, 0.0) DESC";

        using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<AgentStats>();

        while (await reader.ReadAsync(ct))
        {
            results.Add(new AgentStats
            {
                AgentId = reader.GetInt32(0),
                AgentName = reader.GetString(1),
                Status = reader.GetString(2),
                TasksCompleted = reader.GetInt32(3),
                TasksFailed = reader.GetInt32(4),
                TotalTasks = reader.GetInt32(5),
                ReputationScore = reader.GetDouble(6),
                TotalEarnedSats = reader.GetInt64(7),
                AvgVerificationScore = reader.GetDouble(8)
            });
        }

        return results;
    }

    public async Task<IReadOnlyList<TimelineEntry>> GetTimelineAsync(int days, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();

        // Get the start date
        var startDate = DateTime.UtcNow.AddDays(-days).Date;

        // Build a date series and join with task data
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            WITH RECURSIVE dates(d) AS (
                SELECT date(@StartDate)
                UNION ALL
                SELECT date(d, '+1 day') FROM dates WHERE d < date('now')
            )
            SELECT
                dates.d AS Date,
                COALESCE(created.cnt, 0) AS Created,
                COALESCE(completed.cnt, 0) AS Completed,
                COALESCE(failed.cnt, 0) AS Failed,
                COALESCE(spent.total, 0) AS SatsSpent
            FROM dates
            LEFT JOIN (
                SELECT date(CreatedAt) AS dt, COUNT(*) AS cnt
                FROM Tasks
                WHERE date(CreatedAt) >= date(@StartDate)
                GROUP BY date(CreatedAt)
            ) created ON created.dt = dates.d
            LEFT JOIN (
                SELECT date(CompletedAt) AS dt, COUNT(*) AS cnt
                FROM Tasks
                WHERE Status = 'Completed' AND CompletedAt IS NOT NULL AND date(CompletedAt) >= date(@StartDate)
                GROUP BY date(CompletedAt)
            ) completed ON completed.dt = dates.d
            LEFT JOIN (
                SELECT date(UpdatedAt) AS dt, COUNT(*) AS cnt
                FROM Tasks
                WHERE Status = 'Failed' AND date(UpdatedAt) >= date(@StartDate)
                GROUP BY date(UpdatedAt)
            ) failed ON failed.dt = dates.d
            LEFT JOIN (
                SELECT date(SettledAt) AS dt, SUM(AmountSats) AS total
                FROM Payments
                WHERE Status = 'Settled' AND SettledAt IS NOT NULL AND date(SettledAt) >= date(@StartDate)
                GROUP BY date(SettledAt)
            ) spent ON spent.dt = dates.d
            ORDER BY dates.d ASC";

        cmd.Parameters.AddWithValue("@StartDate", startDate.ToString("yyyy-MM-dd"));

        using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<TimelineEntry>();

        while (await reader.ReadAsync(ct))
        {
            results.Add(new TimelineEntry
            {
                Date = reader.GetString(0),
                Created = reader.GetInt32(1),
                Completed = reader.GetInt32(2),
                Failed = reader.GetInt32(3),
                SatsSpent = reader.GetInt64(4)
            });
        }

        return results;
    }
}
