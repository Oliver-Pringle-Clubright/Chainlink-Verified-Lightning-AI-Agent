# Chainlink Consumer Contracts

Smart contracts that make the Lightning Agent Marketplace trustless. Deployed on Ethereum Sepolia.

## Contracts

| Contract | Address | Service | Risk Score |
|----------|---------|---------|-----------|
| VerifiedEscrow | `0xBa837De8D406bbAceD6D9427a9B8859B72178361` | Functions | 26/100 (Low) |
| FairAssignment | `0x1D5E81237019d3C734783283F045F7b2E817Ce12` | VRF v2.5 | 34/100 (Low) |
| ReputationLedger | `0x809a70748C658440186002D185b4f55740941f0B` | Functions | 20/100 (Low) |
| DeadlineEnforcer | `0x44EbDB125843caaCb039061f737b562f29804646` | Automation | 50/100 (Medium) |

### VerifiedEscrow.sol (Functions Consumer)
Non-custodial escrow. Clients deposit ETH/ERC-20. Chainlink Functions verifies milestone completion by querying the marketplace API. Auto-releases on pass, stays locked on fail. Client can reclaim after deadline.

### FairAssignment.sol (VRF Consumer)
Provably random agent selection. Uses Chainlink VRF to select from qualified agents weighted by reputation. Anyone can verify fairness on-chain.

### ReputationLedger.sol (Functions Consumer)
On-chain reputation oracle. Records immutable verification attestations. Syncs reputation from API via Functions. Portable agent identity.

### DeadlineEnforcer.sol (Automation Compatible)
Monitors escrow deadlines. `checkUpkeep()` scans for expired deadlines, `performUpkeep()` triggers refunds. Runs independently of the marketplace server.

## Build

```bash
npm install
forge build
```

## Deploy

```bash
PRIVATE_KEY=0x... \
FUNCTIONS_ROUTER=0xb83E47C2bC239B3bf370bc41e1459A34b41238D0 \
FUNCTIONS_SUB_ID=6384 \
FUNCTIONS_DON_ID=0x66756e2d657468657265756d2d7365706f6c69612d3100000000000000000000 \
VRF_COORDINATOR=0x9DdfaCa8183c41ad55329BdeeD9F6A8d53168B1B \
VRF_SUB_ID=92398185006793675634258428503179375491424184369847930282861450503231237645082 \
VRF_KEY_HASH=0x474e34a077df58807dbe9c96d3c009b23b3c6d0cce433e59bbf5b34f823bc56c \
forge script script/Deploy.s.sol:Deploy --rpc-url $SEPOLIA_RPC --broadcast
```

## After Deployment

1. Add VerifiedEscrow + ReputationLedger as consumers at [functions.chain.link](https://functions.chain.link)
2. Add FairAssignment as consumer at [vrf.chain.link](https://vrf.chain.link)
3. Register DeadlineEnforcer as upkeep at [automation.chain.link](https://automation.chain.link) (Custom Logic trigger)
