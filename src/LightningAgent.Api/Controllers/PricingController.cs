using LightningAgent.Api.DTOs;
using LightningAgent.Core.Configuration;
using LightningAgent.Core.Enums;
using LightningAgent.Core.Interfaces.Data;
using LightningAgent.Core.Interfaces.Services;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace LightningAgent.Api.Controllers;

/// <summary>
/// Multi-source pricing: Chainlink on-chain oracles + CoinGecko API.
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
    private readonly ICoinGeckoClient _coinGeckoClient;
    private readonly CoinGeckoSettings _coinGeckoSettings;
    private readonly PlatformFeeSettings _feeSettings;
    private readonly ILogger<PricingController> _logger;

    public PricingController(
        IPriceCacheRepository priceCacheRepository,
        IPricingService pricingService,
        ICoinGeckoClient coinGeckoClient,
        IOptions<CoinGeckoSettings> coinGeckoSettings,
        IOptions<PlatformFeeSettings> feeSettings,
        ILogger<PricingController> logger)
    {
        _priceCacheRepository = priceCacheRepository;
        _pricingService = pricingService;
        _coinGeckoClient = coinGeckoClient;
        _coinGeckoSettings = coinGeckoSettings.Value;
        _feeSettings = feeSettings.Value;
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
    /// Get all available cached prices (Chainlink + CoinGecko).
    /// Aliased as /api/pricing/rates for ACP compatibility.
    /// </summary>
    [HttpGet("all")]
    [HttpGet("rates")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAllPrices(CancellationToken ct)
    {
        var allPrices = await _pricingService.GetAllCachedPricesAsync(ct);

        var results = allPrices.Select(p => new
        {
            pair = p.Pair,
            priceUsd = p.PriceUsd,
            source = p.Source,
            fetchedAt = p.FetchedAt
        });

        return Ok(results);
    }

    /// <summary>
    /// Get the latest cached price for any supported pair.
    /// Supports Chainlink pairs (BTC/USD, ETH/USD, LINK/USD, LINK/ETH)
    /// and CoinGecko pairs (SOL/USD, AVAX/USD, ADA/USD, DOT/USD, etc.).
    /// Aliased as /api/pricing/rate/{pair} for ACP compatibility.
    /// </summary>
    [HttpGet("rate/{pair}")]
    [HttpGet("{pair}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetPrice(string pair, CancellationToken ct)
    {
        var normalized = NormalizePair(pair);
        if (normalized is null)
            return BadRequest(new { detail = $"Unknown price pair '{pair}'. Use /api/pricing/pairs for supported list." });

        var latest = await _priceCacheRepository.GetLatestAsync(normalized, ct);

        if (latest is not null)
            return Ok(new { pair = latest.Pair, priceUsd = latest.PriceUsd, source = latest.Source, fetchedAt = latest.FetchedAt });

        // Try fetching live
        try
        {
            var price = await _pricingService.GetPriceAsync(normalized, ct);
            return Ok(new { pair = normalized, priceUsd = price, source = "live", fetchedAt = DateTime.UtcNow });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch live price for {Pair}", normalized);
            return Ok(new { pair = normalized, priceUsd = 0.0, source = "none", fetchedAt = DateTime.UtcNow, message = "Price feed not available." });
        }
    }

    /// <summary>
    /// List all supported price pairs and their sources.
    /// </summary>
    [HttpGet("pairs")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetSupportedPairs()
    {
        var chainlinkPairs = new[] { "BTC/USD", "ETH/USD", "LINK/USD", "LINK/ETH" }
            .Select(p => new { pair = p, source = "Chainlink" });

        var coinGeckoPairs = _coinGeckoSettings.Enabled
            ? _coinGeckoClient.GetSupportedPairs()
                .Select(p => new { pair = p, source = "CoinGecko" })
            : Enumerable.Empty<object>()
                .Select(_ => new { pair = "", source = "" });

        // Merge, deduplicating (Chainlink takes priority for pairs that exist in both)
        var chainlinkSet = new HashSet<string>(chainlinkPairs.Select(p => p.pair), StringComparer.OrdinalIgnoreCase);
        var merged = chainlinkPairs.Concat(
            coinGeckoPairs.Where(p => !chainlinkSet.Contains(p.pair)));

        return Ok(new
        {
            coinGeckoEnabled = _coinGeckoSettings.Enabled,
            pairs = merged
        });
    }

    /// <summary>
    /// Force-refresh all CoinGecko prices now.
    /// </summary>
    [HttpPost("coingecko/refresh")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RefreshCoinGecko(CancellationToken ct)
    {
        if (!_coinGeckoSettings.Enabled)
            return BadRequest(new { detail = "CoinGecko is not enabled in configuration." });

        var prices = await _pricingService.RefreshCoinGeckoPricesAsync(ct);
        return Ok(new
        {
            refreshed = prices.Count,
            prices = prices.Select(kv => new { pair = kv.Key, priceUsd = kv.Value })
        });
    }

    /// <summary>
    /// Suggest a price for a task based on type, complexity, and current BTC/USD rate.
    /// </summary>
    [HttpGet("suggest")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SuggestPrice(
        [FromQuery] string taskType,
        [FromQuery] string complexity = "medium",
        [FromQuery] string? description = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(taskType))
            return BadRequest("taskType query parameter is required.");

        var normalizedType = taskType.Trim().ToLowerInvariant();
        var normalizedComplexity = (complexity ?? "medium").Trim().ToLowerInvariant();

        // Base sats by task type and complexity
        long suggestedSats = (normalizedType, normalizedComplexity) switch
        {
            ("code", "low") => 5000,
            ("code", "medium") => 15000,
            ("code", "high") => 50000,
            ("text", "low") => 2000,
            ("text", "medium") => 8000,
            ("text", "high") => 25000,
            ("data", "low") => 3000,
            ("data", "medium") => 10000,
            ("data", "high") => 35000,
            ("image", "low") => 1000,
            ("image", "medium") => 5000,
            ("image", "high") => 15000,
            _ => 5000 // default fallback
        };

        var latest = await _priceCacheRepository.GetLatestAsync("BTC/USD", ct);
        var btcUsdRate = latest?.PriceUsd ?? 60000.0;

        var suggestedUsd = Math.Round((double)suggestedSats / 100_000_000.0 * btcUsdRate, 4);

        return Ok(new
        {
            suggestedSats,
            suggestedUsd,
            btcUsdRate,
            taskType,
            complexity = normalizedComplexity,
            marketRate = "competitive"
        });
    }

    private static string? NormalizePair(string input)
    {
        var clean = input.ToUpperInvariant().Replace("-", "").Replace("_", "").Replace("/", "");
        return clean switch
        {
            // Chainlink pairs
            "BTCUSD" => "BTC/USD",
            "ETHUSD" => "ETH/USD",
            "LINKUSD" => "LINK/USD",
            "LINKETH" => "LINK/ETH",
            // CoinGecko extended pairs
            "SOLUSD" => "SOL/USD",
            "AVAXUSD" => "AVAX/USD",
            "ADAUSD" => "ADA/USD",
            "DOTUSD" => "DOT/USD",
            "UNIUSD" => "UNI/USD",
            "MATICUSD" => "MATIC/USD",
            "ARBUSD" => "ARB/USD",
            "OPUSD" => "OP/USD",
            "ATOMUSD" => "ATOM/USD",
            "NEARUSD" => "NEAR/USD",
            "DOGEUSD" => "DOGE/USD",
            "XRPUSD" => "XRP/USD",
            "BNBUSD" => "BNB/USD",
            "USDCUSD" => "USDC/USD",
            "USDTUSD" => "USDT/USD",
            _ => null
        };
    }

    /// <summary>
    /// Returns the platform fee schedule including early adopter discount info.
    /// </summary>
    [HttpGet("fees")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetFeeSchedule(
        [FromServices] IAgentRepository agentRepo, CancellationToken ct)
    {
        var allAgents = await agentRepo.GetAllAsync(null, ct);
        var earlyAdoptersTaken = allAgents.Count;
        var slotsRemaining = Math.Max(0, _feeSettings.EarlyAdopterSlots - earlyAdoptersTaken);
        var discountRate = _feeSettings.EarlyAdopterDiscount;

        long exampleGross = 10000;
        long exCommission = (long)(exampleGross * _feeSettings.CommissionRate);
        long exVerification = _feeSettings.VerificationFeeSats;
        long exAgentNet = exampleGross - exCommission - exVerification;

        long exDiscountedCommission = (long)(exampleGross * _feeSettings.CommissionRate * (1 - discountRate));
        long exDiscountedVerification = (long)(_feeSettings.VerificationFeeSats * (1 - discountRate));
        long exDiscountedNet = exampleGross - exDiscountedCommission - exDiscountedVerification;

        return Ok(new
        {
            commission = new
            {
                rate = _feeSettings.CommissionRate,
                ratePercent = $"{_feeSettings.CommissionRate:P0}",
                description = $"{_feeSettings.CommissionRate:P0} deducted from agent payout on each milestone completion"
            },
            taskPostingFee = new
            {
                amountSats = _feeSettings.TaskPostingFeeSats,
                description = "Flat fee charged when creating a task (anti-spam)"
            },
            verificationFee = new
            {
                amountSats = _feeSettings.VerificationFeeSats,
                description = "Per-milestone fee covering on-chain verification costs (gas + LINK)"
            },
            earlyAdopter = new
            {
                totalSlots = _feeSettings.EarlyAdopterSlots,
                slotsTaken = earlyAdoptersTaken,
                slotsRemaining,
                discountRate,
                discountPercent = $"{discountRate:P0}",
                description = $"First {_feeSettings.EarlyAdopterSlots} agents receive {discountRate:P0} off all fees for life"
            },
            example = new
            {
                milestonePayout = exampleGross,
                standard = new
                {
                    commission = exCommission,
                    verificationFee = exVerification,
                    agentReceives = exAgentNet
                },
                earlyAdopterRate = new
                {
                    commission = exDiscountedCommission,
                    verificationFee = exDiscountedVerification,
                    agentReceives = exDiscountedNet
                }
            }
        });
    }
}
