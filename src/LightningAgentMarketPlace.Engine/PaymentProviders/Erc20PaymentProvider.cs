using System.Numerics;
using LightningAgentMarketPlace.Core.Configuration;
using LightningAgentMarketPlace.Core.Enums;
using LightningAgentMarketPlace.Core.Interfaces.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nethereum.Web3;
using Nethereum.Web3.Accounts;

namespace LightningAgentMarketPlace.Engine.PaymentProviders;

/// <summary>
/// Sends ERC-20 token payments (USDC, USDT, LINK) on any supported chain.
/// Uses the platform wallet (from PrivateKeyPath) to send tokens.
/// </summary>
public class Erc20PaymentProvider : IPaymentProvider
{
    private readonly ChainlinkSettings _settings;
    private readonly MultiChainSettings _multiChain;
    private readonly ILogger<Erc20PaymentProvider> _logger;

    public PaymentMethod Method => PaymentMethod.Erc20Usdc;
    public bool IsAvailable => !string.IsNullOrEmpty(_settings.PrivateKeyPath);

    public Erc20PaymentProvider(
        IOptions<ChainlinkSettings> settings,
        IOptions<MultiChainSettings> multiChain,
        ILogger<Erc20PaymentProvider> logger)
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

        var tokenAddress = request.TokenAddress;
        if (string.IsNullOrEmpty(tokenAddress))
            return Fail("Token address is required for ERC-20 payments");

        var account = LightningAgentMarketPlace.Chainlink.EthereumAccountProvider.CreateAccount(_settings.PrivateKeyPath);
        if (account is null)
            return Fail("Private key not configured");

        try
        {
            var web3 = new Web3(account, rpcUrl);
            var contract = web3.Eth.GetContract(TokenAddressRegistry.Erc20TransferAbi, tokenAddress);

            // Get token decimals
            var decimalsFunc = contract.GetFunction("decimals");
            var decimals = await decimalsFunc.CallAsync<byte>();

            // Convert USD amount to token amount (stablecoins are 1:1 USD)
            var amount = request.AmountUsd ?? (double)request.AmountSats / 100_000_000.0 * 70_000.0; // rough BTC conversion
            var tokenAmount = new BigInteger(amount * Math.Pow(10, decimals));

            var transferFunc = contract.GetFunction("transfer");
            var txHash = await transferFunc.SendTransactionAsync(
                account.Address,
                request.ReceiverAddress,
                tokenAmount);

            _logger.LogInformation(
                "ERC-20 payment sent: {Amount} tokens to {Receiver} on chain {ChainId}, tx={TxHash}",
                amount, request.ReceiverAddress, chainId, txHash);

            return new PaymentResult
            {
                Success = true,
                TransactionHash = txHash,
                SenderAddress = account.Address,
                AmountWei = tokenAmount.ToString(),
                PaymentType = PaymentType.Erc20,
                PaymentMethod = DetermineMethod(tokenAddress, chainId),
                ChainId = chainId,
                TokenAddress = tokenAddress
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ERC-20 payment failed on chain {ChainId}", chainId);
            return Fail(ex.Message);
        }
    }

    private string? GetRpcUrl(long chainId)
    {
        // Check primary chain
        if (!string.IsNullOrEmpty(_settings.EthereumRpcUrl))
        {
            // Primary chain - we'd need to detect its chain ID, but for simplicity
            // assume it matches if no secondary chain is configured
        }

        // Check secondary chains
        foreach (var (_, chain) in _multiChain.Chains)
        {
            if (chain.ChainId == chainId && !string.IsNullOrEmpty(chain.RpcUrl))
                return chain.RpcUrl;
        }

        // Fallback to primary
        return _settings.EthereumRpcUrl;
    }

    private static PaymentMethod DetermineMethod(string tokenAddress, long chainId)
    {
        var usdc = TokenAddressRegistry.GetUsdcAddress(chainId);
        if (usdc != null && string.Equals(tokenAddress, usdc, StringComparison.OrdinalIgnoreCase))
            return PaymentMethod.Erc20Usdc;

        var usdt = TokenAddressRegistry.GetUsdtAddress(chainId);
        if (usdt != null && string.Equals(tokenAddress, usdt, StringComparison.OrdinalIgnoreCase))
            return PaymentMethod.Erc20Usdt;

        var link = TokenAddressRegistry.GetLinkAddress(chainId);
        if (link != null && string.Equals(tokenAddress, link, StringComparison.OrdinalIgnoreCase))
            return PaymentMethod.Erc20Link;

        return PaymentMethod.Erc20Usdc;
    }

    private static PaymentResult Fail(string error) => new()
    {
        Success = false,
        Error = error,
        PaymentType = PaymentType.Erc20,
        PaymentMethod = PaymentMethod.Erc20Usdc
    };
}
