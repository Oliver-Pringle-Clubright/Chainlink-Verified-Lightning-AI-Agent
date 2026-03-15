namespace LightningAgent.Core.Models;

public class RecurringTask
{
    public int Id { get; set; }
    public int TemplateTaskId { get; set; }
    public string CronExpression { get; set; } = ""; // e.g., "0 0 * * 1" for weekly Monday
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string TaskType { get; set; } = "Code";
    public long MaxPayoutSats { get; set; }
    public bool Active { get; set; } = true;
    public DateTime? LastRunAt { get; set; }
    public DateTime? NextRunAt { get; set; }
    public DateTime CreatedAt { get; set; }
}
