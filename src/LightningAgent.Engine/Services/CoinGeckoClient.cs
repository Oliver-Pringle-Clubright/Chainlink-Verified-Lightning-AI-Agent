using System.Net.Http.Json;
using System.Text.Json;
using LightningAgent.Core.Configuration;
using LightningAgent.Core.Interfaces.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LightningAgent.Engine.Services;

public class CoinGeckoClient : ICoinGeckoClient
{
    private readonly HttpClient _httpClient;
    private readonly CoinGeckoSettings _settings;
    private readonly ILogger<CoinGeckoClient> _logger;

    /// <summary>
    /// Maps CoinGecko coin IDs to display pair names (e.g. "bitcoin" → "BTC/USD").
    /// </summary>
    private static readonly Dictionary<string, string> CoinIdToPair = new(StringComparer.OrdinalIgnoreCase)
    {
        ["bitcoin"] = "BTC/USD",
        ["ethereum"] = "ETH/USD",
        ["chainlink"] = "LINK/USD",
        ["solana"] = "SOL/USD",
        ["avalanche-2"] = "AVAX/USD",
        ["cardano"] = "ADA/USD",
        ["polkadot"] = "DOT/USD",
        ["uniswap"] = "UNI/USD",
        ["matic-network"] = "MATIC/USD",
        ["arbitrum"] = "ARB/USD",
        ["optimism"] = "OP/USD",
        ["cosmos"] = "ATOM/USD",
        ["near"] = "NEAR/USD",
        ["dogecoin"] = "DOGE/USD",
        ["ripple"] = "XRP/USD",
        ["binancecoin"] = "BNB/USD",
        ["usd-coin"] = "USDC/USD",
        ["tether"] = "USDT/USD"
    };

    private static readonly Dictionary<string, string> PairToCoinId =
        CoinIdToPair.ToDictionary(kv => kv.Value, kv => kv.Key, StringComparer.OrdinalIgnoreCase);

    public CoinGeckoClient(
        HttpClient httpClient,
        IOptions<CoinGeckoSettings> settings,
        ILogger<CoinGeckoClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;

        var baseUri = new Uri(_settings.BaseUrl.TrimEnd('/') + "/");
        if (baseUri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException(
                $"CoinGecko BaseUrl must use HTTPS. Got: {baseUri.Scheme}");

        _httpClient.BaseAddress = baseUri;
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");

        if (!string.IsNullOrWhiteSpace(_settings.ApiKey))
        {
            _httpClient.DefaultRequestHeaders.Add("x-cg-pro-api-key", _settings.ApiKey);
        }
    }

    public IReadOnlyList<string> GetSupportedPairs()
    {
        var configuredIds = _settings.CoinIds
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return configuredIds
            .Where(id => CoinIdToPair.ContainsKey(id))
            .Select(id => CoinIdToPair[id])
            .ToList()
            .AsReadOnly();
    }

    public async Task<Dictionary<string, CoinGeckoPrice>> GetAllPricesAsync(CancellationToken ct = default)
    {
        var coinIds = _settings.CoinIds
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var idsParam = string.Join(",", coinIds);

        _logger.LogInformation("Fetching CoinGecko prices for {Count} coins", coinIds.Length);

        var url = $"simple/price?ids={idsParam}&vs_currencies=usd&include_24hr_change=true&include_last_updated_at=true";

        var response = await _httpClient.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        var results = new Dictionary<string, CoinGeckoPrice>(StringComparer.OrdinalIgnoreCase);

        foreach (var coinId in coinIds)
        {
            if (!json.TryGetProperty(coinId, out var coinData))
            {
                _logger.LogWarning("CoinGecko response missing data for {CoinId}", coinId);
                continue;
            }

            if (!coinData.TryGetProperty("usd", out var usdProp))
                continue;

            var pair = CoinIdToPair.GetValueOrDefault(coinId, $"{coinId.ToUpperInvariant()}/USD");
            var priceUsd = usdProp.GetDouble();

            double? change24h = coinData.TryGetProperty("usd_24h_change", out var changeProp)
                && changeProp.ValueKind == JsonValueKind.Number
                    ? changeProp.GetDouble()
                    : null;

            long? lastUpdated = coinData.TryGetProperty("last_updated_at", out var updatedProp)
                && updatedProp.ValueKind == JsonValueKind.Number
                    ? updatedProp.GetInt64()
                    : null;

            var price = new CoinGeckoPrice(pair, coinId, priceUsd, change24h, lastUpdated);
            results[pair] = price;

            _logger.LogDebug("CoinGecko {Pair}: ${Price:F4} (24h: {Change:F2}%)",
                pair, priceUsd, change24h ?? 0);
        }

        _logger.LogInformation("CoinGecko fetched {Count} prices successfully", results.Count);
        return results;
    }

    public async Task<CoinGeckoPrice?> GetPriceAsync(string coinId, CancellationToken ct = default)
    {
        _logger.LogInformation("Fetching CoinGecko price for {CoinId}", coinId);

        var url = $"simple/price?ids={coinId}&vs_currencies=usd&include_24hr_change=true&include_last_updated_at=true";

        var response = await _httpClient.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);

        if (!json.TryGetProperty(coinId, out var coinData)
            || !coinData.TryGetProperty("usd", out var usdProp))
        {
            _logger.LogWarning("CoinGecko returned no data for {CoinId}", coinId);
            return null;
        }

        var pair = CoinIdToPair.GetValueOrDefault(coinId, $"{coinId.ToUpperInvariant()}/USD");

        double? change24h = coinData.TryGetProperty("usd_24h_change", out var changeProp)
            && changeProp.ValueKind == JsonValueKind.Number
                ? changeProp.GetDouble()
                : null;

        long? lastUpdated = coinData.TryGetProperty("last_updated_at", out var updatedProp)
            && updatedProp.ValueKind == JsonValueKind.Number
                ? updatedProp.GetInt64()
                : null;

        return new CoinGeckoPrice(pair, coinId, usdProp.GetDouble(), change24h, lastUpdated);
    }

    /// <summary>
    /// Resolves a display pair (e.g. "SOL/USD") to a CoinGecko coin ID (e.g. "solana").
    /// </summary>
    public static string? ResolveCoinId(string pair)
    {
        return PairToCoinId.GetValueOrDefault(pair.ToUpperInvariant());
    }

    /// <summary>
    /// Resolves a CoinGecko coin ID to a display pair.
    /// </summary>
    public static string ResolvePair(string coinId)
    {
        return CoinIdToPair.GetValueOrDefault(coinId, $"{coinId.ToUpperInvariant()}/USD");
    }
}
