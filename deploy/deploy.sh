#!/bin/bash
# Deploy Lightning Agent to EC2
# Usage: bash deploy.sh <ec2-host> <key-file>
# Example: bash deploy.sh ubuntu@16.171.175.231 ~/.ssh/walletprofiler-key.pem

set -e

EC2_HOST="$1"
KEY_FILE="$2"
REMOTE_DIR="/home/ubuntu/lightningagent"

if [ -z "$EC2_HOST" ] || [ -z "$KEY_FILE" ]; then
  echo "Usage: bash deploy.sh <ec2-user@host> <key-file.pem>"
  echo "Example: bash deploy.sh ubuntu@16.171.175.231 ~/.ssh/walletprofiler-key.pem"
  exit 1
fi

SSH="ssh -i $KEY_FILE -o StrictHostKeyChecking=no"
SCP="scp -i $KEY_FILE -o StrictHostKeyChecking=no"

echo "=== Deploying Lightning Agent to $EC2_HOST ==="

# Create remote directory structure
$SSH $EC2_HOST "mkdir -p $REMOTE_DIR/{Chainlink-Verified-Lightning\ AI-Agent/{src/LightningAgent.Api,deploy},virtuals-protocol-acp}"

# Sync API source
echo "Uploading Lightning Agent API source..."
$SCP -r ../src $EC2_HOST:\"$REMOTE_DIR/Chainlink-Verified-Lightning AI-Agent/\"
$SCP ../LightningAgent.sln $EC2_HOST:\"$REMOTE_DIR/Chainlink-Verified-Lightning AI-Agent/\"
$SCP ../Directory.Build.props $EC2_HOST:\"$REMOTE_DIR/Chainlink-Verified-Lightning AI-Agent/\"

# Sync deploy files
echo "Uploading deploy config..."
$SCP docker-compose.yml Dockerfile.acp-runtime $EC2_HOST:\"$REMOTE_DIR/Chainlink-Verified-Lightning AI-Agent/deploy/\"

# Sync virtuals-protocol-acp (excluding node_modules and .git)
echo "Uploading ACP runtime..."
$SSH $EC2_HOST "mkdir -p $REMOTE_DIR/virtuals-protocol-acp/{src,bin}"
$SCP ../../virtuals-protocol-acp/package.json $EC2_HOST:$REMOTE_DIR/virtuals-protocol-acp/
$SCP ../../virtuals-protocol-acp/package-lock.json $EC2_HOST:$REMOTE_DIR/virtuals-protocol-acp/ 2>/dev/null || true
$SCP -r ../../virtuals-protocol-acp/src $EC2_HOST:$REMOTE_DIR/virtuals-protocol-acp/
$SCP -r ../../virtuals-protocol-acp/bin $EC2_HOST:$REMOTE_DIR/virtuals-protocol-acp/

# Check for .env
$SSH $EC2_HOST "test -f \"$REMOTE_DIR/Chainlink-Verified-Lightning AI-Agent/deploy/.env\"" || {
  echo ""
  echo "WARNING: No .env file found on remote."
  echo "Create $REMOTE_DIR/Chainlink-Verified-Lightning AI-Agent/deploy/.env with:"
  echo "  CLAUDE_API_KEY=your_key"
  echo "  ETH_RPC_URL=your_sepolia_rpc_url"
  echo "  API_KEY=your_api_key"
  echo "  LITE_AGENT_API_KEY=your_acp_key"
}

# Check for acp-config.json
$SSH $EC2_HOST "test -f \"$REMOTE_DIR/Chainlink-Verified-Lightning AI-Agent/deploy/acp-config.json\"" || {
  echo ""
  echo "WARNING: No acp-config.json found on remote."
  echo "Run 'acp setup' locally to create agent identity, then copy config.json to remote."
}

# Build and start
echo ""
echo "Building and starting containers..."
$SSH $EC2_HOST "cd \"$REMOTE_DIR/Chainlink-Verified-Lightning AI-Agent/deploy\" && docker compose up -d --build"

echo ""
echo "=== Deployment complete ==="
echo "Check status: ssh -i $KEY_FILE $EC2_HOST 'cd \"$REMOTE_DIR/Chainlink-Verified-Lightning AI-Agent/deploy\" && docker compose ps'"
echo "View logs:    ssh -i $KEY_FILE $EC2_HOST 'cd \"$REMOTE_DIR/Chainlink-Verified-Lightning AI-Agent/deploy\" && docker compose logs -f'"
