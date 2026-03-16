using System.Numerics;
using LightningAgentMarketPlace.Chainlink;
using LightningAgentMarketPlace.Core.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;
using Nethereum.Hex.HexTypes;
using Nethereum.Web3;

namespace LightningAgentMarketPlace.Engine.Services;

/// <summary>
/// Wraps on-chain calls to the ReputationLedger contract via Nethereum.
/// All calls are intended to be best-effort; callers should wrap invocations
/// in try-catch blocks so failures do not block the main workflow.
/// </summary>
public class OnChainReputationService
{
    private readonly ChainlinkSettings _settings;
    private readonly ILogger<OnChainReputationService> _logger;

    private const string ReputationLedgerAbi = @"[
        {""inputs"":[{""name"":""taskId"",""type"":""uint256""},{""name"":""milestoneId"",""type"":""uint256""},{""name"":""agentId"",""type"":""uint256""},{""name"":""score"",""type"":""uint256""},{""name"":""passed"",""type"":""bool""},{""name"":""proofHash"",""type"":""bytes32""}],""name"":""recordAttestation"",""outputs"":[],""stateMutability"":""nonpayable"",""type"":""function""},
        {""inputs"":[{""name"":""agentId"",""type"":""uint256""}],""name"":""getReputation"",""outputs"":[{""name"":""totalTasks"",""type"":""uint256""},{""name"":""completedTasks"",""type"":""uint256""},{""name"":""verificationPasses"",""type"":""uint256""},{""name"":""verificationFails"",""type"":""uint256""},{""name"":""reputationScore"",""type"":""uint256""},{""name"":""lastUpdated"",""type"":""uint256""}],""stateMutability"":""view"",""type"":""function""}
    ]";

    public OnChainReputationService(
        IOptions<ChainlinkSettings> settings,
        ILogger<OnChainReputationService> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    /// <summary>
    /// Calls recordAttestation on the ReputationLedger contract to record
    /// a verification result on-chain.
    /// </summary>
    public async Task<string> RecordAttestationAsync(
        BigInteger taskId,
        BigInteger milestoneId,
        BigInteger agentId,
        BigInteger score,
        bool passed,
        byte[] proofHash,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Recording on-chain attestation: taskId={TaskId}, milestoneId={MilestoneId}, agentId={AgentId}, score={Score}, passed={Passed}",
            taskId, milestoneId, agentId, score, passed);

        var account = EthereumAccountProvider.CreateAccount(_settings.PrivateKeyPath)
            ?? throw new InvalidOperationException("Private key is required for on-chain attestation recording.");

        var web3 = new Web3(account, _settings.EthereumRpcUrl);
        var contract = web3.Eth.GetContract(ReputationLedgerAbi, _settings.ReputationLedgerAddress);
        var function = contract.GetFunction("recordAttestation");

        var txHash = await function.SendTransactionAsync(
            account.Address,
            new HexBigInteger(300_000), // gas limit
            null,
            taskId,
            milestoneId,
            agentId,
            score,
            passed,
            proofHash);

        _logger.LogInformation("recordAttestation tx sent: {TxHash}", txHash);
        return txHash;
    }

    /// <summary>
    /// Calls getReputation on the ReputationLedger contract (view function).
    /// Returns the agent's on-chain reputation data.
    /// </summary>
    public async Task<OnChainReputation?> GetReputationAsync(
        BigInteger agentId,
        CancellationToken ct = default)
    {
        _logger.LogDebug("Querying on-chain reputation for agentId={AgentId}", agentId);

        var web3 = new Web3(_settings.EthereumRpcUrl);
        var contract = web3.Eth.GetContract(ReputationLedgerAbi, _settings.ReputationLedgerAddress);
        var function = contract.GetFunction("getReputation");

        var result = await function.CallDeserializingToObjectAsync<ReputationOutputDTO>(agentId);

        return new OnChainReputation
        {
            TotalTasks = result.TotalTasks,
            CompletedTasks = result.CompletedTasks,
            VerificationPasses = result.VerificationPasses,
            VerificationFails = result.VerificationFails,
            ReputationScore = result.ReputationScore,
            LastUpdated = result.LastUpdated
        };
    }
}

/// <summary>
/// Represents the on-chain reputation data returned by getReputation(uint256).
/// </summary>
public class OnChainReputation
{
    public BigInteger TotalTasks { get; set; }
    public BigInteger CompletedTasks { get; set; }
    public BigInteger VerificationPasses { get; set; }
    public BigInteger VerificationFails { get; set; }
    public BigInteger ReputationScore { get; set; }
    public BigInteger LastUpdated { get; set; }
}

/// <summary>
/// Nethereum DTO for the getReputation() view function multi-return.
/// </summary>
[FunctionOutput]
public class ReputationOutputDTO : IFunctionOutputDTO
{
    [Parameter("uint256", "totalTasks", 1)]
    public BigInteger TotalTasks { get; set; }

    [Parameter("uint256", "completedTasks", 2)]
    public BigInteger CompletedTasks { get; set; }

    [Parameter("uint256", "verificationPasses", 3)]
    public BigInteger VerificationPasses { get; set; }

    [Parameter("uint256", "verificationFails", 4)]
    public BigInteger VerificationFails { get; set; }

    [Parameter("uint256", "reputationScore", 5)]
    public BigInteger ReputationScore { get; set; }

    [Parameter("uint256", "lastUpdated", 6)]
    public BigInteger LastUpdated { get; set; }
}
