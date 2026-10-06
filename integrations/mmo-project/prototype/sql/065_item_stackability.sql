-- Stackability is authored item identity, not a skill or currency policy.
BEGIN;

-- Serialize the one-time backfill with possession writers and item authoring.
LOCK TABLE item_definitions, character_inventory, ground_items IN SHARE ROW EXCLUSIVE MODE;
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                   WHERE table_schema = 'public' AND table_name = 'item_definitions'
                     AND column_name = 'stackable') THEN
        IF EXISTS (SELECT 1 FROM character_inventory
                   WHERE item_id IN ('inventory_174_logs', 'oak_logs', 'copper_ore', 'tin_ore', 'inventory_150_chunk_of_iron')
                     AND stack_count <> 1) OR
           EXISTS (SELECT 1 FROM ground_items
                   WHERE item_id IN ('inventory_174_logs', 'oak_logs', 'copper_ore', 'tin_ore', 'inventory_150_chunk_of_iron')
                     AND stack_count <> 1) THEN
            RAISE EXCEPTION 'Known non-stackable items have incompatible live quantities; reconcile explicitly before migration 065.';
        END IF;
        ALTER TABLE item_definitions ADD COLUMN stackable BOOLEAN NOT NULL DEFAULT FALSE;
        -- Compatibility applies only once. Reapplication preserves later authoring.
        UPDATE item_definitions SET stackable = item_id NOT IN
            ('inventory_174_logs', 'oak_logs', 'copper_ore', 'tin_ore', 'inventory_150_chunk_of_iron');
    END IF;
END;
$$;

CREATE OR REPLACE FUNCTION validate_item_stackability_publication()
RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF NEW.runtime_enabled AND NOT NEW.stackable AND
       (EXISTS (SELECT 1 FROM character_inventory WHERE item_id = NEW.item_id AND stack_count <> 1) OR
        EXISTS (SELECT 1 FROM ground_items WHERE item_id = NEW.item_id AND stack_count <> 1)) THEN
        RAISE EXCEPTION 'Cannot publish non-stackable item %: retained inventory or ground rows have stack_count other than 1.', NEW.item_id;
    END IF;
    RETURN NEW;
END;
$$;
DROP TRIGGER IF EXISTS item_stackability_publication_guard ON item_definitions;
CREATE TRIGGER item_stackability_publication_guard
BEFORE INSERT OR UPDATE ON item_definitions
FOR EACH ROW EXECUTE FUNCTION validate_item_stackability_publication();

-- A shared definition lock serializes possession writes with publication edits.
-- Draft edits may retain incompatible old rows; new writes may never create them.
CREATE OR REPLACE FUNCTION validate_possession_stackability()
RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE item_stackable BOOLEAN;
BEGIN
    SELECT stackable INTO item_stackable FROM item_definitions
    WHERE item_id = NEW.item_id FOR SHARE;
    IF NOT item_stackable AND NEW.stack_count <> 1 THEN
        RAISE EXCEPTION 'Non-stackable item % requires stack_count = 1.', NEW.item_id;
    END IF;
    RETURN NEW;
END;
$$;
DROP TRIGGER IF EXISTS inventory_stackability_guard ON character_inventory;
CREATE TRIGGER inventory_stackability_guard BEFORE INSERT OR UPDATE OF item_id, stack_count
ON character_inventory FOR EACH ROW EXECUTE FUNCTION validate_possession_stackability();
DROP TRIGGER IF EXISTS ground_stackability_guard ON ground_items;
CREATE TRIGGER ground_stackability_guard BEFORE INSERT OR UPDATE OF item_id, stack_count
ON ground_items FOR EACH ROW EXECUTE FUNCTION validate_possession_stackability();

INSERT INTO schema_migrations (migration_file, migration_number, migration_name, notes)
VALUES ('065_item_stackability.sql', 65, 'item_stackability',
        'Authored stackability with one-time legacy compatibility backfill and publication/possession quantity guards.')
ON CONFLICT (migration_file) DO NOTHING;
COMMIT;
