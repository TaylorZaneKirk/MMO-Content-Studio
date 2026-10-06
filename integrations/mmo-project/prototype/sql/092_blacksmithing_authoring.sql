-- Blacksmithing authored recipes only. No Player progression/activity schema.
BEGIN;
CREATE TABLE blacksmithing_recipes (
    recipe_id text PRIMARY KEY CHECK (recipe_id ~ '^[a-z][a-z0-9_]*$'),
    display_name text NOT NULL CHECK (btrim(display_name) <> ''),
    operation text NOT NULL CHECK (operation IN ('smelt', 'forge')),
    station_definition_id text NOT NULL,
    output_item_id text NOT NULL CHECK (output_item_id ~ '^[a-z][a-z0-9_]*$'),
    output_quantity integer NOT NULL CHECK (output_quantity > 0),
    required_level integer NOT NULL CHECK (required_level BETWEEN 1 AND 99),
    xp_tenths integer NOT NULL CHECK (xp_tenths >= 0),
    duration_ms integer NOT NULL CHECK (duration_ms > 0),
    required_inventory_tool_id text,
    publication_state text NOT NULL DEFAULT 'Draft' CHECK (publication_state IN ('Draft','Published','Disabled')),
    updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    CONSTRAINT blacksmithing_recipe_operation_shape CHECK (
        (operation='smelt' AND station_definition_id='bronze_smelter' AND required_inventory_tool_id IS NULL)
        OR (operation='forge' AND station_definition_id='blacksmithing_anvil' AND required_inventory_tool_id IS NOT DISTINCT FROM 'blacksmithing_hammer'))
);
CREATE TABLE blacksmithing_recipe_inputs (
    recipe_id text NOT NULL REFERENCES blacksmithing_recipes ON DELETE CASCADE,
    input_order integer NOT NULL CHECK (input_order >= 0),
    item_id text NOT NULL CHECK (item_id ~ '^[a-z][a-z0-9_]*$'),
    quantity integer NOT NULL CHECK (quantity > 0),
    PRIMARY KEY(recipe_id,input_order), UNIQUE(recipe_id,item_id)
);
-- Drafts may reference pending items. Runtime/export and host publication check
-- every reference; deferred constraints protect publication against concurrent edits.
CREATE FUNCTION validate_blacksmithing_publication() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE invalid_id text;
BEGIN
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
CREATE CONSTRAINT TRIGGER blacksmithing_publication_guard AFTER INSERT OR UPDATE OR DELETE ON blacksmithing_recipes DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION validate_blacksmithing_publication();
CREATE CONSTRAINT TRIGGER blacksmithing_inputs_guard AFTER INSERT OR UPDATE OR DELETE ON blacksmithing_recipe_inputs DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION validate_blacksmithing_publication();
CREATE CONSTRAINT TRIGGER blacksmithing_item_guard AFTER UPDATE OR DELETE ON item_definitions DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION validate_blacksmithing_publication();
CREATE CONSTRAINT TRIGGER blacksmithing_station_guard AFTER UPDATE OR DELETE ON world_object_definitions DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION validate_blacksmithing_publication();
INSERT INTO schema_migrations(migration_file,migration_number,migration_name,notes)
VALUES ('092_blacksmithing_authoring.sql',92,'blacksmithing_authoring','Focused recipes with ordered multi-input quantities and exact XP tenths; no progression changes.');
COMMIT;
