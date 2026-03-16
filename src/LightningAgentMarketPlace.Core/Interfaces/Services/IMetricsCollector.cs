using LightningAgentMarketPlace.Core.Models.Metrics;

namespace LightningAgentMarketPlace.Core.Interfaces.Services;

public interface IMetricsCollector
{
    void IncrementTasksCreated();
    void IncrementTasksCompleted();
    void IncrementTasksFailed();
    void IncrementMilestonesVerified();
    void IncrementMilestonesFailed();
    void IncrementPaymentsProcessed(long amountSats);
    void IncrementApiRequestsTotal();
    void IncrementApiErrors5xx();

    void SetActiveTasks(int count);
    void SetActiveAgents(int count);
    void SetQueueDepth(int count);
    void SetBtcUsdPrice(double price);

    void RecordRequestDuration(double ms);
    void RecordVerificationDuration(double ms);

    AppMetrics GetSnapshot();
}
