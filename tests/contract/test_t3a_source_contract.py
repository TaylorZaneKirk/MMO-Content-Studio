#!/usr/bin/env python3
"""Fast source-level checks for completed T3A wearable-equipment authoring."""

from __future__ import annotations

import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]


class T3ASourceContractTests(unittest.TestCase):
    def test_t3a_read_and_mutation_routes_exist(self) -> None:
        feature = (
            ROOT
            / "host"
            / "Features"
            / "Equipment"
            / "EquipmentAuthoringFeature.cs"
        ).read_text()
        self.assertIn(
            'MapGroup($"{AuthoringApi.RoutePrefix}/equipment")',
            feature,
        )
        for route in (
            'MapGet("/options"',
            'MapGet(string.Empty',
            'MapGet("/{itemId}"',
            'MapPost("/{itemId}/preview"',
            'MapPut("/{itemId}/draft"',
            'MapPost("/{itemId}/publish"',
            'MapPost("/{itemId}/disable"',
            'MapPost("/{itemId}/delete"',
        ):
            self.assertIn(route, feature)
        for service in (
            "EquipmentItemRepository",
            "EquipmentItemValidator",
            "EquipmentItemAuthoringService",
        ):
            self.assertIn(f"AddSingleton<{service}>()", feature)

    def test_contract_makes_equipability_explicit(self) -> None:
        contracts = (ROOT / "host" / "Contracts" / "EquipmentContracts.cs").read_text()
        for token in (
            'JsonPropertyName("equippable")',
            "SaveEquipmentDraftRequest",
            "EquipmentPreviewRequest",
            "EquipmentValidationResponse",
            "EquipmentMutationResponse",
            'JsonPropertyName("can_remove_equipability")',
            'JsonPropertyName("combat_bonuses")',
        ):
            self.assertIn(token, contracts)

    def test_not_equippable_clears_all_dependent_metadata_transactionally(self) -> None:
        repository = (ROOT / "host" / "Persistence" / "EquipmentItemRepository.cs").read_text()
        self.assertIn("BeginTransactionAsync", repository)
        self.assertIn("runtime_enabled = false", repository)
        self.assertIn("DeleteEquipmentMetadataAsync", repository)
        for table in (
            "item_skill_requirements",
            "item_skill_modifiers",
            "item_combat_profiles",
            "item_combat_bonuses",
        ):
            self.assertIn(f'ExecuteDeleteAsync(connection, transaction, "{table}"', repository)
        self.assertIn("equipment_slot_id = @equipment_slot_id", repository)
        self.assertIn("required_strength = @required_strength", repository)
        self.assertIn("LoadAggregateAsync(connection, transaction", repository)
        self.assertIn("CommitAsync", repository)

    def test_validator_explains_legacy_misclassification_cleanup(self) -> None:
        validator = (ROOT / "host" / "Services" / "EquipmentItemValidator.cs").read_text()
        self.assertIn("equipment_metadata_will_be_removed", validator)
        self.assertIn("Chunk of Iron", validator)
        self.assertIn("non_equippable_metadata_not_empty", validator)
        self.assertIn("weapon_or_tool_requires_t3b", validator)
        self.assertIn("Turn off Equippable", validator)

    def test_t3a_keeps_weapon_editing_deferred_but_allows_declassification(self) -> None:
        service = (ROOT / "host" / "Services" / "EquipmentItemAuthoringService.cs").read_text()
        repository = (ROOT / "host" / "Persistence" / "EquipmentItemRepository.cs").read_text()
        self.assertIn('slotId is "left_hand" or "right_hand"', repository)
        self.assertIn("CanRemoveEquipability", service)
        self.assertIn("WeaponOrTool", service)
        self.assertIn("draft.Equippable && existing.HasCombatProfile", repository)
        self.assertIn("!record.HasConsumableProfile && EquipmentItemRepository.HasEquipmentMetadata(record)", service)

    def test_preview_before_apply_and_reload_verification_are_required(self) -> None:
        service = (ROOT / "host" / "Services" / "EquipmentItemAuthoringService.cs").read_text()
        editor = (ROOT / "content-studio" / "scripts" / "equipment_editor.gd").read_text()
        for token in (
            "PreviewAsync",
            "CalculateChanges",
            "saved equipment aggregate failed reload-and-verify",
            "equipment publication change failed reload-and-verify",
        ):
            self.assertIn(token.lower(), service.lower())
        for token in (
            "preview_equipment",
            "save_equipment_draft",
            "publish_equipment",
            "disable_equipment",
            "_workspace_support.can_apply",
            "_workspace_support.accept_preview",
            "Preview the operation again",
        ):
            self.assertIn(token, editor)

    def test_godot_editor_exposes_equippable_toggle_and_full_wearable_form(self) -> None:
        editor = (ROOT / "content-studio" / "scripts" / "equipment_editor.gd").read_text()
        paper_doll = (ROOT / "content-studio" / "scripts" / "paper_doll_preview.gd").read_text()
        for token in (
            'class_name EquipmentEditor',
            '"Equippable"',
            "Item can be equipped",
            "Wearable slot",
            "Required strength",
            "Additional skill requirements",
            "Skill modifiers while equipped",
            "Combat bonuses",
            "Directional paper-doll preview",
            "_update_paper_doll_preview",
            "Chunk of Iron",
            "PAPER_DOLL_PREVIEW_SCRIPT",
            "_paper_doll_preview.update",
        ):
            self.assertIn(token, editor)
        self.assertIn('"equippable": _equippable.button_pressed', editor)
        self.assertIn('"equipment_slot_id": _selected_metadata(_slot) if _equippable.button_pressed else null', editor)
        self.assertIn('path_join("actors").path_join("player")', paper_doll)
        self.assertIn("DEFAULT_VISUAL_KEYS", paper_doll)
        self.assertIn("_frame_fallbacks", paper_doll)
        self.assertIn("STAGE_PADDING := 8.0", paper_doll)
        self.assertIn("ANCHOR_OFFSET := Vector2(-7, -7)", paper_doll)
        self.assertIn("_source_bounds", paper_doll)
        self.assertIn("_preview_scale", paper_doll)
        self.assertIn("_place_layer", paper_doll)
        self.assertNotIn("image.get_used_rect().size == Vector2i.ZERO", editor)
        self.assertIn("bonuses_variant is Dictionary", editor)

    def test_godot_client_supports_equipment_mutations(self) -> None:
        client = (ROOT / "content-studio" / "scripts" / "authoring_host_client.gd").read_text()
        for token in (
            "equipment_preview_received",
            "equipment_mutation_completed",
            "preview_equipment",
            "save_equipment_draft",
            "publish_equipment",
            "disable_equipment",
            "delete_equipment",
            "OP_EQUIPMENT_PREVIEW",
            "OP_EQUIPMENT_SAVE_DRAFT",
            "OP_EQUIPMENT_DELETE",
        ):
            self.assertIn(token, client)

    def test_health_requires_complete_existing_equipment_schema(self) -> None:
        schema = (
            ROOT
            / "host"
            / "Features"
            / "Equipment"
            / "EquipmentSchemaRequirements.cs"
        ).read_text()
        settings = (ROOT / "host" / "appsettings.json").read_text()
        options = (ROOT / "host" / "Configuration" / "AuthoringHostOptions.cs").read_text()
        for token in (
            "equipment_slot_definitions",
            "item_skill_requirements",
            "item_skill_modifiers",
            "item_combat_profiles",
            "item_combat_bonuses",
            "item_definitions_equipment_slot_id_fkey",
            "item_combat_profiles_attack_type_accuracy_style_check",
        ):
            self.assertIn(token, schema)
        self.assertIn("prototype-equipment-authoring-v1", settings)
        self.assertIn("prototype-equipment-authoring-v1", options)

    def test_catalog_and_scene_expose_equipment_workspace(self) -> None:
        catalog = (
            ROOT
            / "host"
            / "Features"
            / "Equipment"
            / "EquipmentCatalogSectionProvider.cs"
        ).read_text()
        scene = (ROOT / "content-studio" / "scenes" / "Main.tscn").read_text()
        self.assertIn('ContentType => "equipment"', catalog)
        self.assertIn("item.Equippable", catalog)
        self.assertIn('path="res://scripts/equipment_editor.gd"', scene)
        self.assertIn('[node name="Equipment" type="HBoxContainer"', scene)
        self.assertIn('text = "T4 Mob Authoring"', scene)

    def test_godot_equipment_functions_are_not_duplicated(self) -> None:
        editor = (ROOT / "content-studio" / "scripts" / "equipment_editor.gd").read_text()
        function_names = [
            line.split("func ", 1)[1].split("(", 1)[0]
            for line in editor.splitlines()
            if line.startswith("func ")
        ]
        self.assertEqual(len(function_names), len(set(function_names)))

    def test_godot_equipment_editor_has_no_sql_or_database_driver(self) -> None:
        editor = (ROOT / "content-studio" / "scripts" / "equipment_editor.gd").read_text().lower()
        for forbidden in ("npgsql", "insert into", "update item_", "delete from"):
            self.assertNotIn(forbidden, editor)

    def test_delete_operation_removes_equipment_aggregate(self) -> None:
        repository = (ROOT / "host" / "Persistence" / "EquipmentItemRepository.cs").read_text()
        service = (ROOT / "host" / "Services" / "EquipmentItemAuthoringService.cs").read_text()
        editor = (ROOT / "content-studio" / "scripts" / "equipment_editor.gd").read_text()

        self.assertIn("DeleteAsync", repository)
        self.assertIn("DeleteEquipmentMetadataAsync", repository)
        self.assertIn("delete from item_definitions", repository)
        self.assertIn("delete_requires_disabled_item", service)
        self.assertIn("item_delete_blocked_by_references", service)
        self.assertIn('["Delete", "delete"]', editor)
        self.assertIn("_delete_button.text = \"Delete\"", editor)
        self.assertIn("_delete_button.pressed.connect(_preview_delete)", editor)
        self.assertIn("func _preview_delete", editor)
        self.assertIn("delete_equipment(_item_id.text, expected)", editor)


if __name__ == "__main__":
    unittest.main()
