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
            'preload("res://scripts/content_studio_logger.gd")',
            "@onready var _client: AuthoringHostClient = %AuthoringHostClient",
            "_apply_options()",
            "GraphEdit.new()",
            "GraphNode.new()",
            "connection_request",
            "disconnection_request",
            "node_selected",
            "_on_graph_edit_node_selected",
            "_add_lifecycle_section(graph_content)",
            '_add_heading(parent, "Dialogue Lifecycle", 16)',
            "func _add_operation_results_section",
            '_add_heading(parent, "Operation Results", 20)',
            "func _sync_graph_connections_to_draft",
            'if _graph == null or not _graph.has_method("get_connection_list"):',
            '_graph.call("get_connection_list")',
            'var next_node = _optional_variant_payload(node.get("next_node_id", null))',
            'speaker_next_nodes[str(node.get("node_id", ""))] = next_node',
            '"Dialogue graph connection requested"',
            '"Dialogue graph disconnection requested"',
            '"Dialogue graph sync preserved model links without reported graph connections"',
            '"Dialogue graph sync observed reported connections"',
            "func _reset_playthrough_preview",
            "_reset_playthrough_preview()",
            '_add_operation("Delete Dialogue", "delete")',
            '_delete_button.text = "Preview Dialogue Delete"',
            '_delete_node_button.text = "Delete Selected Node"',
            "_delete_node_button.pressed.connect(_delete_selected_node)",
            '_add_entry_button.text = "+ Entry Point"',
            "func _add_entry_point",
            "func _remove_entry_point",
            "func _on_entry_id_changed",
            "func _on_entry_priority_changed",
            '"priority": 10 if not entries.is_empty() else 0',
            '"entry_order": index',
            "_workspace_support.clear_preview",
            "_workspace_support.accept_preview",
            "_workspace_support.can_apply",
            "_workspace_support.render_changes",
            "_workspace_support.render_validation",
            "_workspace_support.operation_name",
            "func _condition_value",
            "func _condition_string_value",
            "func _remap_graph_node_id_after_form_sync",
            "_renamed_graph_node_ids",
            '"Dialogue selected node form synced"',
            '"status": _optional_variant_payload(_condition_value(condition, "quest_status", "status", null))',
            'condition["status"] = condition["quest_status"]',
            'if node_types.is_empty():',
            '{"id": NODE_TYPE_SPEAKER_TEXT, "display_name": "Speaker Text"}',
            '{"id": NODE_TYPE_PLAYER_CHOICE, "display_name": "Player Choice"}',
            '{"id": NODE_TYPE_END, "display_name": "End"}',
        ):
            self.assertIn(token, editor)

        self.assertNotIn("HTTPRequest", editor)
        self.assertNotIn("AuthoringHttpTransport", editor)

    def test_graph_rebuild_preserves_graph_edit_internal_children(self) -> None:
        editor = (ROOT / "content-studio" / "scripts" / "dialogue_editor.gd").read_text()

        self.assertIn("for child in _graph.get_children():", editor)
        self.assertIn("if child is not GraphNode:", editor)
        self.assertIn("continue\n\t\t_graph.remove_child(child)", editor)

    def test_graph_nodes_fit_content_without_fixed_height(self) -> None:
        editor = (ROOT / "content-studio" / "scripts" / "dialogue_editor.gd").read_text()

        for token in (
            "const GRAPH_NODE_MIN_WIDTH := 190.0",
            "const GRAPH_NODE_SUMMARY_WIDTH := 150.0",
            "graph_node.resizable = false",
            "graph_node.custom_minimum_size = Vector2(GRAPH_NODE_MIN_WIDTH, 0)",
            "summary.custom_minimum_size = Vector2(GRAPH_NODE_SUMMARY_WIDTH, 0)",
        ):
            self.assertIn(token, editor)

        self.assertNotIn("graph_node.resizable = true", editor)
        self.assertNotIn("graph_node.custom_minimum_size = Vector2(190, 120)", editor)

    def test_inspector_form_labels_do_not_collapse_to_vertical_text(self) -> None:
        editor = (ROOT / "content-studio" / "scripts" / "dialogue_editor.gd").read_text()

        for token in (
            "const FORM_LABEL_WIDTH := 132.0",
            "inspector.custom_minimum_size = Vector2(320, 0)",
            "label.custom_minimum_size = Vector2(FORM_LABEL_WIDTH, 0)",
        ):
            self.assertIn(token, editor)

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
            "_startup_operations = [OP_MOB_OPTIONS, OP_NPC_OPTIONS, OP_DIALOGUE_OPTIONS, OP_QUEST_OPTIONS]",
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
            'CONDITION_TYPE_QUEST_STATUS := "quest_status"',
            'CONDITION_TYPE_QUEST_STEP := "quest_step"',
            'CONDITION_TYPE_HAS_ITEM := "has_item"',
            "_fill_quest_reference_options",
            "_fill_quest_step_options",
            "_fill_item_reference_options",
            '"quest_references"',
            '"item_references"',
            "supports_runtime_dialogue_catalog",
            "supports_conditions",
            "supports_effects",
            "Available",
        ):
            self.assertIn(token, editor)

        for token in (
            'EFFECT_TYPE_START_QUEST := "start_quest"',
            'EFFECT_TYPE_ADVANCE_QUEST := "advance_quest"',
            'EFFECT_TYPE_COMPLETE_QUEST := "complete_quest"',
            'EFFECT_TYPE_GRANT_ITEM := "grant_item"',
            'EFFECT_TYPE_REMOVE_ITEM := "remove_item"',
            'EFFECT_TYPE_GRANT_EXPERIENCE := "grant_experience"',
            "_add_effects_editor",
            "_default_effect",
            "_effect_summary_list",
            "Effects are attached to player choices",
            '"effects": []',
            '"would_apply_effects"',
        ):
            self.assertIn(token, editor)

        for forbidden in (
            "quest_stage",
            "objective_progress",
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
            "D1-D5 Dialogue Studio authoring",
            "QV3 typed read-only quest/item predicates",
            "QV4 typed choice effects",
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
