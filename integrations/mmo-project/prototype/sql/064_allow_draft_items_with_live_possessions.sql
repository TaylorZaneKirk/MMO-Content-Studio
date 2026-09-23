-- Draft/Disable retain item identity and existing possessions. Authoring may
-- therefore unpublish an item in use; publication is still required to admit
-- new runtime references. Frozen reward/dependency and deletion guards remain.
BEGIN;

CREATE OR REPLACE FUNCTION prevent_runtime_disabled_live_item_refs()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    IF OLD.runtime_enabled = TRUE AND NEW.runtime_enabled = FALSE THEN
        IF EXISTS (SELECT 1 FROM combat_reward_guaranteed_drops WHERE item_id = OLD.item_id) OR
           EXISTS (SELECT 1 FROM combat_reward_guaranteed_drop_plans WHERE item_id = OLD.item_id) THEN
            RAISE EXCEPTION 'Cannot disable runtime item % while frozen combat reward settlement state references it.', OLD.item_id;
        END IF;
    END IF;
    RETURN NEW;
END;
$$;

INSERT INTO schema_migrations (migration_file, migration_number, migration_name, notes)
VALUES ('064_allow_draft_items_with_live_possessions.sql', 64, 'allow_draft_items_with_live_possessions',
        'Allow Draft/Disable with existing inventory, equipment or ground rows; preserve item identity and admission guards.')
ON CONFLICT (migration_file) DO NOTHING;

COMMIT;
