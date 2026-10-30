#!/usr/bin/env bash
# Setup script for a Codex (or any fresh Linux) environment: the .NET 8 SDK,
# the EF tool and both web apps' packages. Run from the repository root:
#   bash tamaneen/scripts/codex-setup.sh
# SQL Server is not installed here; integration tests need Docker or
# TAMANEEN_TEST_SQL pointing at a server, and otherwise run in CI.
set -euo pipefail
cd "$(dirname "$0")/.."

if ! command -v dotnet >/dev/null || ! dotnet --list-sdks | grep -q '^8\.'; then
  curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
  bash /tmp/dotnet-install.sh --channel 8.0 --install-dir "$HOME/.dotnet"
  export PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$PATH"
  echo 'export PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$PATH"' >> "$HOME/.bashrc"
fi

dotnet tool restore
dotnet restore Tamaneen.sln

(cd web-patient && npm ci)
(cd web-staff && npm ci)

echo "Ready. Read tamaneen/AGENTS.md, then docs/HANDOFF.md and docs/PROGRESS.md."
