-- Source only. Coordinated delivery required; existing furniture identities stay intact.
BEGIN;
LOCK TABLE crafting_definitions, character_home_furniture IN ACCESS EXCLUSIVE MODE;
ALTER TABLE world_object_definitions ADD COLUMN floor_decoration boolean NOT NULL DEFAULT false;
ALTER TABLE world_object_definitions ADD CONSTRAINT world_floor_nonblocking CHECK (NOT floor_decoration OR NOT blocks_movement);
DROP TRIGGER crafting_retained_guard ON crafting_definitions;
CREATE FUNCTION migrate_furniture_materials(s jsonb) RETURNS jsonb LANGUAGE sql IMMUTABLE AS $$
 SELECT CASE WHEN s IS NULL THEN NULL ELSE jsonb_set(s,'{furniture}',
 (SELECT jsonb_agg((f - ARRAY['planks','nails']) || jsonb_build_object(
 'inputs',jsonb_build_array(jsonb_build_object('item_id',s->>'plank_item_id','quantity',f->'planks'),jsonb_build_object('item_id',s->>'nails_item_id','quantity',f->'nails')),
 'required_tool_ids',jsonb_build_array(s->>'hammer_item_id',s->>'saw_item_id'),
 'placement_layer','furniture','south_texture_path','') ORDER BY ord)
 FROM jsonb_array_elements(s->'furniture') WITH ORDINALITY AS entries(f,ord))) END
$$;
UPDATE crafting_definitions SET settings=migrate_furniture_materials(settings),published_settings=migrate_furniture_materials(published_settings),updated_at=clock_timestamp();
DROP FUNCTION migrate_furniture_materials(jsonb);
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
   (before_f - ARRAY['recipe_id','display_name','level','xp_tenths','duration_ms','inputs','required_tool_ids','placement_level']) IS DISTINCT FROM
   (after_f - ARRAY['recipe_id','display_name','level','xp_tenths','duration_ms','inputs','required_tool_ids','placement_level'])
  THEN RAISE EXCEPTION 'Retained furniture prevents removing or rebinding item/geometry/art: %',item; END IF;
 END LOOP;
 IF TG_OP='DELETE' THEN RETURN OLD; ELSE RETURN NEW; END IF;
END $$;
CREATE TRIGGER crafting_retained_guard BEFORE UPDATE OR DELETE ON crafting_definitions FOR EACH ROW EXECUTE FUNCTION protect_retained_crafting_furniture();

CREATE OR REPLACE FUNCTION validate_crafting_publication() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE s jsonb; f jsonb; material jsonb; tool text;
BEGIN
 SELECT published_settings INTO s FROM crafting_definitions WHERE definition_id='v1';
 IF s IS NULL THEN
  IF EXISTS(SELECT 1 FROM character_home_furniture) THEN RAISE EXCEPTION 'Placed furniture requires published Crafting'; END IF;
  RETURN NULL;
 END IF;
 PERFORM item_id FROM item_definitions WHERE item_id IN (
  SELECT value FROM jsonb_each_text(s) WHERE key LIKE '%item_id'
  UNION SELECT value->>'item_id' FROM jsonb_array_elements(s->'furniture')
  UNION SELECT i->>'item_id' FROM jsonb_array_elements(s->'furniture') recipe_row CROSS JOIN LATERAL jsonb_array_elements(recipe_row->'inputs') i
  UNION SELECT t FROM jsonb_array_elements(s->'furniture') recipe_row CROSS JOIN LATERAL jsonb_array_elements_text(recipe_row->'required_tool_ids') t) ORDER BY item_id FOR SHARE;
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
  OR ((f->>'placement_layer'='furniture' AND (f->>'footprint_width_tiles')::int=1 AND (f->>'footprint_height_tiles')::int=1)
   OR (f->>'placement_layer'='floor' AND (f->>'footprint_width_tiles')::int=2 AND (f->>'footprint_height_tiles')::int=1 AND (f->>'blocks_movement')::boolean=false)) IS DISTINCT FROM true
  OR f->>'placement_layer' IS NULL
  OR (f->>'occupies_furniture_space')::boolean IS DISTINCT FROM true OR jsonb_typeof(f->'blocks_movement') IS DISTINCT FROM 'boolean'
  OR EXISTS(SELECT 1 FROM jsonb_each_text(s) e WHERE e.key LIKE '%item_id' AND e.value=f->>'item_id')
  OR EXISTS(SELECT 1 FROM jsonb_each_text(COALESCE(NULLIF(s->'arrows','null'::jsonb),'{}'::jsonb)) e WHERE e.key LIKE '%item_id' AND e.value=f->>'item_id')
  THEN RAISE EXCEPTION 'Furniture requires distinct enabled tradeable items and a 1x1 footprint with explicit collision'; END IF;
  IF jsonb_typeof(f->'inputs') IS DISTINCT FROM 'array' OR jsonb_array_length(f->'inputs') NOT BETWEEN 1 AND 8
   OR jsonb_typeof(f->'required_tool_ids') IS DISTINCT FROM 'array' OR jsonb_array_length(f->'required_tool_ids')>4
  THEN RAISE EXCEPTION 'Furniture requires explicit materials and retained tools'; END IF;
  FOR material IN SELECT value FROM jsonb_array_elements(f->'inputs') LOOP
   IF jsonb_typeof(material->'quantity') IS DISTINCT FROM 'number' OR (material->>'quantity')::int NOT BETWEEN 1 AND 10000
    OR material->>'item_id'=f->>'item_id' OR NOT EXISTS(SELECT 1 FROM item_definitions WHERE item_id=material->>'item_id' AND runtime_enabled)
   THEN RAISE EXCEPTION 'Invalid furniture material'; END IF;
  END LOOP;
  FOR tool IN SELECT value FROM jsonb_array_elements_text(f->'required_tool_ids') LOOP
   IF tool=f->>'item_id' OR NOT EXISTS(SELECT 1 FROM item_definitions WHERE item_id=tool AND runtime_enabled AND NOT stackable)
    OR EXISTS(SELECT 1 FROM jsonb_array_elements(f->'inputs') i WHERE i->>'item_id'=tool)
   THEN RAISE EXCEPTION 'Furniture tools must be retained enabled nonstackable items'; END IF;
  END LOOP;
  IF (SELECT count(DISTINCT i->>'item_id') FROM jsonb_array_elements(f->'inputs') i)<>jsonb_array_length(f->'inputs')
   OR (SELECT count(DISTINCT t) FROM jsonb_array_elements_text(f->'required_tool_ids') t)<>jsonb_array_length(f->'required_tool_ids')
  THEN RAISE EXCEPTION 'Furniture materials/tools must be distinct'; END IF;
 END LOOP;
 IF (SELECT count(DISTINCT value->>'item_id') FROM jsonb_array_elements(s->'furniture'))<>jsonb_array_length(s->'furniture')
 OR (SELECT count(DISTINCT value->>'recipe_id') FROM jsonb_array_elements(s->'furniture'))<>jsonb_array_length(s->'furniture')
 OR EXISTS(SELECT 1 FROM character_home_furniture p WHERE NOT EXISTS(
  SELECT 1 FROM jsonb_array_elements(s->'furniture') entry WHERE entry->>'item_id'=p.item_id))
 THEN RAISE EXCEPTION 'Furniture identities must be unique and retain every placed item'; END IF;
 RETURN NULL;
END $$;

SET CONSTRAINTS ALL IMMEDIATE;
ALTER TABLE character_home_furniture DROP CONSTRAINT character_home_furniture_character_id_tile_x_tile_y_key;
ALTER TABLE character_home_furniture DROP CONSTRAINT character_home_furniture_facing_check;
ALTER TABLE character_home_furniture ADD CONSTRAINT character_home_furniture_facing_check CHECK(facing IN ('east','west','south'));
-- A tile-origin unique key cannot represent rectangles or two independent layers.
-- Serialize edits on the existing character row and compare derived rectangles;
-- geometry/layer remain published definition facts, not duplicated placement state.
CREATE FUNCTION protect_furniture_footprints() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE s jsonb; f jsonb; other_f jsonb; placed record; w int; h int; ow int; oh int;
BEGIN
 PERFORM id FROM characters WHERE id=NEW.character_id FOR UPDATE;
 SELECT published_settings INTO s FROM crafting_definitions WHERE definition_id=NEW.definition_id;
 SELECT value INTO f FROM jsonb_array_elements(s->'furniture') WHERE value->>'item_id'=NEW.item_id;
 IF f IS NULL OR (f->>'placement_layer'='floor' AND NEW.facing NOT IN ('east','south'))
  OR (f->>'placement_layer'='furniture' AND NEW.facing NOT IN ('east','west'))
 THEN RAISE EXCEPTION 'Unsupported furniture orientation or definition'; END IF;
 w=(f->>'footprint_width_tiles')::int; h=(f->>'footprint_height_tiles')::int;
 IF NEW.facing='south' THEN w=(f->>'footprint_height_tiles')::int; h=(f->>'footprint_width_tiles')::int; END IF;
 FOR placed IN SELECT * FROM character_home_furniture WHERE character_id=NEW.character_id AND placement_id<>NEW.placement_id LOOP
  SELECT value INTO other_f FROM jsonb_array_elements(s->'furniture') WHERE value->>'item_id'=placed.item_id;
  IF other_f->>'placement_layer' <> f->>'placement_layer' THEN CONTINUE; END IF;
  ow=(other_f->>'footprint_width_tiles')::int; oh=(other_f->>'footprint_height_tiles')::int;
  IF placed.facing='south' THEN ow=(other_f->>'footprint_height_tiles')::int; oh=(other_f->>'footprint_width_tiles')::int; END IF;
  IF NEW.tile_x<placed.tile_x+ow AND NEW.tile_x+w>placed.tile_x AND NEW.tile_y<placed.tile_y+oh AND NEW.tile_y+h>placed.tile_y
  THEN RAISE EXCEPTION 'Furniture footprints overlap on the same layer'; END IF;
 END LOOP;
 RETURN NEW;
END $$;
CREATE TRIGGER home_furniture_footprint_guard BEFORE INSERT OR UPDATE ON character_home_furniture FOR EACH ROW EXECUTE FUNCTION protect_furniture_footprints();
INSERT INTO schema_migrations(migration_file,migration_number,migration_name,notes)
VALUES('119_floor_furniture.sql',119,'floor_furniture','Recipe materials/tools, two home placement layers, rectangular floor runners and reusable world floor art.');
COMMIT;
