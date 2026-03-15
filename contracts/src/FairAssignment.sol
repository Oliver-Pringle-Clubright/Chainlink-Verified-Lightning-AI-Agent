// SPDX-License-Identifier: MIT
pragma solidity ^0.8.24;

import {VRFConsumerBaseV2Plus} from "@chainlink/contracts/src/v0.8/vrf/dev/VRFConsumerBaseV2Plus.sol";
import {VRFV2PlusClient} from "@chainlink/contracts/src/v0.8/vrf/dev/libraries/VRFV2PlusClient.sol";
import {ConfirmedOwner} from "@chainlink/contracts/src/v0.8/shared/access/ConfirmedOwner.sol";

/**
 * @title FairAssignment
 * @notice Provably random agent selection for the Lightning Agent Marketplace.
 *         Uses Chainlink VRF to randomly select an agent from qualified candidates,
 *         weighted by reputation score. Anyone can verify the selection was fair
 *         by checking the VRF proof on-chain.
 *
 * @dev Add this contract as a consumer on your Chainlink VRF subscription.
 */
contract FairAssignment is VRFConsumerBaseV2Plus {

    // ── Structs ──────────────────────────────────────────────────────────

    struct AssignmentRequest {
        uint256 taskId;
        uint256[] candidateAgentIds;
        uint256[] reputationWeights;    // higher = more likely to be selected
        bool     fulfilled;
        uint256  selectedAgentId;
        uint256  randomWord;
    }

    // ── State ────────────────────────────────────────────────────────────

    mapping(uint256 => AssignmentRequest) public requests;  // requestId → request
    mapping(uint256 => uint256) public taskAssignments;     // taskId → selected agentId

    uint256 public s_subscriptionId;
    bytes32 public s_keyHash;
    uint32  public s_callbackGasLimit = 100_000;
    uint16  public s_requestConfirmations = 3;

    address public marketplace;  // Only marketplace can request assignments

    // ── Events ───────────────────────────────────────────────────────────

    event AssignmentRequested(uint256 indexed requestId, uint256 indexed taskId, uint256 candidateCount);
    event AssignmentFulfilled(uint256 indexed requestId, uint256 indexed taskId, uint256 selectedAgentId, uint256 randomWord);

    // ── Errors ───────────────────────────────────────────────────────────

    error OnlyMarketplace();
    error NoCandidates();
    error ArrayLengthMismatch();
    error TaskAlreadyAssigned(uint256 taskId);

    // ── Constructor ──────────────────────────────────────────────────────

    constructor(
        address _vrfCoordinator,
        uint256 _subscriptionId,
        bytes32 _keyHash,
        address _marketplace
    ) VRFConsumerBaseV2Plus(_vrfCoordinator) {
        s_subscriptionId = _subscriptionId;
        s_keyHash = _keyHash;
        marketplace = _marketplace;
    }

    // ── Modifiers ────────────────────────────────────────────────────────

    modifier onlyMarketplace() {
        if (msg.sender != marketplace && msg.sender != owner()) revert OnlyMarketplace();
        _;
    }

    // ── Request Assignment ───────────────────────────────────────────────

    /**
     * @notice Request a provably random agent selection for a task.
     * @param taskId              Off-chain task identifier
     * @param candidateAgentIds   Array of qualified agent IDs
     * @param reputationWeights   Corresponding reputation weights (e.g., score * 100)
     *                            Higher weight = higher probability of selection
     * @return requestId          VRF request ID for tracking
     */
    function requestAssignment(
        uint256 taskId,
        uint256[] calldata candidateAgentIds,
        uint256[] calldata reputationWeights
    ) external onlyMarketplace returns (uint256 requestId) {
        if (candidateAgentIds.length == 0) revert NoCandidates();
        if (candidateAgentIds.length != reputationWeights.length) revert ArrayLengthMismatch();
        if (taskAssignments[taskId] != 0) revert TaskAlreadyAssigned(taskId);

        requestId = s_vrfCoordinator.requestRandomWords(
            VRFV2PlusClient.RandomWordsRequest({
                keyHash: s_keyHash,
                subId: s_subscriptionId,
                requestConfirmations: s_requestConfirmations,
                callbackGasLimit: s_callbackGasLimit,
                numWords: 1,
                extraArgs: VRFV2PlusClient._argsToBytes(
                    VRFV2PlusClient.ExtraArgsV1({nativePayment: false})
                )
            })
        );

        requests[requestId] = AssignmentRequest({
            taskId: taskId,
            candidateAgentIds: candidateAgentIds,
            reputationWeights: reputationWeights,
            fulfilled: false,
            selectedAgentId: 0,
            randomWord: 0
        });

        emit AssignmentRequested(requestId, taskId, candidateAgentIds.length);
    }

    // ── VRF Callback ─────────────────────────────────────────────────────

    function fulfillRandomWords(
        uint256 requestId,
        uint256[] calldata randomWords
    ) internal override {
        AssignmentRequest storage req = requests[requestId];
        require(!req.fulfilled, "Already fulfilled");

        uint256 randomWord = randomWords[0];
        req.randomWord = randomWord;
        req.fulfilled = true;

        // Weighted random selection
        uint256 totalWeight = 0;
        for (uint256 i = 0; i < req.reputationWeights.length; i++) {
            totalWeight += req.reputationWeights[i];
        }

        // If all weights are 0, fall back to uniform distribution
        uint256 selectedAgentId;
        if (totalWeight == 0) {
            selectedAgentId = req.candidateAgentIds[randomWord % req.candidateAgentIds.length];
        } else {
            uint256 roll = randomWord % totalWeight;
            uint256 cumulative = 0;
            for (uint256 i = 0; i < req.reputationWeights.length; i++) {
                cumulative += req.reputationWeights[i];
                if (roll < cumulative) {
                    selectedAgentId = req.candidateAgentIds[i];
                    break;
                }
            }
        }

        req.selectedAgentId = selectedAgentId;
        taskAssignments[req.taskId] = selectedAgentId;

        emit AssignmentFulfilled(requestId, req.taskId, selectedAgentId, randomWord);
    }

    // ── View Functions ───────────────────────────────────────────────────

    /**
     * @notice Get the assignment result for a task.
     * @return agentId  The selected agent (0 if not yet assigned)
     * @return fulfilled Whether VRF has responded
     */
    function getAssignment(uint256 taskId) external view returns (uint256 agentId, bool fulfilled) {
        agentId = taskAssignments[taskId];
        fulfilled = agentId != 0;
    }

    /**
     * @notice Get full details of a VRF request.
     */
    function getRequest(uint256 requestId) external view returns (
        uint256 taskId,
        bool fulfilled,
        uint256 selectedAgentId,
        uint256 randomWord,
        uint256 candidateCount
    ) {
        AssignmentRequest storage req = requests[requestId];
        return (req.taskId, req.fulfilled, req.selectedAgentId, req.randomWord, req.candidateAgentIds.length);
    }

    // ── Admin ────────────────────────────────────────────────────────────

    function setMarketplace(address _marketplace) external onlyOwner {
        marketplace = _marketplace;
    }

    function setKeyHash(bytes32 _keyHash) external onlyOwner {
        s_keyHash = _keyHash;
    }

    function setCallbackGasLimit(uint32 _gasLimit) external onlyOwner {
        s_callbackGasLimit = _gasLimit;
    }
}
