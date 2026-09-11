"""Exercise the actual integration SQL without changing authored content."""

import os
from pathlib import Path
import shutil
import subprocess
import unittest
import uuid


ROOT = Path(__file__).resolve().parents[2]
SQL = ROOT / "integrations/mmo-project/prototype/sql"
FRESH = (SQL / "017_item_consumable_profiles.sql").read_text()
TIMESTAMPS = (SQL / "048_item_consumable_child_timestamps.sql").read_text()
FORWARD = (SQL / "050_item_consumable_deterministic_amount.sql").read_text()
DSN = os.environ.get("CONTENT_STUDIO_MIGRATION_DSN")


class ConsumableAmountSchemaTests(unittest.TestCase):
    def test_fresh_schema_has_one_amount_and_no_food_seed(self) -> None:
        self.assertIn("amount INTEGER NOT NULL", FRESH)
        self.assertIn("CHECK (amount BETWEEN 1 AND 1000000)", FRESH)
        for retired in ("minimum_amount", "maximum_amount", "legacy_food", "INSERT INTO item_consumable"):
            self.assertNotIn(retired, FRESH)


@unittest.skipUnless(DSN and shutil.which("psql"),
                     "psql and CONTENT_STUDIO_MIGRATION_DSN required for disposable migration checks")
class ConsumableAmountMigrationTests(unittest.TestCase):
    def run_sql(self, sql: str) -> subprocess.CompletedProcess:
        # Even functions/triggers live only in this transaction's isolated schema.
        schema = "consumable_test_" + uuid.uuid4().hex
        setup = f"""
            BEGIN;
            CREATE SCHEMA {schema};
            SET LOCAL search_path TO {schema};
            CREATE TABLE item_definitions (item_id TEXT PRIMARY KEY, item_name TEXT, runtime_enabled BOOLEAN);
            CREATE TABLE skill_definitions (skill_id TEXT PRIMARY KEY);
            INSERT INTO item_definitions VALUES ('fixture', 'Apple', FALSE);
        """
        return subprocess.run(
            ["psql", "-X", "-qAt", "-d", DSN, "-v", "ON_ERROR_STOP=1"],
            input=setup + FRESH + TIMESTAMPS + sql + "\nROLLBACK;",
            text=True, capture_output=True, timeout=30,
        )

    @staticmethod
    def old_schema() -> str:
        # Recreate the historical T2 axis on the real schema, including its
        # original constraints. These fixture amounts are not game balance.
        return """
            ALTER TABLE item_consumable_effects DROP CONSTRAINT item_consumable_effects_amount_check;
            ALTER TABLE item_consumable_effects RENAME COLUMN amount TO minimum_amount;
            ALTER TABLE item_consumable_effects ADD COLUMN maximum_amount INTEGER NOT NULL;
            ALTER TABLE item_consumable_effects ADD CONSTRAINT item_consumable_effects_amount_range_check
                CHECK (minimum_amount BETWEEN 1 AND 1000000 AND maximum_amount BETWEEN minimum_amount AND 1000000);
            INSERT INTO item_consumable_profiles (item_id, use_action) VALUES ('fixture', 'eat');
            INSERT INTO item_consumable_effects
                (item_id, effect_index, effect_type, target_id, minimum_amount, maximum_amount, updated_at)
            VALUES ('fixture', 0, 'restore_resource', 'health', 17, 17, '2026-01-01T00:00:00Z'),
                   ('fixture', 1, 'restore_resource', 'special', 9, 9, '2026-02-01T00:00:00Z');
        """

    @staticmethod
    def assert_final_schema() -> str:
        return """
            DO $$ BEGIN
                ASSERT (SELECT count(*) = 1 FROM information_schema.columns
                    WHERE table_schema = current_schema() AND table_name = 'item_consumable_effects'
                      AND column_name IN ('amount', 'minimum_amount', 'maximum_amount'));
                ASSERT EXISTS (SELECT 1 FROM information_schema.columns
                    WHERE table_schema = current_schema() AND table_name = 'item_consumable_effects'
                      AND column_name = 'amount' AND data_type = 'integer' AND is_nullable = 'NO');
                ASSERT EXISTS (SELECT 1 FROM pg_constraint
                    WHERE conrelid = 'item_consumable_effects'::regclass
                      AND conname = 'item_consumable_effects_amount_check');
                ASSERT NOT EXISTS (SELECT 1 FROM pg_constraint
                    WHERE conrelid = 'item_consumable_effects'::regclass
                      AND conname = 'item_consumable_effects_amount_range_check');
            END $$;
        """

    def test_fresh_chain_and_repeat_preserve_deterministic_schema_without_seeding_food(self) -> None:
        result = self.run_sql(FORWARD + FORWARD + self.assert_final_schema() + """
            DO $$ BEGIN
                ASSERT (SELECT count(*) = 0 FROM item_consumable_profiles);
                ASSERT (SELECT count(*) = 0 FROM item_consumable_effects);
            END $$;
        """)
        self.assertEqual(0, result.returncode, result.stderr)

    def test_equal_amounts_migrate_losslessly_and_preserve_unrelated_constraints(self) -> None:
        snapshot = """
            CREATE TEMP TABLE before_rows AS
                SELECT to_jsonb(e) - 'minimum_amount' - 'maximum_amount'
                    || jsonb_build_object('amount', minimum_amount) AS row
                FROM item_consumable_effects e;
            CREATE TEMP TABLE before_constraints AS
                SELECT conname, pg_get_constraintdef(oid) AS definition
                FROM pg_constraint WHERE conrelid = 'item_consumable_effects'::regclass
                    AND conname <> 'item_consumable_effects_amount_range_check';
        """
        verify = """
            DO $$ BEGIN
                ASSERT NOT EXISTS ((SELECT row FROM before_rows)
                    EXCEPT (SELECT to_jsonb(e) FROM item_consumable_effects e));
                ASSERT (SELECT count(*) = 2 FROM item_consumable_effects);
                ASSERT NOT EXISTS ((SELECT * FROM before_constraints) EXCEPT
                    (SELECT conname, pg_get_constraintdef(oid) FROM pg_constraint
                     WHERE conrelid = 'item_consumable_effects'::regclass));
            END $$;
        """
        result = self.run_sql(self.old_schema() + snapshot + FORWARD + FORWARD + self.assert_final_schema() + verify)
        self.assertEqual(0, result.returncode, result.stderr)

    def test_true_range_refuses_entire_migration_without_changing_rows(self) -> None:
        sql = self.old_schema() + """
            UPDATE item_consumable_effects SET maximum_amount = 23 WHERE effect_index = 1;
            CREATE TEMP TABLE before_rows AS SELECT to_jsonb(e) AS row FROM item_consumable_effects e;
            DO $test$ BEGIN
                BEGIN
                    EXECUTE $migration$""" + FORWARD + """$migration$;
                    ASSERT FALSE, 'A true range must refuse migration';
                EXCEPTION WHEN raise_exception THEN
                    ASSERT SQLERRM LIKE '%fixture effect 1 has a true restore range (9 to 23)%', SQLERRM;
                END;
                ASSERT NOT EXISTS ((SELECT row FROM before_rows)
                    EXCEPT (SELECT to_jsonb(e) FROM item_consumable_effects e));
                ASSERT (SELECT count(*) = 2 FROM item_consumable_effects);
                ASSERT NOT EXISTS (SELECT 1 FROM information_schema.columns
                    WHERE table_schema = current_schema() AND table_name = 'item_consumable_effects'
                      AND column_name = 'amount');
                ASSERT EXISTS (SELECT 1 FROM pg_constraint
                    WHERE conrelid = 'item_consumable_effects'::regclass
                      AND conname = 'item_consumable_effects_amount_range_check');
            END $test$;
        """
        result = self.run_sql(sql)
        self.assertEqual(0, result.returncode, result.stderr)

    def test_migrated_schema_rejects_zero_amount(self) -> None:
        result = self.run_sql(self.old_schema() + FORWARD + """
            UPDATE item_consumable_effects SET amount = 0 WHERE effect_index = 0;
        """)
        self.assertNotEqual(0, result.returncode)
        self.assertIn('violates check constraint "item_consumable_effects_amount_check"', result.stderr)


if __name__ == "__main__":
    unittest.main()
