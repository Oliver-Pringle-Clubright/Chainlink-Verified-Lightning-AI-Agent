using System.Threading.Channels;
using LightningAgent.Core.Interfaces.Services;

namespace LightningAgent.Engine.Queue;

/// <summary>
/// In-process async task queue backed by <see cref="Channel{T}"/>.
/// Registered as a singleton so all producers and consumers share the same channel.
/// </summary>
public class TaskQueue : ITaskQueue
{
    private readonly Channel<int> _channel;

    public TaskQueue()
    {
        // Use an unbounded channel; back-pressure is managed upstream via
        // API rate limiting rather than at the queue level.
        _channel = Channel.CreateUnbounded<int>(new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = false
        });
    }

    /// <inheritdoc />
    public async ValueTask EnqueueAsync(int taskId, CancellationToken ct = default)
    {
        await _channel.Writer.WriteAsync(taskId, ct);
    }

    /// <inheritdoc />
    public async ValueTask<int> DequeueAsync(CancellationToken ct = default)
    {
        return await _channel.Reader.ReadAsync(ct);
    }
}
