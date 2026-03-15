// SPDX-License-Identifier: MIT
pragma solidity ^0.8.24;

import {FunctionsClient} from "@chainlink/contracts/src/v0.8/functions/v1_0_0/FunctionsClient.sol";
import {FunctionsRequest} from "@chainlink/contracts/src/v0.8/functions/v1_0_0/libraries/FunctionsRequest.sol";
import {ConfirmedOwner} from "@chainlink/contracts/src/v0.8/shared/access/ConfirmedOwner.sol";

/**
 * @title ReputationLedger
 * @notice On-chain reputation oracle for the Lightning Agent Marketplace.
 *         Stores immutable verification attestations and computes agent reputation
 *         scores that are portable across platforms.
 *
 *         Uses Chainlink Functions to sync reputation data from the marketplace API,
 *         creating a trustless, permanent record of agent performance.
 *
 * @dev Add this contract as a consumer on your Chainlink Functions subscription.
 */
contract ReputationLedger is FunctionsClient, ConfirmedOwner {
    using FunctionsRequest for FunctionsRequest.Request;

    // ── Structs ──────────────────────────────────────────────────────────

    struct AgentReputation {
        uint256 totalTasks;
        uint256 completedTasks;
        uint256 verificationPasses;
        uint256 verificationFails;
        uint256 disputeCount;
        uint256 reputationScore;    // scaled by 1000 (e.g., 850 = 0.850)
        uint256 lastUpdated;        // block timestamp
        bool    exists;
    }

    struct VerificationAttestation {
        uint256 taskId;
        uint256 milestoneId;
        uint256 agentId;
        uint256 score;              // scaled by 1000
        bool    passed;
        bytes32 proofHash;          // SHA256 of the agent's output
        uint256 timestamp;
    }

    // ── State ────────────────────────────────────────────────────────────

    mapping(uint256 => AgentReputation) public agentReputations;    // agentId → reputation
    VerificationAttestation[] public attestations;
    mapping(bytes32 => uint256) public requestToAgentId;

    uint64  public subscriptionId;
    bytes32 public donId;
    uint32  public callbackGasLimit = 300_000;
    bytes   public encryptedSecretsReference;

    string public syncSource;

    uint256 public totalAttestations;

    // ── Events ───────────────────────────────────────────────────────────

    event AttestationRecorded(uint256 indexed attestationId, uint256 indexed taskId, uint256 indexed agentId, bool passed, uint256 score);
    event ReputationUpdated(uint256 indexed agentId, uint256 reputationScore, uint256 totalTasks);
    event ReputationSyncRequested(uint256 indexed agentId, bytes32 requestId);

    // ── Constructor ──────────────────────────────────────────────────────

    constructor(
        address _functionsRouter,
        uint64  _subscriptionId,
        bytes32 _donId
    ) FunctionsClient(_functionsRouter) ConfirmedOwner(msg.sender) {
        subscriptionId = _subscriptionId;
        donId = _donId;

        // Functions source to fetch agent reputation from marketplace API
        syncSource =
            "const agentId = args[0];"
            "const resp = await Functions.makeHttpRequest({"
            "  url: `${secrets.apiUrl}/api/agents/${agentId}/reputation`,"
            "  headers: { 'Accept': 'application/json' }"
            "});"
            "if (resp.error) return Functions.encodeUint256(0);"
            "const r = resp.data;"
            "const packed = BigInt(r.totalTasks) * BigInt(10)**BigInt(24)"
            "  + BigInt(r.completedTasks) * BigInt(10)**BigInt(18)"
            "  + BigInt(r.verificationPasses) * BigInt(10)**BigInt(12)"
            "  + BigInt(r.verificationFails) * BigInt(10)**BigInt(6)"
            "  + BigInt(Math.round(r.reputationScore * 1000));"
            "return Functions.encodeUint256(packed);";
    }

    // ── Record Attestation (called by marketplace backend) ───────────────

    /**
     * @notice Record a verification attestation on-chain.
     *         Only callable by the contract owner (marketplace backend).
     */
    function recordAttestation(
        uint256 taskId,
        uint256 milestoneId,
        uint256 agentId,
        uint256 score,
        bool    passed,
        bytes32 proofHash
    ) external onlyOwner {
        uint256 attestationId = attestations.length;

        attestations.push(VerificationAttestation({
            taskId: taskId,
            milestoneId: milestoneId,
            agentId: agentId,
            score: score,
            passed: passed,
            proofHash: proofHash,
            timestamp: block.timestamp
        }));

        totalAttestations++;

        // Update on-chain reputation counters
        AgentReputation storage rep = agentReputations[agentId];
        if (!rep.exists) {
            rep.exists = true;
            rep.reputationScore = 500; // 0.500 default
        }

        rep.totalTasks++;
        if (passed) {
            rep.completedTasks++;
            rep.verificationPasses++;
        } else {
            rep.verificationFails++;
        }

        // Recalculate reputation score (same formula as off-chain)
        uint256 completionRate = rep.totalTasks > 0
            ? (rep.completedTasks * 1000) / rep.totalTasks
            : 500;

        uint256 totalVerifications = rep.verificationPasses + rep.verificationFails;
        uint256 verificationRate = totalVerifications > 0
            ? (rep.verificationPasses * 1000) / totalVerifications
            : 500;

        uint256 disputePenalty = rep.disputeCount > 10
            ? 0
            : 1000 - (rep.disputeCount * 100);

        // Weighted: 30% completion + 40% verification + 20% disputes + 10% activity
        rep.reputationScore = (completionRate * 30 + verificationRate * 40 + disputePenalty * 20 + 100 * 10) / 100;
        rep.lastUpdated = block.timestamp;

        emit AttestationRecorded(attestationId, taskId, agentId, passed, score);
        emit ReputationUpdated(agentId, rep.reputationScore, rep.totalTasks);
    }

    // ── Sync Reputation via Chainlink Functions ──────────────────────────

    /**
     * @notice Sync an agent's reputation from the marketplace API via Chainlink Functions.
     *         Useful for bootstrapping on-chain reputation from existing off-chain data.
     */
    function syncReputation(uint256 agentId) external returns (bytes32 requestId) {
        FunctionsRequest.Request memory req;
        req.initializeRequestForInlineCode(syncSource);

        string[] memory args = new string[](1);
        args[0] = _uint2str(agentId);
        req.setArgs(args);

        if (encryptedSecretsReference.length > 0) {
            req.addSecretsReference(encryptedSecretsReference);
        }

        requestId = _sendRequest(req.encodeCBOR(), subscriptionId, callbackGasLimit, donId);
        requestToAgentId[requestId] = agentId;

        emit ReputationSyncRequested(agentId, requestId);
    }

    function fulfillRequest(
        bytes32 requestId,
        bytes memory response,
        bytes memory /* err */
    ) internal override {
        uint256 agentId = requestToAgentId[requestId];
        if (agentId == 0 || response.length < 32) return;

        uint256 packed = abi.decode(response, (uint256));

        // Unpack: totalTasks(24dig) | completedTasks(18dig) | passes(12dig) | fails(6dig) | score(3dig)
        uint256 score = packed % 1000;
        packed /= 10 ** 6;
        uint256 fails = packed % (10 ** 6);
        packed /= 10 ** 6;
        uint256 passes = packed % (10 ** 6);
        packed /= 10 ** 6;
        uint256 completed = packed % (10 ** 6);
        packed /= 10 ** 6;
        uint256 total = packed;

        AgentReputation storage rep = agentReputations[agentId];
        rep.exists = true;
        rep.totalTasks = total;
        rep.completedTasks = completed;
        rep.verificationPasses = passes;
        rep.verificationFails = fails;
        rep.reputationScore = score;
        rep.lastUpdated = block.timestamp;

        emit ReputationUpdated(agentId, score, total);
    }

    // ── View Functions ───────────────────────────────────────────────────

    /**
     * @notice Get an agent's on-chain reputation.
     */
    function getReputation(uint256 agentId) external view returns (
        uint256 totalTasks,
        uint256 completedTasks,
        uint256 verificationPasses,
        uint256 verificationFails,
        uint256 reputationScore,
        uint256 lastUpdated
    ) {
        AgentReputation storage rep = agentReputations[agentId];
        return (rep.totalTasks, rep.completedTasks, rep.verificationPasses,
                rep.verificationFails, rep.reputationScore, rep.lastUpdated);
    }

    /**
     * @notice Get total number of on-chain attestations.
     */
    function getAttestationCount() external view returns (uint256) {
        return attestations.length;
    }

    /**
     * @notice Get a specific attestation by index.
     */
    function getAttestation(uint256 index) external view returns (
        uint256 taskId, uint256 milestoneId, uint256 agentId,
        uint256 score, bool passed, bytes32 proofHash, uint256 timestamp
    ) {
        VerificationAttestation storage a = attestations[index];
        return (a.taskId, a.milestoneId, a.agentId, a.score, a.passed, a.proofHash, a.timestamp);
    }

    // ── Admin ────────────────────────────────────────────────────────────

    function setSubscriptionId(uint64 _subId) external onlyOwner { subscriptionId = _subId; }
    function setDonId(bytes32 _donId) external onlyOwner { donId = _donId; }
    function setSyncSource(string calldata _source) external onlyOwner { syncSource = _source; }
    function setEncryptedSecretsReference(bytes calldata _ref) external onlyOwner { encryptedSecretsReference = _ref; }
    function incrementDisputeCount(uint256 agentId) external onlyOwner {
        agentReputations[agentId].disputeCount++;
    }

    function _uint2str(uint256 value) internal pure returns (string memory) {
        if (value == 0) return "0";
        uint256 temp = value;
        uint256 digits;
        while (temp != 0) { digits++; temp /= 10; }
        bytes memory buffer = new bytes(digits);
        while (value != 0) { digits--; buffer[digits] = bytes1(uint8(48 + (value % 10))); value /= 10; }
        return string(buffer);
    }
}
