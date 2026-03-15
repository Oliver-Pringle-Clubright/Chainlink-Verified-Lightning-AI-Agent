// SPDX-License-Identifier: MIT
pragma solidity ^0.8.24;

import {FunctionsClient} from "@chainlink/contracts/src/v0.8/functions/v1_0_0/FunctionsClient.sol";
import {FunctionsRequest} from "@chainlink/contracts/src/v0.8/functions/v1_0_0/libraries/FunctionsRequest.sol";
import {ConfirmedOwner} from "@chainlink/contracts/src/v0.8/shared/access/ConfirmedOwner.sol";
import {IERC20} from "@openzeppelin/contracts/token/ERC20/IERC20.sol";
import {SafeERC20} from "@openzeppelin/contracts/token/ERC20/utils/SafeERC20.sol";
import {ReentrancyGuard} from "@openzeppelin/contracts/utils/ReentrancyGuard.sol";

/**
 * @title VerifiedEscrow
 * @notice Non-custodial escrow for the Lightning Agent Marketplace.
 *         Clients deposit funds (ETH or ERC-20) when posting a task.
 *         Chainlink Functions verifies milestone completion by querying the
 *         marketplace API. Funds auto-release to the agent on pass, or
 *         auto-refund to the client on fail/timeout.
 *
 * @dev Add this contract as a consumer on your Chainlink Functions subscription.
 */
contract VerifiedEscrow is FunctionsClient, ConfirmedOwner, ReentrancyGuard {
    using FunctionsRequest for FunctionsRequest.Request;
    using SafeERC20 for IERC20;

    // ── Structs ──────────────────────────────────────────────────────────

    struct Escrow {
        address client;
        address agent;
        address token;          // address(0) = native ETH
        uint256 amount;
        uint256 taskId;
        uint256 milestoneId;
        uint64  deadline;       // unix timestamp; 0 = no deadline
        EscrowStatus status;
    }

    enum EscrowStatus {
        Active,
        Released,
        Refunded,
        Disputed
    }

    // ── State ────────────────────────────────────────────────────────────

    mapping(uint256 => Escrow) public escrows;
    uint256 public nextEscrowId;

    // Functions request → escrow mapping
    mapping(bytes32 => uint256) public requestToEscrow;

    // Chainlink Functions config
    uint64  public subscriptionId;
    bytes32 public donId;
    uint32  public callbackGasLimit = 300_000;
    string  public verificationSource;

    // Encrypted secrets reference for Functions (API URL + key)
    bytes public encryptedSecretsReference;

    // ── Events ───────────────────────────────────────────────────────────

    event EscrowCreated(uint256 indexed escrowId, address indexed client, address agent, uint256 amount, address token, uint256 taskId, uint256 milestoneId);
    event EscrowReleased(uint256 indexed escrowId, address indexed agent, uint256 amount);
    event EscrowRefunded(uint256 indexed escrowId, address indexed client, uint256 amount);
    event EscrowDisputed(uint256 indexed escrowId);
    event VerificationRequested(uint256 indexed escrowId, bytes32 requestId);
    event VerificationFulfilled(uint256 indexed escrowId, bytes32 requestId, bool passed);

    // ── Errors ───────────────────────────────────────────────────────────

    error EscrowNotActive(uint256 escrowId);
    error NotClient(uint256 escrowId);
    error NotAgent(uint256 escrowId);
    error DeadlineNotReached(uint256 escrowId);
    error InsufficientDeposit();
    error TransferFailed();

    // ── Constructor ──────────────────────────────────────────────────────

    constructor(
        address _functionsRouter,
        uint64  _subscriptionId,
        bytes32 _donId
    ) FunctionsClient(_functionsRouter) ConfirmedOwner(msg.sender) {
        subscriptionId = _subscriptionId;
        donId = _donId;

        // Default verification source — calls marketplace API to check milestone status
        verificationSource =
            "const taskId = args[0];"
            "const milestoneId = args[1];"
            "const resp = await Functions.makeHttpRequest({"
            "  url: `${secrets.apiUrl}/api/milestones/by-task/${taskId}`,"
            "  headers: { 'Accept': 'application/json' }"
            "});"
            "if (resp.error) return Functions.encodeUint256(0);"
            "const ms = resp.data.find(m => m.id == parseInt(milestoneId));"
            "if (!ms) return Functions.encodeUint256(0);"
            "return Functions.encodeUint256(ms.status === 'Passed' ? 1 : 0);";
    }

    // ── Client: Create Escrow ────────────────────────────────────────────

    /**
     * @notice Deposit native ETH into escrow for a task milestone.
     * @param agent     Address that receives funds on successful verification
     * @param taskId    Off-chain task identifier
     * @param milestoneId Off-chain milestone identifier
     * @param deadline  Unix timestamp after which client can reclaim funds (0 = no deadline)
     */
    function createEscrowETH(
        address agent,
        uint256 taskId,
        uint256 milestoneId,
        uint64  deadline
    ) external payable nonReentrant returns (uint256 escrowId) {
        if (msg.value == 0) revert InsufficientDeposit();

        escrowId = nextEscrowId++;
        escrows[escrowId] = Escrow({
            client: msg.sender,
            agent: agent,
            token: address(0),
            amount: msg.value,
            taskId: taskId,
            milestoneId: milestoneId,
            deadline: deadline,
            status: EscrowStatus.Active
        });

        emit EscrowCreated(escrowId, msg.sender, agent, msg.value, address(0), taskId, milestoneId);
    }

    /**
     * @notice Deposit ERC-20 tokens into escrow for a task milestone.
     * @dev Client must approve this contract first.
     */
    function createEscrowERC20(
        address agent,
        address token,
        uint256 amount,
        uint256 taskId,
        uint256 milestoneId,
        uint64  deadline
    ) external nonReentrant returns (uint256 escrowId) {
        if (amount == 0) revert InsufficientDeposit();

        IERC20(token).safeTransferFrom(msg.sender, address(this), amount);

        escrowId = nextEscrowId++;
        escrows[escrowId] = Escrow({
            client: msg.sender,
            agent: agent,
            token: token,
            amount: amount,
            taskId: taskId,
            milestoneId: milestoneId,
            deadline: deadline,
            status: EscrowStatus.Active
        });

        emit EscrowCreated(escrowId, msg.sender, agent, amount, token, taskId, milestoneId);
    }

    // ── Verification: Trigger via Chainlink Functions ────────────────────

    /**
     * @notice Request verification of a milestone via Chainlink Functions.
     *         Can be called by agent (to claim payment) or client (to check status).
     */
    function requestVerification(uint256 escrowId) external nonReentrant returns (bytes32 requestId) {
        Escrow storage e = escrows[escrowId];
        if (e.status != EscrowStatus.Active) revert EscrowNotActive(escrowId);

        FunctionsRequest.Request memory req;
        req.initializeRequestForInlineCode(verificationSource);

        string[] memory args = new string[](2);
        args[0] = _uint2str(e.taskId);
        args[1] = _uint2str(e.milestoneId);
        req.setArgs(args);

        if (encryptedSecretsReference.length > 0) {
            req.addSecretsReference(encryptedSecretsReference);
        }

        requestId = _sendRequest(req.encodeCBOR(), subscriptionId, callbackGasLimit, donId);
        requestToEscrow[requestId] = escrowId;

        emit VerificationRequested(escrowId, requestId);
    }

    /**
     * @notice Chainlink Functions callback. Releases or holds based on result.
     */
    function fulfillRequest(
        bytes32 requestId,
        bytes memory response,
        bytes memory /* err */
    ) internal override {
        uint256 escrowId = requestToEscrow[requestId];
        Escrow storage e = escrows[escrowId];

        if (e.status != EscrowStatus.Active) return;

        // Decode response: 1 = passed, 0 = failed
        bool passed = false;
        if (response.length >= 32) {
            uint256 result = abi.decode(response, (uint256));
            passed = (result == 1);
        }

        emit VerificationFulfilled(escrowId, requestId, passed);

        if (passed) {
            _releaseFunds(escrowId);
        }
        // If not passed, escrow stays Active — agent can retry or client can reclaim after deadline
    }

    // ── Client: Reclaim After Deadline ───────────────────────────────────

    /**
     * @notice Client reclaims funds after the deadline has passed.
     */
    function reclaimAfterDeadline(uint256 escrowId) external nonReentrant {
        Escrow storage e = escrows[escrowId];
        if (e.status != EscrowStatus.Active) revert EscrowNotActive(escrowId);
        if (msg.sender != e.client) revert NotClient(escrowId);
        if (e.deadline == 0 || block.timestamp < e.deadline) revert DeadlineNotReached(escrowId);

        _refundFunds(escrowId);
    }

    // ── Client: Dispute ──────────────────────────────────────────────────

    /**
     * @notice Client marks escrow as disputed. Funds are frozen until owner resolves.
     */
    function openDispute(uint256 escrowId) external {
        Escrow storage e = escrows[escrowId];
        if (e.status != EscrowStatus.Active) revert EscrowNotActive(escrowId);
        if (msg.sender != e.client) revert NotClient(escrowId);

        e.status = EscrowStatus.Disputed;
        emit EscrowDisputed(escrowId);
    }

    // ── Owner: Resolve Dispute ───────────────────────────────────────────

    /**
     * @notice Owner resolves a dispute by releasing to agent or refunding client.
     * @param releaseToAgent True = pay agent, False = refund client
     */
    function resolveDispute(uint256 escrowId, bool releaseToAgent) external onlyOwner nonReentrant {
        Escrow storage e = escrows[escrowId];
        require(e.status == EscrowStatus.Disputed, "Not disputed");

        e.status = EscrowStatus.Active; // temporarily reactivate for internal functions
        if (releaseToAgent) {
            _releaseFunds(escrowId);
        } else {
            _refundFunds(escrowId);
        }
    }

    // ── Admin ────────────────────────────────────────────────────────────

    function setSubscriptionId(uint64 _subId) external onlyOwner {
        subscriptionId = _subId;
    }

    function setDonId(bytes32 _donId) external onlyOwner {
        donId = _donId;
    }

    function setCallbackGasLimit(uint32 _gasLimit) external onlyOwner {
        callbackGasLimit = _gasLimit;
    }

    function setVerificationSource(string calldata _source) external onlyOwner {
        verificationSource = _source;
    }

    function setEncryptedSecretsReference(bytes calldata _ref) external onlyOwner {
        encryptedSecretsReference = _ref;
    }

    // ── Internal ─────────────────────────────────────────────────────────

    function _releaseFunds(uint256 escrowId) internal {
        Escrow storage e = escrows[escrowId];
        e.status = EscrowStatus.Released;
        uint256 amount = e.amount;

        if (e.token == address(0)) {
            (bool ok, ) = payable(e.agent).call{value: amount}("");
            if (!ok) revert TransferFailed();
        } else {
            IERC20(e.token).safeTransfer(e.agent, amount);
        }

        emit EscrowReleased(escrowId, e.agent, amount);
    }

    function _refundFunds(uint256 escrowId) internal {
        Escrow storage e = escrows[escrowId];
        e.status = EscrowStatus.Refunded;
        uint256 amount = e.amount;

        if (e.token == address(0)) {
            (bool ok, ) = payable(e.client).call{value: amount}("");
            if (!ok) revert TransferFailed();
        } else {
            IERC20(e.token).safeTransfer(e.client, amount);
        }

        emit EscrowRefunded(escrowId, e.client, amount);
    }

    function _uint2str(uint256 value) internal pure returns (string memory) {
        if (value == 0) return "0";
        uint256 temp = value;
        uint256 digits;
        while (temp != 0) { digits++; temp /= 10; }
        bytes memory buffer = new bytes(digits);
        while (value != 0) {
            digits--;
            buffer[digits] = bytes1(uint8(48 + (value % 10)));
            value /= 10;
        }
        return string(buffer);
    }

    // Allow contract to receive ETH
    receive() external payable {}
}
