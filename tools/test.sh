#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

python3 -m unittest discover -s "${ROOT}/tests/contract" -p 'test_*.py' -v

if command -v dotnet >/dev/null 2>&1; then
  dotnet build "${ROOT}/host/MMO.ContentStudio.AuthoringHost.csproj" --nologo
else
  echo "[skip] dotnet SDK not installed; host build/runtime test skipped"
fi

if command -v godot >/dev/null 2>&1; then
  godot --headless --path "${ROOT}/content-studio" \
    --script res://tests/contract_fixture_test.gd --quit
elif command -v godot4 >/dev/null 2>&1; then
  godot4 --headless --path "${ROOT}/content-studio" \
    --script res://tests/contract_fixture_test.gd --quit
else
  echo "[skip] Godot 4 not installed; Godot fixture test skipped"
fi
