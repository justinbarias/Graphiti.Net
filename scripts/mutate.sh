#!/usr/bin/env bash
# Runs Stryker.NET on PyCompat with its Property tests, in observe mode (thresholds.break is 0).
# Reports land in StrykerOutput/ at the repository root.
# docs/TESTING.md says how to read the report and when to delete a test.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"
export PATH="${DOTNET_ROOT:-$HOME/.dotnet}:$PATH"

dotnet tool restore

# Stryker starts in the test project. At the repository root it reads the solution, and it then runs each
# test project against each mutant of each product project. That run does not finish (debt T27).
cd tests/Graphiti.Net.Property.Tests
dotnet stryker --config-file "$ROOT/stryker-config.json" --output "$ROOT/StrykerOutput/$(date +%Y-%m-%d.%H-%M-%S)" "$@"
