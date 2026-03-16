using LightningAgentMarketPlace.Core.Interfaces.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace LightningAgentMarketPlace.Api.Hubs;

[Authorize(Policy = "ApiKeyAuthenticated")]
public class AgentNotificationHub : Hub
{
    private readonly ITaskRepository _taskRepository;
    private readonly IAgentRepository _agentRepository;
    private readonly IEscrowRepository _escrowRepository;

    public AgentNotificationHub(
        ITaskRepository taskRepository,
        IAgentRepository agentRepository,
        IEscrowRepository escrowRepository)
    {
        _taskRepository = taskRepository;
        _agentRepository = agentRepository;
        _escrowRepository = escrowRepository;
    }

    public async Task JoinAgentGroup(string agentId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"agent-{agentId}");
    }

    public async Task LeaveAgentGroup(string agentId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"agent-{agentId}");
    }

    /// <summary>
    /// Subscribe to real-time events for a specific task.
    /// </summary>
    public async Task SubscribeToTask(int taskId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"task-{taskId}");
        await Clients.Caller.SendAsync("Subscribed", new { group = $"task-{taskId}" });
    }

    /// <summary>
    /// Unsubscribe from task-specific events.
    /// </summary>
    public async Task UnsubscribeFromTask(int taskId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"task-{taskId}");
        await Clients.Caller.SendAsync("Unsubscribed", new { group = $"task-{taskId}" });
    }

    /// <summary>
    /// Subscribe to real-time events for a specific agent.
    /// </summary>
    public async Task SubscribeToAgent(string agentId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"agent-{agentId}");
        await Clients.Caller.SendAsync("Subscribed", new { group = $"agent-{agentId}" });
    }

    /// <summary>
    /// Returns a snapshot of current system status.
    /// </summary>
    public async Task<object> GetLiveStatus()
    {
        var totalTasks = await _taskRepository.GetCountAsync();
        var pendingTasks = await _taskRepository.GetCountAsync(Core.Enums.TaskStatus.Pending);
        var inProgressTasks = await _taskRepository.GetCountAsync(Core.Enums.TaskStatus.InProgress);
        var completedTasks = await _taskRepository.GetCountAsync(Core.Enums.TaskStatus.Completed);
        var failedTasks = await _taskRepository.GetCountAsync(Core.Enums.TaskStatus.Failed);

        var totalAgents = await _agentRepository.GetCountAsync();
        var activeAgents = await _agentRepository.GetCountAsync(Core.Enums.AgentStatus.Active);

        var heldEscrows = await _escrowRepository.GetCountByStatusAsync(Core.Enums.EscrowStatus.Held);
        var heldAmountSats = await _escrowRepository.GetHeldAmountSatsAsync();

        return new
        {
            tasks = new
            {
                total = totalTasks,
                pending = pendingTasks,
                inProgress = inProgressTasks,
                completed = completedTasks,
                failed = failedTasks
            },
            agents = new
            {
                total = totalAgents,
                active = activeAgents
            },
            escrow = new
            {
                held = heldEscrows,
                heldAmountSats
            },
            timestamp = DateTime.UtcNow.ToString("o")
        };
    }
}
