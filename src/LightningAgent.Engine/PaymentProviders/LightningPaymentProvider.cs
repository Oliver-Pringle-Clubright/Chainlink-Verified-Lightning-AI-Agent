using LightningAgent.Core.Enums;
using LightningAgent.Core.Interfaces.Services;
using Microsoft.Extensions.Logging;

namespace LightningAgent.Engine.PaymentProviders;

/// <summary>
/// Sends payments via Lightning Network HODL invoices.
/// Wraps the existing ILightningClient for the payment provider interface.
/// </summary>
public class LightningPaymentProvider : IPaymentProvider
{
    private readonly ILightningClient _lightning;
    private readonly ILogger<LightningPaymentProvider> _logger;

    public PaymentMethod Method => PaymentMethod.Lightning;
    public bool IsAvailable => _lightning is not null;

    public LightningPaymentProvider(
        ILightningClient lightning,
        ILogger<LightningPaymentProvider> logger)
    {
        _lightning = lightning;
        _logger = logger;
    }

    public async Task<PaymentResult> SendPaymentAsync(PaymentRequest request, CancellationToken ct = default)
    {
        try
        {
            var preimage = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
            var paymentHash = System.Security.Cryptography.SHA256.HashData(preimage);
            var paymentHashHex = Convert.ToHexString(paymentHash).ToLowerInvariant();

            var invoice = await _lightning.CreateHodlInvoiceAsync(
                request.AmountSats,
                request.Memo ?? $"Payment for task {request.TaskId}",
                paymentHash,
                3600,
                ct);

            // Settle immediately (for direct payments)
            await _lightning.SettleInvoiceAsync(preimage, ct);

            _logger.LogInformation(
                "Lightning payment sent: {Amount} sats, hash={Hash}",
                request.AmountSats, paymentHashHex);

            return new PaymentResult
            {
                Success = true,
                PaymentHash = paymentHashHex,
                PaymentType = PaymentType.Streaming,
                PaymentMethod = PaymentMethod.Lightning
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lightning payment failed");
            return new PaymentResult
            {
                Success = false,
                Error = ex.Message,
                PaymentType = PaymentType.Streaming,
                PaymentMethod = PaymentMethod.Lightning
            };
        }
    }
}
