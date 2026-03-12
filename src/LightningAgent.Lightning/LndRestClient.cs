using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using LightningAgent.Core.Configuration;
using LightningAgent.Core.Interfaces.Services;
using LightningAgent.Core.Models.Lightning;
using LightningAgent.Lightning.LndApiModels;

namespace LightningAgent.Lightning;

/// <summary>
/// LND REST API v2 client implementing <see cref="ILightningClient"/>.
/// </summary>
public sealed class LndRestClient : ILightningClient
{
    private readonly HttpClient _httpClient;
    private readonly LightningSettings _settings;
    private readonly ILogger<LndRestClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public LndRestClient(
        HttpClient httpClient,
        IOptions<LightningSettings> settings,
        ILogger<LndRestClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<HodlInvoice> CreateHodlInvoiceAsync(
        long amountSats,
        string memo,
        byte[] paymentHash,
        int expirySec,
        CancellationToken ct = default)
    {
        var hexHash = Convert.ToHexString(paymentHash).ToLowerInvariant();

        var request = new AddHodlInvoiceRequest
        {
            Memo = memo,
            Hash = hexHash,
            Value = amountSats.ToString(),
            Expiry = expirySec.ToString()
        };

        _logger.LogDebug("Creating HODL invoice: {AmountSats} sats, hash={Hash}", amountSats, hexHash);

        var response = await PostAsync<AddHodlInvoiceResponse>("/v2/invoices/hodl", request, ct);

        return new HodlInvoice
        {
            PaymentHash = hexHash,
            PaymentRequest = response.PaymentRequest,
            AmountSats = amountSats,
            Memo = memo,
            ExpiresAt = DateTime.UtcNow.AddSeconds(expirySec),
            State = "OPEN"
        };
    }

    public async Task<bool> SettleInvoiceAsync(byte[] paymentPreimage, CancellationToken ct = default)
    {
        var hexPreimage = Convert.ToHexString(paymentPreimage).ToLowerInvariant();

        var request = new SettleInvoiceRequest
        {
            Preimage = hexPreimage
        };

        try
        {
            _logger.LogDebug("Settling invoice (preimage hash={PreimageHash})",
                Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(paymentPreimage))[..16].ToLowerInvariant());
            await PostAsync<JsonElement>("/v2/invoices/settle", request, ct);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to settle invoice (preimage hash={PreimageHash})",
                Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(paymentPreimage))[..16].ToLowerInvariant());
            return false;
        }
    }

    public async Task<bool> CancelInvoiceAsync(byte[] paymentHash, CancellationToken ct = default)
    {
        var hexHash = Convert.ToHexString(paymentHash).ToLowerInvariant();

        var request = new CancelInvoiceRequest
        {
            PaymentHash = hexHash
        };

        try
        {
            _logger.LogDebug("Cancelling invoice with hash={Hash}", hexHash);
            await PostAsync<JsonElement>("/v2/invoices/cancel", request, ct);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cancel invoice with hash={Hash}", hexHash);
            return false;
        }
    }

    public async Task<PaymentRoute> SendPaymentAsync(string paymentRequest, CancellationToken ct = default)
    {
        var request = new SendPaymentRequest
        {
            PaymentRequest = paymentRequest,
            TimeoutSeconds = 60,
            FeeLimitSat = "100"
        };

        _logger.LogDebug("Sending payment for bolt11={PaymentRequest}", paymentRequest[..Math.Min(30, paymentRequest.Length)]);

        var json = JsonSerializer.Serialize(request, JsonOptions);
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        using var httpResponse = await _httpClient.PostAsync("/v2/router/send", content, ct);
        httpResponse.EnsureSuccessStatusCode();

        // LND's /v2/router/send returns newline-delimited JSON (streaming).
        // We read each line and use the last complete payment response.
        var body = await httpResponse.Content.ReadAsStringAsync(ct);
        SendPaymentResponse? finalResponse = null;

        foreach (var line in body.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            try
            {
                var parsed = JsonSerializer.Deserialize<SendPaymentResponse>(trimmed, JsonOptions);
                if (parsed is not null)
                {
                    finalResponse = parsed;
                }
            }
            catch (JsonException)
            {
                // Skip malformed intermediate lines
            }
        }

        if (finalResponse is null)
        {
            throw new InvalidOperationException("No valid payment response received from LND.");
        }

        if (finalResponse.Status == "FAILED")
        {
            throw new InvalidOperationException(
                $"Payment failed: {finalResponse.FailureReason ?? "unknown reason"}");
        }

        return MapPaymentRoute(finalResponse);
    }

    public async Task<InvoiceState> GetInvoiceStateAsync(byte[] paymentHash, CancellationToken ct = default)
    {
        var hexHash = Convert.ToHexString(paymentHash).ToLowerInvariant();

        _logger.LogDebug("Looking up invoice state for hash={Hash}", hexHash);

        using var response = await _httpClient.GetAsync(
            $"/v2/invoices/lookup?payment_hash={hexHash}", ct);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync(ct);
        var lookup = JsonSerializer.Deserialize<InvoiceLookupResponse>(body, JsonOptions)
            ?? throw new InvalidOperationException("Failed to deserialize invoice lookup response.");

        return MapInvoiceState(lookup, hexHash);
    }

    public async Task<LndInfo> GetInfoAsync(CancellationToken ct = default)
    {
        _logger.LogDebug("Fetching LND node info");

        using var response = await _httpClient.GetAsync("/v1/getinfo", ct);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync(ct);
        var info = JsonSerializer.Deserialize<GetInfoResponse>(body, JsonOptions)
            ?? throw new InvalidOperationException("Failed to deserialize getinfo response.");

        return new LndInfo
        {
            Alias = info.Alias,
            PubKey = info.IdentityPubkey,
            NumActiveChannels = info.NumActiveChannels,
            NumPeers = info.NumPeers,
            BlockHeight = info.BlockHeight,
            SyncedToChain = info.SyncedToChain
        };
    }

    // ── Channel management ────────────────────────────────────────────

    public async Task<ChannelBalance> GetChannelBalanceAsync(CancellationToken ct = default)
    {
        _logger.LogDebug("Fetching channel balance");

        using var response = await _httpClient.GetAsync("/v1/balance/channels", ct);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync(ct);
        var balance = JsonSerializer.Deserialize<ChannelBalanceResponse>(body, JsonOptions)
            ?? throw new InvalidOperationException("Failed to deserialize channel balance response.");

        return new ChannelBalance
        {
            LocalBalanceSats = ParseSat(balance.LocalBalance),
            RemoteBalanceSats = ParseSat(balance.RemoteBalance),
            UnsettledLocalBalanceSats = ParseSat(balance.UnsettledLocalBalance),
            UnsettledRemoteBalanceSats = ParseSat(balance.UnsettledRemoteBalance),
            PendingOpenLocalBalanceSats = ParseSat(balance.PendingOpenLocalBalance),
            PendingOpenRemoteBalanceSats = ParseSat(balance.PendingOpenRemoteBalance)
        };
    }

    public async Task<IReadOnlyList<LndChannel>> ListChannelsAsync(CancellationToken ct = default)
    {
        _logger.LogDebug("Listing channels");

        using var response = await _httpClient.GetAsync("/v1/channels", ct);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync(ct);
        var result = JsonSerializer.Deserialize<ListChannelsResponse>(body, JsonOptions)
            ?? throw new InvalidOperationException("Failed to deserialize list channels response.");

        var channels = new List<LndChannel>();

        if (result.Channels is not null)
        {
            foreach (var ch in result.Channels)
            {
                channels.Add(new LndChannel
                {
                    Active = ch.Active,
                    RemotePubkey = ch.RemotePubkey,
                    ChannelPoint = ch.ChannelPoint,
                    Capacity = long.TryParse(ch.Capacity, out var cap) ? cap : 0,
                    LocalBalance = long.TryParse(ch.LocalBalance, out var lb) ? lb : 0,
                    RemoteBalance = long.TryParse(ch.RemoteBalance, out var rb) ? rb : 0,
                    TotalSatoshisSent = long.TryParse(ch.TotalSatoshisSent, out var sent) ? sent : 0,
                    TotalSatoshisReceived = long.TryParse(ch.TotalSatoshisReceived, out var recv) ? recv : 0,
                    NumUpdates = long.TryParse(ch.NumUpdates, out var upd) ? upd : 0,
                    ChanId = long.TryParse(ch.ChanId, out var cid) ? cid : 0
                });
            }
        }

        return channels;
    }

    public async Task<OpenChannelResult> OpenChannelAsync(string nodePubkey, long localAmountSats, CancellationToken ct = default)
    {
        _logger.LogDebug("Opening channel to {NodePubkey} with {Amount} sats", nodePubkey, localAmountSats);

        var request = new OpenChannelRequest
        {
            NodePubkeyString = nodePubkey,
            LocalFundingAmount = localAmountSats.ToString()
        };

        var response = await PostAsync<OpenChannelResponse>("/v1/channels", request, ct);

        return new OpenChannelResult
        {
            FundingTxId = response.FundingTxidStr ?? string.Empty,
            OutputIndex = response.OutputIndex
        };
    }

    // ── Multi-path payment support ────────────────────────────────────

    public async Task<MultiPathPaymentResult> SendPaymentAsync(
        string paymentRequest,
        long amountSats,
        bool allowMultiPath = true,
        CancellationToken ct = default)
    {
        var feeLimitSat = Math.Max(1, amountSats / 10); // 10% fee limit

        var request = new SendPaymentRequest
        {
            PaymentRequest = paymentRequest,
            TimeoutSeconds = 60,
            FeeLimitSat = feeLimitSat.ToString(),
            Amt = amountSats.ToString(),
            AllowSelfPayment = false,
            MaxParts = allowMultiPath ? 16 : null
        };

        _logger.LogDebug(
            "Sending MPP payment: bolt11={PaymentRequest}, amount={Amount} sats, multiPath={MultiPath}",
            paymentRequest[..Math.Min(30, paymentRequest.Length)], amountSats, allowMultiPath);

        var json = JsonSerializer.Serialize(request, JsonOptions);
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        using var httpResponse = await _httpClient.PostAsync("/v2/router/send", content, ct);
        httpResponse.EnsureSuccessStatusCode();

        // LND's /v2/router/send returns newline-delimited JSON (streaming).
        // We read each line and use the last complete payment response.
        var body = await httpResponse.Content.ReadAsStringAsync(ct);
        SendPaymentResponse? finalResponse = null;

        foreach (var line in body.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            try
            {
                var parsed = JsonSerializer.Deserialize<SendPaymentResponse>(trimmed, JsonOptions);
                if (parsed is not null)
                {
                    finalResponse = parsed;
                }
            }
            catch (JsonException)
            {
                // Skip malformed intermediate lines
            }
        }

        if (finalResponse is null)
        {
            throw new InvalidOperationException("No valid payment response received from LND.");
        }

        if (finalResponse.Status == "FAILED")
        {
            throw new InvalidOperationException(
                $"Payment failed: {finalResponse.FailureReason ?? "unknown reason"}");
        }

        return MapMultiPathPaymentResult(finalResponse);
    }

    // ── Private helpers ───────────────────────────────────────────────

    private static long ParseSat(BalanceAmount? amount)
    {
        if (amount is null) return 0;
        return long.TryParse(amount.Sat, out var sat) ? sat : 0;
    }

    private async Task<T> PostAsync<T>(string path, object requestBody, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(requestBody, JsonOptions);
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        using var response = await _httpClient.PostAsync(path, content, ct);
        response.EnsureSuccessStatusCode();

        var responseBody = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<T>(responseBody, JsonOptions)
            ?? throw new InvalidOperationException($"Failed to deserialize response from {path}.");
    }

    private static PaymentRoute MapPaymentRoute(SendPaymentResponse response)
    {
        var route = new PaymentRoute
        {
            TotalAmtSats = long.TryParse(response.ValueSat, out var amt) ? amt : 0,
            TotalFeesSats = long.TryParse(response.FeeSat, out var fee) ? fee : 0,
            Hops = new List<string>()
        };

        // Extract hop pub keys from the last successful HTLC attempt
        var successfulHtlc = response.Htlcs?
            .LastOrDefault(h => h.Status == "SUCCEEDED")
            ?? response.Htlcs?.LastOrDefault();

        if (successfulHtlc?.Route?.Hops is { } hops)
        {
            foreach (var hop in hops)
            {
                route.Hops.Add(hop.PubKey);
            }
        }

        // Use route-level totals if available and more specific
        if (successfulHtlc?.Route is { } routeInfo)
        {
            if (long.TryParse(routeInfo.TotalAmt, out var routeAmt) && routeAmt > 0)
                route.TotalAmtSats = routeAmt;
            if (long.TryParse(routeInfo.TotalFees, out var routeFees) && routeFees > 0)
                route.TotalFeesSats = routeFees;
        }

        return route;
    }

    private static InvoiceState MapInvoiceState(InvoiceLookupResponse lookup, string hexHash)
    {
        var state = new InvoiceState
        {
            PaymentHash = hexHash,
            State = lookup.State,
            AmountSats = long.TryParse(lookup.Value, out var val) ? val : 0,
            IsHeld = string.Equals(lookup.State, "ACCEPTED", StringComparison.OrdinalIgnoreCase)
        };

        // Parse settle date (LND returns unix timestamp as string)
        if (!string.IsNullOrEmpty(lookup.SettleDate)
            && long.TryParse(lookup.SettleDate, out var settleUnix)
            && settleUnix > 0)
        {
            state.SettledAt = DateTimeOffset.FromUnixTimeSeconds(settleUnix).UtcDateTime;
        }

        return state;
    }

    private static MultiPathPaymentResult MapMultiPathPaymentResult(SendPaymentResponse response)
    {
        var result = new MultiPathPaymentResult
        {
            PaymentPreimage = response.PaymentPreimage,
            PaymentHash = response.PaymentHash,
            AmountSats = long.TryParse(response.ValueSat, out var amt) ? amt : 0,
            FeeSats = long.TryParse(response.FeeSat, out var fee) ? fee : 0,
            Status = response.Status,
            Hops = new List<string>()
        };

        if (response.Htlcs is not null)
        {
            result.NumParts = response.Htlcs.Count(h => h.Status == "SUCCEEDED");

            // Collect hop pubkeys from the last successful HTLC attempt
            var successfulHtlc = response.Htlcs
                .LastOrDefault(h => h.Status == "SUCCEEDED")
                ?? response.Htlcs.LastOrDefault();

            if (successfulHtlc?.Route?.Hops is { } hops)
            {
                foreach (var hop in hops)
                {
                    result.Hops.Add(hop.PubKey);
                }
            }
        }

        return result;
    }
}
