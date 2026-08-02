#!/usr/bin/env python3
"""Fast source-level checks for the T0 host/GUI contract."""

from __future__ import annotations

import json
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]


class T0SourceContractTests(unittest.TestCase):
    def test_host_configuration_is_loopback_only(self) -> None:
        settings = json.loads((ROOT / "host" / "appsettings.json").read_text())
        self.assertEqual(
            settings["AuthoringHost"]["ListenUrl"],
            "http://127.0.0.1:5187",
        )

    def test_host_and_godot_share_api_version(self) -> None:
        host_contract = (ROOT / "host" / "Contracts" / "ApiContracts.cs").read_text()
        godot_client = (
            ROOT / "content-studio" / "scripts" / "authoring_host_client.gd"
        ).read_text()
        self.assertIn('CurrentVersion = "1"', host_contract)
        self.assertIn('API_VERSION := "1"', godot_client)

    def test_t0_routes_exist(self) -> None:
        program = (ROOT / "host" / "Program.cs").read_text()
        for route in (
            "/system/handshake",
            "/system/health",
            "/catalog",
        ):
            self.assertIn(route, program)

    def test_empty_catalog_has_future_workspace_seams(self) -> None:
        catalog_service = (
            ROOT / "host" / "Services" / "ContentCatalogService.cs"
        ).read_text()
        for content_type in ("items", "consumables", "equipment", "mobs", "npcs"):
            self.assertIn(f'"{content_type}"', catalog_service)

    def test_godot_main_scene_exists(self) -> None:
        project = (ROOT / "content-studio" / "project.godot").read_text()
        self.assertIn('run/main_scene="res://scenes/Main.tscn"', project)
        self.assertTrue((ROOT / "content-studio" / "scenes" / "Main.tscn").is_file())

    def test_secrets_are_not_committed(self) -> None:
        gitignore = (ROOT / ".gitignore").read_text()
        self.assertIn("appsettings.Local.json", gitignore)
        example = json.loads(
            (ROOT / "host" / "appsettings.Local.example.json").read_text()
        )
        connection_string = example["ConnectionProfiles"]["Profiles"]["local"][
            "ConnectionString"
        ]
        self.assertIn("replace-me", connection_string)


if __name__ == "__main__":
    unittest.main()
