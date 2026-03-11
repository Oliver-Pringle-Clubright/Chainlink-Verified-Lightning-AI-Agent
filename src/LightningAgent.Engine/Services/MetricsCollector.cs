using LightningAgent.Core.Interfaces.Services;
using LightningAgent.Core.Models.Metrics;

namespace LightningAgent.Engine.Services;

/// <summary>
/// Thread-safe singleton that tracks application metrics using atomic operations.
/// </summary>
public class MetricsCollector : IMetricsCollector
{
    // Counters
    private long _tasksCreated;
    private long _tasksCompleted;
    private long _tasksFailed;
    private long _milestonesVerified;
    private long _milestonesFailed;
    private long _paymentsProcessed;
    private long _totalSatsPaid;
    private long _apiRequestsTotal;
    private long _apiErrors5xx;

    // Gauges
    private int _activeTasks;
    private int _activeAgents;
    private int _queueDepth;
    private double _btcUsdPrice;

    // Rolling averages for histograms
    private long _requestDurationCount;
    private double _requestDurationTotal;
    private readonly object _requestDurationLock = new();

    private long _verificationDurationCount;
    private double _verificationDurationTotal;
    private readonly object _verificationDurationLock = new();

    public void IncrementTasksCreated() => Interlocked.Increment(ref _tasksCreated);
    public void IncrementTasksCompleted() => Interlocked.Increment(ref _tasksCompleted);
    public void IncrementTasksFailed() => Interlocked.Increment(ref _tasksFailed);
    public void IncrementMilestonesVerified() => Interlocked.Increment(ref _milestonesVerified);
    public void IncrementMilestonesFailed() => Interlocked.Increment(ref _milestonesFailed);

    public void IncrementPaymentsProcessed(long amountSats)
    {
        Interlocked.Increment(ref _paymentsProcessed);
        Interlocked.Add(ref _totalSatsPaid, amountSats);
    }

    public void IncrementApiRequestsTotal() => Interlocked.Increment(ref _apiRequestsTotal);
    public void IncrementApiErrors5xx() => Interlocked.Increment(ref _apiErrors5xx);

    public void SetActiveTasks(int count) => Interlocked.Exchange(ref _activeTasks, count);
    public void SetActiveAgents(int count) => Interlocked.Exchange(ref _activeAgents, count);
    public void SetQueueDepth(int count) => Interlocked.Exchange(ref _queueDepth, count);

    public void SetBtcUsdPrice(double price)
    {
        Interlocked.Exchange(ref _btcUsdPrice, price);
    }

    public void RecordRequestDuration(double ms)
    {
        lock (_requestDurationLock)
        {
            _requestDurationCount++;
            _requestDurationTotal += ms;
        }
    }

    public void RecordVerificationDuration(double ms)
    {
        lock (_verificationDurationLock)
        {
            _verificationDurationCount++;
            _verificationDurationTotal += ms;
        }
    }

    public AppMetrics GetSnapshot()
    {
        double avgRequestMs;
        lock (_requestDurationLock)
        {
            avgRequestMs = _requestDurationCount > 0
                ? _requestDurationTotal / _requestDurationCount
                : 0.0;
        }

        double avgVerificationMs;
        lock (_verificationDurationLock)
        {
            avgVerificationMs = _verificationDurationCount > 0
                ? _verificationDurationTotal / _verificationDurationCount
                : 0.0;
        }

        return new AppMetrics
        {
            TasksCreated = Interlocked.Read(ref _tasksCreated),
            TasksCompleted = Interlocked.Read(ref _tasksCompleted),
            TasksFailed = Interlocked.Read(ref _tasksFailed),
            MilestonesVerified = Interlocked.Read(ref _milestonesVerified),
            MilestonesFailed = Interlocked.Read(ref _milestonesFailed),
            PaymentsProcessed = Interlocked.Read(ref _paymentsProcessed),
            TotalSatsPaid = Interlocked.Read(ref _totalSatsPaid),
            ApiRequestsTotal = Interlocked.Read(ref _apiRequestsTotal),
            ApiErrors5xx = Interlocked.Read(ref _apiErrors5xx),
            ActiveTasks = Volatile.Read(ref _activeTasks),
            ActiveAgents = Volatile.Read(ref _activeAgents),
            QueueDepth = Volatile.Read(ref _queueDepth),
            BtcUsdPrice = Volatile.Read(ref _btcUsdPrice),
            AvgRequestDurationMs = avgRequestMs,
            AvgVerificationDurationMs = avgVerificationMs,
            CollectedAt = DateTime.UtcNow
        };
    }
}
