namespace LightningAgentMarketPlace.Api.Helpers;

public static class EnumValidator
{
    public static bool IsValidTaskType(string value)
        => Enum.TryParse<LightningAgentMarketPlace.Core.Enums.TaskType>(value, true, out _);

    public static bool IsValidSkillType(string value)
        => Enum.TryParse<LightningAgentMarketPlace.Core.Enums.SkillType>(value, true, out _);

    public static bool IsValidAgentStatus(string value)
        => Enum.TryParse<LightningAgentMarketPlace.Core.Enums.AgentStatus>(value, true, out _);
}
