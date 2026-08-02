#!/usr/bin/env python3
"""Fast source-level checks for the T2 consumable-authoring slice."""

from __future__ import annotations

import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]


class T2SourceContractTests(unittest.TestCase):
    def test_t2_routes_exist(self) -> None:
        program = (ROOT / "host" / "Program.cs").read_text()
        for route in (
            "/consumables/options",
            "/consumables/{{itemId}}",
            "/consumables/{{itemId}}/preview",
            "/consumables/{{itemId}}/draft",
            "/consumables/{{itemId}}/publish",
            "/consumables/{{itemId}}/disable",
        ):
            self.assertIn(route, program)

    def test_consumable_schema_is_declarative_and_ordered(self) -> None:
        migration = (
            ROOT
            / "integrations"
            / "mmo-project"
            / "prototype"
            / "sql"
            / "017_item_consumable_profiles.sql"
        ).read_text().lower()
        for table in (
            "item_consumable_profiles",
            "item_consumable_requirements",
            "item_consumable_effects",
        ):
            self.assertIn(f"create table if not exists {table}", migration)
        self.assertIn("requirement_index", migration)
        self.assertIn("effect_index", migration)
        self.assertIn("restore_resource", migration)
        self.assertIn("minimum_amount", migration)
        self.assertIn("maximum_amount", migration)
        self.assertIn("item_consumable_effects_amount_range_check", migration)
        self.assertIn("skill_minimum", migration)
        self.assertIn("item_consumable_requirements_skill_id_fkey", migration)
        self.assertIn("item_consumable_requirements_identity_key", migration)
        self.assertIn("item_consumable_effects_identity_key", migration)
        self.assertIn("requirement_index between 0 and 15", migration)
        self.assertIn("effect_index between 0 and 15", migration)
        self.assertNotIn("script_body", migration)
        self.assertNotIn("code_expression", migration)

    def test_consumable_save_is_one_transaction_and_replaces_children(self) -> None:
        repository = (ROOT / "host" / "Persistence" / "ConsumableItemRepository.cs").read_text()
        service = (ROOT / "host" / "Services" / "ConsumableItemAuthoringService.cs").read_text()
        self.assertGreaterEqual(repository.count("BeginTransactionAsync"), 2)
        self.assertIn("ReplaceRequirementsAsync", repository)
        self.assertIn("ReplaceEffectsAsync", repository)
        self.assertIn("for update of i", repository.lower())
        self.assertIn("reload-and-verify", service)

    def test_publication_requires_effect_and_valid_references(self) -> None:
        validator = (ROOT / "host" / "Services" / "ConsumableItemValidator.cs").read_text()
        for code in (
            "consumable_has_no_effects",
            "result_item_not_found",
            "result_item_not_published",
            "unknown_requirement_skill",
            "duplicate_resource_effect",
        ):
            self.assertIn(code, validator)
        self.assertIn("ValidForPublication", validator)



    def test_current_food_behavior_is_seeded_without_overwrite(self) -> None:
        migration = (
            ROOT
            / "integrations"
            / "mmo-project"
            / "prototype"
            / "sql"
            / "017_item_consumable_profiles.sql"
        ).read_text()
        self.assertIn("legacy_food", migration)
        self.assertIn("('Apple', 'eat', 2, 4", migration)
        self.assertIn("('Cooked Pig', 'eat', 21, 28", migration)
        self.assertIn("ON CONFLICT (item_id) DO NOTHING", migration)

    def test_restore_effect_preserves_current_random_range_semantics(self) -> None:
        contracts = (ROOT / "host" / "Contracts" / "ConsumableContracts.cs").read_text()
        validator = (ROOT / "host" / "Services" / "ConsumableItemValidator.cs").read_text()
        editor = (ROOT / "content-studio" / "scripts" / "consumable_editor.gd").read_text()
        self.assertIn("minimum_amount", contracts)
        self.assertIn("maximum_amount", contracts)
        self.assertIn("MaximumAmount < effect.MinimumAmount", validator)
        self.assertIn("minimum_amount", editor)
        self.assertIn("maximum_amount", editor)

    def test_true_instance_charges_are_not_faked(self) -> None:
        contracts = (ROOT / "host" / "Contracts" / "ConsumableContracts.cs").read_text()
        integration = (ROOT / "integrations" / "mmo-project" / "README.md").read_text()
        self.assertIn("SupportsInstanceCharges", contracts)
        self.assertIn("result_item_id", integration)
        self.assertIn("remain deferred", integration)

    def test_basic_item_workspace_rejects_consumable_profiles(self) -> None:
        repository = (ROOT / "host" / "Persistence" / "BasicItemRepository.cs").read_text()
        service = (ROOT / "host" / "Services" / "BasicItemAuthoringService.cs").read_text()
        self.assertIn("has_consumable_profile", repository)
        self.assertIn("HasConsumableProfile", repository)
        self.assertIn('"Consumable"', service)

    def test_godot_consumable_editor_uses_preview_before_apply(self) -> None:
        editor = (ROOT / "content-studio" / "scripts" / "consumable_editor.gd").read_text()
        self.assertIn("_preview_signature", editor)
        self.assertIn("Preview the operation again", editor)
        self.assertIn("Apply Previewed Operation", editor)
        self.assertIn("_collect_requirements", editor)
        self.assertIn("_collect_effects", editor)


    def test_godot_consumable_functions_are_not_duplicated(self) -> None:
        editor = (ROOT / "content-studio" / "scripts" / "consumable_editor.gd").read_text()
        function_names = [
            line.split("func ", 1)[1].split("(", 1)[0]
            for line in editor.splitlines()
            if line.startswith("func ")
        ]
        self.assertEqual(len(function_names), len(set(function_names)))

    def test_godot_consumable_editor_has_no_sql_or_database_driver(self) -> None:
        editor = (ROOT / "content-studio" / "scripts" / "consumable_editor.gd").read_text().lower()
        self.assertNotIn("npgsql", editor)
        self.assertNotIn("insert into", editor)
        self.assertNotIn("update item_", editor)

    def test_health_requires_t2_schema(self) -> None:
        health = (ROOT / "host" / "Services" / "AuthoringHealthService.cs").read_text()
        for table in (
            "item_consumable_profiles",
            "item_consumable_requirements",
            "item_consumable_effects",
            "skill_definitions",
        ):
            self.assertIn(table, health)
        settings = (ROOT / "host" / "appsettings.json").read_text()
        self.assertIn("prototype-equipment-authoring-v1", settings)


    def test_published_result_items_are_database_guarded(self) -> None:
        migration = (
            ROOT
            / "integrations"
            / "mmo-project"
            / "prototype"
            / "sql"
            / "017_item_consumable_profiles.sql"
        ).read_text()
        basic_repository = (ROOT / "host" / "Persistence" / "BasicItemRepository.cs").read_text()
        basic_service = (ROOT / "host" / "Services" / "BasicItemAuthoringService.cs").read_text()
        health = (ROOT / "host" / "Services" / "AuthoringHealthService.cs").read_text()
        self.assertIn("item_definitions_consumable_result_publication_guard", migration)
        self.assertIn("item_consumable_profiles_result_publication_guard", migration)
        self.assertIn("HasPublishedConsumableResultReferencesAsync", basic_repository)
        self.assertIn("item_has_published_consumable_references", basic_service)
        self.assertIn("item_definitions_consumable_result_publication_guard", health)
        self.assertIn("item_consumable_requirements_skill_id_fkey", health)

    def test_publish_revalidates_references_inside_transaction(self) -> None:
        repository = (ROOT / "host" / "Persistence" / "ConsumableItemRepository.cs").read_text()
        self.assertIn("EnsurePublicationReferencesAsync", repository)
        self.assertIn("for share of result", repository.lower())
        self.assertIn("ConsumablePublicationIntegrityException", repository)

    def test_runtime_integration_gap_is_explicit(self) -> None:
        validator = (ROOT / "host" / "Services" / "ConsumableItemValidator.cs").read_text()
        self.assertIn("runtime_consumption_integration_pending", validator)
        self.assertIn("still needs the T2 runtime-consumption integration", validator)
        self.assertIn("ValidationSeverity.Warning", validator)


if __name__ == "__main__":
    unittest.main()
