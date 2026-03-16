namespace LightningAgentMarketPlace.Core.Interfaces.Services;

/// <summary>
/// In-process async task queue for background orchestration processing.
/// </summary>
public interface ITaskQueue
{
    /// <summary>
    /// Enqueues a task ID for background processing.
    /// </summary>
    ValueTask EnqueueAsync(int taskId, CancellationToken ct = default);

    /// <summary>
    /// Dequeues the next task ID. Blocks asynchronously until an item is available
    /// or the cancellation token is triggered.
    /// </summary>
    ValueTask<int> DequeueAsync(CancellationToken ct = default);
}
