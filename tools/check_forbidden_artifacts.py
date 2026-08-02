#!/usr/bin/env python3
"""Reject transport/bootstrap artifacts that must never enter normal source PRs."""

from __future__ import annotations

import fnmatch
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]

FORBIDDEN_PATTERNS = (
    ".t3*-bootstrap/**",
    ".payload/**",
    "*.b64",
    "**/*.b64",
    "*.base64",
    "**/*.base64",
    ".github/workflows/apply-*.yml",
    ".github/workflows/apply-*.yaml",
)


def tracked_files() -> list[str]:
    completed = subprocess.run(
        ["git", "ls-files", "-z"],
        cwd=ROOT,
        check=True,
        capture_output=True,
    )
    return [
        value.decode("utf-8")
        for value in completed.stdout.split(b"\0")
        if value
    ]


def main() -> int:
    violations = sorted(
        path
        for path in tracked_files()
        if any(fnmatch.fnmatch(path, pattern) for pattern in FORBIDDEN_PATTERNS)
    )
    if not violations:
        print("Forbidden-artifact check passed.")
        return 0

    print("Forbidden transport/bootstrap artifacts are tracked:", file=sys.stderr)
    for path in violations:
        print(f"  - {path}", file=sys.stderr)
    print(
        "Publish ordinary source files through Git. Do not commit encoded payloads "
        "or one-use source-application workflows.",
        file=sys.stderr,
    )
    return 1


if __name__ == "__main__":
    raise SystemExit(main())
