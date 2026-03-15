using System.Numerics;
using LightningAgent.Chainlink;
using LightningAgent.Core.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;
using Nethereum.Hex.HexTypes;
using Nethereum.Web3;

namespace LightningAgent.Engine.Services;

/// <summary>
/// Wraps on-chain calls to the VerifiedEscrow contract via Nethereum.
/// All calls are intended to be best-effort; callers should wrap invocations
/// in try-catch blocks so failures do not block the main workflow.
/// </summary>
public class OnChainEscrowService
{
    private readonly ChainlinkSettings _settings;
    private readonly ILogger<OnChainEscrowService> _logger;

    private const string VerifiedEscrowAbi = @"[
        {""inputs"":[{""name"":""agent"",""type"":""address""},{""name"":""taskId"",""type"":""uint256""},{""name"":""milestoneId"",""type"":""uint256""},{""name"":""deadline"",""type"":""uint64""}],""name"":""createEscrowETH"",""outputs"":[{""name"":""escrowId"",""type"":""uint256""}],""stateMutability"":""payable"",""type"":""function""},
        {""inputs"":[{""name"":""agent"",""type"":""address""},{""name"":""token"",""type"":""address""},{""name"":""amount"",""type"":""uint256""},{""name"":""taskId"",""type"":""uint256""},{""name"":""milestoneId"",""type"":""uint256""},{""name"":""deadline"",""type"":""uint64""}],""name"":""createEscrowERC20"",""outputs"":[{""name"":""escrowId"",""type"":""uint256""}],""stateMutability"":""nonpayable"",""type"":""function""},
        {""inputs"":[{""name"":""escrowId"",""type"":""uint256""}],""name"":""requestVerification"",""outputs"":[{""name"":""requestId"",""type"":""bytes32""}],""stateMutability"":""nonpayable"",""type"":""function""},
        {""inputs"":[{""name"":""escrowId"",""type"":""uint256""}],""name"":""escrows"",""outputs"":[{""name"":""client"",""type"":""address""},{""name"":""agent"",""type"":""address""},{""name"":""token"",""type"":""address""},{""name"":""amount"",""type"":""uint256""},{""name"":""taskId"",""type"":""uint256""},{""name"":""milestoneId"",""type"":""uint256""},{""name"":""deadline"",""type"":""uint64""},{""name"":""status"",""type"":""uint8""}],""stateMutability"":""view"",""type"":""function""}
    ]";

    public OnChainEscrowService(
        IOptions<ChainlinkSettings> settings,
        ILogger<OnChainEscrowService> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    /// <summary>
    /// Calls createEscrowETH on the VerifiedEscrow contract.
    /// The <paramref name="amount"/> is sent as msg.value (in wei).
    /// </summary>
    /// <returns>The transaction hash of the on-chain escrow creation.</returns>
    public async Task<string> CreateEscrowETHAsync(
        string agent,
        BigInteger taskId,
        BigInteger milestoneId,
        BigInteger amount,
        ulong deadline,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Creating on-chain ETH escrow for agent={Agent}, taskId={TaskId}, milestoneId={MilestoneId}, amount={Amount}, deadline={Deadline}",
            agent, taskId, milestoneId, amount, deadline);

        var account = EthereumAccountProvider.CreateAccount(_settings.PrivateKeyPath)
            ?? throw new InvalidOperationException("Private key is required for on-chain escrow creation.");

        var web3 = new Web3(account, _settings.EthereumRpcUrl);
        var contract = web3.Eth.GetContract(VerifiedEscrowAbi, _settings.VerifiedEscrowAddress);
        var function = contract.GetFunction("createEscrowETH");

        var txHash = await function.SendTransactionAsync(
            account.Address,
            new HexBigInteger(300_000), // gas limit
            new HexBigInteger(amount),  // msg.value
            agent,
            taskId,
            milestoneId,
            deadline);

        _logger.LogInformation("createEscrowETH tx sent: {TxHash}", txHash);
        return txHash;
    }

    /// <summary>
    /// Calls createEscrowERC20 on the VerifiedEscrow contract.
    /// Caller must ensure the contract has sufficient ERC-20 allowance before calling this.
    /// </summary>
    /// <returns>The on-chain escrow ID.</returns>
    public async Task<string> CreateEscrowERC20Async(
        string agent,
        string token,
        BigInteger amount,
        BigInteger taskId,
        BigInteger milestoneId,
        ulong deadline,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Creating on-chain ERC20 escrow for agent={Agent}, token={Token}, amount={Amount}, taskId={TaskId}, milestoneId={MilestoneId}",
            agent, token, amount, taskId, milestoneId);

        var account = EthereumAccountProvider.CreateAccount(_settings.PrivateKeyPath)
            ?? throw new InvalidOperationException("Private key is required for on-chain escrow creation.");

        var web3 = new Web3(account, _settings.EthereumRpcUrl);
        var contract = web3.Eth.GetContract(VerifiedEscrowAbi, _settings.VerifiedEscrowAddress);
        var function = contract.GetFunction("createEscrowERC20");

        var txHash = await function.SendTransactionAsync(
            account.Address,
            new HexBigInteger(300_000), // gas limit
            null,                        // no ETH value
            agent,
            token,
            amount,
            taskId,
            milestoneId,
            deadline);

        _logger.LogInformation("createEscrowERC20 tx sent: {TxHash}", txHash);
        return txHash;
    }

    /// <summary>
    /// Calls requestVerification on the VerifiedEscrow contract.
    /// Returns the Chainlink Functions requestId (bytes32) as a hex string.
    /// </summary>
    public async Task<string> RequestVerificationAsync(
        BigInteger escrowId,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Requesting on-chain verification for escrowId={EscrowId}", escrowId);

        var account = EthereumAccountProvider.CreateAccount(_settings.PrivateKeyPath)
            ?? throw new InvalidOperationException("Private key is required for on-chain verification request.");

        var web3 = new Web3(account, _settings.EthereumRpcUrl);
        var contract = web3.Eth.GetContract(VerifiedEscrowAbi, _settings.VerifiedEscrowAddress);
        var function = contract.GetFunction("requestVerification");

        var txHash = await function.SendTransactionAsync(
            account.Address,
            new HexBigInteger(500_000), // gas limit
            null,
            escrowId);

        _logger.LogInformation("requestVerification tx sent: {TxHash}", txHash);
        return txHash;
    }

    /// <summary>
    /// Reads the escrows mapping for a given escrow ID (view function).
    /// </summary>
    public async Task<OnChainEscrowInfo?> GetEscrowAsync(
        BigInteger escrowId,
        CancellationToken ct = default)
    {
        _logger.LogDebug("Querying on-chain escrow info for escrowId={EscrowId}", escrowId);

        var web3 = new Web3(_settings.EthereumRpcUrl);
        var contract = web3.Eth.GetContract(VerifiedEscrowAbi, _settings.VerifiedEscrowAddress);
        var function = contract.GetFunction("escrows");

        var result = await function.CallDeserializingToObjectAsync<EscrowOutputDTO>(escrowId);

        return new OnChainEscrowInfo
        {
            Client = result.Client,
            Agent = result.Agent,
            Token = result.Token,
            Amount = result.Amount,
            TaskId = result.TaskId,
            MilestoneId = result.MilestoneId,
            Deadline = result.Deadline,
            Status = result.Status
        };
    }
}

/// <summary>
/// Represents the on-chain escrow data returned by the escrows(uint256) view function.
/// </summary>
public class OnChainEscrowInfo
{
    public string Client { get; set; } = "";
    public string Agent { get; set; } = "";
    public string Token { get; set; } = "";
    public BigInteger Amount { get; set; }
    public BigInteger TaskId { get; set; }
    public BigInteger MilestoneId { get; set; }
    public ulong Deadline { get; set; }
    public byte Status { get; set; }
}

/// <summary>
/// Nethereum DTO for the escrows() view function multi-return.
/// </summary>
[FunctionOutput]
public class EscrowOutputDTO : IFunctionOutputDTO
{
    [Parameter("address", "client", 1)]
    public string Client { get; set; } = "";

    [Parameter("address", "agent", 2)]
    public string Agent { get; set; } = "";

    [Parameter("address", "token", 3)]
    public string Token { get; set; } = "";

    [Parameter("uint256", "amount", 4)]
    public BigInteger Amount { get; set; }

    [Parameter("uint256", "taskId", 5)]
    public BigInteger TaskId { get; set; }

    [Parameter("uint256", "milestoneId", 6)]
    public BigInteger MilestoneId { get; set; }

    [Parameter("uint64", "deadline", 7)]
    public ulong Deadline { get; set; }

    [Parameter("uint8", "status", 8)]
    public byte Status { get; set; }
}
