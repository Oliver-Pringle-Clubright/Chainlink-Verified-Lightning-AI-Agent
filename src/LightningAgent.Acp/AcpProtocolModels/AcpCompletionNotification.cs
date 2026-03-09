namespace LightningAgent.Acp.AcpProtocolModels;

/// <summary>
/// Wire format for notifying an ACP endpoint that a task has been completed.
/// </summary>
public class AcpCompletionNotification
{
    public string TaskId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Result { get; set; } = string.Empty;
    public DateTime CompletedAt { get; set; }
}
