#!/usr/bin/env python3
"""Fast source-level checks for T3B hand-equipment domain foundation."""

from __future__ import annotations

import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
MMO_ROOT = ROOT.parents[1]


class T3BSourceContractTests(unittest.TestCase):
    def test_t3b_routes_and_services_are_feature_owned(self) -> None:
        feature = (
            ROOT
            / "host"
            / "Features"
            / "HandEquipment"
            / "HandEquipmentAuthoringFeature.cs"
        ).read_text()
        self.assertIn(
            'MapGroup($"{AuthoringApi.RoutePrefix}/hand-equipment")',
            feature,
        )
        for route in (
            'MapGet("/options"',
            "MapGet(string.Empty",
            'MapGet("/{itemId}"',
            'MapPost("/{itemId}/preview"',
            'MapPut("/{itemId}/draft"',
            'MapPost("/{itemId}/publish"',
            'MapPost("/{itemId}/disable"',
        ):
            self.assertIn(route, feature)
        for service in (
            "HandEquipmentRepository",
            "HandEquipmentAuthoringRegistry",
            "HandEquipmentItemValidator",
            "HandEquipmentAuthoringService",
        ):
            self.assertIn(f"AddSingleton<{service}>()", feature)

    def test_contract_exposes_complete_hand_equipment_aggregate(self) -> None:
        contracts = (
            ROOT / "host" / "Contracts" / "HandEquipmentContracts.cs"
        ).read_text()
        for token in (
            "HandEquipmentItemDefinition",
            'JsonPropertyName("equippable")',
            'JsonPropertyName("equipment_slot_id")',
            'JsonPropertyName("required_strength")',
            'JsonPropertyName("requirements")',
            'JsonPropertyName("skill_modifiers")',
            'JsonPropertyName("weapon_profile")',
            'JsonPropertyName("combat_bonuses")',
            'JsonPropertyName("tool_capabilities")',
            'JsonPropertyName("preview_signature")',
            "HandEquipmentPublicationRequest",
        ):
            self.assertIn(token, contracts)
        self.assertNotIn("two_handed", contracts)
        self.assertNotIn("durability", contracts.lower())
        self.assertNotIn("charges", contracts.lower())

    def test_repository_replaces_children_and_clears_stale_specializations(self) -> None:
        repository = (
            ROOT / "host" / "Persistence" / "HandEquipmentRepository.cs"
        ).read_text()
        for token in (
            "BeginTransactionAsync",
            "for update of i",
            "runtime_enabled = false",
            "ReplaceRequirementsAsync",
            "ReplaceModifiersAsync",
            "ReplaceWeaponProfileAsync",
            "ReplaceCombatBonusesAsync",
            "ReplaceToolCapabilitiesAsync",
            "DeleteAllEquipmentMetadataAsync",
            'ExecuteDeleteAsync(connection, transaction, "item_combat_profiles"',
            'ExecuteDeleteAsync(connection, transaction, "item_tool_capabilities"',
            "LoadAggregateAsync(connection, transaction",
            "CommitAsync",
        ):
            self.assertIn(token, repository)

    def test_validation_matches_runtime_weapon_and_tool_rules(self) -> None:
        validator = (
            ROOT / "host" / "Services" / "HandEquipmentItemValidator.cs"
        ).read_text()
        registry = (
            ROOT / "host" / "Services" / "HandEquipmentAuthoringRegistry.cs"
        ).read_text()
        for token in (
            "right_hand",
            "left_hand",
            "right_hand_weapon_profile_required",
            "left_hand_weapon_profile_not_runtime_supported",
            "unsupported_attack_family",
            "unsupported_attack_style",
            "invalid_weapon_range",
            "invalid_attack_speed_units",
            "duplicate_tool_capability",
            "unknown_tool_capability",
            "non_hand_specialization",
        ):
            self.assertIn(token, validator + registry)
        self.assertIn("CombatUnitMilliseconds = 600", registry)
        self.assertIn('new("melee", "Melee")', registry)
        self.assertNotIn("attack_speed_ms", validator + registry)

    def test_schema_migration_is_structural_and_bidirectionally_guarded(self) -> None:
        migration_paths = [
            ROOT
            / "integrations"
            / "mmo-project"
            / "prototype"
            / "sql"
            / "018_item_tool_capabilities.sql",
        ]
        mirrored = (
            MMO_ROOT
            / "prototype"
            / "sql"
            / "018_item_tool_capabilities.sql"
        )
        if mirrored.exists():
            migration_paths.append(mirrored)

        migrations = [path.read_text() for path in migration_paths]
        if len(migrations) == 2:
            self.assertEqual(migrations[0], migrations[1])

        for migration in migrations:
            for token in (
                "CREATE TABLE IF NOT EXISTS item_tool_capabilities",
                "capability_id TEXT NOT NULL",
                "capability_order INTEGER NOT NULL",
                "power_tier INTEGER NOT NULL DEFAULT 1",
                "item_tool_capabilities_hand_slot_guard",
                "item_definitions_tool_capability_slot_guard",
                "prevent_non_hand_slot_with_tool_capabilities",
                "DEFERRABLE INITIALLY DEFERRED",
                "'right_hand', 'left_hand'",
            ):
                self.assertIn(token, migration)
            self.assertNotIn("inventory_17_mining_hammer", migration)
            self.assertNotIn("INSERT INTO ITEM_TOOL_CAPABILITIES", migration.upper())
            for forbidden in ("durability", "ammo", "charges", "item_instance"):
                self.assertNotIn(forbidden, migration.lower())

    def test_t3b_has_no_godot_sql_or_full_editor_slice(self) -> None:
        godot_sources = "\n".join(
            path.read_text(errors="ignore")
            for path in (ROOT / "content-studio").rglob("*.gd")
        ).lower()
        for forbidden in ("insert into item_tool_capabilities", "npgsql", "delete from item_tool"):
            self.assertNotIn(forbidden, godot_sources)

        scene = (ROOT / "content-studio" / "scenes" / "Main.tscn").read_text()
        self.assertNotIn('text = "Weapons and Tools"', scene)

    def test_docs_track_t3b_foundation_scope(self) -> None:
        acceptance = (ROOT / "docs" / "T3B_ACCEPTANCE.md").read_text()
        api = (ROOT / "docs" / "API_V1.md").read_text()
        for token in (
            "weapon_profile",
            "tool_capabilities",
            "preview_signature",
            "right_hand",
            "left_hand",
            "attack_speed_units",
        ):
            self.assertIn(token, acceptance)
            self.assertIn(token, api)
        self.assertIn("handoff artifact", acceptance)
        self.assertIn("must be reviewed and applied", acceptance)
        self.assertNotIn("mirrored into the local MMO Project runtime path", acceptance)


if __name__ == "__main__":
    unittest.main()
