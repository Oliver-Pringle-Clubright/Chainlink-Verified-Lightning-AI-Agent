# CLAUDE.md

## Project Overview

**Lightning AI-Agent Marketplace** (v2.5.0) — A trustless AI agent marketplace where work is verified on-chain via Chainlink, payments are non-custodial via Lightning Network, and agent selection is provably fair via VRF. Built with .NET 10, Solidity 0.8.24, and SQLite.

## Repository Structure

```
├── src/                          # C# source (9 projects, ~340 files)
│   ├── LightningAgentMarketPlace.Api/       # REST API (24 controllers, SignalR, middleware)
│   ├── LightningAgentMarketPlace.Engine/     # Business logic, workflows, background jobs
│   ├── LightningAgentMarketPlace.Core/       # Models, interfaces, enums, configuration classes
│   ├── LightningAgentMarketPlace.Data/       # SQLite repositories (ADO.NET, 13 tables)
│   ├── LightningAgentMarketPlace.Chainlink/  # Smart contract clients (Nethereum)
│   ├── LightningAgentMarketPlace.Lightning/  # LND REST client (HODL invoices)
│   ├── LightningAgentMarketPlace.AI/         # Claude API, orchestration, fraud detection
│   ├── LightningAgentMarketPlace.Verification/ # Pluggable verification strategies
│   └── LightningAgentMarketPlace.Acp/        # Agent Commerce Protocol models
├── contracts/                    # Solidity smart contracts (Foundry)
│   └── src/
│       ├── VerifiedEscrow.sol        # Non-custodial escrow w/ Chainlink Functions
│       ├── FairAssignment.sol        # VRF-based agent selection
│       ├── ReputationLedger.sol      # On-chain reputation oracle
│       └── DeadlineEnforcer.sol      # Chainlink Automation deadlines
├── tests/                        # xUnit tests (unit + integration)
│   └── LightningAgentMarketPlace.Tests/
├── deploy/                       # Docker Compose, .env examples, deploy scripts
├── docs/                         # Design specs, technical docs, user guide
├── docker-compose.yml            # API + LND node containers
└── Directory.Build.props         # Shared .NET build properties
```

## Build & Run

```bash
# Build the solution
dotnet build LightningAgentMarketPlace.sln

# Run the API (default: http://localhost:5210)
dotnet run --project src/LightningAgentMarketPlace.Api

# Run tests
dotnet test tests/LightningAgentMarketPlace.Tests

# Docker
docker compose up --build
```

## Smart Contracts (Foundry)

```bash
cd contracts
forge build
forge test
```

Deployed on Sepolia:
- VerifiedEscrow: `0xBa837De8D406bbAceD6D9427a9B8859B72178361`
- FairAssignment: `0x1D5E81237019d3C734783283F045F7b2E817Ce12`
- ReputationLedger: `0x809a70748C658440186002D185b4f55740941f0B`
- DeadlineEnforcer: `0x44EbDB125843caaCb039061f737b562f29804646`

## Architecture

### Request Flow
1. Client creates task via REST API (`/api/tasks`)
2. TaskOrchestrator decomposes into milestones
3. AgentMatcher selects agent (VRF-weighted by reputation)
4. EscrowManager creates HODL invoice (Lightning) or on-chain escrow
5. Agent completes milestone, submits output
6. Verification pipeline runs (compile, schema, similarity, AI judge, or CLIP)
7. Chainlink Functions verifies on-chain
8. Escrow releases payment on pass; dispute flow on fail

### Key Services (Engine Layer)
- **TaskOrchestrator** — Full task lifecycle management
- **EscrowManager** — HODL invoice create/settle/cancel/refund
- **PaymentRouter** — Routes to Lightning, ERC-20, Native, or CCIP provider
- **PricingService** — Dynamic pricing via Chainlink price feeds + CoinGecko
- **ReputationService** — Score = 30% completion + 40% verification + 20% disputes + 10% activity
- **FraudDetector** — Sybil, recycled output, and anomaly detection
- **DisputeResolver** — AI-arbitrated dispute resolution

### Background Jobs (15 hosted services)
AgentWorkerService, InvoiceStatusPoller, EscrowExpiryChecker, PriceFeedRefresher, ChainlinkResponsePoller, VrfResponsePoller, CcipMessagePoller, StaleTaskReassigner, SpendLimitResetter, ParentTaskCompletionService, RecurringTaskService, DataCleanupService, VrfAuditSampler, EscrowRetryService, AutomatedBackupService.

### Verification Strategies
1. **CodeCompileVerification** — Compilation + unit test pass rate
2. **SchemaValidationVerification** — JSON schema compliance
3. **TextSimilarityVerification** — Semantic similarity scoring
4. **AiJudgeVerification** — Claude subjective assessment
5. **ClipScoreVerification** — Vision-language model scoring

## Configuration

Primary config: `src/LightningAgentMarketPlace.Api/appsettings.json`

Key sections: `Lightning`, `Chainlink` (testnet/mainnet addresses), `ClaudeAi`, `Escrow`, `Pricing`, `Verification`, `SpendLimits`, `PlatformFees`, `MultiChain`, `CoinGecko`, `JWT`.

Environment variables (see `deploy/.env.example`):
- `CLAUDE_API_KEY` — Claude API key
- `ETH_RPC_URL` — Ethereum RPC endpoint
- `API_KEY` — Platform API key
- `IS_TESTNET` — Toggle testnet mode

## Database

SQLite via ADO.NET (not EF Core). 13 tables:
Agents, AgentCapabilities, AgentReputation, Tasks, Milestones, Escrows, Payments, Verifications, Disputes, PriceCache, AuditLog, SpendLimits, VerificationStrategyConfig.

Migrations run via `MigrationRunner` on startup. Repositories in `src/LightningAgentMarketPlace.Data/Repositories/`.

## Code Conventions

- **Framework**: .NET 10, C# latest, nullable enabled, implicit usings
- **Naming**: PascalCase classes, `I{Name}` interfaces, descriptive enum values
- **Project naming**: `LightningAgentMarketPlace.{Layer}`
- **DI**: Constructor injection everywhere, registered in `Program.cs`
- **Async**: All I/O operations use async/await with CancellationToken
- **Data access**: Raw ADO.NET with parameterized queries (no ORM)
- **Error handling**: Custom exceptions, ExceptionHandlingMiddleware for API responses
- **Auth**: API key (HMAC-SHA256) + JWT bearer tokens
- **Security**: AES-256-GCM encryption for preimages, URL validation, webhook signing
- **Testing**: xUnit, unit tests in `Unit/`, integration tests in `Integration/`

## Multi-Chain Support

Supported networks: Ethereum, Arbitrum, Base, Polygon, BNB Chain, Optimism, Avalanche (testnet + mainnet for each). Chain config in `MultiChainSettings`.

## API

24 controllers under `src/LightningAgentMarketPlace.Api/Controllers/`. Key endpoints:
- `POST /api/tasks` — Create task
- `POST /api/agents` — Register agent
- `POST /api/milestones/{id}/submit` — Submit milestone output
- `GET /api/pricing/estimate` — Price estimation
- `POST /api/disputes` — Open dispute
- `GET /api/health` — Health check

Real-time events via SignalR hub at `/hubs/notifications`.

API docs available via Scalar at the root URL when running.

## Important Files

- `src/LightningAgentMarketPlace.Api/Program.cs` — App entry point, DI registration
- `src/LightningAgentMarketPlace.Engine/Services/TaskOrchestrator.cs` — Core workflow
- `src/LightningAgentMarketPlace.Engine/Services/EscrowManager.cs` — Payment escrow logic
- `src/LightningAgentMarketPlace.Data/Migrations/DatabaseInitializer.cs` — Schema creation
- `contracts/src/VerifiedEscrow.sol` — Main escrow contract
- `docker-compose.yml` — Container orchestration
- `deploy/.env.example` — Required environment variables

## Things to Watch Out For

- **Never commit secrets**: `.env` files, macaroon files, TLS certs, and API keys are gitignored
- **SQLite concurrency**: Single-writer limitation; background jobs coordinate via the task queue
- **Testnet by default**: `IsTest: true` in config; mainnet requires explicit configuration
- **HODL invoices**: Must be settled or canceled — never left hanging (EscrowExpiryChecker enforces this)
- **Chainlink subscriptions**: Functions, VRF, and Automation each require funded subscriptions
- **Contract addresses**: Testnet addresses are hardcoded in appsettings; update for new deployments
