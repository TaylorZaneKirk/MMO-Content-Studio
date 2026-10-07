-- Prepared only: apply with explicit delivery authorization, never during implementation.
BEGIN;
CREATE TABLE crafting_definitions (
 definition_id text PRIMARY KEY CHECK(definition_id='v1'),
 settings jsonb NOT NULL CHECK(jsonb_typeof(settings)='object'),
 published_settings jsonb CHECK(published_settings IS NULL OR jsonb_typeof(published_settings)='object'),
 publication_state text NOT NULL DEFAULT 'Draft' CHECK(publication_state IN ('Draft','Published','Disabled')),
 updated_at timestamptz NOT NULL DEFAULT clock_timestamp()
);
CREATE TABLE character_home_furniture (
 character_id uuid NOT NULL REFERENCES characters(id) ON DELETE CASCADE,
 placement_id uuid NOT NULL,
 definition_id text NOT NULL REFERENCES crafting_definitions(definition_id) ON DELETE RESTRICT,
 tile_x integer NOT NULL CHECK(tile_x BETWEEN 0 AND 11),
 tile_y integer NOT NULL CHECK(tile_y BETWEEN 0 AND 7),
 facing text NOT NULL CHECK(facing IN ('east','west')),
 PRIMARY KEY(character_id,placement_id), UNIQUE(character_id,tile_x,tile_y)
);
-- Preserve the identity and physical meaning of retained furniture. Draft edits
-- remain harmless; only replacing/removing published facts is guarded here.
CREATE FUNCTION protect_retained_crafting_furniture() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
 IF EXISTS(SELECT 1 FROM character_home_furniture WHERE definition_id=OLD.definition_id) AND (
  TG_OP='DELETE' OR NEW.published_settings IS NULL OR
  (NEW.published_settings - ARRAY['saw_level','saw_xp_tenths','saw_duration_ms','chair_level','chair_xp_tenths','chair_duration_ms','chair_planks','chair_nails','placement_level'])
   IS DISTINCT FROM
  (OLD.published_settings - ARRAY['saw_level','saw_xp_tenths','saw_duration_ms','chair_level','chair_xp_tenths','chair_duration_ms','chair_planks','chair_nails','placement_level'])
 ) THEN RAISE EXCEPTION 'Retained home furniture prevents disabling/deleting or rebinding its published item/geometry/art; pick it up first'; END IF;
 IF TG_OP='DELETE' THEN RETURN OLD; ELSE RETURN NEW; END IF;
END $$;
CREATE TRIGGER crafting_retained_guard BEFORE UPDATE OR DELETE ON crafting_definitions FOR EACH ROW EXECUTE FUNCTION protect_retained_crafting_furniture();
-- The starter-home template references this fixed fixture even when recipes
-- are disabled. Removing it would make the packaged map impossible to load.
CREATE FUNCTION protect_starter_home_workbench() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
 IF OLD.definition_id='home_workbench' THEN
  IF TG_OP='DELETE' THEN RAISE EXCEPTION 'Starter homes require the fixed workbench'; END IF;
  IF NEW.definition_id<>OLD.definition_id OR NEW.publication_state<>'Published'
   OR NOT NEW.blocks_movement OR NEW.footprint_width_tiles<>1 OR NEW.footprint_height_tiles<>1
  THEN RAISE EXCEPTION 'The starter-home workbench must remain published, blocking and 1x1'; END IF;
 END IF;
 IF TG_OP='DELETE' THEN RETURN OLD; ELSE RETURN NEW; END IF;
END $$;
CREATE TRIGGER starter_home_workbench_guard BEFORE UPDATE OR DELETE ON world_object_definitions
 FOR EACH ROW EXECUTE FUNCTION protect_starter_home_workbench();
CREATE FUNCTION validate_crafting_publication() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE s jsonb;
BEGIN
 SELECT published_settings INTO s FROM crafting_definitions WHERE definition_id='v1';
 IF s IS NULL THEN
  IF EXISTS(SELECT 1 FROM character_home_furniture) THEN RAISE EXCEPTION 'Placed furniture requires published Crafting facts'; END IF;
  RETURN NULL;
 END IF;
 PERFORM item_id FROM item_definitions WHERE item_id IN (SELECT value FROM jsonb_each_text(s) WHERE key LIKE '%item_id') ORDER BY item_id FOR SHARE;
 PERFORM definition_id FROM world_object_definitions WHERE definition_id=s->>'station_definition_id' FOR SHARE;
 PERFORM definition_id FROM world_object_interactions WHERE definition_id=s->>'station_definition_id' ORDER BY interaction_order FOR SHARE;
 IF NOT s ?& ARRAY['saw_item_id','hammer_item_id','log_item_id','plank_item_id','nails_item_id','chair_item_id']
 OR EXISTS(SELECT 1 FROM jsonb_each_text(s) e WHERE e.key LIKE '%item_id' AND NOT EXISTS(
   SELECT 1 FROM item_definitions i WHERE i.item_id=e.value AND i.runtime_enabled
    AND i.stackable=(e.key='nails_item_id') AND (e.key<>'chair_item_id' OR i.trade_policy='tradeable')))
 OR NOT EXISTS(SELECT 1 FROM world_object_definitions w JOIN world_object_interactions a USING(definition_id)
   WHERE w.definition_id=s->>'station_definition_id' AND w.publication_state='Published' AND w.blocks_movement
    AND w.footprint_width_tiles=1 AND w.footprint_height_tiles=1 AND a.action_id='crafting')
 OR (s->>'footprint_width_tiles')::integer IS DISTINCT FROM 1 OR (s->>'footprint_height_tiles')::integer IS DISTINCT FROM 1
 OR (s->>'occupies_furniture_space')::boolean IS DISTINCT FROM true OR (s->>'blocks_movement')::boolean IS DISTINCT FROM false
 THEN RAISE EXCEPTION 'Published Crafting requires enabled item shapes, tradeable chair, 1x1 walkable furniture and published blocking 1x1 workbench'; END IF;
 RETURN NULL;
END $$;
CREATE CONSTRAINT TRIGGER crafting_publication_guard AFTER INSERT OR UPDATE OR DELETE ON crafting_definitions DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION validate_crafting_publication();
CREATE CONSTRAINT TRIGGER crafting_item_guard AFTER UPDATE OR DELETE ON item_definitions DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION validate_crafting_publication();
CREATE CONSTRAINT TRIGGER crafting_station_guard AFTER UPDATE OR DELETE ON world_object_definitions DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION validate_crafting_publication();
CREATE CONSTRAINT TRIGGER crafting_action_guard AFTER INSERT OR UPDATE OR DELETE ON world_object_interactions DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION validate_crafting_publication();
CREATE CONSTRAINT TRIGGER crafting_placement_guard AFTER INSERT OR UPDATE ON character_home_furniture DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION validate_crafting_publication();
INSERT INTO schema_migrations(migration_file,migration_number,migration_name,notes)
VALUES('096_crafting_furniture.sql',96,'crafting_furniture','Crafting rules and owner home furniture with atomic possession transfer.');
COMMIT;
