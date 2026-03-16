namespace LightningAgentMarketPlace.Api.Helpers;

public static class AuthorizationHelper
{
    public static bool IsAdmin(HttpContext context) =>
        context.Items.ContainsKey("IsAdmin") && (bool)context.Items["IsAdmin"]!;

    public static int? GetAuthenticatedAgentId(HttpContext context) =>
        context.Items.TryGetValue("AuthenticatedAgentId", out var id) ? (int?)id : null;

    /// <summary>
    /// Returns true only if DevMode is explicitly enabled via configuration,
    /// or if the caller is admin, or if the authenticated agent matches the given agentId.
    /// </summary>
    public static bool CanAccessAgent(HttpContext context, int agentId)
    {
        if (IsDevMode(context)) return true;
        if (IsAdmin(context)) return true;

        var authId = GetAuthenticatedAgentId(context);
        return authId.HasValue && authId.Value == agentId;
    }

    /// <summary>
    /// Returns true only if DevMode is explicitly enabled or the caller is admin.
    /// </summary>
    public static bool IsAdminOrDevMode(HttpContext context)
    {
        if (IsDevMode(context)) return true;
        return IsAdmin(context);
    }

    /// <summary>
    /// Returns true only when ApiSecurity:DevMode=true is set AND no API key is configured.
    /// This must be an intentional opt-in, not an implicit fallback.
    /// </summary>
    private static bool IsDevMode(HttpContext context) =>
        context.Items.ContainsKey("DevMode") && (bool)context.Items["DevMode"]!;
}
