-- Fixed authored smelting chance; existing recipes and all forging remain guaranteed.
BEGIN;
DO $$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM schema_migrations WHERE migration_file='110_food_preparation.sql')
 OR EXISTS(SELECT 1 FROM schema_migrations WHERE migration_file='111_blacksmithing_success.sql')
 THEN RAISE EXCEPTION 'Migration 111 requires 110 and must be applied once'; END IF;
END $$;
ALTER TABLE blacksmithing_recipes ADD COLUMN success_percent integer NOT NULL DEFAULT 100;
ALTER TABLE blacksmithing_recipes ADD CONSTRAINT blacksmithing_success_shape
 CHECK (success_percent BETWEEN 1 AND 100 AND (operation <> 'forge' OR success_percent=100));
INSERT INTO schema_migrations(migration_file,migration_number,migration_name,notes)
VALUES ('111_blacksmithing_success.sql',111,'blacksmithing_success','Fixed smelting chance; forging remains guaranteed.');
COMMIT;
