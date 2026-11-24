#!/usr/bin/env bash
# Clones upstream Graphiti at the pinned commit into .upstream/graphiti (git-ignored).
# The port reads this checkout. Nothing in the product build depends on it.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PIN_FILE="$ROOT/port/UPSTREAM_PIN"
REPO_URL="$(sed -n 's/^repo=//p' "$PIN_FILE")"
COMMIT="$(sed -n 's/^commit=//p' "$PIN_FILE")"
DEST="$ROOT/.upstream/graphiti"

if [ -d "$DEST/.git" ] && [ "$(git -C "$DEST" rev-parse HEAD)" = "$COMMIT" ]; then
  echo "upstream already at $COMMIT"
  exit 0
fi

rm -rf "$DEST"
mkdir -p "$DEST"
git -C "$DEST" init -q
git -C "$DEST" remote add origin "$REPO_URL"
git -C "$DEST" fetch -q --depth 1 origin "$COMMIT"
git -C "$DEST" checkout -q FETCH_HEAD
echo "upstream checked out at $(git -C "$DEST" rev-parse HEAD)"
