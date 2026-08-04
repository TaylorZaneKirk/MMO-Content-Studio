#!/usr/bin/env python3
"""Source contracts for the T5D Godot NPC workspace."""

from __future__ import annotations

import subprocess
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
SCRIPTS = ROOT / "content-studio" / "scripts"
SCENE = ROOT / "content-studio" / "scenes" / "Main.tscn"
MMO_PROJECT = ROOT.parents[1]


class T5DGodotNpcWorkspaceTests(unittest.TestCase):
    def test_npc_editor_exists_and_scene_exposes_top_level_workspace(self) -> None:
        editor_path = SCRIPTS / "npc_editor.gd"
        self.assertTrue(editor_path.exists())
        scene = SCENE.read_text()

        for token in (
            'path="res://scripts/npc_editor.gd"',
            '[node name="Items" type="HBoxContainer" parent="Margin/Root/Tabs"]',
            '[node name="Mobs" type="HBoxContainer" parent="Margin/Root/Tabs"]',
            '[node name="NPCs" type="HBoxContainer" parent="Margin/Root/Tabs"]',
            '[node name="Environment" type="HBoxContainer" parent="Margin/Root/Tabs"]',
            'script = ExtResource("5_npcs")',
        ):
            self.assertIn(token, scene)

        for forbidden in (
            '[node name="Dialogue"',
            '[node name="Quests"',
            '[node name="Services"',
            '[node name="Shops"',
        ):
            self.assertNotIn(forbidden, scene)

    def test_client_facade_supports_npc_api_via_transport(self) -> None:
        client = (SCRIPTS / "authoring_host_client.gd").read_text()

        for token in (
            "signal npc_options_received",
            "signal npc_catalog_received",
            "signal npc_definition_received",
            "signal npc_preview_received",
            "signal npc_mutation_completed",
            "signal npc_delete_completed",
            "func load_npc_options",
            "func load_npcs",
            "func load_npc",
            "func preview_npc",
            "func save_npc_draft",
            "func publish_npc",
            "func disable_npc",
            "func delete_npc",
            '"/api/v1/npcs/options"',
            '"/api/v1/npcs%s"',
            '"/api/v1/npcs/%s"',
            '"/api/v1/npcs/%s/preview"',
            '"/api/v1/npcs/%s/draft"',
            '"/api/v1/npcs/%s/publish"',
            '"/api/v1/npcs/%s/disable"',
            '"/api/v1/npcs/%s/delete"',
            '"expected_updated_at_utc": expected_updated_at_utc',
            '"preview_signature": preview_signature',
            "_transport.request(operation, path, method, payload)",
        ):
            self.assertIn(token, client)

        self.assertNotIn("HTTPRequest.new()", client)
        self.assertNotIn("JSON.parse_string", client)

    def test_startup_initializes_npcs_without_connection_blocking(self) -> None:
        client = (SCRIPTS / "authoring_host_client.gd").read_text()
        connection_operations = client.split("const CONNECTION_OPERATIONS := [", 1)[1].split("]", 1)[0]

        self.assertNotIn("OP_NPC_OPTIONS", connection_operations)
        self.assertIn("_startup_operations = [OP_MOB_OPTIONS, OP_NPC_OPTIONS]", client)
        self.assertIn("OP_NPC_OPTIONS:", client)
        self.assertIn("npc_options_received.emit(data)", client)
        self.assertIn("npc_catalog_received.emit(data)", client)
        self.assertIn("operation in [OP_MOB_OPTIONS, OP_MOBS, OP_NPC_OPTIONS, OP_NPCS]", client)
        self.assertIn("_request_next_startup_operation()", client)

    def test_editor_uses_shared_client_and_workspace_support(self) -> None:
        editor = (SCRIPTS / "npc_editor.gd").read_text()

        for token in (
            "class_name NpcEditor",
            'preload("res://scripts/authoring_workspace_support.gd")',
            "WORKSPACE_SUPPORT_SCRIPT.new()",
            "@onready var _client: AuthoringHostClient = %AuthoringHostClient",
            "_client.npc_options_received.connect",
            "_client.npc_catalog_received.connect",
            "_client.npc_definition_received.connect",
            "_client.npc_preview_received.connect",
            "_client.npc_mutation_completed.connect",
            "_client.npc_delete_completed.connect",
            "_workspace_support.clear_preview",
            "_workspace_support.accept_preview",
            "_workspace_support.can_apply",
            "_workspace_support.render_changes",
            "_workspace_support.render_validation",
            "_workspace_support.operation_name",
        ):
            self.assertIn(token, editor)

        for forbidden in (
            "HTTPRequest",
            "AuthoringHttpTransport",
            "JSON.parse_string",
            "Npgsql",
            "SELECT ",
            "INSERT INTO",
            "UPDATE ",
            "DELETE FROM",
        ):
            self.assertNotIn(forbidden, editor)

    def test_editor_contains_required_sections_and_scrollable_layout(self) -> None:
        editor = (SCRIPTS / "npc_editor.gd").read_text()

        for token in (
            "Identity",
            "Visuals",
            "Movement",
            "Interaction",
            "Dialogue Reference",
            "Authoring Notes",
            "Runtime and Placement Guidance",
            "Preview",
            "Reference Diagnostics",
            "Exact Logical Changes",
            "Validation",
            "ScrollContainer.new()",
            "catalog_panel",
            "form_panel",
            "preview_panel",
        ):
            self.assertIn(token, editor)

    def test_complete_payload_includes_every_t5_field_without_placement_or_quest_fields(self) -> None:
        editor = (SCRIPTS / "npc_editor.gd").read_text()
        payload = editor.split("func _payload() -> Dictionary:", 1)[1].split("func _on_search_changed", 1)[0]

        for token in (
            '"display_name": _display_name.text',
            '"visual_texture_path": _visual_path.text',
            '"source_width": int(_source_width.value)',
            '"source_height": int(_source_height.value)',
            '"visual_anchor_offset_x": float(_anchor_x.value)',
            '"visual_anchor_offset_y": float(_anchor_y.value)',
            '"visual_render_scale": float(_render_scale.value)',
            '"footprint_width_tiles": int(_footprint_width.value)',
            '"footprint_height_tiles": int(_footprint_height.value)',
            '"movement_behavior": _selected_metadata(_movement_behavior)',
            '"wander_radius_tiles": int(_wander_radius.value)',
            '"tick_interval_ms": int(_tick_interval.value)',
            '"idle_chance": float(_idle_chance.value)',
            '"interaction_enabled": interaction_enabled',
            '"interaction_range_tiles": int(_interaction_range.value)',
            '"default_interaction": _selected_metadata(_default_interaction)',
            '"default_dialogue_id": _optional_payload(dialogue_value)',
            '"notes": _optional_payload(_notes.text)',
            '"expected_updated_at_utc": _current_npc.get("updated_at_utc", null)',
            '"preview_signature": null',
        ):
            self.assertIn(token, payload)

        for forbidden in (
            "spawn_id",
            "map_id",
            "region_id",
            "chunk_id",
            "tile_x",
            "tile_y",
            "home_tile",
            "patrol",
            '"facing"',
            "quest_id",
            "QuestDefinition",
            "service_script",
        ):
            self.assertNotIn(forbidden, payload)

    def test_new_flow_and_saved_flow_manage_identity_and_timestamp(self) -> None:
        editor = (SCRIPTS / "npc_editor.gd").read_text()

        for token in (
            "func _start_new_npc",
            "_current_npc = {}",
            "_is_new = true",
            '_updated.text = "Not saved"',
            "_npc_id.editable = _schema_available",
            "func _load_npc",
            "_is_new = false",
            "_npc_id.editable = false",
            '_updated.text = str(payload.get("updated_at_utc", "Unknown"))',
            "_npc_id.editable = editable and _is_new",
        ):
            self.assertIn(token, editor)

        new_flow = editor.split("func _start_new_npc", 1)[1].split("func _preview", 1)[0]
        self.assertNotIn("Time.get", new_flow)
        self.assertNotIn("updated_at_utc", new_flow)

    def test_contextual_movement_interaction_and_dialogue_controls(self) -> None:
        editor = (SCRIPTS / "npc_editor.gd").read_text()

        for token in (
            "Movement behavior is reusable. Tiled placement supplies the NPC's home coordinate and initial facing.",
            "func _update_movement_controls",
            'movement == "random_wander"',
            'movement == "static"',
            "_wander_radius.value = 0",
            "T5 currently supports one server-authoritative Talk interaction.",
            "func _update_interaction_controls",
            "var dialogue_value := _dialogue_id.text.strip_edges() if interaction_enabled else \"\"",
            "_dialogue_id.text = \"\"",
            "_dialogue_options.disabled = not enabled",
            "supports_complete_dialogue_reference_validation",
            "can_validate_dialogue_references",
            "Runtime dialogue catalog visibility is incomplete",
        ):
            self.assertIn(token, editor)

    def test_notes_visual_preview_and_capability_statuses_are_visible(self) -> None:
        editor = (SCRIPTS / "npc_editor.gd").read_text()

        for token in (
            "Authoring notes are not exported to the runtime NPC catalog.",
            "class NpcVisualPreview",
            "Preview uses the host-resolved asset path.",
            "Preview uses the configured game_client_assets root.",
            "footprint_tiles",
            "anchor_offset",
            "render_scale",
            '"south"',
            '"west"',
            '"east"',
            '"north"',
            "supports_runtime_npc_catalog",
            "supports_quest_authoring",
            "supports_multiple_interactions",
            "Not yet implemented",
            "Not supported",
            "Placement is authored in Tiled using npc_definition_id.",
        ):
            self.assertIn(token, editor)

    def test_preview_apply_lifecycle_and_reference_diagnostics(self) -> None:
        editor = (SCRIPTS / "npc_editor.gd").read_text()

        for token in (
            'payload["target_operation"] = _selected_metadata(_operation)',
            "payload.erase(\"preview_signature\")",
            "_client.preview_npc(npc_definition_id, payload)",
            'str(payload.get("preview_signature", ""))',
            "_workspace_support.preview_signature",
            "_workspace_support.can_apply(operation, preview_signature)",
            'payload["preview_signature"] = preview_signature',
            "_client.save_npc_draft(npc_definition_id, payload)",
            "_client.publish_npc(npc_definition_id, expected, preview_signature)",
            "_client.disable_npc(npc_definition_id, expected, preview_signature)",
            "_client.delete_npc(npc_definition_id, expected, preview_signature)",
            "_delete_button.pressed.connect(_preview_delete)",
            "_clear_preview()",
            "_reload_npc_id = npc_id",
            "_client.load_npcs(_search.text)",
            "_client.load_npc(npc_id)",
            "npc_version_conflict",
            "func _render_reference_summary",
            "known_reference_count",
            "reference_sources",
            "reference_check_complete",
            "Reference visibility is incomplete until runtime/Tiled handoff work is finished.",
        ):
            self.assertIn(token, editor)

    def test_docs_mark_npc_workspace_and_runtime_hardening(self) -> None:
        docs = "\n".join(
            [
                (ROOT / "README.md").read_text(),
                (ROOT / "docs" / "ROADMAP.md").read_text(),
                (ROOT / "docs" / "ARCHITECTURE.md").read_text(),
                (ROOT / "docs" / "API_V1.md").read_text(),
                (ROOT / "docs" / "GODOT_WORKSPACE_SUPPORT.md").read_text(),
                (ROOT / "docs" / "T5_NPC_AUTHORING_PLAN.md").read_text(),
                (ROOT / "docs" / "T5_NPC_ACCEPTANCE.md").read_text(),
                (ROOT / "integrations" / "mmo-project" / "README.md").read_text(),
            ]
        )

        for token in (
            "T5F runtime/reference hardening implemented",
            "The NPCs workspace authors reusable definitions only; placement remains in Tiled",
            "quest-authoring and multiple-interaction capability",
            "supports_runtime_npc_catalog = true",
            "database, generated chunk, or Tiled source spawn references block disable",
        ):
            self.assertIn(token, docs)

        for forbidden in (
            "runtime NPC catalog export implemented",
            "Quest authoring implemented",
            "Tiled placement editor implemented",
        ):
            self.assertNotIn(forbidden, docs)

    def test_mmo_project_checkout_is_unchanged_except_nested_repo_pointer(self) -> None:
        result = subprocess.run(
            ["git", "status", "--short"],
            cwd=MMO_PROJECT,
            check=True,
            text=True,
            capture_output=True,
        )
        allowed_paths = (
            "docs/development/CONTENT_AUTHORING_GUIDE.md",
            "prototype/importer/",
            "prototype/server/features/README.md",
            "prototype/server/features/npcs/application/NpcRuntimeService.cs",
            "prototype/server/features/static_content/application/",
            "prototype/shared/maps/generated/starter_region/",
            "prototype/shared/maps/npcs/",
            "prototype/shared/maps/tiled/mmoproject.tmx",
            "prototype/shared/maps/tiled/regions/starter_region.tmj",
            "prototype/shared/maps/tiled/regions/starter_region.tmx",
            "prototype/sql/",
            "prototype/tests/MMO.Project.Prototype.MapPublisher.Tests/MapPublisher/NpcCatalogExporterTests.cs",
            "prototype/tests/MMO.Project.Prototype.Server.Tests/CombatActorRuntimeProviderTests.cs",
            "prototype/tests/MMO.Project.Prototype.Server.Tests/GeneratedRegionRuntimeAdapterTests.cs",
            "prototype/tests/MMO.Project.Prototype.Server.Tests/MapPublisher/",
            "prototype/tools/MapPublisher/",
            "tools/MMO-Content-Studio",
            "tools/mmoproject.tiled-session",
        )
        unexpected = []
        for line in result.stdout.splitlines():
            path = line[3:]
            if not path.startswith(allowed_paths):
                unexpected.append(line)
        self.assertEqual([], unexpected)


if __name__ == "__main__":
    unittest.main()
