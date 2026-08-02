#!/usr/bin/env python3
"""Source contracts for the shared Godot HTTP/envelope transport."""

from __future__ import annotations

import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
SCRIPTS = ROOT / "content-studio" / "scripts"


class GodotHttpTransportTests(unittest.TestCase):
    def test_transport_owns_http_and_envelope_parsing(self) -> None:
        transport = (SCRIPTS / "http_json_client.gd").read_text()
        for token in (
            "class_name AuthoringHttpTransport",
            "HTTPRequest.new()",
            "JSON.parse_string",
            "X-Content-Studio-Api-Version",
            "X-Request-Id",
            "REQUEST_TIMEOUT_SECONDS",
            "request_succeeded.emit",
            "request_failed.emit",
        ):
            self.assertIn(token, transport)

    def test_facade_delegates_transport_instead_of_parsing_http(self) -> None:
        facade = (SCRIPTS / "authoring_host_client.gd").read_text()
        self.assertIn('preload("res://scripts/http_json_client.gd")', facade)
        self.assertIn("_transport.request(operation, path, method, payload)", facade)
        self.assertNotIn("HTTPRequest.new()", facade)
        self.assertNotIn("JSON.parse_string", facade)
        self.assertNotIn("enum RequestKind", facade)
        self.assertNotIn("func _extract_error_message", facade)
        self.assertLess(len(facade.splitlines()), 370)

    def test_public_client_surface_remains_available(self) -> None:
        facade = (SCRIPTS / "authoring_host_client.gd").read_text()
        for token in (
            "func connect_and_load",
            "func import_item_asset",
            "func preview_item",
            "func preview_consumable",
            "func preview_equipment",
            "signal item_mutation_completed",
            "signal consumable_mutation_completed",
            "signal equipment_mutation_completed",
        ):
            self.assertIn(token, facade)

    def test_operation_names_replace_multi_location_enum_mapping(self) -> None:
        facade = (SCRIPTS / "authoring_host_client.gd").read_text()
        for token in (
            'OP_HANDSHAKE := "handshake"',
            'OP_ITEM_PREVIEW := "item_preview"',
            'OP_CONSUMABLE_PREVIEW := "consumable_preview"',
            'OP_EQUIPMENT_PREVIEW := "equipment_preview"',
        ):
            self.assertIn(token, facade)

    def test_godot_functions_are_unique(self) -> None:
        for file_name in ("http_json_client.gd", "authoring_host_client.gd"):
            source = (SCRIPTS / file_name).read_text()
            functions = [
                line.split("func ", 1)[1].split("(", 1)[0]
                for line in source.splitlines()
                if line.startswith("func ")
            ]
            self.assertEqual(len(functions), len(set(functions)), file_name)


if __name__ == "__main__":
    unittest.main()
