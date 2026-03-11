using LightningAgent.Api.DTOs;
using LightningAgent.Core.Enums;
using LightningAgent.Core.Interfaces.Data;
using LightningAgent.Core.Interfaces.Services;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;

namespace LightningAgent.Api.Controllers;

/// <summary>
/// Provides multi-pair pricing and task cost estimation via Chainlink oracles.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/pricing")]
[Route("api/v{version:apiVersion}/pricing")]
[Produces("application/json")]
public class PricingController : ControllerBase
{
    private readonly IPriceCacheRepository _priceCacheRepository;
    private readonly IPricingService _pricingService;
    private readonly ILogger<PricingController> _logger;

    public PricingController(
        IPriceCacheRepository priceCacheRepository,
        IPricingService pricingService,
        ILogger<PricingController> logger)
    {
        _priceCacheRepository = priceCacheRepository;
        _pricingService = pricingService;
        _logger = logger;
    }

    /// <summary>
    /// Get the latest cached BTC/USD price.
    /// </summary>
    [HttpGet("btcusd")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetBtcUsdPrice(CancellationToken ct)
    {
        var latest = await _priceCacheRepository.GetLatestAsync("BTC/USD", ct);

        if (latest is null)
        {
            // Return a stub price when no cached price exists
            return Ok(new
            {
                pair = "BTC/USD",
                priceUsd = 0.0,
                source = "none",
                fetchedAt = DateTime.UtcNow,
                message = "No cached price available. Price feed has not been populated yet."
            });
        }

        return Ok(new
        {
            pair = latest.Pair,
            priceUsd = latest.PriceUsd,
            source = latest.Source,
            fetchedAt = latest.FetchedAt
        });
    }

    /// <summary>
    /// Estimate the cost of a task in sats and USD based on type and complexity.
    /// </summary>
    [HttpPost("estimate")]
    [ProducesResponseType(typeof(PriceEstimateResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<PriceEstimateResponse>> EstimateTaskCost(
        [FromBody] PriceEstimateRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.TaskType))
            return BadRequest("TaskType is required.");
        if (!Enum.TryParse<TaskType>(request.TaskType, ignoreCase: true, out _))
            return BadRequest($"Invalid TaskType '{request.TaskType}'. Valid values: {string.Join(", ", Enum.GetNames<TaskType>())}");

        var latest = await _priceCacheRepository.GetLatestAsync("BTC/USD", ct);
        var btcUsdRate = latest?.PriceUsd ?? 60000.0; // fallback estimate

        // Base cost estimate (in sats) by complexity
        long baseSats = (request.EstimatedComplexity?.ToLowerInvariant()) switch
        {
            "low" => 1000,
            "medium" => 5000,
            "high" => 25000,
            _ => 5000
        };

        // Convert sats to USD: sats / 100_000_000 * btcUsdRate
        var estimatedUsd = (double)baseSats / 100_000_000.0 * btcUsdRate;

        return Ok(new PriceEstimateResponse
        {
            EstimatedSats = baseSats,
            EstimatedUsd = Math.Round(estimatedUsd, 4),
            BtcUsdRate = btcUsdRate
        });
    }

    /// <summary>
    /// Get the latest cached price for any supported pair (BTC/USD, ETH/USD, LINK/USD, LINK/ETH).
    /// </summary>
    [HttpGet("{pair}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetPrice(string pair, CancellationToken ct)
    {
        // Normalize pair format: "ethusd" → "ETH/USD", "eth-usd" → "ETH/USD"
        var normalized = NormalizePair(pair);
        if (normalized is null)
            return BadRequest(new { detail = $"Unknown price pair '{pair}'. Supported: btcusd, ethusd, linkusd, linketh" });

        var latest = await _priceCacheRepository.GetLatestAsync(normalized, ct);

        if (latest is null)
        {
            // Try fetching live
            try
            {
                var price = await _pricingService.GetPriceAsync(normalized, ct);
                return Ok(new { pair = normalized, priceUsd = price, source = "Chainlink", fetchedAt = DateTime.UtcNow });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch live price for {Pair}", normalized);
                return Ok(new { pair = normalized, priceUsd = 0.0, source = "none", fetchedAt = DateTime.UtcNow, message = "Price feed not available." });
            }
        }

        return Ok(new { pair = latest.Pair, priceUsd = latest.PriceUsd, source = latest.Source, fetchedAt = latest.FetchedAt });
    }

    /// <summary>
    /// Get all available cached prices at once.
    /// </summary>
    [HttpGet("all")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAllPrices(CancellationToken ct)
    {
        var pairs = new[] { "BTC/USD", "ETH/USD", "LINK/USD", "LINK/ETH" };
        var results = new List<object>();

        foreach (var pair in pairs)
        {
            var latest = await _priceCacheRepository.GetLatestAsync(pair, ct);
            if (latest is not null)
            {
                results.Add(new { pair = latest.Pair, priceUsd = latest.PriceUsd, source = latest.Source, fetchedAt = latest.FetchedAt });
            }
        }

        return Ok(results);
    }

    private static string? NormalizePair(string input)
    {
        var clean = input.ToUpperInvariant().Replace("-", "").Replace("_", "").Replace("/", "");
        return clean switch
        {
            "BTCUSD" => "BTC/USD",
            "ETHUSD" => "ETH/USD",
            "LINKUSD" => "LINK/USD",
            "LINKETH" => "LINK/ETH",
            _ => null
        };
    }
}
