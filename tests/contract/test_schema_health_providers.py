#!/usr/bin/env python3
"""Source contracts for feature-provided database schema health requirements."""

from __future__ import annotations

import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
HOST = ROOT / "host"


class SchemaHealthProviderTests(unittest.TestCase):
    def test_health_service_aggregates_feature_requirements(self) -> None:
        service = (HOST / "Services" / "AuthoringHealthService.cs").read_text()
        for token in (
            "IEnumerable<IAuthoringSchemaRequirementProvider>",
            "provider.GetRequirements()",
            "DistinctBy(requirement => requirement.Key",
            "_schemaHealthInspector.CheckAsync",
        ):
            self.assertIn(token, service)
        for hardcoded in (
            '"item_definitions"',
            '"item_consumable_profiles"',
            '"item_combat_profiles"',
            "information_schema.tables",
        ):
            self.assertNotIn(hardcoded, service)

    def test_inspector_owns_information_schema_queries(self) -> None:
        inspector = (HOST / "Health" / "SchemaHealthInspector.cs").read_text()
        for token in (
            "information_schema.tables",
            "information_schema.columns",
            "information_schema.table_constraints",
            "information_schema.triggers",
            "AuthoringSchemaRequirementKind.Table",
            "AuthoringSchemaRequirementKind.Column",
            "AuthoringSchemaRequirementKind.Constraint",
            "AuthoringSchemaRequirementKind.Trigger",
        ):
            self.assertIn(token, inspector)

    def test_each_feature_registers_a_schema_provider(self) -> None:
        expectations = {
            "Items/ItemAuthoringFeature.cs": "ItemSchemaRequirements",
            "Consumables/ConsumableAuthoringFeature.cs": "ConsumableSchemaRequirements",
            "Equipment/EquipmentAuthoringFeature.cs": "EquipmentSchemaRequirements",
            "HandEquipment/HandEquipmentAuthoringFeature.cs": "HandEquipmentSchemaRequirements",
        }
        for relative_path, provider in expectations.items():
            feature = (HOST / "Features" / relative_path).read_text()
            self.assertIn(
                f"AddSingleton<IAuthoringSchemaRequirementProvider, {provider}>()",
                feature,
            )

    def test_feature_manifests_own_their_schema_contracts(self) -> None:
        expectations = {
            "Items/ItemSchemaRequirements.cs": (
                "item_definitions",
                "character_inventory",
                "item_definitions_runtime_disable_guard",
            ),
            "Consumables/ConsumableSchemaRequirements.cs": (
                "item_consumable_profiles",
                "item_consumable_effects_amount_range_check",
                "item_consumable_profiles_result_publication_guard",
            ),
            "Equipment/EquipmentSchemaRequirements.cs": (
                "equipment_slot_definitions",
                "item_combat_profiles",
                "item_combat_profiles_attack_type_accuracy_style_check",
            ),
            "HandEquipment/HandEquipmentSchemaRequirements.cs": (
                "item_tool_capabilities",
                "item_tool_capabilities_power_tier_check",
                "item_tool_capabilities_hand_slot_guard",
            ),
        }
        for relative_path, tokens in expectations.items():
            manifest = (HOST / "Features" / relative_path).read_text()
            self.assertIn("IAuthoringSchemaRequirementProvider", manifest)
            for token in tokens:
                self.assertIn(token, manifest)

    def test_shared_requirement_model_has_stable_deduplication_key(self) -> None:
        model = (HOST / "Health" / "AuthoringSchemaRequirement.cs").read_text()
        self.assertIn("public string Key", model)
        for factory in ("Table", "Column", "Constraint", "Trigger"):
            self.assertIn(f"AuthoringSchemaRequirement {factory}", model)

    def test_program_registers_inspector_not_feature_schema_details(self) -> None:
        program = (HOST / "Program.cs").read_text()
        self.assertIn("AddSingleton<SchemaHealthInspector>()", program)
        self.assertNotIn("ItemSchemaRequirements", program)
        self.assertNotIn("ConsumableSchemaRequirements", program)
        self.assertNotIn("EquipmentSchemaRequirements", program)
        self.assertNotIn("HandEquipmentSchemaRequirements", program)


if __name__ == "__main__":
    unittest.main()
