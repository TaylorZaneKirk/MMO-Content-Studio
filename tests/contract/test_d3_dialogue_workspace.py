#!/usr/bin/env python3
"""Source contracts for the D3 Godot Dialogue Studio workspace."""

from __future__ import annotations

import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]


class D3DialogueWorkspaceTests(unittest.TestCase):
    def test_dialogue_editor_exists_as_top_level_workspace(self) -> None:
        main_scene = (ROOT / "content-studio" / "scenes" / "Main.tscn").read_text()
        editor = (ROOT / "content-studio" / "scripts" / "dialogue_editor.gd").read_text()

        self.assertIn('path="res://scripts/dialogue_editor.gd"', main_scene)
        self.assertIn('[node name="NPCs"', main_scene)
        self.assertIn('[node name="Dialogue"', main_scene)
        self.assertLess(main_scene.index('[node name="NPCs"'), main_scene.index('[node name="Dialogue"'))
        self.assertLess(main_scene.index('[node name="Dialogue"'), main_scene.index('[node name="Environment"'))
        self.assertIn("extends HBoxContainer", editor)
        self.assertIn("class_name DialogueEditor", editor)

    def test_editor_uses_shared_client_workspace_support_and_graph_controls(self) -> None:
        editor = (ROOT / "content-studio" / "scripts" / "dialogue_editor.gd").read_text()

        for token in (
            'preload("res://scripts/authoring_workspace_support.gd")',
            "@onready var _client: AuthoringHostClient = %AuthoringHostClient",
            "GraphEdit.new()",
            "GraphNode.new()",
            "connection_request",
            "disconnection_request",
            "_workspace_support.clear_preview",
            "_workspace_support.accept_preview",
            "_workspace_support.can_apply",
            "_workspace_support.render_changes",
            "_workspace_support.render_validation",
            "_workspace_support.operation_name",
        ):
            self.assertIn(token, editor)

        self.assertNotIn("HTTPRequest", editor)
        self.assertNotIn("AuthoringHttpTransport", editor)

    def test_client_exposes_complete_d2_dialogue_route_family_and_isolated_startup(self) -> None:
        client = (ROOT / "content-studio" / "scripts" / "authoring_host_client.gd").read_text()

        for token in (
            "signal dialogue_options_received",
            "signal dialogue_catalog_received",
            "signal dialogue_definition_received",
            "signal dialogue_preview_received",
            "signal dialogue_playthrough_received",
            "signal dialogue_mutation_completed",
            "signal dialogue_delete_completed",
            'const OP_DIALOGUE_OPTIONS := "dialogue_options"',
            "_startup_operations = [OP_MOB_OPTIONS, OP_NPC_OPTIONS, OP_DIALOGUE_OPTIONS]",
            "_request_next_startup_operation()",
            "/api/v1/dialogues/options",
            "/api/v1/dialogues%s",
            "/api/v1/dialogues/%s/preview",
            "/api/v1/dialogues/%s/playthrough",
            "/api/v1/dialogues/%s/draft",
            "/api/v1/dialogues/%s/publish",
            "/api/v1/dialogues/%s/disable",
            "/api/v1/dialogues/%s/delete",
        ):
            self.assertIn(token, client)

    def test_complete_payload_current_node_types_and_deferred_authoring_scope(self) -> None:
        editor = (ROOT / "content-studio" / "scripts" / "dialogue_editor.gd").read_text()

        for token in (
            'const NODE_TYPE_SPEAKER_TEXT := "speaker_text"',
            'const NODE_TYPE_PLAYER_CHOICE := "player_choice"',
            'const NODE_TYPE_END := "end"',
            '"display_name"',
            '"schema_version"',
            '"entry_points"',
            '"nodes"',
            '"metadata_description"',
            '"notes"',
            '"expected_updated_at_utc"',
            '"preview_signature"',
            '"conditions": []',
            "supports_runtime_dialogue_catalog",
            "supports_conditions",
            "supports_effects",
            "Not supported in D3",
            "Available",
        ):
            self.assertIn(token, editor)

        for forbidden in (
            "quest_id",
            "quest_stage",
            "objective_progress",
            "start_quest",
            "complete_quest",
            "portrait",
            "localization",
            "cutscene",
            "audio",
        ):
            self.assertNotIn(forbidden, editor)

    def test_workspace_routing_links_npcs_and_dialogue_without_cross_editor_coupling(self) -> None:
        main = (ROOT / "content-studio" / "scripts" / "main.gd").read_text()
        npc = (ROOT / "content-studio" / "scripts" / "npc_editor.gd").read_text()
        dialogue = (ROOT / "content-studio" / "scripts" / "dialogue_editor.gd").read_text()

        for token in (
            "signal workspace_open_requested(workspace_id: String, resource_id: String)",
            'workspace_open_requested.emit("dialogue", dialogue_id)',
            "func open_resource(npc_definition_id: String)",
            "Open Dialogue",
        ):
            self.assertIn(token, npc)

        for token in (
            "signal workspace_open_requested(workspace_id: String, resource_id: String)",
            'workspace_open_requested.emit("npcs", npc_definition_id)',
            "func open_resource(dialogue_definition_id: String)",
            'source_text.begins_with("npc:")',
        ):
            self.assertIn(token, dialogue)

        for token in (
            "npc_editor.workspace_open_requested.connect(_on_workspace_open_requested)",
            "dialogue_editor.workspace_open_requested.connect(_on_workspace_open_requested)",
            'match workspace_id:',
            'dialogue_editor.open_resource(resource_id)',
            'npc_editor.open_resource(resource_id)',
        ):
            self.assertIn(token, main)

    def test_docs_mark_d4_and_d5_complete_without_quest_scope(self) -> None:
        docs = "\n".join(
            [
                (ROOT / "README.md").read_text(),
                (ROOT / "docs" / "ROADMAP.md").read_text(),
                (ROOT / "docs" / "API_V1.md").read_text(),
                (ROOT / "docs" / "ARCHITECTURE.md").read_text(),
                (ROOT / "docs" / "GODOT_WORKSPACE_SUPPORT.md").read_text(),
                (ROOT / "docs" / "DIALOGUE_STUDIO_DOMAIN_MODEL.md").read_text(),
                (ROOT / "docs" / "DIALOGUE_STUDIO_IMPLEMENTATION_PLAN.md").read_text(),
                (ROOT / "docs" / "DIALOGUE_STUDIO_ACCEPTANCE.md").read_text(),
            ]
        )

        for token in (
            "D3 Godot Dialogue Studio implemented",
            "Dialogue workspace after NPCs and before Environment",
            "GraphEdit",
            "NPC cross-navigation",
            "D4 MMO Project runtime catalog handoff implemented",
            "D1-D5 non-quest Dialogue Studio authoring",
            "no quest, condition, or effect authoring",
        ):
            self.assertIn(token, docs)

        for forbidden in (
            "Quest authoring implemented",
            "D4 MMO Project runtime catalog handoff remains pending",
            "D5 hardening and playthrough verification remain pending",
        ):
            self.assertNotIn(forbidden, docs)


if __name__ == "__main__":
    unittest.main()
