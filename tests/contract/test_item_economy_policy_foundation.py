from pathlib import Path
import unittest


ROOT = Path(__file__).resolve().parents[2]
MMO_PROJECT_ROOT = ROOT.parent.parent


class ItemEconomyPolicyFoundationTests(unittest.TestCase):
    def test_migration_029_is_byte_identical_to_mmo_project(self) -> None:
        studio = ROOT / "integrations" / "mmo-project" / "prototype" / "sql" / "029_item_economy_policy.sql"
        runtime = MMO_PROJECT_ROOT / "prototype" / "sql" / "029_item_economy_policy.sql"

        self.assertTrue(studio.exists())
        self.assertTrue(runtime.exists())
        self.assertEqual(studio.read_bytes(), runtime.read_bytes())

    def test_unified_item_authoring_keeps_economy_in_the_existing_aggregate(self) -> None:
        contracts = (ROOT / "host" / "Contracts" / "ItemContracts.cs").read_text()
        editor = (ROOT / "content-studio" / "scripts" / "item_editor.gd").read_text()

        self.assertIn("ItemEconomyLifecycleDefinition", contracts)
        self.assertIn('JsonPropertyName("economy_lifecycle")', contracts)
        self.assertIn('"economy_lifecycle": _economy_payload()', editor)
        self.assertIn("Economy and Lifecycle", editor)
        self.assertIn("exact non-negative 64-bit integers", editor)
        self.assertIn("authoring-only in V1", editor)
        self.assertIn("are not executed", editor)
        self.assertIn("func _apply_economy", editor)
        self.assertIn("func _update_economy_controls", editor)
        self.assertIn("func _has_valid_economy_integers", editor)
        self.assertIn("_apply_economy({})", editor)
        self.assertIn('"death_transform_item_id": _optional_payload(_death_transform_item_id.text) if transform else null', editor)
        self.assertIn('"npc_buy_price": _economic_integer(_npc_buy_price) if shop == "npc_buys" or shop == "npc_buys_and_sells" else null', editor)
        self.assertIn('"reclaim_policy": _selected_metadata(_reclaim_policy) if reclaim else "none"', editor)
        self.assertIn("_condition_policy_id", editor)
        self.assertIn("_repair_policy_id", editor)

    def test_migration_029_contains_the_required_runtime_guards(self) -> None:
        migration = (ROOT / "integrations" / "mmo-project" / "prototype" / "sql" / "029_item_economy_policy.sql").read_text()
        requirements = (ROOT / "host" / "Features" / "Items" / "ItemSchemaRequirements.cs").read_text()

        for name in [
            "reference_value",
            "trade_policy",
            "death_behavior",
            "death_transform_item_id",
            "shop_policy",
            "npc_buy_price",
            "npc_sell_price",
            "reclaim_policy",
            "reclaim_value",
            "condition_policy_id",
            "repair_policy_id",
            "item_definitions_runtime_transform_target_delete_guard",
        ]:
            self.assertIn(name, migration)
            self.assertIn(name, requirements)
        self.assertIn("validate_runtime_item_economy_policy", migration)
        self.assertIn("item_definitions_runtime_economy_policy_guard", migration)
        self.assertIn("item_definitions_runtime_economy_policy_guard", requirements)
        self.assertIn("item_definitions_death_transform_policy_check", migration)

    def test_godot_economy_controls_preserve_exact_values_and_inactive_fields(self) -> None:
        editor = (ROOT / "content-studio" / "scripts" / "item_editor.gd").read_text()

        self.assertIn("var _reference_value: LineEdit", editor)
        self.assertIn("var _npc_buy_price: LineEdit", editor)
        self.assertIn("var _npc_sell_price: LineEdit", editor)
        self.assertIn("var _reclaim_value: LineEdit", editor)
        self.assertIn('"reference_value": _economic_integer(_reference_value)', editor)
        self.assertIn('else null', editor)
        self.assertIn("_death_transform_item_id.editable = transform", editor)
        self.assertIn('_npc_buy_price.editable = shop == "npc_buys" or shop == "npc_buys_and_sells"', editor)
        self.assertIn('_npc_sell_price.editable = shop == "npc_sells" or shop == "npc_buys_and_sells"', editor)
        self.assertIn("_reclaim_policy.disabled = not reclaim", editor)
        self.assertIn("_condition_policy_id", editor)
        self.assertIn("_repair_policy_id", editor)
        self.assertIn("_reference_value, _death_transform_item_id, _npc_buy_price", editor)


if __name__ == "__main__":
    unittest.main()
