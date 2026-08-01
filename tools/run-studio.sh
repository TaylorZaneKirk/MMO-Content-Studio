#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

if command -v godot >/dev/null 2>&1; then
  exec godot --path "${ROOT}/content-studio"
fi

if command -v godot4 >/dev/null 2>&1; then
  exec godot4 --path "${ROOT}/content-studio"
fi

echo "Godot 4 was not found on PATH." >&2
exit 1
