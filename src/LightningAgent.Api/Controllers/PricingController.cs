using LightningAgent.Api.DTOs;
using LightningAgent.Core.Enums;
using LightningAgent.Core.Interfaces.Data;
using Microsoft.AspNetCore.Mvc;

namespace LightningAgent.Api.Controllers;

[ApiController]
[Route("api/pricing")]
public class PricingController : ControllerBase
{
    private readonly IPriceCacheRepository _priceCacheRepository;
    private readonly ILogger<PricingController> _logger;

    public PricingController(
        IPriceCacheRepository priceCacheRepository,
        ILogger<PricingController> logger)
    {
        _priceCacheRepository = priceCacheRepository;
        _logger = logger;
    }

    [HttpGet("btcusd")]
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

    [HttpPost("estimate")]
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
}
