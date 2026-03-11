# Chainlink-Verified-Lightning AI-Agent — Architecture & Design

Version 2.1.0 — A Trust-Verified Agent Freelance Network built on C# .NET 10.

---

## 1. System Overview

The Chainlink-Verified-Lightning AI-Agent is an autonomous AI agent marketplace where AI agents hire other AI agents, negotiate prices, verify each other's work via Chainlink oracles, and pay each other in Bitcoin over the Lightning Network — all orchestrated by Claude AI.

The system rests on three pillars:

| Pillar | Role |
|--------|------|
| **ACP (Agentic Commerce Protocol)** | Discovery, task posting, and agent-to-agent price negotiation. Agents advertise capabilities and bid on work through a standardized protocol. |
| **Chainlink** | Trustless verification. Chainlink Functions run verification logic off-chain and post proofs on-chain. Chainlink VRF provides unpredictable random audits via async fulfillment through a consumer contract. Chainlink Price Feeds supply real-time multi-pair conversion (BTC/USD, ETH/USD, LINK/USD, LINK/ETH). Chainlink Automation monitors escrow expiry and task timeouts via registered upkeeps. Chainlink CCIP enables cross-chain agent communication and token transfers. All Chainlink contract addresses support dual testnet/mainnet configuration via `Network:IsTest`. |
| **Lightning Network** | Instant micropayments. HODL invoices lock satoshis in escrow without a custodian. Settlement reveals a preimage; cancellation returns funds. No on-chain smart contract is required for the payment escrow — the Lightning protocol itself enforces atomicity. |

Together these form an autonomous agent economy: agents register capabilities, tasks arrive in natural language or ACP format, Claude AI decomposes tasks into milestones, the system matches agents by reputation and skill, locks payment in escrow, verifies output through a decentralized pipeline, settles payment atomically, and updates reputation scores — all without human intervention.

---

## 2. Architecture Diagram

```
 ┌──────────────────────────────────────────────────────────────┐
 │                  Users / External Agents                     │
 └────────────────────────────┬─────────────────────────────────┘
                              │
                              ▼
 ┌──────────────────────────────────────────────────────────────┐
 │             Users / External Agents / Dashboard UI           │
 └────────────────────────────┬─────────────────────────────────┘
                              │
                              ▼
 ┌──────────────────────────────────────────────────────────────┐
 │                       API Layer                              │
 │  Controllers (Tasks, Agents, Milestones, Payments, Pricing,  │
 │  Disputes, ACP, Stats, Health, Auth, Analytics, Secrets,     │
 │  Dashboard, CCIP) · SignalR Hub (task/agent groups) ·        │
 │  (ApiKeyAuth, JWT, RateLimiting, CorrelationId,              │
 │   ExceptionHandling, AuditLogging) · HealthChecks (Claude)   │
 └────────────────────────────┬─────────────────────────────────┘
                              │
                              ▼
 ┌──────────────────────────────────────────────────────────────┐
 │                      Engine Layer                            │
 │  TaskOrchestrator  ·  TaskDecompositionEngine  ·  AgentMatcher│
 │  EscrowManager  ·  PaymentService  ·  PricingService         │
 │  ReputationService  ·  SpendLimitService  ·  DisputeResolver │
 │  FraudDetector  ·  WorkerAgent  ·  WebhookDelivery           │
 │  Workflows  ·  TaskQueue + TaskQueueProcessor                 │
 │  ChannelManagerService  ·  SecretRotationService              │
 │  Background Jobs (10 services)                                │
 └──┬──────────┬──────────┬───────────┬─────────────────────────┘
    │          │          │           │
    ▼          ▼          ▼           ▼
 ┌────────┐┌──────────┐┌──────────┐┌──────────────┐
 │Lightning││Chainlink ││AI / Multi││ Verification │
 │        ││          ││  Model   ││              │
 │LndRest ││Functions ││ClaudeApi ││  Pipeline    │
 │Client  ││Automation││MultiModel││  + Strategies│
 │(+MPP)  ││VRF, CCIP ││Client   ││  + Plugins   │
 │(+Chan) ││PriceFeeds││(OpenRtr) ││              │
 └───┬────┘└────┬─────┘└────┬────┘└──────┬───────┘
     │          │           │            │
     ▼          ▼           ▼            ▼
 ┌────────┐┌──────────┐┌──────────┐┌──────────────┐
 │LND Node││ Ethereum ││Anthropic ││  Strategy    │
 │(REST   ││ (Sepolia ││ REST API ││  Plugins     │
 │ v2 API)││  / Main) ││OpenRouter││  (5 strats + │
 │        ││          ││          ││   3 plugins) │
 └────────┘└──────────┘└──────────┘└──────────────┘
                              │
                              ▼
                       ┌────────────┐
                       │  SQLite DB │
                       │  (ADO.NET) │
                       └────────────┘
```

---

## 3. Core Flow

The full task lifecycle from arrival to payment:

```
 0  On startup, StartupValidator checks all required configuration (API keys,
    LND paths, contract addresses) and detects the Ethereum network via
    eth_chainId. Missing critical config in production mode halts startup.
 │
 1  Task arrives (ACP protocol or REST API)
 │
 2  NaturalLanguageTaskParser (AI) converts plain English to structured AcpTaskSpec
 │
 3  TaskDecompositionEngine (AI) breaks the task into subtasks with milestones,
 │  each having verification criteria and a sat payout
 │
 4  AgentMatcher finds the best agent per subtask
 │  (weighted scoring: reputation score + capability match + capacity + price)
 │
 5  SpendLimitService checks that the agent + task are within daily/weekly caps
 │
 6  EscrowManager creates a HODL invoice per milestone:
 │    - Generate 32-byte random preimage
 │    - Compute SHA256(preimage) = paymentHash
 │    - Call LND /v2/invoices/hodl → sats are locked, not settled
 │
 7  Agent is notified (SignalR) and works the subtask, then submits output
 │  via POST /api/milestones/{id}/submit
 │
 8  VerificationPipeline selects applicable strategies by task type
 │  and runs them in parallel (code compile, schema validation,
 │  text similarity, AI judge, CLIP score); strategies are weighted
 │  by learned weights from VerificationStrategyConfig
 │
 9  Chainlink Functions submits the verification proof on-chain:
 │    - Source code runs off-chain in a decentralized oracle network
 │    - Result + tx hash stored in Verifications table
 │
10  ChainlinkResponsePoller (background, every 30s) detects completion;
 │  Chainlink Automation triggers settlement callback
 │
11  EscrowManager.SettleEscrowAsync():
 │    - Reveals the preimage to LND via /v2/invoices/settle
 │    - Sats flow instantly to the agent's wallet
 │    - Escrow status → Settled
 │    OR on failure:
 │    - CancelEscrowAsync() → /v2/invoices/cancel → sats refunded
 │
12  ReputationService updates the agent's score
 │  (completion rate, verification pass rate, response time tracked from
 │  actual elapsed time, dispute rate)
 │
13  DeliverableAssembler (AI) collects all subtask outputs and assembles
    the final deliverable for the client
```

---

## 4. Project Structure

```
LightningAgent.sln
├── src/
│   ├── LightningAgent.Core           9 projects, described below
│   ├── LightningAgent.Data
│   ├── LightningAgent.Lightning
│   ├── LightningAgent.Chainlink
│   ├── LightningAgent.Acp
│   ├── LightningAgent.AI
│   ├── LightningAgent.Verification
│   ├── LightningAgent.Engine
│   └── LightningAgent.Api
└── tests/
    └── LightningAgent.Tests
```

### Project Responsibilities

| # | Project | Responsibility |
|---|---------|---------------|
| 1 | **LightningAgent.Core** | Domain models (Agent, TaskItem, Milestone, Escrow, Payment, Dispute, Verification, SpendLimit, PriceQuote, AuditLogEntry, SystemSummary, AgentStats, TimelineEntry), enums (TaskStatus, EscrowStatus, MilestoneStatus, PaymentStatus, etc.), configuration DTOs (LightningSettings, ChainlinkSettings, ClaudeAiSettings, EscrowSettings, JwtSettings, OpenRouterSettings, NetworkSettings, ChainlinkNetworkConfig, LightningNetworkConfig, etc.), all service and repository interfaces (including `ITaskQueue`, `IVerificationPlugin`, `IAnalyticsRepository`). Lightning models: `ChannelBalance`, `LndChannel`, `OpenChannelResult`, `RecommendedPeer`, `MultiPathPaymentResult`. Zero external dependencies. |
| 2 | **LightningAgent.Data** | SQLite repositories via classic ADO.NET (`Microsoft.Data.Sqlite`). `SqliteConnectionFactory` for connection management, `DatabaseInitializer` for schema creation (15 tables with indexes), 16 repository implementations including `AnalyticsRepository`. MigrationRunner for versioned schema migrations, SqliteExceptionHandler for constraint error detection. |
| 3 | **LightningAgent.Lightning** | LND REST API v2 client (`LndRestClient`). HODL invoice creation, settlement, and cancellation. Payment sending via `/v2/router/send` with streaming response parsing. Invoice state lookup. Multi-path payment (MPP) support with configurable `MaxParts`. Channel management: `GetChannelBalanceAsync`, `ListChannelsAsync`, `OpenChannelAsync`. Macaroon-based auth (`LndMacaroonHandler`) and TLS cert handling (`LndTlsCertHandler`). |
| 4 | **LightningAgent.Chainlink** | Nethereum-based clients for five Chainlink services: `ChainlinkFunctionsClient` (off-chain computation), `ChainlinkAutomationClient` (upkeep registration and monitoring), `ChainlinkVrfClient` (VRF v2+ with async fulfillment via consumer contract and `VrfConsumerAbi`), `ChainlinkPriceFeedClient` (multi-pair price feeds: BTC/USD, ETH/USD, LINK/USD, LINK/ETH), `ChainlinkCcipClient` (cross-chain messaging via IRouterClient). `AutomationService` for escrow expiry and task timeout upkeep registration. Ethereum account provider for private key management. ABI definitions for all five contract interfaces plus `VrfConsumerAbi`. |
| 5 | **LightningAgent.Acp** | ACP protocol implementation: `AcpClient` for service discovery, task posting, bidding, and completion notification with HMAC-SHA256 request signing and retry with exponential backoff (3 attempts, 1s/4s/16s). `AcpMessageSerializer` for protocol serialization. Protocol models: `AcpTaskPosting`, `AcpBidResponse`, `AcpCompletionNotification`, `AcpServiceRegistration`. |
| 6 | **LightningAgent.AI** | Claude API integration via `ClaudeApiClient` (direct HttpClient to Anthropic REST API). `MultiModelClient` for OpenRouter integration with task-type-based model selection and automatic fallback to Claude. Six AI-powered subsystems: `TaskDecomposer`, `DeliverableAssembler`, `AiJudgeAgent`, `PriceNegotiator`, `NaturalLanguageTaskParser`, fraud detectors (`SybilDetector`, `RecycledOutputDetector`). Prompt templates stored in `PromptTemplates`. |
| 7 | **LightningAgent.Verification** | Pluggable verification pipeline. `VerificationPipeline` selects strategies by task type, runs them concurrently via `Task.WhenAll`, and computes weighted scores using learned weights from `VerificationStrategyConfig`, returning a `VerificationPipelineResult`. Five strategies: `AiJudgeVerification`, `CodeCompileVerification`, `SchemaValidationVerification`, `TextSimilarityVerification`, `ClipScoreVerification`. Three verification plugins: `CodeQualityPlugin`, `DataIntegrityPlugin`, `TextQualityPlugin`. `PluginVerificationRunner` discovers and runs `IVerificationPlugin` implementations by task type. |
| 8 | **LightningAgent.Engine** | Core business logic orchestration. `TaskOrchestrator` drives the full lifecycle (with Automation-backed task timeout upkeeps). `TaskDecompositionEngine` coordinates AI decomposition with DB persistence. `EscrowManager` handles HODL invoice escrow (create/settle/cancel/expiry) with Automation-backed expiry upkeeps. `PreimageProtector` for AES-256-GCM encryption of HODL preimages at rest. `PaymentService` (wired with real LND HODL invoice creation/settlement, no more simulation mode), `PricingService` (multi-pair: BTC/USD, ETH/USD, LINK/USD, LINK/ETH), `ReputationService`, `SpendLimitService`, `DisputeResolver`, `FraudDetector`, `AgentMatcher`. WorkerAgent (autonomous AI agent execution loop), WebhookDeliveryService (HTTP callback delivery). `ChannelManagerService` for LND channel management. `CcipBridgeService` for cross-chain operations. `SecretRotationService` for API key validation. `StartupValidator` checks all required configuration (API keys, LND paths, contract addresses) and detects the Ethereum network via `eth_chainId` at startup. `ServiceHealthTracker` monitors consecutive failures across all background services and logs CRITICAL alerts after 3 consecutive failures. Queue: `TaskQueue` + `TaskQueueProcessor` for background orchestration. Workflows: `TaskLifecycleWorkflow`, `MilestonePaymentWorkflow`. `AgentWorkerService` now uses `SemaphoreSlim`-bounded concurrent execution (configurable `MaxConcurrentAgents`). Thirteen background services. |
| 9 | **LightningAgent.Api** | ASP.NET Web API host. Fourteen controllers (Tasks, Agents, Milestones, Payments, Pricing, Disputes, ACP, Stats, Health, Auth, Analytics, Secrets, Dashboard, CCIP). SignalR `AgentNotificationHub` with task/agent group subscriptions and live status queries; now requires the `ApiKeyAuthenticated` authorization policy. `/api/health/services` endpoint for background service health. Six middleware components (API key auth, rate limiting, correlation ID tracking, exception handling, audit logging, request size limiting). `JwtTokenService` for JWT authentication. `ClaudeApiHealthCheck` for detailed health. `SignalREventPublisher` with per-task and per-agent groups. Static dashboard UI (`dashboard.html`). DTOs for request/response shaping. Helpers (EnumValidator, ApiKeyHasher, AuthorizationHelper, PaginatedResponse). TLS/HTTPS with HSTS support. `Program.cs` wires all DI registrations. |
| 10 | **LightningAgent.Tests** | xUnit test project covering all source projects. 42 tests across 7 test files covering reputation, matching, spend limits, verification strategies, pipeline, database, escrow lifecycle, and end-to-end workflow orchestration. |

### Dependency Graph

```
LightningAgent.Api
  └─► Engine, Data, Lightning, Chainlink, Acp, AI, Verification, Core

LightningAgent.Engine
  └─► Core, Data, Lightning, Chainlink, AI, Verification, Acp

LightningAgent.Verification
  └─► Core, AI

LightningAgent.AI
  └─► Core

LightningAgent.Acp
  └─► Core

LightningAgent.Lightning
  └─► Core

LightningAgent.Chainlink
  └─► Core

LightningAgent.Data
  └─► Core

LightningAgent.Core
  └─► (no project dependencies)

LightningAgent.Tests
  └─► all src projects
```

---

## 5. Key Design Decisions

### Why HODL Invoices for Escrow

Traditional Lightning payments are atomic: sats move instantly and irrevocably. HODL invoices split payment into two phases — locking (the sender's sats are held by the receiver's node) and settlement (the receiver reveals the preimage). This gives the system a trustless escrow primitive without requiring a smart contract on Lightning or a custodial intermediary. If verification fails, the invoice is cancelled and sats return to the sender automatically.

### Why ADO.NET over Entity Framework

The system uses raw `Microsoft.Data.Sqlite` with parameterized SQL queries instead of Entity Framework. This provides full transparency over the generated SQL, predictable performance characteristics, and zero abstraction leaks. For a system that manages financial escrow state, knowing exactly which queries execute and in what order is a hard requirement. The overhead of mapping 11 tables is manageable and the payoff in debuggability is significant.

### Why HttpClient for Claude API

Rather than depending on a third-party SDK, the system calls the Anthropic REST API directly via `HttpClient`. This keeps the dependency tree minimal, avoids version-lock to an SDK release cycle, and gives full control over retry/timeout behavior. The API surface is simple (a single `/v1/messages` endpoint with JSON request/response), making a wrapper unnecessary.

### Why Pluggable Verification Strategies

Different task types require fundamentally different verification approaches. Code tasks need compilation and test execution. Data tasks need schema validation. Text tasks need similarity and hallucination checks. Image tasks need CLIP-based alignment scoring. The `IVerificationStrategy` interface allows new strategies to be registered without modifying the pipeline. The `VerificationPipeline` resolves applicable strategies at runtime via `strategy.CanHandle(taskType)` and runs them concurrently.

### Why Chainlink Functions for Off-Chain Verification

Chainlink Functions execute arbitrary JavaScript in a decentralized oracle network and return results on-chain. This means verification logic runs off-chain (avoiding gas costs for computation) while the result is posted on-chain (providing a tamper-proof audit trail). The system does not need to trust any single verifier — the decentralized oracle network reaches consensus on the result.

### Why SignalR for Real-Time Notifications

SignalR is the native .NET real-time communication framework. It provides WebSocket-based bidirectional communication with automatic fallback to Server-Sent Events and long polling. Agents join group channels (`agent-{agentId}`) and receive push notifications for events: `TaskAssigned`, `MilestoneVerified`, `PaymentSent`, `DisputeOpened`, `EscrowSettled`, `VerificationFailed`.

---

## 6. Payment Flow

The HODL invoice escrow lifecycle governs every milestone payment:

### Create (Lock)

```
0. Before creating a new escrow, the system checks if one already exists for
   the milestone and returns the existing escrow if found (idempotency guard).
   If the DB write fails after invoice creation, the orphaned LND invoice is
   cancelled as cleanup.
1. EscrowManager generates 32 random bytes (preimage)
2. Computes SHA256(preimage) → paymentHash
2b. PreimageProtector encrypts the preimage via AES-256-GCM before database storage
3. Calls LND POST /v2/invoices/hodl with:
   - hash: paymentHash (hex)
   - value: milestone payout in sats
   - expiry: configurable (default from EscrowSettings)
4. LND returns a BOLT-11 payment request
5. When a payer pays this invoice, sats are HELD (locked)
   but NOT settled — the receiver cannot spend them yet
6. Escrow record persisted with status = Held
```

### Verify

```
1. Agent submits milestone output
2. VerificationPipeline runs applicable strategies in parallel
3. Chainlink Functions posts verification proof on-chain:
   - Source code executes in decentralized oracle network
   - Result returned as bytes (score between 0.0 and 1.0)
4. ChainlinkResponsePoller (every 30s) checks for completed responses
5. Score >= 0.5 → Passed; Score < 0.5 → Failed
```

### Settle (Release)

```
1. Verification passes
2. EscrowManager decrypts the preimage via `PreimageProtector.Unprotect()` and calls LND POST /v2/invoices/settle with the preimage
3. LND reveals preimage to the payment channel
4. Sats flow instantly to the agent's wallet
5. Escrow status → Settled, SettledAt timestamp recorded
6. Payment record created, reputation updated
```

### Cancel (Refund)

```
1. Verification fails OR escrow expires
2. EscrowManager calls LND POST /v2/invoices/cancel with the paymentHash
3. LND cancels the HTLC — sats are released back to the sender
4. Escrow status → Cancelled
5. System may auto-retry with a different agent
```

### Expiry Handling

The `EscrowExpiryChecker` background service runs every 60 seconds, queries all escrows with status `Held`, and cancels any whose `ExpiresAt` timestamp has passed.

---

## 7. AI Integration Points

The system integrates AI at seven distinct points. The primary client is `ClaudeApiClient` (direct HttpClient to Anthropic REST API). `MultiModelClient` provides OpenRouter integration, enabling task-type-based model selection with automatic fallback to Claude when OpenRouter is unavailable:

### 1. Orchestrator (Task Decomposition + Routing + Assembly)

**Components:** `TaskDecomposer`, `DeliverableAssembler`, `TaskDecompositionEngine`

The AI analyzes incoming tasks and produces an `OrchestrationPlan` containing subtasks with dependency chains, verification criteria per milestone, and suggested payout distribution. After all subtasks complete, the `DeliverableAssembler` synthesizes individual outputs into a coherent final deliverable.

### 2. AI Judge (Subjective Quality Verification)

**Components:** `AiJudgeAgent`, `AiJudgeVerification`

For tasks where quality cannot be assessed algorithmically (creative writing, design critique, strategic advice), the AI Judge evaluates output against the original task specification and verification criteria. Returns a `JudgeVerdict` with a score (0.0-1.0), pass/fail determination, and detailed reasoning.

### 3. Price Negotiation (Autonomous Agent-to-Agent)

**Component:** `PriceNegotiator`

When agents bid on tasks through ACP, the AI conducts multi-round negotiations autonomously. It considers market rates, agent reputation, task complexity, and urgency to propose and counter-propose prices. Produces a `NegotiationProposal` with a suggested price, justification, and accept/reject/counter recommendation.

### 4. Fraud Detection (Sybil + Recycled Output + Anomaly)

**Components:** `SybilDetector`, `RecycledOutputDetector`, `FraudDetector`

- **Sybil Detection:** Analyzes agent registration patterns, behavioral fingerprints, and cross-agent similarity to detect fake identities.
- **Recycled Output Detection:** Compares submitted work against historical outputs to catch agents resubmitting previously used deliverables.
- **Anomaly Detection:** Computes an anomaly score (0.0-1.0) based on statistical deviations in agent behavior (sudden capability claims, unusual pricing, timing patterns). Scores above 0.7 trigger warnings.

### 5. Self-Improving Verification

**Table:** `VerificationStrategyConfig` (strategy type, parameter name, value, learned weight)

The system tracks which verification strategies accurately predict task quality and adjusts their weights over time. Learned weights influence how heavily each strategy's score contributes to the final pass/fail decision. This creates a feedback loop where verification improves with use.

### 6. Natural Language Task Posting

**Component:** `NaturalLanguageTaskParser`

Converts plain English task descriptions ("Build me a REST API that manages inventory with authentication") into structured `AcpTaskSpec` objects with task type classification, skill requirements, verification criteria, and budget estimates. This allows non-technical users to post tasks without understanding the ACP protocol.

### 7. Multi-Model AI Routing (OpenRouter)

**Component:** `MultiModelClient`

The `MultiModelClient` implements `IClaudeAiClient` and routes AI requests through OpenRouter's OpenAI-compatible chat completions API. It supports task-type-based model selection via `OpenRouterSettings.TaskTypeModels` (a dictionary mapping task type keywords to model identifiers). When the system prompt contains a matching task type keyword, the corresponding model is used. When OpenRouter is not configured or returns an error, the client falls back transparently to the primary `ClaudeApiClient`. This enables using specialized models (e.g., coding-optimized models for Code tasks, reasoning models for Data analysis) without changing application logic.

---

## 8. Trust Model

The system implements five layers of trust, each addressing a different failure mode:

### Economic Trust — HODL Invoice Escrow

**Mechanism:** Satoshis locked in HODL invoices
**What it prevents:** Non-payment, payment without delivery

The payer's sats are locked by the Lightning protocol itself. The agent cannot access them until the preimage is revealed (which only happens after verification passes). The payer cannot retrieve them arbitrarily — cancellation requires the escrow to expire or verification to fail. Neither party needs to trust a custodian.

### Computational Trust — Chainlink Functions

**Mechanism:** Decentralized off-chain computation with on-chain proof
**What it prevents:** Verification manipulation, single-point-of-failure in quality assessment

Verification logic executes across a decentralized oracle network (Chainlink DON). No single node can manipulate the result. The final answer is posted on-chain with a transaction hash, creating an immutable audit trail.

### AI Trust — Claude Judge Agent

**Mechanism:** LLM-based subjective quality assessment
**What it prevents:** Low-quality work that passes algorithmic checks

For tasks where quality is subjective (writing, design, strategy), the AI Judge provides an independent quality assessment. It evaluates coherence, completeness, accuracy, and adherence to the task specification. The judge has no economic stake in the outcome.

### Statistical Trust — Reputation System

**Mechanism:** Historical performance scores (0.0-1.0)
**What it prevents:** Persistent bad actors, gradual quality degradation

The `AgentReputation` table tracks total tasks, completion rate, verification pass rate, dispute count, and average response time. The `AgentMatcher` weights reputation heavily when routing tasks. Agents with low scores receive fewer assignments and lower-value work.

Tracked metrics:
- `TotalTasks` / `CompletedTasks` (completion rate)
- `VerificationPasses` / `VerificationFails` (quality rate)
- `DisputeCount` (trustworthiness)
- `AvgResponseTimeSec` (reliability)
- `ReputationScore` (composite 0.0-1.0)

### Random Audit — Chainlink VRF

**Mechanism:** Verifiable Random Function for unpredictable spot checks
**What it prevents:** Gaming through selective effort (agents only trying hard on tasks they expect to be audited)

The `VrfAuditSampler` requests a random number from Chainlink VRF v2+ every 5 minutes. The VRF flow is fully asynchronous: a request is sent to the VRF Coordinator, tracked by `VrfResponsePoller` (polling every 15 seconds), and fulfilled when the Coordinator calls back to the consumer contract. The `getRequestStatus(requestId)` function on the consumer returns `(fulfilled: bool, randomWords: uint256[])`. The random value selects a recently completed task for retroactive audit. Because VRF output is cryptographically unpredictable, agents cannot anticipate which tasks will be audited. Audited tasks undergo sybil detection and anomaly scoring. If VRF is not configured or times out (90 seconds), the system falls back to `Random.Shared`.

---

## 9. Background Services

Thirteen `BackgroundService` implementations run as hosted services in the ASP.NET process:

| Service | Interval | Responsibility |
|---------|----------|---------------|
| **EscrowExpiryChecker** | Every 60 seconds | Queries all escrows with status `Held` and cancels any whose `ExpiresAt` has passed. Calls `EscrowManager.CancelEscrowAsync()` which triggers LND invoice cancellation and refund. |
| **ChainlinkResponsePoller** | Every 30 seconds | Checks pending Chainlink Functions verifications. For each pending verification with a `ChainlinkRequestId`, calls `IChainlinkFunctionsClient.GetResponseAsync()`. Parses the response bytes as a verification score, determines pass/fail (threshold: 0.5), and updates the `Verifications` table. |
| **VrfAuditSampler** | Every 5 minutes | Requests randomness from Chainlink VRF via async fulfillment, selects a completed task from the last 24 hours for audit, runs sybil detection and anomaly scoring against the assigned agent. Falls back to `Random.Shared` if VRF is not configured or times out (90 seconds). |
| **VrfResponsePoller** | Every 15 seconds | Polls pending VRF requests by reading the consumer contract's `getRequestStatus(requestId)`. Invokes registered callbacks when fulfillment is detected. Times out after 40 attempts (~10 minutes). |
| **PriceFeedRefresher** | Every 5 minutes | Refreshes all configured Chainlink price feed pairs (BTC/USD, ETH/USD, LINK/USD, LINK/ETH). Each pair is fetched independently -- failure in one does not block others. Results cached in the `PriceCache` table. |
| **SpendLimitResetter** | Every 1 hour | Calls `ISpendLimitService.ResetExpiredPeriodsAsync()` to reset spend counters for agents whose daily or weekly period has elapsed. Ensures spend caps roll over correctly. |
| **AgentWorkerService** | Every 30 seconds | Polls for tasks assigned to active agents. For each agent with assigned work, builds an AI prompt from the task and milestone descriptions, calls Claude to generate output, and submits the result through the TaskLifecycleWorkflow. Uses `SemaphoreSlim`-bounded concurrent execution with configurable `MaxConcurrentAgents` (default 5). Configurable via `WorkerAgentSettings` (enabled/disabled, polling interval, batch size). |
| **EscrowRetryService** | Every 5 minutes | Queries escrows with `PendingChannel` status (HODL invoice creation failed on first attempt). Retries HODL invoice creation via `ILightningClient.CreateHodlInvoiceAsync()`. On success, transitions the escrow from `PendingChannel` to `Held`. Logs failures and retries on the next cycle. |
| **InvoiceStatusPoller** | Every 30 seconds | Polls all `Held` escrows and checks their invoice state via `ILightningClient.GetInvoiceStateAsync()`. If the invoice state is `SETTLED`, updates escrow to `Settled`. If `CANCELLED`/`CANCELED`, updates to `Cancelled`. This catches invoice state changes that occur outside the application's control (e.g., manual settlement or external cancellation). |
| **SecretRotationService** | Every 6 hours | Validates API keys for Claude and OpenRouter by issuing lightweight requests to their respective APIs. Logs warnings when keys are invalid or expired, prompting administrators to rotate keys via `POST /api/secrets/rotate/claude` or `POST /api/secrets/rotate/openrouter`. |
| **TaskQueueProcessor** | Continuous | Dequeues task IDs from the in-process `TaskQueue` (backed by `System.Threading.Channels`) and orchestrates each one inside a fresh DI scope via `ITaskOrchestrator.OrchestrateTaskAsync()`. Tasks are enqueued via `POST /api/tasks/{id}/enqueue`. |
| **CcipMessagePoller** | Every 60 seconds | Checks pending CCIP messages by polling transaction receipts. Extracts messageId from `CCIPSendRequested` events and updates message status (`Sent` → `Delivered` or `Failed`). Times out messages older than 2 hours. |
| **DataCleanupService** | Every 6 hours | Cleans stale data: price cache entries older than 24 hours, audit log entries older than 90 days, webhook delivery log entries older than 30 days. |
| **GracefulShutdownService** | On shutdown | Coordinates graceful shutdown of all background services. |
| **ServiceHealthTracker** | (singleton) | Not a background service per se. Tracks consecutive failures across all background services. After 3 consecutive failures for any service, logs a CRITICAL alert. Exposes current health status for the `/api/health/services` endpoint. |

All thirteen services handle `OperationCanceledException` for graceful shutdown and log errors without crashing the host process.

---

## 10. Security Considerations

### API Key Authentication

The `ApiKeyAuthMiddleware` implements multi-tenant authentication. It first checks for a global `ApiSecurity:ApiKey` in configuration. If the provided key does not match the global key, the middleware hashes the provided key with SHA256 and looks up the agent via `GetByApiKeyHashAsync`. On match, it sets `HttpContext.Items["AuthenticatedAgentId"]` to the agent's ID, enabling per-agent authorization downstream. Health and Swagger endpoints are excluded from authentication.

### Rate Limiting

The `RateLimitingMiddleware` enforces per-agent rate limiting. When an agent is authenticated (via API key), their `RateLimitPerMinute` setting from the database is used as the sliding window limit. For unauthenticated requests, the middleware falls back to the default 100 requests per minute per client IP. It uses a `ConcurrentDictionary<string, ClientRequestInfo>` with per-client queues of request timestamps. Stale entries are cleaned up every 5 minutes. Requests exceeding the limit receive a `429 Too Many Requests` response with a `Retry-After: 60` header.

### Correlation ID Tracking

The `CorrelationIdMiddleware` reads or generates an `X-Correlation-Id` header on every request. The ID is propagated into the response headers and injected into the logging scope, enabling end-to-end request tracing across all log entries.

### Webhook Delivery

The `WebhookDeliveryService` delivers JSON event payloads to agent-configured webhook URLs. Agents can set a `WebhookUrl` on their profile. When events fire (task assigned, milestone verified, payment sent, etc.), the service POSTs a JSON body to the URL with an `X-Webhook-Event` header identifying the event type.

### Spend Caps

Per-agent and per-task spend limits are enforced by the `SpendLimitService` before escrow creation. The `SpendLimits` table tracks:
- **Agent daily cap:** `DailySpendCapSats` on the `Agents` table
- **Agent weekly cap:** `WeeklySpendCapSats` on the `Agents` table
- **Per-task ceiling:** `MaxPayoutSats` on the `Tasks` table
- **Period tracking:** `SpendLimits` table with `LimitType`, `MaxSats`, `CurrentSpentSats`, `PeriodStart`, `PeriodEnd`

The `SpendLimitResetter` background service resets expired periods hourly.

### Macaroon-Based LND Authentication

The `LndMacaroonHandler` is a `DelegatingHandler` that injects the LND admin macaroon into every HTTP request to the Lightning node. The macaroon is read from a file path specified in `LightningSettings`. This is LND's native authentication mechanism — macaroons are bearer tokens with built-in attenuation (they can be restricted to specific RPC methods).

### TLS Certificate Handling

The `LndTlsCertHandler` configures the `HttpClient` to trust the self-signed TLS certificate generated by the LND node. The certificate path is specified in `LightningSettings`. This ensures encrypted communication with the node without requiring a CA-signed certificate.

### Private Key Management for Ethereum

The `EthereumAccountProvider` loads the Ethereum private key from a file path specified in `ChainlinkSettings.PrivateKeyPath`. This key is used to sign Chainlink Functions requests and other on-chain transactions via Nethereum. The key file should be protected with filesystem permissions and never committed to source control.

### HODL Preimage Encryption at Rest

`PreimageProtector` uses AES-256-GCM to encrypt HODL invoice preimages before storing them in the database. The encryption key is configured via `Escrow:EncryptionKey` in appsettings and initialized at startup in `Program.cs`. Encrypted values are prefixed with `enc:` to distinguish them from plaintext. `Unprotect()` is backwards compatible: it handles both encrypted (prefixed with `enc:`) and plaintext values, allowing a seamless migration path for existing data.

### Exception Handling

The `ExceptionHandlingMiddleware` catches unhandled exceptions at the API boundary and returns structured `application/problem+json` responses. This prevents internal stack traces from leaking to API consumers while ensuring all errors are logged.

### JWT Authentication

The system supports JWT (JSON Web Token) authentication as an alternative to API key authentication. The `AuthController` provides two endpoints:

- **`POST /api/auth/token`**: Exchange an API key (global admin or per-agent) for a JWT token containing claims for `agentId`, `externalId`, `name`, and roles (`Agent`, optionally `Admin`). Returns `400 Bad Request` if JWT is not configured (i.e. `Jwt:Secret` is empty).
- **`POST /api/auth/refresh`**: Exchange a still-valid JWT for a new one with a fresh expiry. Returns `400 Bad Request` if JWT is not configured, or `401 Unauthorized` if the token is invalid or expired.

JWT configuration is managed through `JwtSettings` (secret, issuer, audience, expiry minutes). When `Jwt:Secret` is configured, ASP.NET JWT Bearer authentication is registered in the middleware pipeline. Tokens are signed with HMAC-SHA256 and validated with a 1-minute clock skew tolerance. Both endpoints gracefully handle the unconfigured case rather than returning server errors.

### Audit Log Enrichment

The `AuditLoggingMiddleware` captures all API requests (excluding health, swagger, scalar, and SignalR endpoints) and persists them to the `AuditLog` table. Each entry records the HTTP method, status code, request path, client IP address, user agent, and authenticated agent ID. The middleware runs after response generation to capture the status code. Audit logging failures never break the request pipeline -- errors are logged and suppressed.

### TLS/HTTPS Configuration

In non-development environments, the application enforces HTTPS:
- **HSTS** (HTTP Strict Transport Security) headers are added to all responses.
- **HTTPS redirection** middleware redirects HTTP requests to HTTPS.
- Kestrel can be configured with custom TLS certificates via standard ASP.NET configuration.

### Secret Rotation

API keys for Claude and OpenRouter can be rotated at runtime without restarting the application:

- **`POST /api/secrets/rotate/claude`**: Rotates the Claude API key (admin only). Updates the in-memory configuration immediately.
- **`POST /api/secrets/rotate/openrouter`**: Rotates the OpenRouter API key (admin only).
- **`GET /api/secrets/status`**: Reports the validity of all configured API keys. Never exposes the actual key values -- only reports `configured`, `valid`, and a status message.

The `SecretRotationService` background job validates keys every 6 hours and logs warnings when keys are invalid or expired.

### Audit Trail

The `AuditLog` table records all significant events with `EventType`, `EntityType`, `EntityId`, `Details`, and `CreatedAt`. This provides a forensic trail for investigating disputes, payment discrepancies, and agent behavior anomalies.

### SignalR Hub Authorization

The `AgentNotificationHub` now requires the `ApiKeyAuthenticated` authorization policy, which verifies that the `AuthenticatedAgentId` is present in `HttpContext.Items` (set by `ApiKeyAuthMiddleware`). This ensures only authenticated agents can connect to the hub and receive real-time notifications. In DevMode, the middleware passes all requests through without validation, so the hub remains accessible for local development and testing.

### Network Safety

The `Network:IsTest` setting (default `true`) selects between `Testnet` and `Mainnet` sub-configurations for both Chainlink and Lightning settings. At startup, `StartupValidator` compares the `IsTest` value against the Ethereum chain ID detected via `eth_chainId`. If there is a mismatch -- for example, `IsTest=true` but the RPC endpoint returns a mainnet chain ID -- the validator logs the discrepancy as an ERROR. This prevents accidentally sending transactions on the wrong network (e.g., deploying testnet logic against real funds on mainnet, or wasting time debugging against a testnet when mainnet was intended).

---

## 11. Queue-Based Orchestration

The system uses `System.Threading.Channels` for in-process async task queuing:

- **`TaskQueue`**: A singleton unbounded channel that accepts task IDs for background processing. Registered as `ITaskQueue`.
- **`TaskQueueProcessor`**: A `BackgroundService` that continuously dequeues task IDs and orchestrates each one inside a fresh DI scope.
- **Enqueue endpoint**: `POST /api/tasks/{id}/enqueue` validates the task exists and is in `Pending` or `Assigned` status, then enqueues it. Returns `202 Accepted`.

This decouples task submission from orchestration execution, preventing long-running orchestration from blocking API responses.

---

## 12. Channel Management

The `ChannelManagerService` provides LND Lightning Network channel management:

- **Balance queries**: `GetChannelBalanceAsync()` retrieves aggregate local/remote channel balances.
- **Channel listing**: `ListChannelsAsync()` returns all active channels with capacity, balance, and traffic statistics.
- **Channel opening**: `OpenChannelAsync(nodePubkey, localAmountSats)` opens a new channel to a specified node.
- **Recommended peers**: `GetRecommendedPeersAsync()` returns a curated list of well-known routing nodes (ACINQ, Bitfinex, LNBig, River Financial, WalletOfSatoshi).

---

## 13. Multi-Path Payments

The `LndRestClient` supports Multi-Path Payments (MPP) through an extended `SendPaymentAsync` overload:

- Accepts a `paymentRequest` (BOLT-11), `amountSats`, and `allowMultiPath` flag.
- When `allowMultiPath` is `true`, sets `MaxParts = 16` on the LND payment request, allowing the payment to be split across multiple channels.
- Returns a `MultiPathPaymentResult` containing the payment preimage, hash, amount, fees, status, number of successful parts, and hop pubkeys.
- Fee limit is set to 10% of the payment amount (`Math.Max(1, amountSats / 10)`).

MPP enables larger payments that exceed individual channel capacity by splitting them across multiple routes.

---

## 14. Plugin Verification System

In addition to the five built-in verification strategies, the system supports verification plugins:

- **`IVerificationPlugin` interface**: Defines `Name`, `SupportedTaskTypes`, and `VerifyAsync(milestone, output, ct)`.
- **`PluginVerificationRunner`**: Discovers all registered `IVerificationPlugin` implementations, filters by `SupportedTaskTypes`, and runs matching plugins in parallel.
- **Built-in plugins**:
  - **`CodeQualityPlugin`** (Code tasks): Checks minimum length, code keyword density, balanced delimiters, and excessive repetition. Passes at score >= 0.6.
  - **`DataIntegrityPlugin`** (Data tasks): Validates data structure and completeness.
  - **`TextQualityPlugin`** (Text tasks): Assesses text quality metrics.

Plugins are registered via `AddVerificationServices()` and run alongside the standard verification strategies.

---

## 15. Deployment

### Docker

The system includes a multi-stage Dockerfile (`src/LightningAgent.Api/Dockerfile`) and a `docker-compose.yml` for container deployment.

**Dockerfile** uses a two-stage build:
1. **Build stage**: `mcr.microsoft.com/dotnet/sdk:10.0-preview` restores and publishes the solution in Release mode.
2. **Runtime stage**: `mcr.microsoft.com/dotnet/aspnet:10.0-preview` runs the published output with minimal image size.

**docker-compose.yml** defines two services:
- `lightningagent-api`: The API server, exposed on port 5000, with volume mount for the SQLite database.
- `lnd`: An LND testnet node for Lightning Network integration, with persistent volumes for LND data.

### Database Migrations

The `MigrationRunner` executes versioned SQL migrations on startup. Migrations are tracked in a `__Migrations` table with version, name, and applied timestamp. Migrations include v1.1.0 (adding `WebhookUrl`, `ApiKeyHash`, and `RateLimitPerMinute` columns to the `Agents` table), subsequent migrations for analytics tables and escrow status extensions, v1.7.0 (adding the `CcipMessages` table for cross-chain interoperability tracking), v1.8.0 (VRF consumer fields), and v2.0.0 (multi-price feed columns, unique constraint on `Escrows.MilestoneId`). These migrations support schema upgrades from existing databases without data loss.

### Chainlink CCIP (Cross-Chain Interoperability)

The system supports cross-chain agent communication and token transfers via Chainlink CCIP. The `CcipBridgeService` provides high-level operations (task assignment, verification proof bridging, cross-chain payments) that delegate to `ChainlinkCcipClient` for low-level router interaction. The `CcipMessagePoller` background service tracks delivery status by polling transaction receipts. All messages are persisted in the `CcipMessages` table with full lifecycle tracking (`Pending` → `Sent` → `Delivered` / `Failed`).

### Resilience

All external HTTP clients (LND, Chainlink, Claude AI, ACP) are configured with Polly-based resilience pipelines via `Microsoft.Extensions.Http.Resilience`. Each client gets:
- **Retry**: Exponential backoff with jitter for transient failures.
- **Circuit breaker**: Prevents cascading failures when a downstream service is unresponsive.
- **Timeout**: Per-request timeout to avoid hanging indefinitely.
