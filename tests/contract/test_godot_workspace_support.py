#!/usr/bin/env python3
"""Source contracts for the shared Godot authoring-workspace foundation."""

from __future__ import annotations

import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
SCRIPTS = ROOT / "content-studio" / "scripts"


class GodotWorkspaceSupportTests(unittest.TestCase):
    def test_support_owns_preview_gate_and_feedback_rendering(self) -> None:
        support = (SCRIPTS / "authoring_workspace_support.gd").read_text()
        for token in (
            "class_name AuthoringWorkspaceSupport",
            "preview_signature",
            "preview_operation",
            "preview_applicable",
            "func clear_preview",
            "func accept_preview",
            "func can_apply",
            "func render_changes",
            "func render_validation",
            "func operation_name",
            "No persisted values would change.",
            "No validation messages.",
        ):
            self.assertIn(token, support)

    def test_preview_gate_requires_operation_signature_and_applicability(self) -> None:
        support = (SCRIPTS / "authoring_workspace_support.gd").read_text()
        for token in (
            "preview_applicable",
            "preview_operation == operation",
            "preview_signature == signature",
        ):
            self.assertIn(token, support)

    def test_support_remains_ui_only(self) -> None:
        support = (SCRIPTS / "authoring_workspace_support.gd").read_text().lower()
        for forbidden in (
            "httprequest",
            "npgsql",
            "insert into",
            "update item_",
            "delete from",
        ):
            self.assertNotIn(forbidden, support)

    def test_support_has_a_tracked_godot_uid(self) -> None:
        uid = (SCRIPTS / "authoring_workspace_support.gd.uid").read_text().strip()
        self.assertTrue(uid.startswith("uid://"))

    def test_support_functions_are_unique(self) -> None:
        support = (SCRIPTS / "authoring_workspace_support.gd").read_text()
        functions = [
            line.split("func ", 1)[1].split("(", 1)[0]
            for line in support.splitlines()
            if line.startswith("func ")
        ]
        self.assertEqual(len(functions), len(set(functions)))

    def test_migration_sequence_is_documented(self) -> None:
        documentation = (ROOT / "docs" / "GODOT_WORKSPACE_SUPPORT.md").read_text()
        self.assertIn("PR #6", documentation)
        self.assertIn("existing editors", documentation)
        self.assertIn("future workspace", documentation)


if __name__ == "__main__":
    unittest.main()
