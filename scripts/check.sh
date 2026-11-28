#!/usr/bin/env bash
# The gate that every change must pass. Cheap checks run first.
#
#   scripts/check.sh          docs, build, format, scan
#   scripts/check.sh --test   the same, then the tests and the judge against the Neo4j sidecar
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"
export PATH="${DOTNET_ROOT:-$HOME/.dotnet}:$PATH"

step() { printf '\n== %s\n' "$1"; }

step "docs"
python3 scripts/check_docs.py

step "build"
dotnet build Graphiti.Net.slnx -warnaserror --nologo -v quiet

step "format"
dotnet format Graphiti.Net.slnx --verify-no-changes

step "scan (shortcuts in the ported code)"
python3 port/tools/scan_port.py src --exclude /obj/ --exclude /bin/ --allow port/tools/scan_port_allow.txt

if [ "${1:-}" = "--test" ]; then
  step "neo4j"
  docker compose up -d --wait neo4j

  step "test"
  dotnet test --solution Graphiti.Net.slnx --no-build

  step "judge (.NET build against the golden files)"
  dotnet tests/Graphiti.Net.JudgeHost/bin/Debug/net10.0/Graphiti.Net.JudgeHost.dll --selfcheck port/judge/embedder-vectors.json
  # --verbose prints 20 difference lines for a case that fails and is not listed (debt T32).
  python3 port/judge/run.py --build dotnet --verbose
fi

printf '\ncheck: passed\n'
