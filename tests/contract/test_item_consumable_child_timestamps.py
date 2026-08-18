from pathlib import Path
import unittest


ROOT = Path(__file__).resolve().parents[2]


class ItemConsumableChildTimestampTests(unittest.TestCase):
    def test_migration_is_mirrored_to_mmo_project(self) -> None:
        studio = ROOT / "integrations" / "mmo-project" / "prototype" / "sql" / "048_item_consumable_child_timestamps.sql"
        runtime = ROOT.parent.parent / "prototype" / "sql" / "048_item_consumable_child_timestamps.sql"

        self.assertTrue(studio.exists())
        self.assertTrue(runtime.exists())
        self.assertEqual(studio.read_bytes(), runtime.read_bytes())

    def test_health_contract_covers_consumable_child_updated_at_columns(self) -> None:
        requirements = (ROOT / "host" / "Features" / "Items" / "ItemSchemaRequirements.cs").read_text()
        repository = (ROOT / "host" / "Persistence" / "UnifiedItemRepository.cs").read_text()

        for table in ("item_consumable_requirements", "item_consumable_effects"):
            self.assertIn(f'AuthoringSchemaRequirement.Column("{table}", "updated_at")', requirements)
            self.assertIn(f'insert into {table}', repository)

    def test_migration_adds_only_missing_consumable_child_timestamps(self) -> None:
        migration = (
            ROOT
            / "integrations"
            / "mmo-project"
            / "prototype"
            / "sql"
            / "048_item_consumable_child_timestamps.sql"
        ).read_text()

        self.assertIn("ALTER TABLE item_consumable_requirements", migration)
        self.assertIn("ALTER TABLE item_consumable_effects", migration)
        self.assertEqual(migration.count("ADD COLUMN IF NOT EXISTS updated_at"), 2)
        self.assertNotIn("DROP", migration.upper())


if __name__ == "__main__":
    unittest.main()
