using System.ComponentModel.DataAnnotations;
using LightningAgent.Api.Helpers;
using LightningAgent.Core.Configuration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace LightningAgent.Api.Controllers;

/// <summary>
/// Admin-only endpoints for rotating API keys and checking key validity.
/// </summary>
[ApiController]
[Route("api/secrets")]
[Produces("application/json")]
public class SecretsController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly IOptionsMonitor<ClaudeAiSettings> _claudeSettings;
    private readonly IOptionsMonitor<OpenRouterSettings> _openRouterSettings;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<SecretsController> _logger;

    public SecretsController(
        IConfiguration configuration,
        IOptionsMonitor<ClaudeAiSettings> claudeSettings,
        IOptionsMonitor<OpenRouterSettings> openRouterSettings,
        IHttpClientFactory httpClientFactory,
        ILogger<SecretsController> logger)
    {
        _configuration = configuration;
        _claudeSettings = claudeSettings;
        _openRouterSettings = openRouterSettings;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>
    /// Rotate the Claude API key. Admin only.
    /// </summary>
    [HttpPost("rotate/claude")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public IActionResult RotateClaudeKey([FromBody] RotateKeyRequest request)
    {
        if (!AuthorizationHelper.IsAdminOrDevMode(HttpContext))
            return Forbid();

        if (string.IsNullOrWhiteSpace(request.NewKey))
            return BadRequest("NewKey is required.");

        // Update the configuration in memory
        _configuration["ClaudeAi:ApiKey"] = request.NewKey;

        _logger.LogInformation("Claude API key has been rotated by admin");

        return Ok(new { message = "Claude API key rotated successfully. Note: for persistence, update your appsettings or user secrets." });
    }

    /// <summary>
    /// Rotate the OpenRouter API key. Admin only.
    /// </summary>
    [HttpPost("rotate/openrouter")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public IActionResult RotateOpenRouterKey([FromBody] RotateKeyRequest request)
    {
        if (!AuthorizationHelper.IsAdminOrDevMode(HttpContext))
            return Forbid();

        if (string.IsNullOrWhiteSpace(request.NewKey))
            return BadRequest("NewKey is required.");

        // Update the configuration in memory
        _configuration["OpenRouter:ApiKey"] = request.NewKey;

        _logger.LogInformation("OpenRouter API key has been rotated by admin");

        return Ok(new { message = "OpenRouter API key rotated successfully. Note: for persistence, update your appsettings or user secrets." });
    }

    /// <summary>
    /// Check validity of all configured API keys. Admin only.
    /// Never returns the actual key values.
    /// </summary>
    [HttpGet("status")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetKeyStatus(CancellationToken ct)
    {
        if (!AuthorizationHelper.IsAdminOrDevMode(HttpContext))
            return Forbid();

        var claudeStatus = await CheckClaudeKeyStatusAsync(ct);
        var openRouterStatus = await CheckOpenRouterKeyStatusAsync(ct);

        return Ok(new
        {
            claude = claudeStatus,
            openRouter = openRouterStatus
        });
    }

    private async Task<object> CheckClaudeKeyStatusAsync(CancellationToken ct)
    {
        var settings = _claudeSettings.CurrentValue;

        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            return new { configured = false, valid = (bool?)null, message = "No API key configured" };
        }

        try
        {
            using var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10);

            var request = new HttpRequestMessage(HttpMethod.Get, "https://api.anthropic.com/v1/models");
            request.Headers.Add("x-api-key", settings.ApiKey);
            request.Headers.Add("anthropic-version", "2023-06-01");

            var response = await client.SendAsync(request, ct);

            if (response.IsSuccessStatusCode)
            {
                return new { configured = true, valid = (bool?)true, message = "API key is valid" };
            }

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                return new { configured = true, valid = (bool?)false, message = "API key is invalid or expired" };
            }

            return new
            {
                configured = true,
                valid = (bool?)null,
                message = $"Unable to determine validity (HTTP {(int)response.StatusCode})"
            };
        }
        catch (Exception ex)
        {
            return new
            {
                configured = true,
                valid = (bool?)null,
                message = $"Connectivity error: {ex.Message}"
            };
        }
    }

    private async Task<object> CheckOpenRouterKeyStatusAsync(CancellationToken ct)
    {
        var settings = _openRouterSettings.CurrentValue;

        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            return new { configured = false, valid = (bool?)null, message = "No API key configured" };
        }

        try
        {
            using var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10);

            var request = new HttpRequestMessage(HttpMethod.Get, $"{settings.BaseUrl}/models");
            request.Headers.Add("Authorization", $"Bearer {settings.ApiKey}");

            var response = await client.SendAsync(request, ct);

            if (response.IsSuccessStatusCode)
            {
                return new { configured = true, valid = (bool?)true, message = "API key is valid" };
            }

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                return new { configured = true, valid = (bool?)false, message = "API key is invalid or expired" };
            }

            return new
            {
                configured = true,
                valid = (bool?)null,
                message = $"Unable to determine validity (HTTP {(int)response.StatusCode})"
            };
        }
        catch (Exception ex)
        {
            return new
            {
                configured = true,
                valid = (bool?)null,
                message = $"Connectivity error: {ex.Message}"
            };
        }
    }
}

public class RotateKeyRequest
{
    [Required(AllowEmptyStrings = false)]
    [StringLength(500, MinimumLength = 1)]
    public string NewKey { get; set; } = "";
}
