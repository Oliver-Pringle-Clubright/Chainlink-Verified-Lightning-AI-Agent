# Lightning_AI-Agent_Marketplace — Implementation Plan

## Overview
A Trust-Verified Agent Freelance Network where AI agents hire other AI agents, negotiate prices, verify each other's work via Chainlink, and pay each other in Bitcoin over Lightning Network — all orchestrated by Claude AI.

## Tech Stack
| Layer | Tech |
|-------|------|
| Backend | C# .NET 10 Web API |
| Database | SQLite via classic ADO.NET (`Microsoft.Data.Sqlite`) |
| Lightning | LND REST API via HttpClient |
| Chainlink | Nethereum (Functions, Automation, VRF, Price Feeds) |
| ACP | HTTP client/server for ACP-compatible endpoints |
| AI | Claude API via HttpClient (Anthropic REST API) |
| Real-time | SignalR for agent notifications |

---

## Solution Structure

```
Lightning_AI-Agent_Marketplace/
    LightningAgent.sln
    Directory.Build.props
    src/
        LightningAgent.Core/              # Models, Enums, Interfaces, Config DTOs
        LightningAgent.Data/              # SQLite repositories via ADO.NET
        LightningAgent.Lightning/         # LND REST API client (HODL invoices, streaming)
        LightningAgent.Chainlink/         # Chainlink Functions, Automation, VRF, Price Feeds
        LightningAgent.Acp/              # ACP protocol client/server
        LightningAgent.AI/               # Claude API (orchestrator, judge, negotiation, fraud)
        LightningAgent.Verification/     # Pluggable verification strategies
        LightningAgent.Engine/           # Task orchestration, escrow, payments, workflows
        LightningAgent.Api/              # ASP.NET Web API host, SignalR, controllers
    tests/
        LightningAgent.Tests/
```

### Project Dependency Graph
```
Api -> Engine, Data, Lightning, Chainlink, Acp, AI, Verification, Core
Engine -> Core, Data, Lightning, Chainlink, AI, Verification, Acp
Verification -> Core, AI
AI -> Core
Acp -> Core
Lightning -> Core
Chainlink -> Core
Data -> Core
Tests -> all src projects
```

---

## Feature List

### Core Flow
1. ACP task negotiation (receive tasks, match to agents)
2. Chainlink Functions verification of agent output
3. Lightning streaming payments on verified milestones
4. Escrow via HODL invoices (lock sats, settle on verify pass, refund on fail)

### Agent Reputation System
- Track completion rate, verification pass rate, response time, dispute rate
- Priority matching based on reputation score (0.0–1.0)
- Chainlink VRF for random audit sampling

### Task Decomposition Engine
- AI breaks large tasks into subtasks with individual milestones
- Each subtask has verification criteria and Lightning payment
- Parallel execution across multiple agents
- Auto-reassignment on subtask failure

### Escrow & Dispute Resolution
- HODL invoice escrow per milestone
- Auto-retry with different agent on verification fail
- Multi-sig dispute resolution with AI arbiter agent

### Dynamic Pricing via Chainlink
- Chainlink Price Feeds for real-time BTC/USD conversion
- Supply/demand pricing logic

### Verification Plugin System
- Pluggable `IVerificationStrategy` interface
- Code tasks: compile + test pass rate
- Data tasks: schema validation + statistical checks
- Text tasks: similarity scoring, hallucination detection
- Image tasks: CLIP score
- AI Judge: subjective quality assessment via Claude

### Agent Capability Registry
- Skills, task types, capacity, pricing in SQLite
- REST API for registration and discovery
- Reputation-weighted task routing engine

### Webhook & Event System
- SignalR hub for real-time notifications
- Events: TaskAssigned, MilestoneVerified, PaymentSent, DisputeOpened, EscrowSettled

### Rate Limiting & Spend Controls
- Per-agent daily/weekly spend caps
- Per-task maximum payout ceiling

### AI Features (Claude API)
1. **AI Orchestrator** — task decomposition, routing, assembly of final deliverable
2. **AI Judge Agent** — subjective quality verification, hallucination detection
3. **Autonomous Price Negotiation** — agents negotiate price/terms via AI
4. **Fraud & Quality Detection** — sybil detection, recycled output detection, anomaly detection
5. **Self-Improving Verification** — learns which strategies work, tunes thresholds
6. **Natural Language Task Posting** — plain English → structured ACP task spec

---

## Database Schema (SQLite)

### Agents
```sql
CREATE TABLE Agents (
    Id                  INTEGER PRIMARY KEY AUTOINCREMENT,
    ExternalId          TEXT NOT NULL UNIQUE,
    Name                TEXT NOT NULL,
    WalletPubkey        TEXT,
    Status              TEXT NOT NULL DEFAULT 'Active',
    DailySpendCapSats   INTEGER NOT NULL DEFAULT 0,
    WeeklySpendCapSats  INTEGER NOT NULL DEFAULT 0,
    CreatedAt           TEXT NOT NULL,
    UpdatedAt           TEXT NOT NULL
);
```

### AgentCapabilities
```sql
CREATE TABLE AgentCapabilities (
    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
    AgentId         INTEGER NOT NULL REFERENCES Agents(Id),
    SkillType       TEXT NOT NULL,
    TaskTypes       TEXT NOT NULL,
    MaxConcurrency  INTEGER NOT NULL DEFAULT 1,
    PriceSatsPerUnit INTEGER NOT NULL,
    AvgResponseSec  INTEGER,
    CreatedAt       TEXT NOT NULL
);
CREATE INDEX IX_AgentCapabilities_SkillType ON AgentCapabilities(SkillType);
```

### AgentReputation
```sql
CREATE TABLE AgentReputation (
    Id                  INTEGER PRIMARY KEY AUTOINCREMENT,
    AgentId             INTEGER NOT NULL UNIQUE REFERENCES Agents(Id),
    TotalTasks          INTEGER NOT NULL DEFAULT 0,
    CompletedTasks      INTEGER NOT NULL DEFAULT 0,
    VerificationPasses  INTEGER NOT NULL DEFAULT 0,
    VerificationFails   INTEGER NOT NULL DEFAULT 0,
    DisputeCount        INTEGER NOT NULL DEFAULT 0,
    AvgResponseTimeSec  REAL NOT NULL DEFAULT 0,
    ReputationScore     REAL NOT NULL DEFAULT 0.5,
    LastUpdated         TEXT NOT NULL
);
```

### Tasks
```sql
CREATE TABLE Tasks (
    Id                  INTEGER PRIMARY KEY AUTOINCREMENT,
    ExternalId          TEXT NOT NULL UNIQUE,
    ParentTaskId        INTEGER REFERENCES Tasks(Id),
    ClientId            TEXT NOT NULL,
    Title               TEXT NOT NULL,
    Description         TEXT NOT NULL,
    TaskType            TEXT NOT NULL,
    Status              TEXT NOT NULL DEFAULT 'Pending',
    AcpSpec             TEXT,
    VerificationCriteria TEXT,
    MaxPayoutSats       INTEGER NOT NULL,
    ActualPayoutSats    INTEGER DEFAULT 0,
    PriceUsd            REAL,
    AssignedAgentId     INTEGER REFERENCES Agents(Id),
    Priority            INTEGER NOT NULL DEFAULT 0,
    CreatedAt           TEXT NOT NULL,
    UpdatedAt           TEXT NOT NULL,
    CompletedAt         TEXT
);
CREATE INDEX IX_Tasks_Status ON Tasks(Status);
CREATE INDEX IX_Tasks_AssignedAgentId ON Tasks(AssignedAgentId);
CREATE INDEX IX_Tasks_ParentTaskId ON Tasks(ParentTaskId);
```

### Milestones
```sql
CREATE TABLE Milestones (
    Id                  INTEGER PRIMARY KEY AUTOINCREMENT,
    TaskId              INTEGER NOT NULL REFERENCES Tasks(Id),
    SequenceNumber      INTEGER NOT NULL,
    Title               TEXT NOT NULL,
    Description         TEXT,
    VerificationCriteria TEXT NOT NULL,
    PayoutSats          INTEGER NOT NULL,
    Status              TEXT NOT NULL DEFAULT 'Pending',
    VerificationResult  TEXT,
    InvoicePaymentHash  TEXT,
    CreatedAt           TEXT NOT NULL,
    VerifiedAt          TEXT,
    PaidAt              TEXT
);
CREATE INDEX IX_Milestones_TaskId ON Milestones(TaskId);
```

### Escrows
```sql
CREATE TABLE Escrows (
    Id                  INTEGER PRIMARY KEY AUTOINCREMENT,
    MilestoneId         INTEGER NOT NULL REFERENCES Milestones(Id),
    TaskId              INTEGER NOT NULL REFERENCES Tasks(Id),
    AmountSats          INTEGER NOT NULL,
    PaymentHash         TEXT NOT NULL UNIQUE,
    PaymentPreimage     TEXT,
    Status              TEXT NOT NULL DEFAULT 'Held',
    HodlInvoice         TEXT NOT NULL,
    CreatedAt           TEXT NOT NULL,
    SettledAt           TEXT,
    ExpiresAt           TEXT NOT NULL
);
CREATE INDEX IX_Escrows_PaymentHash ON Escrows(PaymentHash);
CREATE INDEX IX_Escrows_Status ON Escrows(Status);
```

### Payments
```sql
CREATE TABLE Payments (
    Id                  INTEGER PRIMARY KEY AUTOINCREMENT,
    EscrowId            INTEGER REFERENCES Escrows(Id),
    TaskId              INTEGER NOT NULL REFERENCES Tasks(Id),
    MilestoneId         INTEGER REFERENCES Milestones(Id),
    AgentId             INTEGER NOT NULL REFERENCES Agents(Id),
    AmountSats          INTEGER NOT NULL,
    AmountUsd           REAL,
    PaymentHash         TEXT,
    PaymentType         TEXT NOT NULL,
    Status              TEXT NOT NULL DEFAULT 'Pending',
    CreatedAt           TEXT NOT NULL,
    SettledAt           TEXT
);
```

### Verifications
```sql
CREATE TABLE Verifications (
    Id                  INTEGER PRIMARY KEY AUTOINCREMENT,
    MilestoneId         INTEGER NOT NULL REFERENCES Milestones(Id),
    TaskId              INTEGER NOT NULL REFERENCES Tasks(Id),
    StrategyType        TEXT NOT NULL,
    ChainlinkRequestId  TEXT,
    ChainlinkTxHash     TEXT,
    InputHash           TEXT,
    Score               REAL,
    Passed              INTEGER NOT NULL DEFAULT 0,
    Details             TEXT,
    CreatedAt           TEXT NOT NULL,
    CompletedAt         TEXT
);
CREATE INDEX IX_Verifications_MilestoneId ON Verifications(MilestoneId);
```

### Disputes
```sql
CREATE TABLE Disputes (
    Id                  INTEGER PRIMARY KEY AUTOINCREMENT,
    TaskId              INTEGER NOT NULL REFERENCES Tasks(Id),
    MilestoneId         INTEGER REFERENCES Milestones(Id),
    InitiatedBy         TEXT NOT NULL,
    InitiatorId         TEXT NOT NULL,
    Reason              TEXT NOT NULL,
    Status              TEXT NOT NULL DEFAULT 'Open',
    Resolution          TEXT,
    ArbiterAgentId      INTEGER REFERENCES Agents(Id),
    AmountDisputedSats  INTEGER NOT NULL,
    CreatedAt           TEXT NOT NULL,
    ResolvedAt          TEXT
);
```

### PriceCache
```sql
CREATE TABLE PriceCache (
    Id                  INTEGER PRIMARY KEY AUTOINCREMENT,
    Pair                TEXT NOT NULL,
    PriceUsd            REAL NOT NULL,
    Source              TEXT NOT NULL DEFAULT 'ChainlinkPriceFeed',
    FetchedAt           TEXT NOT NULL
);
CREATE INDEX IX_PriceCache_Pair_FetchedAt ON PriceCache(Pair, FetchedAt);
```

### AuditLog
```sql
CREATE TABLE AuditLog (
    Id                  INTEGER PRIMARY KEY AUTOINCREMENT,
    EventType           TEXT NOT NULL,
    EntityType          TEXT NOT NULL,
    EntityId            INTEGER NOT NULL,
    Details             TEXT,
    CreatedAt           TEXT NOT NULL
);
CREATE INDEX IX_AuditLog_EventType ON AuditLog(EventType);
CREATE INDEX IX_AuditLog_EntityType_EntityId ON AuditLog(EntityType, EntityId);
```

### SpendLimits
```sql
CREATE TABLE SpendLimits (
    Id                  INTEGER PRIMARY KEY AUTOINCREMENT,
    AgentId             INTEGER REFERENCES Agents(Id),
    TaskId              INTEGER REFERENCES Tasks(Id),
    LimitType           TEXT NOT NULL,
    MaxSats             INTEGER NOT NULL,
    CurrentSpentSats    INTEGER NOT NULL DEFAULT 0,
    PeriodStart         TEXT NOT NULL,
    PeriodEnd           TEXT NOT NULL
);
```

### VerificationStrategyConfig
```sql
CREATE TABLE VerificationStrategyConfig (
    Id                  INTEGER PRIMARY KEY AUTOINCREMENT,
    StrategyType        TEXT NOT NULL,
    ParameterName       TEXT NOT NULL,
    ParameterValue      TEXT NOT NULL,
    LearnedWeight       REAL DEFAULT 1.0,
    UpdatedAt           TEXT NOT NULL
);
CREATE UNIQUE INDEX IX_VerStrat_Type_Param ON VerificationStrategyConfig(StrategyType, ParameterName);
```

---

## API Endpoints

### Tasks
| Method | Route | Description |
|--------|-------|-------------|
| POST | `/api/tasks` | Create task (or from natural language) |
| GET | `/api/tasks` | List tasks with filtering |
| GET | `/api/tasks/{id}` | Get task details with milestones |
| POST | `/api/tasks/{id}/assign` | Manually assign agent |
| POST | `/api/tasks/{id}/cancel` | Cancel task |
| GET | `/api/tasks/{id}/subtasks` | Get subtasks |

### Agents
| Method | Route | Description |
|--------|-------|-------------|
| POST | `/api/agents/register` | Register agent with capabilities |
| GET | `/api/agents` | List agents |
| GET | `/api/agents/{id}` | Agent details with reputation |
| PUT | `/api/agents/{id}/capabilities` | Update capabilities |
| GET | `/api/agents/{id}/reputation` | Reputation breakdown |
| POST | `/api/agents/{id}/suspend` | Suspend agent |

### Milestones
| Method | Route | Description |
|--------|-------|-------------|
| GET | `/api/tasks/{taskId}/milestones` | List milestones |
| GET | `/api/milestones/{id}` | Milestone details |
| POST | `/api/milestones/{id}/submit` | Submit output for verification |
| GET | `/api/milestones/{id}/verification` | Verification status |

### Payments & Escrows
| Method | Route | Description |
|--------|-------|-------------|
| GET | `/api/payments` | List payments |
| GET | `/api/payments/{id}` | Payment details |
| GET | `/api/escrows` | List escrows |
| GET | `/api/escrows/{id}` | Escrow details |

### Disputes
| Method | Route | Description |
|--------|-------|-------------|
| POST | `/api/disputes` | Open dispute |
| GET | `/api/disputes/{id}` | Dispute details |
| POST | `/api/disputes/{id}/resolve` | Submit resolution |

### Pricing
| Method | Route | Description |
|--------|-------|-------------|
| GET | `/api/pricing/btcusd` | Current BTC/USD (Chainlink) |
| POST | `/api/pricing/estimate` | Estimate task cost |

### ACP Protocol
| Method | Route | Description |
|--------|-------|-------------|
| GET | `/api/acp/services` | Service discovery |
| POST | `/api/acp/tasks` | ACP task posting |
| POST | `/api/acp/negotiate` | Price negotiation |
| POST | `/api/acp/complete` | Completion notification |

### SignalR Hub
| Hub | Route |
|-----|-------|
| AgentNotificationHub | `/hubs/agent-notifications` |

Events: TaskAssigned, MilestoneVerified, PaymentSent, DisputeOpened, EscrowSettled, VerificationFailed

---

## Build Phases

### Phase 1: Foundation (Core + Data + API skeleton)
- All enums, models, config classes, interfaces
- SQLite connection factory, DatabaseInitializer, repositories
- Minimal API with Swagger, health check

### Phase 2: Lightning Integration
- LND REST client with HODL invoices
- EscrowManager, PaymentService
- Payments/Escrows controllers

### Phase 3: Agent Registry + Reputation
- ReputationService, AgentMatcher, SpendLimitService
- Full AgentsController

### Phase 4: Chainlink Integration
- Functions, Automation, VRF, PriceFeed clients
- PricingService, PricingController

### Phase 5: Verification Pipeline
- ClaudeApiClient
- All verification strategies (AiJudge, CodeCompile, SchemaValidation, TextSimilarity, ClipScore)
- Self-improving tuner

### Phase 6: Task Orchestration (Full Loop)
- AI TaskDecomposer, SubtaskRouter, DeliverableAssembler
- TaskOrchestrator, TaskLifecycleWorkflow
- Full TasksController, MilestonesController

### Phase 7: ACP Protocol + Disputes
- AcpClient, protocol models
- DisputeResolver, PriceNegotiator
- AcpController, DisputesController

### Phase 8: AI Fraud Detection + Background Jobs
- SybilDetector, RecycledOutputDetector, AnomalyDetector
- All background services (EscrowExpiryChecker, ChainlinkResponsePoller, VrfAuditSampler, PriceFeedRefresher)

### Phase 9: Real-time + Polish
- SignalR hub, event wiring
- Auth middleware, rate limiting, exception handling
- Unit + integration tests

---

## Core Orchestration Flow

```
1. Task arrives (ACP or REST API)
2. NaturalLanguageTaskParser (AI) → structured AcpTaskSpec
3. TaskDecompositionEngine (AI) → milestones
4. AgentMatcher → best agent (reputation + capability weighted)
5. EscrowManager → HODL invoice per milestone (sats locked)
6. Agent works → submits output
7. VerificationPipeline → runs applicable strategies
8. Chainlink Functions → submits verification proof on-chain
9. Chainlink Automation → detects pass, triggers callback
10. EscrowManager.Settle() → preimage revealed, sats flow
11. ReputationService → updates agent score
12. DeliverableAssembler (AI) → assembles final output
```
