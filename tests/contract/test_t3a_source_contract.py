#!/usr/bin/env python3
"""Fast source-level checks for the T3A wearable-equipment read slice."""

from __future__ import annotations

import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]


class T3ASourceContractTests(unittest.TestCase):
    def test_t3a_routes_exist_without_mutation_routes(self) -> None:
        program = (ROOT / "host" / "Program.cs").read_text()
        for route in (
            "/equipment/options",
            "/equipment",
            "/equipment/{{itemId}}",
        ):
            self.assertIn(route, program)
        for route in (
            "/equipment/{{itemId}}/preview",
            "/equipment/{{itemId}}/draft",
            "/equipment/{{itemId}}/publish",
            "/equipment/{{itemId}}/disable",
        ):
            self.assertNotIn(route, program)

    def test_equipment_contract_reads_full_current_schema(self) -> None:
        contracts = (ROOT / "host" / "Contracts" / "EquipmentContracts.cs").read_text()
        for field in (
            "equipment_slot_id",
            "required_strength",
            "requirements",
            "skill_modifiers",
            "combat_profile",
            "combat_bonuses",
            "visual_asset_key",
        ):
            self.assertIn(field, contracts)
        for bonus in (
            "attack_thrust",
            "attack_slash",
            "attack_crush",
            "attack_ranged",
            "attack_magic",
            "strength_melee",
            "defence_magic",
        ):
            self.assertIn(bonus, contracts)

    def test_repository_reads_existing_game_tables_without_writes(self) -> None:
        repository = (ROOT / "host" / "Persistence" / "EquipmentItemRepository.cs").read_text()
        for table in (
            "equipment_slot_definitions",
            "item_skill_requirements",
            "item_skill_modifiers",
            "item_combat_profiles",
            "item_combat_bonuses",
        ):
            self.assertIn(table, repository)
        self.assertNotIn("insert into", repository.lower())
        self.assertNotIn("update item_", repository.lower())
        self.assertNotIn("delete from", repository.lower())
        self.assertNotIn("BeginTransactionAsync", repository)

    def test_t3a_defers_hand_slots_to_t3b(self) -> None:
        service = (ROOT / "host" / "Services" / "EquipmentItemAuthoringService.cs").read_text()
        repository = (ROOT / "host" / "Persistence" / "EquipmentItemRepository.cs").read_text()
        self.assertIn('"left_hand" or "right_hand"', repository)
        self.assertIn("WeaponOrTool", service)
        self.assertIn("deferredHandSlots", service)
        self.assertIn("!record.HasCombatProfile", service)

    def test_visual_asset_key_is_derived_not_persisted(self) -> None:
        service = (ROOT / "host" / "Services" / "EquipmentItemAuthoringService.cs").read_text()
        acceptance = (ROOT / "docs" / "T3A_ACCEPTANCE.md").read_text()
        self.assertIn("DeriveVisualAssetKey", service)
        self.assertIn("\\u2019", service)
        self.assertIn("legs", service)
        self.assertIn("does not store a separate visual asset override", service)
        self.assertIn("derived metadata", acceptance)

    def test_health_requires_t3a_schema(self) -> None:
        health = (ROOT / "host" / "Services" / "AuthoringHealthService.cs").read_text()
        settings = (ROOT / "host" / "appsettings.json").read_text()
        for table in (
            "equipment_slot_definitions",
            "item_skill_requirements",
            "item_skill_modifiers",
            "item_combat_profiles",
            "item_combat_bonuses",
        ):
            self.assertIn(table, health)
        self.assertIn("item_definitions_equipment_slot_id_fkey", health)
        self.assertIn("item_combat_profiles_attack_type_accuracy_style_check", health)
        self.assertIn("prototype-equipment-authoring-v1", settings)

    def test_catalog_exposes_equipment_workspace(self) -> None:
        catalog = (ROOT / "host" / "Services" / "ContentCatalogService.cs").read_text()
        self.assertIn('"equipment"', catalog)
        self.assertIn("EditableInEquipment", catalog)

    def test_godot_scene_has_equipment_tab(self) -> None:
        scene = (ROOT / "content-studio" / "scenes" / "Main.tscn").read_text()
        self.assertIn('path="res://scripts/equipment_editor.gd"', scene)
        self.assertIn('[node name="Equipment" type="HBoxContainer"', scene)
        self.assertIn('text = "T3A Wearable Equipment"', scene)

    def test_godot_client_loads_equipment_after_consumables(self) -> None:
        client = (ROOT / "content-studio" / "scripts" / "authoring_host_client.gd").read_text()
        for token in (
            "equipment_options_received",
            "equipment_received",
            "equipment_item_received",
            "/api/v1/equipment/options",
            "/api/v1/equipment",
            "load_equipment_item",
        ):
            self.assertIn(token, client)
        self.assertIn("RequestKind.EQUIPMENT_OPTIONS", client)

    def test_godot_equipment_editor_is_read_only(self) -> None:
        editor = (ROOT / "content-studio" / "scripts" / "equipment_editor.gd").read_text()
        for token in (
            "Read-only T3A view",
            "Deferred Hand Slots",
            "Skill requirements",
            "Skill modifiers",
            "Combat profile",
            "Combat bonuses",
            "Visible for context",
        ):
            self.assertIn(token, editor)
        self.assertNotIn("preview_equipment", editor)
        self.assertNotIn("save_equipment", editor)
        self.assertNotIn("publish_equipment", editor)
        self.assertNotIn("disable_equipment", editor)


if __name__ == "__main__":
    unittest.main()
