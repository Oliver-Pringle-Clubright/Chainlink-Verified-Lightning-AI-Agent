using LightningAgentMarketPlace.Core.Models.Lightning;

namespace LightningAgentMarketPlace.Core.Interfaces.Services;

public interface ILightningClient
{
    Task<HodlInvoice> CreateHodlInvoiceAsync(long amountSats, string memo, byte[] paymentHash, int expirySec, CancellationToken ct = default);
    Task<bool> SettleInvoiceAsync(byte[] paymentPreimage, CancellationToken ct = default);
    Task<bool> CancelInvoiceAsync(byte[] paymentHash, CancellationToken ct = default);
    Task<PaymentRoute> SendPaymentAsync(string paymentRequest, CancellationToken ct = default);
    Task<InvoiceState> GetInvoiceStateAsync(byte[] paymentHash, CancellationToken ct = default);
    Task<LndInfo> GetInfoAsync(CancellationToken ct = default);

    // Channel management
    Task<ChannelBalance> GetChannelBalanceAsync(CancellationToken ct = default);
    Task<IReadOnlyList<LndChannel>> ListChannelsAsync(CancellationToken ct = default);
    Task<OpenChannelResult> OpenChannelAsync(string nodePubkey, long localAmountSats, CancellationToken ct = default);

    // Multi-path payment support
    Task<MultiPathPaymentResult> SendPaymentAsync(string paymentRequest, long amountSats, bool allowMultiPath = true, CancellationToken ct = default);
}
