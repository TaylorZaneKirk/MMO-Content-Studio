#!/usr/bin/env python3
"""Source contracts for A3 item equipped-visual authoring."""

from pathlib import Path
import unittest


ROOT = Path(__file__).resolve().parents[2]


class A3ItemEquippedVisualAuthoringTests(unittest.TestCase):
    def test_migration_adds_equipped_visual_tables_and_touch_triggers(self) -> None:
        migration = (ROOT / "integrations" / "mmo-project" / "prototype" / "sql" / "028_item_equipped_visuals.sql").read_text()
        for token in (
            "CREATE TABLE IF NOT EXISTS item_equipped_visuals",
            "CREATE TABLE IF NOT EXISTS item_equipped_visual_pose_anchors",
            "asset_key",
            "rig_id",
            "binding_type",
            "render_layer_id",
            "socket_id",
            "secondary_socket_id",
            "grip_anchor_x",
            "grip_anchor_y",
            "touch_item_definition_updated_at",
            "item_equipped_visuals_touch_item_updated_at",
            "item_equipped_visual_pose_anchors_touch_item_updated_at",
        ):
            self.assertIn(token, migration)

    def test_item_contracts_and_services_extend_unified_aggregate_in_place(self) -> None:
        contracts = (ROOT / "host" / "Contracts" / "ItemContracts.cs").read_text()
        authoring = (ROOT / "host" / "Services" / "UnifiedItemAuthoringService.cs").read_text()
        validator = (ROOT / "host" / "Services" / "UnifiedItemValidator.cs").read_text()
        repository = (ROOT / "host" / "Persistence" / "UnifiedItemRepository.cs").read_text()
        feature = (ROOT / "host" / "Features" / "Items" / "ItemAuthoringFeature.cs").read_text()

        for token in (
            "ItemEquippedVisualDefinition",
            "ItemEquippedVisualDraft",
            'JsonPropertyName("equipped_visual")',
            'JsonPropertyName("actor_rig_catalog")',
            'JsonPropertyName("equipped_visual_binding_types")',
        ):
            self.assertIn(token, contracts)
        for token in (
            "ActorAppearanceCatalogService",
            "actorRigCatalog",
            "equipment.EquippedVisual",
        ):
            self.assertIn(token, authoring)
        for token in (
            "ValidateEquippedVisual",
            "unknown_equipped_visual_rig",
            "secondary_socket_not_supported",
            "missing_grip_anchor_direction",
        ):
            self.assertIn(token, validator)
        for token in (
            "LoadEquippedVisualAsync",
            "LoadEquippedVisualGripAnchorsAsync",
            "ReplaceEquippedVisualAsync",
            '"item_equipped_visuals"',
            '"item_equipped_visual_pose_anchors"',
        ):
            self.assertIn(token, repository)
        self.assertIn("AddSingleton<ActorAppearanceCatalogService>()", feature)

    def test_editor_and_preview_upgrade_existing_equipped_appearance_section(self) -> None:
        editor = (ROOT / "content-studio" / "scripts" / "item_editor.gd").read_text()
        preview = (ROOT / "content-studio" / "scripts" / "paper_doll_preview.gd").read_text()

        for token in (
            "Enable authored equipped visual metadata",
            "Visual asset key",
            "Render layer",
            "Socket",
            "Attachment X/Y",
            "Actual game scale",
            '"equipped_visual": _equipped_visual_payload()',
            "_paper_doll_preview.configure_rig_catalog",
            "_on_paper_doll_grip_anchor_changed",
            "_copy_previous_pose_anchor",
            "_clear_current_pose_anchor",
        ):
            self.assertIn(token, editor)
        for token in (
            "signal grip_anchor_changed",
            "configure_rig_catalog",
            "resolve_source_pixel_offset",
            "_apply_drag_position",
            "_resolve_rig_socket_position",
        ):
            self.assertIn(token, preview)
        self.assertNotIn("const LAYER_ORDER", preview)

    def test_item_schema_health_tracks_new_tables_and_triggers(self) -> None:
        manifest = (ROOT / "host" / "Features" / "Items" / "ItemSchemaRequirements.cs").read_text()
        for token in (
            'Table("item_equipped_visuals")',
            'Table("item_equipped_visual_pose_anchors")',
            'Constraint("item_equipped_visuals_binding_type_check")',
            'Constraint("item_equipped_visual_pose_anchors_frame_check")',
            'Trigger(\n            "item_equipped_visuals",',
            'Trigger(\n            "item_equipped_visual_pose_anchors",',
        ):
            self.assertIn(token, manifest)

    def test_foreground_overlays_remain_canonical_rig_metadata(self) -> None:
        contracts = (ROOT / "host" / "Contracts" / "ActorAppearanceContracts.cs").read_text()
        parser = (ROOT / "host" / "Services" / "ActorAppearanceCatalogService.cs").read_text()
        preview = (ROOT / "content-studio" / "scripts" / "paper_doll_preview.gd").read_text()
        item_contracts = (ROOT / "host" / "Contracts" / "ItemContracts.cs").read_text()
        migration = (ROOT / "integrations" / "mmo-project" / "prototype" / "sql" / "028_item_equipped_visuals.sql").read_text()

        for token in (
            "ActorRigForegroundOverlayDefinition",
            "SourcePixelRectangleDefinition",
            'JsonPropertyName("foreground_overlays")',
            "TryReadForegroundOverlays",
            "TryReadOptionalDirectionalRectangles",
            "_render_foreground_overlays",
            "_resolve_foreground_overlay_source_rect",
            "AtlasTexture",
        ):
            self.assertIn(token, contracts + parser + preview)

        self.assertNotIn("foreground_overlay", item_contracts)
        self.assertNotIn("foreground_overlay", migration)

    def test_studio_contract_tracks_the_runtime_foreground_overlay_pose(self) -> None:
        runtime_catalog_path = ROOT.parent.parent / "prototype" / "client" / "actors" / "appearance" / "data" / "rigs" / "catalog_v1.json"
        self.assertTrue(runtime_catalog_path.is_file())

        import json

        catalog = json.loads(runtime_catalog_path.read_text())
        rig = next(value for value in catalog["rigs"] if value["rig_id"] == "humanoid_v1")
        overlay = rig["foreground_overlays"]["right_hand_primary_grip"]
        rect = overlay["source_rect_by_direction"]["N"]["1"]

        self.assertEqual("right_hand_primary", overlay["socket_id"])
        self.assertEqual("body", overlay["source_layer_id"])
        self.assertEqual(40, overlay["z_index_by_direction"]["N"])
        self.assertEqual({"x": 120, "y": 98, "width": 16, "height": 16}, rect)

    def test_studio_tracks_the_runtime_left_hand_socket_and_optional_overlay(self) -> None:
        runtime_catalog_path = ROOT.parent.parent / "prototype" / "client" / "actors" / "appearance" / "data" / "rigs" / "catalog_v1.json"
        editor = (ROOT / "content-studio" / "scripts" / "item_editor.gd").read_text()

        import json

        catalog = json.loads(runtime_catalog_path.read_text())
        rig = next(value for value in catalog["rigs"] if value["rig_id"] == "humanoid_v1")
        socket = rig["sockets"]["left_hand_primary"]
        overlay = rig["foreground_overlays"]["left_hand_primary_grip"]

        self.assertEqual({"x": 52, "y": 84}, socket["N"]["1"])
        self.assertEqual({"x": 116, "y": 84}, socket["S"]["4"])
        self.assertEqual("left_hand_primary", overlay["socket_id"])
        self.assertEqual("body", overlay["source_layer_id"])
        self.assertIsNone(overlay["source_rect_by_direction"]["S"]["4"])
        self.assertIn("_default_socket_id_for_equipment_slot", editor)
        self.assertIn('return "left_hand_primary"', editor)


if __name__ == "__main__":
    unittest.main()
