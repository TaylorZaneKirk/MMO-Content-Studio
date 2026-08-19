#!/usr/bin/env python3
"""Source contracts for shared Godot authoring-workspace behavior."""

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
            'preload("res://scripts/content_studio_logger.gd")',
            'CONTENT_STUDIO_LOGGER.info("Validation message rendered"',
            "RichTextLabel.new()",
            "label.selection_enabled = true",
            "label.fit_content = true",
            "label.scroll_active = false",
            "No persisted values would change.",
            "No validation messages.",
            "container.remove_child(child)",
        ):
            self.assertIn(token, support)

    def test_existing_editors_delegate_shared_behavior(self) -> None:
        editors = ("item_editor.gd", "mob_editor.gd")
        for file_name in editors:
            editor = (SCRIPTS / file_name).read_text()
            self.assertIn(
                'preload("res://scripts/authoring_workspace_support.gd")',
                editor,
                file_name,
            )
            self.assertIn("WORKSPACE_SUPPORT_SCRIPT.new()", editor, file_name)
            self.assertIn("_workspace_support.clear_preview", editor, file_name)
            self.assertIn("_workspace_support.accept_preview", editor, file_name)
            self.assertIn("_workspace_support.can_apply", editor, file_name)
            self.assertIn("_workspace_support.render_changes", editor, file_name)
            self.assertIn("_workspace_support.render_validation", editor, file_name)
            self.assertIn("_workspace_support.operation_name", editor, file_name)

    def test_editors_do_not_redeclare_shared_preview_state_or_renderers(self) -> None:
        editors = ("item_editor.gd", "mob_editor.gd")
        for file_name in editors:
            editor = (SCRIPTS / file_name).read_text()
            for forbidden in (
                "var _preview_signature",
                "var _preview_operation",
                "var _preview_applicable",
                "var _preview_is_applicable",
                "func _render_changes",
                "func _render_validation",
                "func _operation_name",
                "func _operation_display_name",
            ):
                self.assertNotIn(forbidden, editor, file_name)

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
        self.assertIn("The U3/U4 Items workspace and the Mobs workspace", documentation)
        self.assertIn("legacy Consumables, Equipment, and Weapons & Tools editor scripts were", documentation)
        self.assertIn("existing editor", documentation)
        self.assertIn("New workspaces", documentation)


if __name__ == "__main__":
    unittest.main()
