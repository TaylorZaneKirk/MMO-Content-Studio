#!/usr/bin/env python3
"""Source contracts for the T4C Godot Mobs workspace."""

from __future__ import annotations

import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
SCRIPTS = ROOT / "content-studio" / "scripts"
MMO_PROJECT = ROOT.parents[1]


class T4CGodotMobWorkspaceTests(unittest.TestCase):
    def test_scene_exposes_dedicated_mobs_workspace(self) -> None:
        scene = (ROOT / "content-studio" / "scenes" / "Main.tscn").read_text()

        self.assertIn('path="res://scripts/mob_editor.gd"', scene)
        self.assertIn('[node name="Mobs" type="HBoxContainer" parent="Margin/Root/Tabs"]', scene)
        self.assertIn('script = ExtResource("6_mobs")', scene)
        self.assertIn('[node name="Environment" type="HBoxContainer" parent="Margin/Root/Tabs"]', scene)

    def test_client_supports_mob_api_via_transport(self) -> None:
        facade = (SCRIPTS / "authoring_host_client.gd").read_text()

        for token in (
            "signal mob_options_received",
            "signal mob_catalog_received",
            "signal mob_item_received",
            "signal mob_preview_received",
            "signal mob_mutation_completed",
            "func load_mob_options",
            "func load_mobs",
            "func load_mob",
            "func preview_mob",
            "func save_mob_draft",
            "func publish_mob",
            "func disable_mob",
            '"/api/v1/mobs/options"',
            '"/api/v1/mobs%s"',
            '"/api/v1/mobs/%s/preview"',
            '"/api/v1/mobs/%s/draft"',
            '"/api/v1/mobs/%s/publish"',
            '"/api/v1/mobs/%s/disable"',
            '"expected_updated_at_utc": expected_updated_at_utc',
            '"preview_signature": preview_signature',
            "_transport.request(operation, path, method, payload)",
        ):
            self.assertIn(token, facade)

        self.assertNotIn("HTTPRequest.new()", facade)
        self.assertNotIn("JSON.parse_string", facade)

    def test_editor_uses_shared_workspace_support_and_host_client(self) -> None:
        editor = (SCRIPTS / "mob_editor.gd").read_text()

        for token in (
            "class_name MobEditor",
            'preload("res://scripts/authoring_workspace_support.gd")',
            "WORKSPACE_SUPPORT_SCRIPT.new()",
            "@onready var _client: AuthoringHostClient = %AuthoringHostClient",
            "_client.mob_options_received.connect",
            "_client.mob_catalog_received.connect",
            "_client.mob_item_received.connect",
            "_client.mob_preview_received.connect",
            "_client.mob_mutation_completed.connect",
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
            "JSON.parse_string",
            "Npgsql",
            "PostgreSQL",
            "SELECT ",
            "INSERT INTO",
            "UPDATE ",
            "DELETE FROM",
            "var _preview_signature",
            "func _render_changes",
            "func _render_validation",
        ):
            self.assertNotIn(forbidden, editor)

    def test_editor_sends_complete_t4b_draft_payload_without_placement_fields(self) -> None:
        editor = (SCRIPTS / "mob_editor.gd").read_text()

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
            '"max_health": int(_max_health.value)',
            '"movement_speed_tiles_per_second": float(_movement_speed.value)',
            '"combat_faction_id": _selected_metadata(_faction)',
            '"can_proactively_target_hostile_mobs": proactive',
            '"mob_detection_radius_tiles": int(_detection_radius.value) if proactive else 0',
            '"mob_target_scan_interval_ms": int(_scan_interval.value) if proactive else 0',
            '"mob_target_scan_candidate_limit": int(_candidate_limit.value) if proactive else 0',
            '"primary_combat_profile": _combat_profile_payload() if _attack_enabled.button_pressed else null',
            '"combat_bonuses": _bonus_payload()',
            '"guaranteed_drops": _drop_payload()',
            '"expected_updated_at_utc": _current_mob.get("updated_at_utc", null)',
        ):
            self.assertIn(token, editor)

        for forbidden in (
            "spawn_id",
            "map_id",
            "region_id",
            "home_tile",
            "patrol",
            "respawn",
            "leash_radius",
        ):
            self.assertNotIn(forbidden, editor)

    def test_mob_bonus_fallback_fields_match_shared_contract(self) -> None:
        editor = (SCRIPTS / "mob_editor.gd").read_text()
        equipment_contract = (ROOT / "host" / "Contracts" / "EquipmentContracts.cs").read_text()
        expected_fields = (
            "attack_thrust",
            "attack_slash",
            "attack_crush",
            "attack_ranged",
            "attack_magic",
            "strength_melee",
            "strength_ranged",
            "strength_magic",
            "defence_thrust",
            "defence_slash",
            "defence_crush",
            "defence_ranged",
            "defence_magic",
        )

        fallback = editor.split("const DEFAULT_BONUS_FIELDS := [", 1)[1].split("]", 1)[0]
        self.assertEqual(26, fallback.count('"'))
        for field in expected_fields:
            self.assertIn(f'"{field}"', fallback)
            self.assertIn(f'JsonPropertyName("{field}")', equipment_contract)

        for stale_field in (
            "attack_stab",
            "defence_stab",
            "melee_strength",
            "ranged_strength",
            "magic_damage",
        ):
            self.assertNotIn(stale_field, fallback)

    def test_editor_models_current_runtime_combat_not_a_general_attack_system(self) -> None:
        editor = (SCRIPTS / "mob_editor.gd").read_text()

        for token in (
            "Primary Attack",
            '"attack_type": _selected_metadata(_attack_type)',
            '"accuracy_style": _selected_metadata(_accuracy_style)',
            '"minimum_range_tiles": int(_minimum_range.value)',
            '"maximum_range_tiles": int(_maximum_range.value)',
            '"attack_speed_units": int(_attack_speed_units.value)',
            '"attack_level": int(_attack_level.value)',
            '"strength_level": int(_strength_level.value)',
            '"defence_level": int(_defence_level.value)',
            "_attack_interval.text = \"%d units x %d ms = %d ms\"",
            "primary_combat_profile",
        ):
            self.assertIn(token, editor)

        for forbidden in (
            "attack_speed_ms",
            "weighted_attack",
            "attack_weight",
            "projectile",
            "status_effect",
            "special_attack",
            "cooldown_ms",
            "ability",
        ):
            self.assertNotIn(forbidden, editor)

    def test_editor_preserves_ordered_guaranteed_drops_only(self) -> None:
        editor = (SCRIPTS / "mob_editor.gd").read_text()

        for token in (
            "Guaranteed Drops",
            "_options.get(\"published_drop_items\"",
            "func _drop_payload() -> Array:",
            '"drop_order": order',
            '"item_id": item_id',
            '"stack_count": int(stack.value)',
            "func _move_drop",
            "func _drop_items_have_duplicates",
        ):
            self.assertIn(token, editor)

        for forbidden in (
            "probability",
            "weight",
            "drop_chance",
            "roll_group",
            "quantity_min",
            "quantity_max",
        ):
            self.assertNotIn(forbidden, editor)

    def test_editor_preview_apply_lifecycle_uses_signatures(self) -> None:
        editor = (SCRIPTS / "mob_editor.gd").read_text()

        for token in (
            'payload["target_operation"] = _selected_metadata(_operation)',
            "_client.preview_mob(mob_definition_id, payload)",
            'str(payload.get("preview_signature", ""))',
            "_workspace_support.preview_signature",
            "_workspace_support.can_apply(operation, preview_signature)",
            "publish_mob(mob_definition_id, expected, preview_signature)",
            "disable_mob(mob_definition_id, expected, preview_signature)",
            'payload["preview_signature"] = preview_signature',
            "save_mob_draft(mob_definition_id, payload)",
            "_clear_preview()",
            "_is_loading",
        ):
            self.assertIn(token, editor)

    def test_mob_startup_continues_after_hand_equipment_failure(self) -> None:
        facade = (SCRIPTS / "authoring_host_client.gd").read_text()
        connection_operations = facade.split("const CONNECTION_OPERATIONS := [", 1)[1].split("]", 1)[0]

        self.assertNotIn("OP_HAND_EQUIPMENT_OPTIONS", connection_operations)
        self.assertNotIn("OP_HAND_EQUIPMENT,", connection_operations)
        self.assertNotIn("OP_MOB_OPTIONS", connection_operations)
        self.assertIn("connection_state_changed.emit(\"connected\"", facade)
        self.assertIn("func _continue_after_hand_equipment_initialization", facade)
        self.assertIn("OP_HAND_EQUIPMENT:\n\t\t\thand_equipment_received.emit(data)\n\t\t\t_continue_after_hand_equipment_initialization()", facade)
        self.assertIn("if operation == OP_HAND_EQUIPMENT_OPTIONS or operation == OP_HAND_EQUIPMENT:", facade)
        self.assertIn("_request(OP_MOB_OPTIONS, \"/api/v1/mobs/options\")", facade)

    def test_visual_preview_uses_host_resolved_png_and_footprint(self) -> None:
        editor = (SCRIPTS / "mob_editor.gd").read_text()

        for token in (
            "class MobVisualPreview",
            "draw_rect(footprint",
            "draw_circle(anchor",
            "draw_texture_rect_region",
            "Image.load_from_file(file_path)",
            "asset_preview_file_path",
            '"visual_anchor_offset_x": float(_anchor_x.value)',
            '"visual_anchor_offset_y": float(_anchor_y.value)',
            "_render_scale",
            "_footprint_width",
            "_footprint_height",
        ):
            self.assertIn(token, editor)

    def test_mob_workspace_layout_keeps_labels_readable_and_route_errors_actionable(self) -> None:
        editor = (SCRIPTS / "mob_editor.gd").read_text()
        scene = (ROOT / "content-studio" / "scenes" / "Main.tscn").read_text()

        for token in (
            "label.custom_minimum_size = Vector2(170, 0)",
            'grid.add_theme_constant_override("h_separation", 12)',
            'grid.add_theme_constant_override("v_separation", 6)',
            'message.contains("route does not exist")',
            "restart the authoring host from this branch",
        ):
            self.assertIn(token, editor)

        self.assertIn('text = "T4 Mob Authoring"', scene)

    def test_t4c_workspace_does_not_author_tiled_placement(self) -> None:
        editor = (SCRIPTS / "mob_editor.gd").read_text()
        for forbidden in (
            "spawn_id",
            "map_id",
            "region_id",
            "home_tile",
            "leash_radius",
            "respawn",
            "patrol",
        ):
            self.assertNotIn(forbidden, editor)


if __name__ == "__main__":
    unittest.main()
