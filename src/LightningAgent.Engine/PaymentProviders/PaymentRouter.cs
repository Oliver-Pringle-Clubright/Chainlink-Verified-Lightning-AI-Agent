using LightningAgent.Core.Enums;
using LightningAgent.Core.Interfaces.Services;
using Microsoft.Extensions.Logging;

namespace LightningAgent.Engine.PaymentProviders;

/// <summary>
/// Routes payment requests to the appropriate payment provider based on the requested method.
/// Falls back to available providers if the preferred method is unavailable.
/// </summary>
public class PaymentRouter
{
    private readonly IEnumerable<IPaymentProvider> _providers;
    private readonly ILogger<PaymentRouter> _logger;

    public PaymentRouter(
        IEnumerable<IPaymentProvider> providers,
        ILogger<PaymentRouter> logger)
    {
        _providers = providers;
        _logger = logger;
    }

    /// <summary>
    /// Sends a payment using the specified method, or falls back to an available provider.
    /// </summary>
    public async Task<PaymentResult> SendAsync(PaymentMethod preferredMethod, PaymentRequest request, CancellationToken ct = default)
    {
        // Try preferred method first
        var provider = _providers.FirstOrDefault(p => p.Method == preferredMethod && p.IsAvailable);

        if (provider is null)
        {
            _logger.LogWarning(
                "Preferred payment method {Method} is not available, finding fallback",
                preferredMethod);

            // Fallback to any available provider
            provider = _providers.FirstOrDefault(p => p.IsAvailable);
        }

        if (provider is null)
        {
            _logger.LogError("No payment providers are available");
            return new PaymentResult
            {
                Success = false,
                Error = "No payment providers are available. Configure at least one payment method.",
                PaymentType = PaymentType.Direct,
                PaymentMethod = preferredMethod
            };
        }

        _logger.LogInformation(
            "Routing payment to {Method} provider: {Amount} sats for task {TaskId}",
            provider.Method, request.AmountSats, request.TaskId);

        return await provider.SendPaymentAsync(request, ct);
    }

    /// <summary>
    /// Returns all available payment methods.
    /// </summary>
    public IReadOnlyList<PaymentMethodInfo> GetAvailableMethods()
    {
        return _providers.Select(p => new PaymentMethodInfo
        {
            Method = p.Method,
            IsAvailable = p.IsAvailable,
            Name = GetMethodName(p.Method)
        }).ToList();
    }

    private static string GetMethodName(PaymentMethod method) => method switch
    {
        PaymentMethod.Lightning => "Lightning Network",
        PaymentMethod.OnChainBtc => "On-Chain Bitcoin",
        PaymentMethod.Erc20Usdc => "USDC (ERC-20)",
        PaymentMethod.Erc20Usdt => "USDT (ERC-20)",
        PaymentMethod.Erc20Link => "LINK (ERC-20)",
        PaymentMethod.NativeEth => "ETH (Native)",
        PaymentMethod.NativeMatic => "MATIC (Native)",
        PaymentMethod.NativeBnb => "BNB (Native)",
        PaymentMethod.NativeAvax => "AVAX (Native)",
        PaymentMethod.CcipBridge => "CCIP Cross-Chain",
        _ => method.ToString()
    };

    public record PaymentMethodInfo
    {
        public PaymentMethod Method { get; init; }
        public bool IsAvailable { get; init; }
        public string Name { get; init; } = "";
    }
}
