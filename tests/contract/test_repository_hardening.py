#!/usr/bin/env python3
"""Repository-level guardrails for repeatable source-based development."""

from __future__ import annotations

import json
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]


class RepositoryHardeningTests(unittest.TestCase):
    def test_ci_builds_host_and_runs_contracts(self) -> None:
        workflow = (ROOT / ".github" / "workflows" / "ci.yml").read_text()
        for token in (
            "actions/setup-dotnet@v4",
            "dotnet restore",
            "dotnet build",
            "python3 -m unittest discover",
            "check_forbidden_artifacts.py",
        ):
            self.assertIn(token, workflow)

    def test_forbidden_artifact_guard_covers_transport_patterns(self) -> None:
        guard = (ROOT / "tools" / "check_forbidden_artifacts.py").read_text()
        for token in (
            ".t3*-bootstrap/**",
            ".payload/**",
            "*.b64",
            "*.base64",
            ".github/workflows/apply-*.yml",
        ):
            self.assertIn(token, guard)

    def test_canonical_check_command_composes_all_checks(self) -> None:
        check = (ROOT / "tools" / "check.sh").read_text()
        self.assertIn("check_forbidden_artifacts.py", check)
        self.assertIn('"${ROOT}/tools/test.sh"', check)
        self.assertIn("Static JSON/XML validation passed.", check)

    def test_dotnet_sdk_policy_is_pinned(self) -> None:
        policy = json.loads((ROOT / "global.json").read_text())
        self.assertEqual("10.0.100", policy["sdk"]["version"])
        self.assertEqual("latestFeature", policy["sdk"]["rollForward"])
        self.assertFalse(policy["sdk"]["allowPrerelease"])


if __name__ == "__main__":
    unittest.main()
