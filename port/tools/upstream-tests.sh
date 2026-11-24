#!/usr/bin/env bash
# Runs the upstream pytest suite at the pinned commit, in the configurations that upstream CI uses.
# Expected results are in docs/exec-plans/active/0002-judge-and-source-prep.md.
# Needs: scripts/fetch-upstream.sh, uv, and the Neo4j sidecar.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
UP="$ROOT/.upstream/graphiti"
PYTHON_VERSION="$(sed -n 's/^python=//p' "$ROOT/port/UPSTREAM_PIN")"

cd "$UP"
uv sync --frozen --extra dev --python "$PYTHON_VERSION" >/dev/null 2>&1

export GRAPHITI_TELEMETRY_ENABLED=false
unset OPENAI_API_KEY ANTHROPIC_API_KEY GOOGLE_API_KEY GROQ_API_KEY VOYAGE_API_KEY
run() { .venv/bin/python -m pytest -q -p no:cacheprovider -m "not integration" "$@" 2>&1 | tail -1; }

echo "A. unit tests, no database"
DISABLE_NEPTUNE=1 DISABLE_NEO4J=1 DISABLE_FALKORDB=1 DISABLE_RELATE=1 run tests/ \
  --ignore=tests/test_graphiti_int.py --ignore=tests/test_graphiti_mock.py \
  --ignore=tests/test_node_int.py --ignore=tests/test_edge_int.py \
  --ignore=tests/test_entity_exclusion_int.py --ignore=tests/driver/

export NEO4J_URI="${NEO4J_URI:-bolt://localhost:7687}" NEO4J_USER="${NEO4J_USER:-neo4j}" NEO4J_PASSWORD="${NEO4J_PASSWORD:-password}"
export DISABLE_NEPTUNE=1 DISABLE_FALKORDB=1 DISABLE_RELATE=1

echo "B. database tests, Neo4j only"
run tests/test_graphiti_mock.py tests/test_node_int.py tests/test_edge_int.py tests/driver/test_neo4j_driver_routing.py

echo "C. add_triplet tests, Neo4j only (upstream CI does not run this file with a database)"
run tests/test_add_triplet.py || true
