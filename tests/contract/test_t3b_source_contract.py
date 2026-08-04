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
            'MapPost("/{itemId}/delete"',
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
            "DeleteEquipmentMetadataAsync",
            'ExecuteDeleteAsync(connection, transaction, "item_combat_profiles"',
            'ExecuteDeleteAsync(connection, transaction, "item_tool_capabilities"',
            "LoadAggregateAsync(connection, transaction",
            "CommitAsync",
        ):
            self.assertIn(token, repository)
        self.assertIn("await ReplaceToolCapabilitiesAsync(connection, transaction, itemId, draft.ToolCapabilities, cancellationToken);", repository)

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

    def test_dirty_publication_previews_validate_requested_draft_shape(self) -> None:
        service = (
            ROOT / "host" / "Services" / "HandEquipmentAuthoringService.cs"
        ).read_text()

        for token in (
            "var hasUnsavedOperationChanges =",
            'operation is "publish" or "disable" or "delete"',
            "&& !EquivalentDraft(existing, requested)",
            'operation == "save_draft" || hasUnsavedOperationChanges',
            "? requested",
            ": FromRecord(existing)",
            'operation == "publish" && !hasUnsavedOperationChanges',
            "if (hasUnsavedOperationChanges)",
            '"unsaved_hand_equipment_changes"',
        ):
            self.assertIn(token, service)

    def test_default_catalog_hides_declassified_basic_items(self) -> None:
        service = (
            ROOT / "host" / "Services" / "HandEquipmentAuthoringService.cs"
        ).read_text()
        editor = (ROOT / "content-studio" / "scripts" / "hand_equipment_editor.gd").read_text()

        for token in (
            "var hasSearch = !string.IsNullOrWhiteSpace(search);",
            ".Where(record => VisibleInHandEquipmentCatalog(record, hasSearch))",
            "private static bool VisibleInHandEquipmentCatalog(",
            "EquipmentItemRepository.IsHandSlot(record.EquipmentSlotId)",
            "record.HasCombatProfile",
            "record.HasToolCapabilities",
            "hasSearch && EditableInHandEquipment(record)",
        ):
            self.assertIn(token, service)

        for token in (
            "func _belongs_in_hand_equipment_catalog(payload: Dictionary) -> bool:",
            "if not _belongs_in_hand_equipment_catalog(item):",
            "saved as Basic. It is now available in Basic Items.",
            "_client.load_hand_equipment(_search.text)",
        ):
            self.assertIn(token, editor)

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

    def test_godot_navigation_exposes_dedicated_weapons_and_tools_workspace(self) -> None:
        scene = (ROOT / "content-studio" / "scenes" / "Main.tscn").read_text()
        self.assertIn('path="res://scripts/hand_equipment_editor.gd"', scene)
        self.assertIn('[node name="Weapons & Tools" type="HBoxContainer"', scene)
        self.assertIn('script = ExtResource("5_hand_equipment")', scene)
        self.assertIn('[node name="Equipment" type="HBoxContainer"', scene)

    def test_godot_client_supports_hand_equipment_api_via_transport(self) -> None:
        client = (ROOT / "content-studio" / "scripts" / "authoring_host_client.gd").read_text()
        for token in (
            "hand_equipment_options_received",
            "hand_equipment_received",
            "hand_equipment_item_received",
            "hand_equipment_preview_received",
            "hand_equipment_mutation_completed",
            "load_hand_equipment_options",
            "load_hand_equipment",
            "load_hand_equipment_item",
            "preview_hand_equipment",
            "save_hand_equipment_draft",
            "publish_hand_equipment",
            "disable_hand_equipment",
            "delete_hand_equipment",
            '"/api/v1/hand-equipment/options"',
            '"/api/v1/hand-equipment%s"',
            '"/api/v1/hand-equipment/%s/preview"',
            '"/api/v1/hand-equipment/%s/draft"',
            '"/api/v1/hand-equipment/%s/publish"',
            '"/api/v1/hand-equipment/%s/disable"',
            '"/api/v1/hand-equipment/%s/delete"',
            "OP_HAND_EQUIPMENT_OPTIONS",
            "OP_HAND_EQUIPMENT_SAVE_DRAFT",
            "OP_HAND_EQUIPMENT_DELETE",
            "_transport.request",
        ):
            self.assertIn(token, client)
        self.assertNotIn("HTTPRequest.new()", client)

    def test_hand_equipment_editor_uses_shared_workspace_support(self) -> None:
        editor = (ROOT / "content-studio" / "scripts" / "hand_equipment_editor.gd").read_text()
        for token in (
            'class_name HandEquipmentEditor',
            'preload("res://scripts/authoring_workspace_support.gd")',
            "WORKSPACE_SUPPORT_SCRIPT.new()",
            "_workspace_support.clear_preview",
            "_workspace_support.accept_preview",
            "_workspace_support.can_apply",
            "_workspace_support.render_changes",
            "_workspace_support.render_validation",
            "_workspace_support.operation_name",
        ):
            self.assertIn(token, editor)
        for forbidden in (
            "var _preview_signature",
            "var _preview_operation",
            "var _preview_applicable",
            "func _render_changes",
            "func _render_validation",
        ):
            self.assertNotIn(forbidden, editor)

    def test_hand_equipment_editor_supports_weapon_tool_and_combined_payloads(self) -> None:
        editor = (ROOT / "content-studio" / "scripts" / "hand_equipment_editor.gd").read_text()
        for token in (
            '"weapon_profile": _weapon_profile_payload() if equippable and hand_slot else null',
            '"tool_capabilities": _collect_tool_capabilities()',
            "_weapon_enabled",
            "_add_tool_row",
            "_collect_tool_capabilities",
            "_tool_rows.get_children()",
            '"attack_speed_units": int(_weapon_speed_units.value)',
            "COMBAT_UNIT_MILLISECONDS := 600",
            "units x %d ms = %d ms",
            '"expected_updated_at_utc": _current_item.get("updated_at_utc", null)',
            'payload["preview_signature"] = preview_signature',
            "publish_hand_equipment(_item_id.text, expected, preview_signature)",
            "disable_hand_equipment(_item_id.text, expected, preview_signature)",
            "delete_hand_equipment(_item_id.text, expected, preview_signature)",
        ):
            self.assertIn(token, editor)
        self.assertNotIn("attack_speed_ms", editor)

    def test_delete_operation_removes_hand_equipment_aggregate(self) -> None:
        repository = (ROOT / "host" / "Persistence" / "HandEquipmentRepository.cs").read_text()
        service = (ROOT / "host" / "Services" / "HandEquipmentAuthoringService.cs").read_text()
        editor = (ROOT / "content-studio" / "scripts" / "hand_equipment_editor.gd").read_text()

        for table in (
            "item_skill_requirements",
            "item_skill_modifiers",
            "item_combat_profiles",
            "item_combat_bonuses",
            "item_tool_capabilities",
        ):
            self.assertIn(f'ExecuteDeleteAsync(connection, transaction, "{table}"', repository)
        self.assertIn("delete_requires_disabled_item", service)
        self.assertIn("item_delete_blocked_by_references", service)
        self.assertIn('["Delete", "delete"]', editor)
        self.assertIn("_delete_button.text = \"Delete\"", editor)
        self.assertIn("_delete_button.pressed.connect(_preview_delete)", editor)
        self.assertIn("func _preview_delete", editor)
        self.assertIn("preview_signature_mismatch", service)

    def test_hand_equipment_editor_surfaces_slot_rules_and_form_cleanup(self) -> None:
        editor = (ROOT / "content-studio" / "scripts" / "hand_equipment_editor.gd").read_text()
        for token in (
            "right_hand",
            "left_hand",
            "HAND_VISIBLE_SLOTS",
            "left_hand weapon publication is blocked",
            "right_hand supports current active weapon publication",
            "Not equippable: preview will clear slot",
            "Wearable slot selected: preview will remove hand-only weapon profile",
            "Tool capabilities work from inventory or equipment. Equipability is optional.",
            "_weapon_profile_payload() if equippable and hand_slot else null",
            '"tool_capabilities": _collect_tool_capabilities()',
            'if not _value:\n\t\t_select_option(_operation, "save_draft")',
        ):
            self.assertIn(token, editor)

    def test_paper_doll_behavior_is_shared_between_equipment_workspaces(self) -> None:
        equipment = (ROOT / "content-studio" / "scripts" / "equipment_editor.gd").read_text()
        hand = (ROOT / "content-studio" / "scripts" / "hand_equipment_editor.gd").read_text()
        paper_doll = (ROOT / "content-studio" / "scripts" / "paper_doll_preview.gd").read_text()
        for editor in (equipment, hand):
            self.assertIn('preload("res://scripts/paper_doll_preview.gd")', editor)
            self.assertIn("_paper_doll_preview.update", editor)
        for token in (
            'LAYER_ORDER := ["cape", "right_hand", "legs", "boots", "body", "left_hand", "gloves", "head"]',
            'DEFAULT_VISUAL_KEYS := {"head": "head1", "body": "defbod", "legs": "defbod"}',
            'ANCHOR_OFFSET := Vector2(-7, -7)',
            'if direction == "N" and not values.has(4):',
            'return 0 if direction == "W" else 30',
            'path_join("actors").path_join("player")',
            "normalize_visual_key",
        ):
            self.assertIn(token, paper_doll)

    def test_t3b_godot_has_no_sql_or_database_driver(self) -> None:
        godot_sources = "\n".join(
            path.read_text(errors="ignore")
            for path in (ROOT / "content-studio").rglob("*.gd")
        ).lower()
        for forbidden in ("insert into item_tool_capabilities", "npgsql", "delete from item_tool"):
            self.assertNotIn(forbidden, godot_sources)

    def test_no_bootstrap_or_recovery_artifacts_are_restored(self) -> None:
        forbidden_paths = (
            ROOT / ".t3b-bootstrap",
            ROOT / ".t3b-v2-stage",
            ROOT / ".github" / "workflows" / "apply-t3b.yml",
            ROOT / ".github" / "workflows" / "apply-t3b-repair.yml",
        )
        for path in forbidden_paths:
            self.assertFalse(path.exists(), str(path))

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
