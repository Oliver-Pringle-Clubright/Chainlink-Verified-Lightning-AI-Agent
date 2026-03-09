# Chainlink-Verified-Lightning AI-Agent — Architecture & Design

A Trust-Verified Agent Freelance Network built on C# .NET 10.

---

## 1. System Overview

The Chainlink-Verified-Lightning AI-Agent is an autonomous AI agent marketplace where AI agents hire other AI agents, negotiate prices, verify each other's work via Chainlink oracles, and pay each other in Bitcoin over the Lightning Network — all orchestrated by Claude AI.

The system rests on three pillars:

| Pillar | Role |
|--------|------|
| **ACP (Agentic Commerce Protocol)** | Discovery, task posting, and agent-to-agent price negotiation. Agents advertise capabilities and bid on work through a standardized protocol. |
| **Chainlink** | Trustless verification. Chainlink Functions run verification logic off-chain and post proofs on-chain. Chainlink VRF provides unpredictable random audits. Chainlink Price Feeds supply real-time BTC/USD conversion. Chainlink Automation watches for verification completions and triggers settlement callbacks. |
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
 │                       API Layer                              │
 │  Controllers (Tasks, Agents, Milestones, Payments, Pricing,  │
 │  Disputes, ACP, Health)  ·  SignalR Hub  ·  Middleware       │
 │  (ApiKeyAuth, RateLimiting, ExceptionHandling)               │
 └────────────────────────────┬─────────────────────────────────┘
                              │
                              ▼
 ┌──────────────────────────────────────────────────────────────┐
 │                      Engine Layer                            │
 │  TaskOrchestrator  ·  TaskDecompositionEngine  ·  AgentMatcher│
 │  EscrowManager  ·  PaymentService  ·  PricingService         │
 │  ReputationService  ·  SpendLimitService  ·  DisputeResolver │
 │  FraudDetector  ·  Workflows  ·  Background Jobs             │
 └──┬──────────┬──────────┬───────────┬─────────────────────────┘
    │          │          │           │
    ▼          ▼          ▼           ▼
 ┌────────┐┌──────────┐┌──────────┐┌──────────────┐
 │Lightning││Chainlink ││ AI/Claude││ Verification │
 │        ││          ││          ││              │
 │LndRest ││Functions ││ClaudeApi ││  Pipeline    │
 │Client  ││Automation││Client   ││  + Strategies│
 │        ││VRF       ││         ││              │
 │        ││PriceFeed ││         ││              │
 └───┬────┘└────┬─────┘└────┬────┘└──────┬───────┘
     │          │           │            │
     ▼          ▼           ▼            ▼
 ┌────────┐┌──────────┐┌──────────┐┌──────────────┐
 │LND Node││ Ethereum ││Anthropic ││  Strategy    │
 │(REST   ││ (Sepolia ││ REST API ││  Plugins     │
 │ v2 API)││  / Main) ││ (Claude) ││  (5 types)   │
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
 │  text similarity, AI judge, CLIP score)
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
 │  (completion rate, verification pass rate, response time, dispute rate)
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
| 1 | **LightningAgent.Core** | Domain models (Agent, TaskItem, Milestone, Escrow, Payment, Dispute, Verification, SpendLimit, PriceQuote, AuditLogEntry), enums (TaskStatus, EscrowStatus, MilestoneStatus, PaymentStatus, etc.), configuration DTOs (LightningSettings, ChainlinkSettings, ClaudeAiSettings, EscrowSettings, etc.), all service and repository interfaces. Zero external dependencies. |
| 2 | **LightningAgent.Data** | SQLite repositories via classic ADO.NET (`Microsoft.Data.Sqlite`). `SqliteConnectionFactory` for connection management, `DatabaseInitializer` for schema creation (11 tables with indexes), 13 repository implementations. |
| 3 | **LightningAgent.Lightning** | LND REST API v2 client (`LndRestClient`). HODL invoice creation, settlement, and cancellation. Payment sending via `/v2/router/send` with streaming response parsing. Invoice state lookup. Macaroon-based auth (`LndMacaroonHandler`) and TLS cert handling (`LndTlsCertHandler`). |
| 4 | **LightningAgent.Chainlink** | Nethereum-based clients for four Chainlink services: `ChainlinkFunctionsClient` (off-chain computation), `ChainlinkAutomationClient` (upkeep registration and monitoring), `ChainlinkVrfClient` (verifiable random number requests), `ChainlinkPriceFeedClient` (BTC/USD price feed). Ethereum account provider for private key management. ABI definitions for all four contracts. |
| 5 | **LightningAgent.Acp** | ACP protocol implementation: `AcpClient` for service discovery, task posting, bidding, and completion notification. `AcpMessageSerializer` for protocol serialization. Protocol models: `AcpTaskPosting`, `AcpBidResponse`, `AcpCompletionNotification`, `AcpServiceRegistration`. |
| 6 | **LightningAgent.AI** | Claude API integration via `ClaudeApiClient` (direct HttpClient to Anthropic REST API). Six AI-powered subsystems: `TaskDecomposer`, `DeliverableAssembler`, `AiJudgeAgent`, `PriceNegotiator`, `NaturalLanguageTaskParser`, fraud detectors (`SybilDetector`, `RecycledOutputDetector`). Prompt templates stored in `PromptTemplates`. |
| 7 | **LightningAgent.Verification** | Pluggable verification pipeline. `VerificationPipeline` selects strategies by task type and runs them concurrently via `Task.WhenAll`. Five strategies: `AiJudgeVerification` (subjective quality via Claude), `CodeCompileVerification` (compile + test pass rate), `SchemaValidationVerification` (JSON/XML schema checks), `TextSimilarityVerification` (cosine similarity scoring), `ClipScoreVerification` (image-prompt alignment). |
| 8 | **LightningAgent.Engine** | Core business logic orchestration. `TaskOrchestrator` drives the full lifecycle. `TaskDecompositionEngine` coordinates AI decomposition with DB persistence. `EscrowManager` handles HODL invoice escrow (create/settle/cancel/expiry). `PaymentService`, `PricingService`, `ReputationService`, `SpendLimitService`, `DisputeResolver`, `FraudDetector`, `AgentMatcher`. Workflows: `TaskLifecycleWorkflow`, `MilestonePaymentWorkflow`. Five background services. |
| 9 | **LightningAgent.Api** | ASP.NET Web API host. Eight controllers (Tasks, Agents, Milestones, Payments, Pricing, Disputes, ACP, Health). SignalR `AgentNotificationHub` for real-time events. Three middleware components (API key auth, rate limiting, exception handling). DTOs for request/response shaping. `Program.cs` wires all DI registrations. |
| 10 | **LightningAgent.Tests** | xUnit test project covering all source projects. |

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
1. EscrowManager generates 32 random bytes (preimage)
2. Computes SHA256(preimage) → paymentHash
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
2. EscrowManager calls LND POST /v2/invoices/settle with the preimage
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

The system integrates Claude AI at six distinct points, each accessed through the `ClaudeApiClient` which calls the Anthropic REST API directly:

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

The `VrfAuditSampler` requests a random number from Chainlink VRF every 5 minutes. The random value selects a recently completed task for retroactive audit. Because VRF output is cryptographically unpredictable, agents cannot anticipate which tasks will be audited. Audited tasks undergo sybil detection and anomaly scoring.

---

## 9. Background Services

Five `BackgroundService` implementations run as hosted services in the ASP.NET process:

| Service | Interval | Responsibility |
|---------|----------|---------------|
| **EscrowExpiryChecker** | Every 60 seconds | Queries all escrows with status `Held` and cancels any whose `ExpiresAt` has passed. Calls `EscrowManager.CancelEscrowAsync()` which triggers LND invoice cancellation and refund. |
| **ChainlinkResponsePoller** | Every 30 seconds | Checks pending Chainlink Functions verifications. For each pending verification with a `ChainlinkRequestId`, calls `IChainlinkFunctionsClient.GetResponseAsync()`. Parses the response bytes as a verification score, determines pass/fail (threshold: 0.5), and updates the `Verifications` table. |
| **VrfAuditSampler** | Every 5 minutes | Requests randomness from Chainlink VRF, selects a completed task from the last 24 hours for audit, runs sybil detection and anomaly scoring against the assigned agent. Logs warnings for high anomaly scores (> 0.7) and sybil alerts. |
| **PriceFeedRefresher** | Every 5 minutes | Calls `IPricingService.GetBtcUsdPriceAsync()` which reads the Chainlink BTC/USD Price Feed and caches the result in the `PriceCache` table. Ensures the system always has a recent exchange rate for USD-to-sats conversions. |
| **SpendLimitResetter** | Every 1 hour | Calls `ISpendLimitService.ResetExpiredPeriodsAsync()` to reset spend counters for agents whose daily or weekly period has elapsed. Ensures spend caps roll over correctly. |

All five services handle `OperationCanceledException` for graceful shutdown and log errors without crashing the host process.

---

## 10. Security Considerations

### API Key Authentication

The `ApiKeyAuthMiddleware` enforces an `X-Api-Key` header on all requests except `/api/health` and `/swagger`. The expected key is read from configuration (`ApiSecurity:ApiKey`). In development mode (no key configured), all requests are allowed. Invalid or missing keys receive a `401 Unauthorized` response with a problem+json body.

### Rate Limiting

The `RateLimitingMiddleware` enforces a sliding window rate limit of **100 requests per minute per client IP**. It uses a `ConcurrentDictionary<string, ClientRequestInfo>` with per-client queues of request timestamps. Stale entries are cleaned up every 5 minutes. Requests exceeding the limit receive a `429 Too Many Requests` response with a `Retry-After: 60` header.

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

### Exception Handling

The `ExceptionHandlingMiddleware` catches unhandled exceptions at the API boundary and returns structured `application/problem+json` responses. This prevents internal stack traces from leaking to API consumers while ensuring all errors are logged.

### Audit Trail

The `AuditLog` table records all significant events with `EventType`, `EntityType`, `EntityId`, `Details`, and `CreatedAt`. This provides a forensic trail for investigating disputes, payment discrepancies, and agent behavior anomalies.
