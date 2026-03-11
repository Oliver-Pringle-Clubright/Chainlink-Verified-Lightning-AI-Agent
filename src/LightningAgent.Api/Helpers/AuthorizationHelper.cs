namespace LightningAgent.Api.Helpers;

public static class AuthorizationHelper
{
    public static bool IsAdmin(HttpContext context) =>
        context.Items.ContainsKey("IsAdmin") && (bool)context.Items["IsAdmin"]!;

    public static int? GetAuthenticatedAgentId(HttpContext context) =>
        context.Items.TryGetValue("AuthenticatedAgentId", out var id) ? (int?)id : null;

    /// <summary>
    /// Returns true if the request is in dev mode (no auth configured),
    /// or if the caller is admin, or if the authenticated agent matches the given agentId.
    /// </summary>
    public static bool CanAccessAgent(HttpContext context, int agentId)
    {
        // Dev mode: no auth items set at all means unauthenticated dev mode - allow access
        if (!context.Items.ContainsKey("IsAdmin") && !context.Items.ContainsKey("AuthenticatedAgentId"))
            return true;

        if (IsAdmin(context)) return true;

        var authId = GetAuthenticatedAgentId(context);
        return authId.HasValue && authId.Value == agentId;
    }

    /// <summary>
    /// Returns true if the request is in dev mode or the caller is admin.
    /// </summary>
    public static bool IsAdminOrDevMode(HttpContext context)
    {
        // Dev mode: no auth items set at all
        if (!context.Items.ContainsKey("IsAdmin") && !context.Items.ContainsKey("AuthenticatedAgentId"))
            return true;

        return IsAdmin(context);
    }
}
