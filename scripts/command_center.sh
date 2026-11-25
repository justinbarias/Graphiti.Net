#!/usr/bin/env bash
# Builds the command center status board. docs/FRONTEND.md describes the page.
#
#   scripts/command_center.sh               writes docs/generated/command-center.html
#   scripts/command_center.sh --artifact    writes the form that a Claude artifact needs
#   scripts/command_center.sh --check       prints problems, exits 1 if one exists
#
# The generator is part of the agent-harness skill. Set COMMAND_CENTER to its path if the skill is in another place.
# The script runs from the repository root, so a relative --out path starts there. Each run writes the data file again.
set -euo pipefail
cd "$(dirname "$0")/.."

if ! python3 -c 'import sys; sys.exit(sys.version_info < (3, 11))'; then
  echo "command_center: python3 must be version 3.11 or later." >&2
  exit 1
fi

generator="${COMMAND_CENTER:-$HOME/.claude/skills/agent-harness/command-center/command_center.py}"
if [ ! -f "$generator" ]; then
  echo "command_center: no generator at $generator." >&2
  echo "Install the agent-harness skill, or set COMMAND_CENTER to the path of command_center.py." >&2
  exit 1
fi

python3 scripts/command_center_data.py >&2   # --json prints the board data on standard output
python3 "$generator" --root . "$@"
