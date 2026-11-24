#!/usr/bin/env bash
# Runs Stryker.NET in observe mode (thresholds.break is 0). Reports land in StrykerOutput/.
# docs/TESTING.md says how to read the report and when to delete a test.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"
export PATH="${DOTNET_ROOT:-$HOME/.dotnet}:$PATH"

docker compose up -d --wait neo4j
dotnet tool restore
dotnet stryker --config-file stryker-config.json "$@"
