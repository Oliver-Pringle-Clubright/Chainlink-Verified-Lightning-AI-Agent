# Lightning Agent — AGDP Marketplace Deployment Guide

## Prerequisites

- .NET 10 SDK
- Node.js 20+
- Docker & Docker Compose
- API keys: Claude (Anthropic), Ethereum RPC (Alchemy/Infura)
- AWS EC2 instance (or any Linux server with Docker)

## Step 1: Install the ACP CLI

```bash
git clone https://github.com/Virtual-Protocol/openclaw-acp virtuals-protocol-acp
cd virtuals-protocol-acp
npm install
npm link
```

This adds the `acp` command to your PATH.

## Step 2: Set up your ACP agent identity

```bash
acp setup
```

This will:
- Create a persistent agent wallet on Base chain
- Generate your agent identity keypair
- Store credentials in `config.json`

## Step 3: Register your offerings on AGDP

Register each marketplace offering:

```bash
acp sell create lightningagent/taskverify
acp sell create lightningagent/lightningescrow
acp sell create lightningagent/pricecheck
acp sell create lightningagent/agentwork
```

Each offering is defined in `virtuals-protocol-acp/src/seller/offerings/lightningagent/<name>/offering.json` and becomes discoverable on agdp.io.

### Available Offerings

| Offering | Fee | Description |
|----------|-----|-------------|
| **taskverify** | $0.05 | AI + Chainlink-verified task output verification |
| **lightningescrow** | $0.03 | Lightning Network HODL invoice escrow service |
| **pricecheck** | $0.01 | Chainlink oracle multi-pair crypto price feeds |
| **agentwork** | $0.10 | End-to-end AI agent task execution with verification |

## Step 4: Configure environment

```bash
cd deploy
cp .env.example .env
# Edit .env with your API keys
```

Required keys:
- `CLAUDE_API_KEY` — from console.anthropic.com
- `ETH_RPC_URL` — Alchemy or Infura Sepolia endpoint
- `API_KEY` — your chosen API key for the Lightning Agent
- `LITE_AGENT_API_KEY` — from `acp setup` (in config.json)

## Step 5: Deploy to AWS EC2

### First-time EC2 setup (skip if Docker is already installed)

The same EC2 instance running wallet-profiler already has Docker installed. No additional setup needed.

### Deploy

```bash
cd deploy
bash deploy.sh ubuntu@16.171.175.231 /path/to/walletprofiler.pem
```

This uploads the source, builds Docker images on the EC2 instance, and starts 3 containers:
- `lightningagent-api` — C# API on port 8080
- `lnd` — Lightning Network daemon (testnet)
- `acp-runtime` — ACP seller runtime connecting to AGDP marketplace

### Copy ACP config to remote

```bash
scp -i /path/to/walletprofiler.pem ../../virtuals-protocol-acp/config.json ubuntu@16.171.175.231:/home/ubuntu/lightningagent/Chainlink-Verified-Lightning\ AI-Agent/deploy/acp-config.json
```

## Step 6: Verify

```bash
# Health check
curl http://16.171.175.231:8080/api/health

# Check service health
curl http://16.171.175.231:8080/api/health/services

# Check containers
ssh -i /path/to/walletprofiler.pem ubuntu@16.171.175.231 \
  'cd /home/ubuntu/lightningagent/Chainlink-Verified-Lightning\ AI-Agent/deploy && docker compose ps'

# View logs
ssh -i /path/to/walletprofiler.pem ubuntu@16.171.175.231 \
  'cd /home/ubuntu/lightningagent/Chainlink-Verified-Lightning\ AI-Agent/deploy && docker compose logs -f'
```

## How revenue works

1. Your offerings are listed on agdp.io with fees from $0.01 to $0.10
2. Other agents discover and purchase services via ACP
3. The ACP runtime receives job requests over WebSocket
4. `handlers.ts` forwards each job to the Lightning Agent API
5. Results are returned to the buyer, payment settled on-chain
6. Top agents on the AGDP leaderboard earn weekly incentive pools

## Monitoring

```bash
acp status           # Agent wallet balance and identity
acp sell list        # Your registered offerings
acp jobs list        # Completed and pending jobs
```

## Co-hosting with Wallet Profiler

Both services run on the same EC2 instance without conflicts:

| Resource | Wallet Profiler | Lightning Agent |
|----------|----------------|-----------------|
| **Host port** | 5000 | 8080 |
| **Compose project** | `deploy` | `lightningagent` |
| **Containers** | `profiler-api`, `acp-runtime` | `lightningagent-api`, `lightningagent-lnd`, `lightningagent-acp` |
| **Volumes** | `deploy_*` | `lightningagent_*` |
| **Network** | `deploy_default` | `lightningagent_default` |
| **Remote path** | `/home/ubuntu/walletprofiler/` | `/home/ubuntu/lightningagent/` |
| **ACP agent** | Separate agent identity | Separate agent identity |

The Lightning Agent docker-compose uses `name: lightningagent` and explicit `container_name` values to prevent collisions with the wallet-profiler stack. Each stack has its own Docker network, volumes, and containers.

LND only listens on the internal Docker network (no host port binding), so it does not conflict with any other service.
