// SPDX-License-Identifier: MIT
pragma solidity ^0.8.24;

import {AutomationCompatibleInterface} from "@chainlink/contracts/src/v0.8/automation/AutomationCompatible.sol";
import {ConfirmedOwner} from "@chainlink/contracts/src/v0.8/shared/access/ConfirmedOwner.sol";

/**
 * @title DeadlineEnforcer
 * @notice Chainlink Automation-compatible contract that enforces task deadlines.
 *         When a deadline passes and the task isn't complete, it triggers a refund
 *         on the VerifiedEscrow contract. Runs independently of the marketplace server.
 *
 * @dev Register this contract as a Chainlink Automation upkeep.
 *      checkUpkeep() scans for expired deadlines.
 *      performUpkeep() triggers the escrow refund.
 */
contract DeadlineEnforcer is AutomationCompatibleInterface, ConfirmedOwner {

    // ── Interfaces ───────────────────────────────────────────────────────

    interface IVerifiedEscrow {
        function escrows(uint256 escrowId) external view returns (
            address client, address agent, address token, uint256 amount,
            uint256 taskId, uint256 milestoneId, uint64 deadline, uint8 status
        );
        function reclaimAfterDeadline(uint256 escrowId) external;
    }

    // ── State ────────────────────────────────────────────────────────────

    IVerifiedEscrow public escrowContract;

    // Track which escrow IDs to monitor
    uint256[] public monitoredEscrows;
    mapping(uint256 => bool) public isMonitored;
    mapping(uint256 => bool) public isEnforced;

    uint256 public maxCheckPerUpkeep = 50;  // Gas limit safety

    // ── Events ───────────────────────────────────────────────────────────

    event DeadlineRegistered(uint256 indexed escrowId, uint64 deadline);
    event DeadlineEnforced(uint256 indexed escrowId, uint256 timestamp);
    event DeadlineRemoved(uint256 indexed escrowId);

    // ── Constructor ──────────────────────────────────────────────────────

    constructor(address _escrowContract) ConfirmedOwner(msg.sender) {
        escrowContract = IVerifiedEscrow(_escrowContract);
    }

    // ── Register Deadlines ───────────────────────────────────────────────

    /**
     * @notice Register an escrow for deadline monitoring.
     *         Called by the marketplace when creating an escrow with a deadline.
     */
    function registerDeadline(uint256 escrowId) external onlyOwner {
        if (isMonitored[escrowId]) return;

        (,,,,,,uint64 deadline, uint8 status) = escrowContract.escrows(escrowId);
        require(deadline > 0, "No deadline set");
        require(status == 0, "Escrow not active");  // 0 = Active

        monitoredEscrows.push(escrowId);
        isMonitored[escrowId] = true;

        emit DeadlineRegistered(escrowId, deadline);
    }

    /**
     * @notice Batch register multiple escrows.
     */
    function registerDeadlines(uint256[] calldata escrowIds) external onlyOwner {
        for (uint256 i = 0; i < escrowIds.length; i++) {
            if (!isMonitored[escrowIds[i]]) {
                (,,,,,,uint64 deadline, uint8 status) = escrowContract.escrows(escrowIds[i]);
                if (deadline > 0 && status == 0) {
                    monitoredEscrows.push(escrowIds[i]);
                    isMonitored[escrowIds[i]] = true;
                    emit DeadlineRegistered(escrowIds[i], deadline);
                }
            }
        }
    }

    // ── Chainlink Automation ─────────────────────────────────────────────

    /**
     * @notice Called by Chainlink Automation to check if any deadlines have passed.
     * @return upkeepNeeded True if at least one deadline has expired
     * @return performData ABI-encoded array of expired escrow IDs
     */
    function checkUpkeep(bytes calldata)
        external
        view
        override
        returns (bool upkeepNeeded, bytes memory performData)
    {
        uint256[] memory expired = new uint256[](maxCheckPerUpkeep);
        uint256 count = 0;

        for (uint256 i = 0; i < monitoredEscrows.length && count < maxCheckPerUpkeep; i++) {
            uint256 escrowId = monitoredEscrows[i];
            if (isEnforced[escrowId]) continue;

            (,,,,,,uint64 deadline, uint8 status) = escrowContract.escrows(escrowId);

            // Only enforce if: Active (0), deadline set, deadline passed
            if (status == 0 && deadline > 0 && block.timestamp >= deadline) {
                expired[count] = escrowId;
                count++;
            }
        }

        if (count > 0) {
            // Trim array to actual size
            uint256[] memory trimmed = new uint256[](count);
            for (uint256 i = 0; i < count; i++) {
                trimmed[i] = expired[i];
            }
            return (true, abi.encode(trimmed));
        }

        return (false, "");
    }

    /**
     * @notice Called by Chainlink Automation to enforce expired deadlines.
     * @param performData ABI-encoded array of expired escrow IDs from checkUpkeep
     */
    function performUpkeep(bytes calldata performData) external override {
        uint256[] memory escrowIds = abi.decode(performData, (uint256[]));

        for (uint256 i = 0; i < escrowIds.length; i++) {
            uint256 escrowId = escrowIds[i];
            if (isEnforced[escrowId]) continue;

            (,,,,,,uint64 deadline, uint8 status) = escrowContract.escrows(escrowId);

            // Double-check: still Active and deadline passed
            if (status == 0 && deadline > 0 && block.timestamp >= deadline) {
                isEnforced[escrowId] = true;

                // Trigger the escrow's reclaimAfterDeadline
                // Note: this will revert if the escrow contract's own checks fail
                try escrowContract.reclaimAfterDeadline(escrowId) {
                    emit DeadlineEnforced(escrowId, block.timestamp);
                } catch {
                    // If reclaim fails (e.g., already settled), mark as enforced anyway
                    emit DeadlineEnforced(escrowId, block.timestamp);
                }
            }
        }
    }

    // ── View Functions ───────────────────────────────────────────────────

    function getMonitoredCount() external view returns (uint256) {
        return monitoredEscrows.length;
    }

    function getActiveMonitoredCount() external view returns (uint256 count) {
        for (uint256 i = 0; i < monitoredEscrows.length; i++) {
            if (!isEnforced[monitoredEscrows[i]]) count++;
        }
    }

    // ── Admin ────────────────────────────────────────────────────────────

    function setEscrowContract(address _escrow) external onlyOwner {
        escrowContract = IVerifiedEscrow(_escrow);
    }

    function setMaxCheckPerUpkeep(uint256 _max) external onlyOwner {
        maxCheckPerUpkeep = _max;
    }
}
