// SPDX-License-Identifier: MIT
pragma solidity ^0.8.24;

import {Script, console} from "forge-std/Script.sol";
import {VerifiedEscrow} from "../src/VerifiedEscrow.sol";
import {FairAssignment} from "../src/FairAssignment.sol";
import {ReputationLedger} from "../src/ReputationLedger.sol";
import {DeadlineEnforcer} from "../src/DeadlineEnforcer.sol";

/**
 * @title Deploy
 * @notice Deploys all 4 Chainlink consumer contracts to Sepolia.
 *
 * Usage:
 *   forge script script/Deploy.s.sol:Deploy \
 *     --rpc-url $SEPOLIA_RPC_URL \
 *     --broadcast \
 *     --verify
 *
 * Required env vars:
 *   PRIVATE_KEY              - deployer wallet private key
 *   FUNCTIONS_ROUTER         - Chainlink Functions Router address
 *   FUNCTIONS_SUB_ID         - Functions subscription ID
 *   FUNCTIONS_DON_ID         - Functions DON ID (bytes32)
 *   VRF_COORDINATOR          - Chainlink VRF Coordinator address
 *   VRF_SUB_ID               - VRF subscription ID
 *   VRF_KEY_HASH             - VRF key hash (bytes32)
 */
contract Deploy is Script {
    function run() external {
        // Read config from environment
        uint256 deployerKey = vm.envUint("PRIVATE_KEY");
        address functionsRouter = vm.envAddress("FUNCTIONS_ROUTER");
        uint64 functionsSubId = uint64(vm.envUint("FUNCTIONS_SUB_ID"));
        bytes32 donId = vm.envBytes32("FUNCTIONS_DON_ID");
        address vrfCoordinator = vm.envAddress("VRF_COORDINATOR");
        uint256 vrfSubId = vm.envUint("VRF_SUB_ID");
        bytes32 vrfKeyHash = vm.envBytes32("VRF_KEY_HASH");

        address deployer = vm.addr(deployerKey);

        vm.startBroadcast(deployerKey);

        // 1. Deploy VerifiedEscrow (Functions Consumer)
        VerifiedEscrow escrow = new VerifiedEscrow(
            functionsRouter,
            functionsSubId,
            donId
        );
        console.log("VerifiedEscrow deployed at:", address(escrow));

        // 2. Deploy FairAssignment (VRF Consumer)
        FairAssignment assignment = new FairAssignment(
            vrfCoordinator,
            vrfSubId,
            vrfKeyHash,
            deployer  // marketplace = deployer initially
        );
        console.log("FairAssignment deployed at:", address(assignment));

        // 3. Deploy ReputationLedger (Functions Consumer)
        ReputationLedger reputation = new ReputationLedger(
            functionsRouter,
            functionsSubId,
            donId
        );
        console.log("ReputationLedger deployed at:", address(reputation));

        // 4. Deploy DeadlineEnforcer (Automation Compatible)
        DeadlineEnforcer enforcer = new DeadlineEnforcer(address(escrow));
        console.log("DeadlineEnforcer deployed at:", address(enforcer));

        vm.stopBroadcast();

        // Print summary
        console.log("\n=== DEPLOYMENT SUMMARY ===");
        console.log("Deployer:          ", deployer);
        console.log("VerifiedEscrow:    ", address(escrow));
        console.log("FairAssignment:    ", address(assignment));
        console.log("ReputationLedger:  ", address(reputation));
        console.log("DeadlineEnforcer:  ", address(enforcer));
        console.log("\nNext steps:");
        console.log("1. Add VerifiedEscrow as Functions consumer at functions.chain.link");
        console.log("2. Add ReputationLedger as Functions consumer at functions.chain.link");
        console.log("3. Add FairAssignment as VRF consumer at vrf.chain.link");
        console.log("4. Register DeadlineEnforcer as Automation upkeep at automation.chain.link");
    }
}
