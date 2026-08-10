#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

python3 "${ROOT}/tools/check_forbidden_artifacts.py"

python3 -m unittest discover -s "${ROOT}/tests/contract" -p 'test_*.py' -v

if command -v dotnet >/dev/null 2>&1; then
  dotnet build "${ROOT}/host/MMO.ContentStudio.AuthoringHost.csproj" --nologo
  dotnet test "${ROOT}/tests/host/MMO.ContentStudio.AuthoringHost.Tests/MMO.ContentStudio.AuthoringHost.Tests.csproj" --configuration Release --nologo
else
  echo "[skip] dotnet SDK not installed; host build/runtime test skipped"
fi

REPO_GODOT="${ROOT}/../../tools/godot/Godot_v4.7-stable_mono_linux.x86_64"

if [[ -x "${REPO_GODOT}" ]]; then
  "${REPO_GODOT}" --headless --path "${ROOT}/content-studio" \
    --script res://tests/contract_fixture_test.gd --quit
  "${REPO_GODOT}" --headless --path "${ROOT}/content-studio" \
    --script res://tests/actor_socket_calibration_fixture_test.gd --quit
  "${REPO_GODOT}" --headless --path "${ROOT}/content-studio" \
    --script res://tests/equipment_grip_anchor_fixture_test.gd --quit
  "${REPO_GODOT}" --headless --path "${ROOT}/content-studio" \
    --script res://tests/actor_item_alignment_fixture_test.gd --quit
elif command -v godot >/dev/null 2>&1; then
  godot --headless --path "${ROOT}/content-studio" \
    --script res://tests/contract_fixture_test.gd --quit
  godot --headless --path "${ROOT}/content-studio" \
    --script res://tests/actor_socket_calibration_fixture_test.gd --quit
  godot --headless --path "${ROOT}/content-studio" \
    --script res://tests/equipment_grip_anchor_fixture_test.gd --quit
  godot --headless --path "${ROOT}/content-studio" \
    --script res://tests/actor_item_alignment_fixture_test.gd --quit
elif command -v godot4 >/dev/null 2>&1; then
  godot4 --headless --path "${ROOT}/content-studio" \
    --script res://tests/contract_fixture_test.gd --quit
  godot4 --headless --path "${ROOT}/content-studio" \
    --script res://tests/actor_socket_calibration_fixture_test.gd --quit
  godot4 --headless --path "${ROOT}/content-studio" \
    --script res://tests/equipment_grip_anchor_fixture_test.gd --quit
  godot4 --headless --path "${ROOT}/content-studio" \
    --script res://tests/actor_item_alignment_fixture_test.gd --quit
else
  echo "[skip] Godot 4 not installed; Godot fixture test skipped"
fi
