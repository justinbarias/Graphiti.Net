#!/usr/bin/env bash
# One setup path for every environment: local machines, the devcontainer, Claude cloud
# sessions and CI. It is safe to run more than once.
#
#   scripts/setup-env.sh            install the .NET SDK from global.json, restore tools and packages
#   scripts/setup-env.sh --python   also install uv, which only the judge recorder needs
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

DOTNET_ROOT_DIR="${DOTNET_ROOT:-$HOME/.dotnet}"
export PATH="$DOTNET_ROOT_DIR:$DOTNET_ROOT_DIR/tools:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1

WANT_MAJOR_MINOR="$(sed -n 's/.*"version": *"\([0-9]*\.[0-9]*\)\..*/\1/p' global.json | head -1)"

have_sdk() {
  command -v dotnet >/dev/null 2>&1 && dotnet --list-sdks 2>/dev/null | grep -q "^${WANT_MAJOR_MINOR}\."
}

if ! have_sdk; then
  echo "setup-env: installing .NET SDK ${WANT_MAJOR_MINOR} into $DOTNET_ROOT_DIR"
  installer="$(mktemp)"
  if curl -fsSL https://dot.net/v1/dotnet-install.sh -o "$installer"; then
    bash "$installer" --jsonfile "$ROOT/global.json" --install-dir "$DOTNET_ROOT_DIR"
  elif command -v apt-get >/dev/null 2>&1; then
    # Fallback for networks that block dot.net but allow the Ubuntu archive.
    SUDO=""; [ "$(id -u)" -ne 0 ] && SUDO="sudo"
    $SUDO apt-get update -y
    $SUDO apt-get install -y "dotnet-sdk-${WANT_MAJOR_MINOR}"
  else
    echo "setup-env: cannot download the .NET SDK. Install SDK ${WANT_MAJOR_MINOR} by hand." >&2
    exit 1
  fi
  rm -f "$installer"
fi

if ! have_sdk; then
  echo "setup-env: .NET SDK ${WANT_MAJOR_MINOR} is still missing after the install step." >&2
  exit 1
fi

dotnet tool restore
dotnet restore Graphiti.Net.slnx

if [ -d .git ]; then
  git config core.hooksPath .githooks
fi

if [ "${1:-}" = "--python" ] && ! command -v uv >/dev/null 2>&1; then
  echo "setup-env: installing uv for the judge recorder"
  curl -fsSL https://astral.sh/uv/install.sh | sh
fi

echo "setup-env: ready ($(dotnet --version))"
