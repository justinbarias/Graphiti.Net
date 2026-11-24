#!/usr/bin/env bash
# Claude Code SessionStart hook (.claude/settings.json).
# It does work only in Claude cloud sessions, where CLAUDE_CODE_REMOTE is "true".
# Local machines and the devcontainer use scripts/setup-env.sh directly.
set -uo pipefail

if [ "${CLAUDE_CODE_REMOTE:-}" != "true" ]; then
  exit 0
fi

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

DOTNET_ROOT_DIR="$HOME/.dotnet"

# Later Bash commands in the session read this file, so the SDK stays on PATH.
if [ -n "${CLAUDE_ENV_FILE:-}" ]; then
  {
    echo "export DOTNET_ROOT=\"$DOTNET_ROOT_DIR\""
    echo "export PATH=\"$DOTNET_ROOT_DIR:$DOTNET_ROOT_DIR/tools:\$PATH\""
    echo "export DOTNET_CLI_TELEMETRY_OPTOUT=1"
    echo "export DOTNET_NOLOGO=1"
  } >> "$CLAUDE_ENV_FILE"
fi

if ! DOTNET_ROOT="$DOTNET_ROOT_DIR" bash scripts/setup-env.sh; then
  echo "session-start: setup-env.sh failed. Run it by hand and read the error." >&2
fi

# Start the Neo4j sidecar in the background so the session does not wait for the image pull.
if command -v docker >/dev/null 2>&1; then
  (
    if ! docker info >/dev/null 2>&1 && command -v dockerd >/dev/null 2>&1; then
      (sudo -n dockerd || dockerd) >/tmp/dockerd.log 2>&1 &
      for _ in $(seq 1 30); do docker info >/dev/null 2>&1 && break; sleep 1; done
    fi
    docker compose up -d neo4j >/tmp/neo4j-up.log 2>&1
  ) &
fi

exit 0
