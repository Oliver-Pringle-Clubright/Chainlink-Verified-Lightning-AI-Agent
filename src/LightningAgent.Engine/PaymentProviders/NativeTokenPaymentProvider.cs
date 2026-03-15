using System.Numerics;
using LightningAgent.Core.Configuration;
using LightningAgent.Core.Enums;
using LightningAgent.Core.Interfaces.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nethereum.Hex.HexTypes;
using Nethereum.Web3;

namespace LightningAgent.Engine.PaymentProviders;

/// <summary>
/// Sends native token payments (ETH, MATIC, BNB, AVAX) on any supported chain.
/// </summary>
public class NativeTokenPaymentProvider : IPaymentProvider
{
    private readonly ChainlinkSettings _settings;
    private readonly MultiChainSettings _multiChain;
    private readonly ILogger<NativeTokenPaymentProvider> _logger;

    public PaymentMethod Method => PaymentMethod.NativeEth;
    public bool IsAvailable => !string.IsNullOrEmpty(_settings.PrivateKeyPath);

    public NativeTokenPaymentProvider(
        IOptions<ChainlinkSettings> settings,
        IOptions<MultiChainSettings> multiChain,
        ILogger<NativeTokenPaymentProvider> logger)
    {
        _settings = settings.Value;
        _multiChain = multiChain.Value;
        _logger = logger;
    }

    public async Task<PaymentResult> SendPaymentAsync(PaymentRequest request, CancellationToken ct = default)
    {
        var chainId = request.ChainId ?? 1;
        var rpcUrl = GetRpcUrl(chainId);
        if (string.IsNullOrEmpty(rpcUrl))
            return Fail($"No RPC URL configured for chain {chainId}");

        var account = LightningAgent.Chainlink.EthereumAccountProvider.CreateAccount(_settings.PrivateKeyPath);
        if (account is null)
            return Fail("Private key not configured");

        try
        {
            var web3 = new Web3(account, rpcUrl);

            // Convert sats to wei (1 BTC = 100M sats, use price feed for conversion)
            // For native tokens, AmountUsd is more reliable
            var amountEth = request.AmountUsd ?? (double)request.AmountSats / 100_000_000.0;
            var amountWei = new BigInteger(amountEth * 1e18);

            var tx = await web3.Eth.GetEtherTransferService()
                .TransferEtherAndWaitForReceiptAsync(request.ReceiverAddress, (decimal)amountEth);

            var currency = TokenAddressRegistry.GetNativeCurrency(chainId);
            _logger.LogInformation(
                "Native payment sent: {Amount} {Currency} to {Receiver} on chain {ChainId}, tx={TxHash}",
                amountEth, currency, request.ReceiverAddress, chainId, tx.TransactionHash);

            return new PaymentResult
            {
                Success = true,
                TransactionHash = tx.TransactionHash,
                SenderAddress = account.Address,
                AmountWei = amountWei.ToString(),
                PaymentType = PaymentType.NativeToken,
                PaymentMethod = GetMethodForChain(chainId),
                ChainId = chainId
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Native token payment failed on chain {ChainId}", chainId);
            return Fail(ex.Message);
        }
    }

    private string? GetRpcUrl(long chainId)
    {
        foreach (var (_, chain) in _multiChain.Chains)
        {
            if (chain.ChainId == chainId && !string.IsNullOrEmpty(chain.RpcUrl))
                return chain.RpcUrl;
        }
        return _settings.EthereumRpcUrl;
    }

    private static PaymentMethod GetMethodForChain(long chainId) => chainId switch
    {
        137 or 80002 => PaymentMethod.NativeMatic,
        56 or 97 => PaymentMethod.NativeBnb,
        43114 or 43113 => PaymentMethod.NativeAvax,
        _ => PaymentMethod.NativeEth
    };

    private static PaymentResult Fail(string error) => new()
    {
        Success = false,
        Error = error,
        PaymentType = PaymentType.NativeToken,
        PaymentMethod = PaymentMethod.NativeEth
    };
}
