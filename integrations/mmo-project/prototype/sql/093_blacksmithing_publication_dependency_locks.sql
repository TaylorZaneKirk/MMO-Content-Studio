-- Follow-up to092: serialize publication against concurrent dependency disable.
-- No content rows or historical migrations are rewritten.
BEGIN;
CREATE OR REPLACE FUNCTION validate_blacksmithing_publication() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE invalid_id text;
BEGIN
    -- Publish holds shared locks on its dependencies until commit. A concurrent
    -- Disable must wait, then validate against the committed recipe (or vice versa).
    -- Ordered acquisition avoids inconsistent dependency order across publishers.
    PERFORM d.item_id FROM item_definitions d
    WHERE d.item_id IN (
        SELECT r.output_item_id FROM blacksmithing_recipes r WHERE r.publication_state='Published'
        UNION
        SELECT r.required_inventory_tool_id FROM blacksmithing_recipes r WHERE r.publication_state='Published'
        UNION
        SELECT i.item_id FROM blacksmithing_recipe_inputs i
        JOIN blacksmithing_recipes r USING(recipe_id) WHERE r.publication_state='Published'
    ) ORDER BY d.item_id FOR SHARE;
    PERFORM w.definition_id FROM world_object_definitions w
    WHERE w.definition_id IN (
        SELECT r.station_definition_id FROM blacksmithing_recipes r WHERE r.publication_state='Published'
    ) ORDER BY w.definition_id FOR SHARE;

    SELECT r.recipe_id INTO invalid_id FROM blacksmithing_recipes r
    WHERE r.publication_state='Published' AND (
        NOT EXISTS (SELECT 1 FROM blacksmithing_recipe_inputs i WHERE i.recipe_id=r.recipe_id)
        OR NOT EXISTS (SELECT 1 FROM world_object_definitions w WHERE w.definition_id=r.station_definition_id AND w.publication_state='Published')
        OR NOT EXISTS (SELECT 1 FROM item_definitions d WHERE d.item_id=r.output_item_id AND d.runtime_enabled)
        OR (r.required_inventory_tool_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM item_definitions d WHERE d.item_id=r.required_inventory_tool_id AND d.runtime_enabled))
        OR EXISTS (SELECT 1 FROM blacksmithing_recipe_inputs i WHERE i.recipe_id=r.recipe_id AND NOT EXISTS (SELECT 1 FROM item_definitions d WHERE d.item_id=i.item_id AND d.runtime_enabled))
    ) ORDER BY r.recipe_id LIMIT 1;
    IF invalid_id IS NOT NULL THEN RAISE EXCEPTION 'Published Blacksmithing recipe % has missing/unpublished item, station or input references', invalid_id; END IF;
    RETURN NULL;
END $$;
INSERT INTO schema_migrations(migration_file,migration_number,migration_name,notes)
VALUES ('093_blacksmithing_publication_dependency_locks.sql',93,'blacksmithing_publication_dependency_locks',
        'Hold item/station shared row locks through recipe publication to prevent concurrent disable write skew.');
COMMIT;
