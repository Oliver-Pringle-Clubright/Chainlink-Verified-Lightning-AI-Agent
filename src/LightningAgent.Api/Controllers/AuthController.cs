using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using LightningAgent.Api.Authentication;
using LightningAgent.Api.Helpers;
using LightningAgent.Core.Interfaces.Data;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;

namespace LightningAgent.Api.Controllers;

/// <summary>
/// Handles JWT token issuance and refresh for agent and admin authentication.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/auth")]
[Route("api/v{version:apiVersion}/auth")]
[Produces("application/json")]
public class AuthController : ControllerBase
{
    private readonly JwtTokenService _jwtTokenService;
    private readonly IAgentRepository _agentRepository;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        JwtTokenService jwtTokenService,
        IAgentRepository agentRepository,
        IConfiguration configuration,
        ILogger<AuthController> logger)
    {
        _jwtTokenService = jwtTokenService;
        _agentRepository = agentRepository;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Exchange an API key for a JWT token.
    /// </summary>
    [HttpPost("token")]
    [ProducesResponseType(typeof(TokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetToken([FromBody] TokenRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.ApiKey))
            return BadRequest("ApiKey is required.");

        var jwtSecret = _configuration["Jwt:Secret"];
        if (string.IsNullOrWhiteSpace(jwtSecret))
            return BadRequest("JWT authentication is not configured. Set Jwt:Secret in appsettings or user secrets.");

        var configuredKey = _configuration["ApiSecurity:ApiKey"];

        // Check if this is the global admin API key — supports comma-separated list
        if (!string.IsNullOrWhiteSpace(configuredKey))
        {
            var configuredKeys = configuredKey.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var providedBytes = Encoding.UTF8.GetBytes(request.ApiKey);
            bool isAdminKey = false;
            foreach (var key in configuredKeys)
            {
                if (CryptographicOperations.FixedTimeEquals(providedBytes, Encoding.UTF8.GetBytes(key)))
                {
                    isAdminKey = true;
                    break;
                }
            }

            if (isAdminKey)
            {
                // Create a synthetic admin agent for token generation
                var adminAgent = new LightningAgent.Core.Models.Agent
                {
                    Id = 0,
                    ExternalId = "admin",
                    Name = "Admin"
                };

                var adminToken = _jwtTokenService.GenerateToken(adminAgent, isAdmin: true);
                _logger.LogInformation("JWT token issued for admin user");

                return Ok(new TokenResponse
                {
                    Token = adminToken,
                    ExpiresIn = GetExpirySeconds(),
                    TokenType = "Bearer"
                });
            }
        }

        // Check for per-agent API key (salted hash requires checking each agent)
        var agent = await _agentRepository.GetByApiKeyAsync(request.ApiKey, ct);

        if (agent is null)
        {
            _logger.LogWarning("JWT token request with invalid API key");
            return Unauthorized("Invalid API key.");
        }

        var token = _jwtTokenService.GenerateToken(agent);
        _logger.LogInformation("JWT token issued for agent {AgentId}", agent.Id);

        return Ok(new TokenResponse
        {
            Token = token,
            ExpiresIn = GetExpirySeconds(),
            TokenType = "Bearer"
        });
    }

    /// <summary>
    /// Refresh a JWT token. The existing token must still be valid.
    /// </summary>
    [HttpPost("refresh")]
    [ProducesResponseType(typeof(TokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> RefreshToken([FromBody] RefreshRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Token))
            return BadRequest("Token is required.");

        var jwtSecret = _configuration["Jwt:Secret"];
        if (string.IsNullOrWhiteSpace(jwtSecret))
            return BadRequest("JWT authentication is not configured. Set Jwt:Secret in appsettings or user secrets.");

        var principal = _jwtTokenService.ValidateToken(request.Token);
        if (principal is null)
            return Unauthorized("Invalid or expired token.");

        var agentIdClaim = principal.FindFirst("agentId")?.Value;
        var isAdmin = principal.IsInRole("Admin");

        // Re-validate the agent still exists and is active
        if (int.TryParse(agentIdClaim, out var parsedId) && parsedId > 0)
        {
            var dbAgent = await _agentRepository.GetByIdAsync(parsedId, CancellationToken.None);
            if (dbAgent is null)
                return Unauthorized("Agent no longer exists.");
            if (dbAgent.Status != LightningAgent.Core.Enums.AgentStatus.Active)
                return Unauthorized("Agent is suspended or inactive.");

            // Use DB values, not token claims (prevents privilege persistence)
            var freshToken = _jwtTokenService.GenerateToken(dbAgent, isAdmin);
            _logger.LogInformation("JWT token refreshed for agent {AgentId}", dbAgent.Id);
            return Ok(new TokenResponse { Token = freshToken, ExpiresIn = GetExpirySeconds(), TokenType = "Bearer" });
        }

        // Admin token refresh (id=0)
        var externalIdClaim = principal.FindFirst("externalId")?.Value ?? "";
        var nameClaim = principal.FindFirst("name")?.Value ?? "";

        var agent = new LightningAgent.Core.Models.Agent
        {
            Id = int.TryParse(agentIdClaim, out var id) ? id : 0,
            ExternalId = externalIdClaim,
            Name = nameClaim
        };

        var newToken = _jwtTokenService.GenerateToken(agent, isAdmin);
        _logger.LogInformation("JWT token refreshed for agent {AgentId}", agent.Id);

        return Ok(new TokenResponse
        {
            Token = newToken,
            ExpiresIn = GetExpirySeconds(),
            TokenType = "Bearer"
        });
    }

    private int GetExpirySeconds()
    {
        var jwtSection = _configuration.GetSection("Jwt");
        var expiryMinutes = jwtSection.GetValue<int>("ExpiryMinutes");
        return (expiryMinutes > 0 ? expiryMinutes : 60) * 60;
    }
}

public class TokenRequest
{
    [Required(AllowEmptyStrings = false)]
    [StringLength(200, MinimumLength = 1)]
    public string ApiKey { get; set; } = "";
}

public class RefreshRequest
{
    [Required(AllowEmptyStrings = false)]
    [MinLength(1)]
    public string Token { get; set; } = "";
}

public class TokenResponse
{
    public string Token { get; set; } = "";
    public int ExpiresIn { get; set; }
    public string TokenType { get; set; } = "Bearer";
}
