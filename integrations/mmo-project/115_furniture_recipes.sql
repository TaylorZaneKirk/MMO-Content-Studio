-- Prepared only. Apply with the matching server/Studio/client release; no live application here.
BEGIN;
LOCK TABLE crafting_definitions, character_home_furniture IN ACCESS EXCLUSIVE MODE;
ALTER TABLE character_home_furniture ADD COLUMN item_id text REFERENCES item_definitions(item_id) ON DELETE RESTRICT;
-- Existing placements are the previously published chair, including custom bindings.
UPDATE character_home_furniture p SET item_id=c.published_settings->>'chair_item_id'
 FROM crafting_definitions c WHERE c.definition_id=p.definition_id;
-- Finish existing deferred placement checks before the next table alteration.
SET CONSTRAINTS ALL IMMEDIATE;
ALTER TABLE character_home_furniture ALTER COLUMN item_id SET NOT NULL;
SET CONSTRAINTS ALL DEFERRED;

-- Convert both draft and last-published facts independently, preserving every value.
CREATE FUNCTION migrate_chair_recipe(s jsonb) RETURNS jsonb LANGUAGE sql IMMUTABLE AS $$
 SELECT CASE WHEN s IS NULL THEN NULL ELSE
 (s - ARRAY['chair_item_id','chair_level','chair_xp_tenths','chair_duration_ms','chair_planks','chair_nails',
 'placement_level','footprint_width_tiles','footprint_height_tiles','occupies_furniture_space','blocks_movement','east_texture_path','west_texture_path']) ||
 jsonb_build_object('furniture',jsonb_build_array(jsonb_build_object(
 'recipe_id','craft_chair','display_name','Crude wooden chair','item_id',s->'chair_item_id',
 'level',s->'chair_level','xp_tenths',s->'chair_xp_tenths','duration_ms',s->'chair_duration_ms',
 'planks',s->'chair_planks','nails',s->'chair_nails','placement_level',s->'placement_level',
 'footprint_width_tiles',s->'footprint_width_tiles','footprint_height_tiles',s->'footprint_height_tiles',
 'occupies_furniture_space',s->'occupies_furniture_space','blocks_movement',s->'blocks_movement',
 'east_texture_path',s->'east_texture_path','west_texture_path',s->'west_texture_path'))) END
$$;
DROP TRIGGER crafting_retained_guard ON crafting_definitions;
UPDATE crafting_definitions SET settings=migrate_chair_recipe(settings), published_settings=migrate_chair_recipe(published_settings),updated_at=clock_timestamp();
DROP FUNCTION migrate_chair_recipe(jsonb);

CREATE OR REPLACE FUNCTION protect_retained_crafting_furniture() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE item text; before_f jsonb; after_f jsonb;
BEGIN
 FOR item IN SELECT DISTINCT item_id FROM character_home_furniture WHERE definition_id=OLD.definition_id LOOP
  IF TG_OP='DELETE' OR NEW.published_settings IS NULL THEN
   RAISE EXCEPTION 'Retained furniture prevents disabling or deleting Crafting';
  END IF;
  SELECT f INTO before_f FROM jsonb_array_elements(OLD.published_settings->'furniture') f WHERE f->>'item_id'=item;
  SELECT f INTO after_f FROM jsonb_array_elements(NEW.published_settings->'furniture') f WHERE f->>'item_id'=item;
  IF after_f IS NULL OR
   (before_f - ARRAY['recipe_id','display_name','level','xp_tenths','duration_ms','planks','nails','placement_level']) IS DISTINCT FROM
   (after_f - ARRAY['recipe_id','display_name','level','xp_tenths','duration_ms','planks','nails','placement_level'])
  THEN RAISE EXCEPTION 'Retained furniture prevents removing or rebinding item/geometry/art: %',item; END IF;
 END LOOP;
 IF TG_OP='DELETE' THEN RETURN OLD; ELSE RETURN NEW; END IF;
END $$;
CREATE TRIGGER crafting_retained_guard BEFORE UPDATE OR DELETE ON crafting_definitions FOR EACH ROW EXECUTE FUNCTION protect_retained_crafting_furniture();

CREATE OR REPLACE FUNCTION validate_crafting_publication() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE s jsonb; f jsonb;
BEGIN
 SELECT published_settings INTO s FROM crafting_definitions WHERE definition_id='v1';
 IF s IS NULL THEN
  IF EXISTS(SELECT 1 FROM character_home_furniture) THEN RAISE EXCEPTION 'Placed furniture requires published Crafting'; END IF;
  RETURN NULL;
 END IF;
 PERFORM item_id FROM item_definitions WHERE item_id IN (
  SELECT value FROM jsonb_each_text(s) WHERE key LIKE '%item_id'
  UNION SELECT value->>'item_id' FROM jsonb_array_elements(s->'furniture')) ORDER BY item_id FOR SHARE;
 PERFORM definition_id FROM world_object_definitions WHERE definition_id=s->>'station_definition_id' FOR SHARE;
 PERFORM definition_id FROM world_object_interactions WHERE definition_id=s->>'station_definition_id' ORDER BY interaction_order FOR SHARE;
 IF NOT s ?& ARRAY['saw_item_id','hammer_item_id','log_item_id','plank_item_id','nails_item_id']
 OR EXISTS(SELECT 1 FROM jsonb_each_text(s) e WHERE e.key LIKE '%item_id' AND NOT EXISTS(
  SELECT 1 FROM item_definitions i WHERE i.item_id=e.value AND i.runtime_enabled AND i.stackable=(e.key='nails_item_id')))
 OR NOT EXISTS(SELECT 1 FROM world_object_definitions w JOIN world_object_interactions a USING(definition_id)
  WHERE w.definition_id=s->>'station_definition_id' AND w.publication_state='Published' AND w.blocks_movement
  AND w.footprint_width_tiles=1 AND w.footprint_height_tiles=1 AND a.action_id='crafting')
 OR jsonb_typeof(s->'furniture') IS DISTINCT FROM 'array' OR jsonb_array_length(s->'furniture')=0
 THEN RAISE EXCEPTION 'Published Crafting requires enabled item shapes, furniture and a published workbench'; END IF;
 FOR f IN SELECT value FROM jsonb_array_elements(s->'furniture') LOOP
  IF NOT EXISTS(SELECT 1 FROM item_definitions WHERE item_id=f->>'item_id' AND runtime_enabled AND NOT stackable AND trade_policy='tradeable')
  OR (f->>'footprint_width_tiles')::int IS DISTINCT FROM 1 OR (f->>'footprint_height_tiles')::int IS DISTINCT FROM 1
  OR (f->>'occupies_furniture_space')::boolean IS DISTINCT FROM true OR (f->>'blocks_movement')::boolean IS DISTINCT FROM false
  OR EXISTS(SELECT 1 FROM jsonb_each_text(s) e WHERE e.key LIKE '%item_id' AND e.value=f->>'item_id')
  OR EXISTS(SELECT 1 FROM jsonb_each_text(COALESCE(NULLIF(s->'arrows','null'::jsonb),'{}'::jsonb)) e WHERE e.key LIKE '%item_id' AND e.value=f->>'item_id')
  THEN RAISE EXCEPTION 'Furniture requires distinct enabled tradeable items and a 1x1 nonblocking footprint'; END IF;
 END LOOP;
 IF (SELECT count(DISTINCT value->>'item_id') FROM jsonb_array_elements(s->'furniture'))<>jsonb_array_length(s->'furniture')
 OR (SELECT count(DISTINCT value->>'recipe_id') FROM jsonb_array_elements(s->'furniture'))<>jsonb_array_length(s->'furniture')
 OR EXISTS(SELECT 1 FROM character_home_furniture p WHERE NOT EXISTS(
  SELECT 1 FROM jsonb_array_elements(s->'furniture') entry WHERE entry->>'item_id'=p.item_id))
 THEN RAISE EXCEPTION 'Furniture identities must be unique and retain every placed item'; END IF;
 RETURN NULL;
END $$;
INSERT INTO schema_migrations(migration_file,migration_number,migration_name,notes)
VALUES('115_furniture_recipes.sql',115,'furniture_recipes','Shared furniture recipes and exact placed item identity; existing chairs preserved.');
COMMIT;
