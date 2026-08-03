#!/usr/bin/env python3
"""Source contracts for the one-command development launcher."""

from __future__ import annotations

import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]


class DevelopmentLauncherTests(unittest.TestCase):
    def test_launcher_validates_configuration_and_waits_for_health(self) -> None:
        script = (ROOT / "tools" / "dev.sh").read_text()
        for token in (
            "host/appsettings.Local.json",
            "CONTENT_STUDIO_HOST_URL",
            "/api/v1/system/health",
            "curl --silent --show-error --fail",
            '"${ROOT}/tools/check.sh"',
        ):
            self.assertIn(token, script)

    def test_launcher_reuses_external_host_and_cleans_up_owned_host(self) -> None:
        script = (ROOT / "tools" / "dev.sh").read_text()
        self.assertIn("Reusing authoring host", script)
        self.assertIn("HOST_PID=$!", script)
        self.assertIn("kill -0", script)
        self.assertIn("trap cleanup EXIT INT TERM", script)
        self.assertIn('"${ROOT}/tools/run-studio.sh"', script)

    def test_launcher_has_skip_check_and_help_modes(self) -> None:
        script = (ROOT / "tools" / "dev.sh").read_text()
        self.assertIn("--skip-check", script)
        self.assertIn("--help", script)
        self.assertIn("Unknown argument", script)

    def test_root_executable_launches_host_and_client(self) -> None:
        script_path = ROOT / "mmo-content-studio"
        script = script_path.read_text()
        self.assertTrue(script_path.exists())
        self.assertIn("#!/usr/bin/env bash", script)
        self.assertIn('"${ROOT}/tools/dev.sh" --skip-check', script)
        self.assertIn('"${ROOT}/tools/dev.sh" "${ARGS[@]}"', script)
        self.assertIn("--check", script)
        self.assertIn("Starts the local .NET authoring host", script)

    def test_ci_and_local_checks_parse_shell_scripts(self) -> None:
        workflow = (ROOT / ".github" / "workflows" / "ci.yml").read_text()
        local_check = (ROOT / "tools" / "check.sh").read_text()
        self.assertIn("bash -n tools/*.sh", workflow)
        self.assertIn('bash -n "${ROOT}"/tools/*.sh "${ROOT}/mmo-content-studio"', local_check)


if __name__ == "__main__":
    unittest.main()
