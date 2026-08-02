#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

python3 "${ROOT}/tools/check_forbidden_artifacts.py"
python3 - <<'PY' "${ROOT}"
from pathlib import Path
import json
import sys
import xml.etree.ElementTree as ET

root = Path(sys.argv[1])
for path in root.rglob("*.json"):
    if any(part in {"bin", "obj", ".godot"} for part in path.parts):
        continue
    json.loads(path.read_text(encoding="utf-8"))
for path in root.rglob("*.csproj"):
    ET.parse(path)
print("Static JSON/XML validation passed.")
PY

"${ROOT}/tools/test.sh"
