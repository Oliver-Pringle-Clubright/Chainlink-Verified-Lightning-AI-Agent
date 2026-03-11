using LightningAgent.Api.Authentication;
using LightningAgent.Api.Helpers;
using LightningAgent.Core.Interfaces.Data;
using Microsoft.AspNetCore.Mvc;

namespace LightningAgent.Api.Controllers;

[ApiController]
[Route("api/auth")]
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
    public async Task<IActionResult> GetToken([FromBody] TokenRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.ApiKey))
            return BadRequest("ApiKey is required.");

        var jwtSecret = _configuration["Jwt:Secret"];
        if (string.IsNullOrWhiteSpace(jwtSecret))
            return BadRequest("JWT authentication is not configured. Set Jwt:Secret in appsettings or user secrets.");

        var configuredKey = _configuration["ApiSecurity:ApiKey"];

        // Check if this is the global admin API key
        if (!string.IsNullOrWhiteSpace(configuredKey) &&
            string.Equals(request.ApiKey, configuredKey, StringComparison.Ordinal))
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

        // Check for per-agent API key
        var hash = ApiKeyHasher.Hash(request.ApiKey);
        var agent = await _agentRepository.GetByApiKeyHashAsync(hash, ct);

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
    public IActionResult RefreshToken([FromBody] RefreshRequest request)
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
        var externalIdClaim = principal.FindFirst("externalId")?.Value ?? "";
        var nameClaim = principal.FindFirst("name")?.Value ?? "";
        var isAdmin = principal.IsInRole("Admin");

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
    public string ApiKey { get; set; } = "";
}

public class RefreshRequest
{
    public string Token { get; set; } = "";
}

public class TokenResponse
{
    public string Token { get; set; } = "";
    public int ExpiresIn { get; set; }
    public string TokenType { get; set; } = "Bearer";
}
