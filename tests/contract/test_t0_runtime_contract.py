#!/usr/bin/env python3
"""Starts the .NET host and verifies the T0 JSON API contract when dotnet exists."""

from __future__ import annotations

import json
import os
import shutil
import subprocess
import time
import unittest
import urllib.error
import urllib.request
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
HOST_PROJECT = ROOT / "host" / "MMO.ContentStudio.AuthoringHost.csproj"
BASE_URL = "http://127.0.0.1:5197/api/v1"


@unittest.skipUnless(shutil.which("dotnet"), "dotnet SDK is not installed")
class T0RuntimeContractTests(unittest.TestCase):
    process: subprocess.Popen[str]

    @classmethod
    def setUpClass(cls) -> None:
        env = os.environ.copy()
        env["AuthoringHost__ListenUrl"] = "http://127.0.0.1:5197"
        env["ConnectionProfiles__Profiles__local__ConnectionString"] = ""
        cls.process = subprocess.Popen(
            [
                "dotnet",
                "run",
                "--project",
                str(HOST_PROJECT),
                "--no-launch-profile",
            ],
            cwd=ROOT / "host",
            env=env,
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            text=True,
        )
        cls._wait_for_host()

    @classmethod
    def tearDownClass(cls) -> None:
        cls.process.terminate()
        try:
            cls.process.wait(timeout=8)
        except subprocess.TimeoutExpired:
            cls.process.kill()

    @classmethod
    def _wait_for_host(cls) -> None:
        deadline = time.monotonic() + 45
        last_error: Exception | None = None
        while time.monotonic() < deadline:
            if cls.process.poll() is not None:
                output = cls.process.stdout.read() if cls.process.stdout else ""
                raise AssertionError(f"Authoring host exited early:\n{output}")
            try:
                cls._get("/system/handshake")
                return
            except (urllib.error.URLError, ConnectionError) as error:
                last_error = error
                time.sleep(0.25)
        raise AssertionError(f"Authoring host did not become ready: {last_error}")

    @staticmethod
    def _get(path: str) -> dict:
        request = urllib.request.Request(
            BASE_URL + path,
            headers={
                "Accept": "application/json",
                "X-Request-Id": "python-contract-test",
            },
        )
        with urllib.request.urlopen(request, timeout=3) as response:
            return json.loads(response.read().decode("utf-8"))

    def test_handshake(self) -> None:
        envelope = self._get("/system/handshake")
        self.assertTrue(envelope["success"])
        self.assertEqual(envelope["api_version"], "1")
        self.assertEqual(envelope["request_id"], "python-contract-test")
        self.assertIn("1", envelope["data"]["supported_api_versions"])

    def test_health_reports_unconfigured_database_without_failing(self) -> None:
        envelope = self._get("/system/health")
        self.assertTrue(envelope["success"])
        self.assertEqual(envelope["data"]["database"]["status"], "Unconfigured")
        self.assertIn(envelope["data"]["overall_status"], ("Degraded", "Unhealthy"))

    def test_catalog_is_versioned_and_exposes_workspaces(self) -> None:
        envelope = self._get("/catalog")
        self.assertTrue(envelope["success"])
        sections = envelope["data"]["sections"]
        self.assertEqual(
            [section["content_type"] for section in sections],
            ["items", "loot_tables", "mobs", "npcs", "dialogues"],
        )
        self.assertTrue(all(section["entries"] == [] for section in sections))


if __name__ == "__main__":
    unittest.main()
