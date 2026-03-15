# Chainlink Consumer Contracts

Smart contracts that make the Lightning Agent Marketplace trustless.

## Contracts

### 1. VerifiedEscrow.sol (Functions Consumer)
**Non-custodial escrow.** Clients deposit ETH/ERC-20 tokens. Chainlink Functions verifies milestone completion by querying the marketplace API. Auto-releases on pass, stays locked on fail.

- **Add to**: Functions subscription
- **Key feature**: Funds held by contract, not platform

### 2. FairAssignment.sol (VRF Consumer)
**Provably random agent selection.** Uses Chainlink VRF to randomly select from qualified agents, weighted by reputation. Anyone can verify fairness on-chain.

- **Add to**: VRF subscription
- **Key feature**: Provably fair, auditable assignment

### 3. ReputationLedger.sol (Functions Consumer)
**On-chain reputation oracle.** Records verification attestations immutably. Syncs reputation from marketplace API via Functions. Portable agent identity.

- **Add to**: Functions subscription
- **Key feature**: Immutable, portable reputation

### 4. DeadlineEnforcer.sol (Automation Compatible)
**Automated deadline enforcement.** Monitors escrow deadlines via Chainlink Automation. Auto-refunds when deadlines pass, even if marketplace server is down.

- **Register as**: Chainlink Automation upkeep
- **Key feature**: Trustless deadline enforcement

## Deployment Order

1. Deploy `VerifiedEscrow` → add as Functions consumer
2. Deploy `FairAssignment` → add as VRF consumer
3. Deploy `ReputationLedger` → add as Functions consumer
4. Deploy `DeadlineEnforcer(escrowAddress)` → register as Automation upkeep

## Deployment (Foundry)

```bash
# Install Foundry
curl -L https://foundry.paradigm.xyz | bash
foundryup

# Install dependencies
forge install smartcontractkit/chainlink --no-commit
forge install OpenZeppelin/openzeppelin-contracts --no-commit

# Deploy to Sepolia
forge create src/VerifiedEscrow.sol:VerifiedEscrow \
  --rpc-url $SEPOLIA_RPC \
  --private-key $PRIVATE_KEY \
  --constructor-args $FUNCTIONS_ROUTER $SUBSCRIPTION_ID $DON_ID

# After deployment, add the contract address as a consumer:
# - Functions: https://functions.chain.link/sepolia/$SUB_ID → Add Consumer
# - VRF: https://vrf.chain.link/sepolia/$SUB_ID → Add Consumer
```

## Subscription Setup

### Functions (for VerifiedEscrow + ReputationLedger)
1. Go to https://functions.chain.link
2. Select your subscription (e.g., Sepolia #6384)
3. Click "Add Consumer"
4. Paste the deployed contract address
5. Upload encrypted secrets (API URL + key) via the Functions toolkit

### VRF (for FairAssignment)
1. Go to https://vrf.chain.link
2. Select your subscription
3. Click "Add Consumer"
4. Paste the FairAssignment contract address

### Automation (for DeadlineEnforcer)
1. Go to https://automation.chain.link
2. Click "Register New Upkeep"
3. Select "Custom logic"
4. Paste the DeadlineEnforcer contract address
5. Set gas limit (500,000 recommended)
6. Fund with LINK
