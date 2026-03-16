namespace LightningAgentMarketPlace.Core.Models;

public class WebhookSubscription
{
    public int Id { get; set; }
    public int? AgentId { get; set; }
    public string Url { get; set; } = "";
    public string Events { get; set; } = ""; // comma-separated: TaskAssigned,MilestoneVerified,PaymentSent
    public string? Secret { get; set; }
    public bool Active { get; set; } = true;
    public DateTime CreatedAt { get; set; }
}
