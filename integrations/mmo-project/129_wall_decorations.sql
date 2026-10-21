-- Prepared only. No live application without coordinated release authorization.
BEGIN;
LOCK TABLE crafting_definitions, character_home_furniture IN ACCESS EXCLUSIVE MODE;
DO $$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM schema_migrations WHERE migration_number=128)
 OR EXISTS(SELECT 1 FROM schema_migrations WHERE migration_number=129)
 THEN RAISE EXCEPTION 'Apply wall decoration migration129 once after128'; END IF;
END $$;
CREATE OR REPLACE FUNCTION validate_crafting_publication() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE s jsonb; f jsonb; material jsonb; tool text; saw jsonb; k text;
BEGIN
 SELECT published_settings INTO s FROM crafting_definitions WHERE definition_id='v1';
 IF s IS NULL THEN
  IF EXISTS(SELECT 1 FROM character_home_furniture) THEN RAISE EXCEPTION 'Placed furniture requires published Crafting'; END IF;
  RETURN NULL;
 END IF;
 PERFORM item_id FROM item_definitions WHERE item_id IN (
  SELECT value FROM jsonb_each_text(s) WHERE key LIKE '%item_id'
  UNION SELECT value->>'log_item_id' FROM jsonb_array_elements(s->'sawing')
  UNION SELECT value->>'plank_item_id' FROM jsonb_array_elements(s->'sawing')
  UNION SELECT value->>'item_id' FROM jsonb_array_elements(s->'furniture')
  UNION SELECT i->>'item_id' FROM jsonb_array_elements(s->'furniture') recipe_row CROSS JOIN LATERAL jsonb_array_elements(recipe_row->'inputs') i
  UNION SELECT t FROM jsonb_array_elements(s->'furniture') recipe_row CROSS JOIN LATERAL jsonb_array_elements_text(recipe_row->'required_tool_ids') t) ORDER BY item_id FOR SHARE;
 PERFORM definition_id FROM world_object_definitions WHERE definition_id=s->>'station_definition_id' FOR SHARE;
 PERFORM definition_id FROM world_object_interactions WHERE definition_id=s->>'station_definition_id' ORDER BY interaction_order FOR SHARE;
 IF NOT s ?& ARRAY['saw_item_id','hammer_item_id','log_item_id','nails_item_id']
 OR EXISTS(SELECT 1 FROM jsonb_each_text(s) e WHERE e.key LIKE '%item_id' AND NOT EXISTS(
  SELECT 1 FROM item_definitions i WHERE i.item_id=e.value AND i.runtime_enabled AND i.stackable=(e.key='nails_item_id')))
 OR NOT EXISTS(SELECT 1 FROM world_object_definitions w JOIN world_object_interactions a USING(definition_id)
  WHERE w.definition_id=s->>'station_definition_id' AND w.publication_state='Published' AND w.blocks_movement
  AND w.footprint_width_tiles=1 AND w.footprint_height_tiles=1 AND a.action_id='crafting')
 OR jsonb_typeof(s->'furniture') IS DISTINCT FROM 'array' OR jsonb_array_length(s->'furniture')=0
 THEN RAISE EXCEPTION 'Published Crafting requires enabled item shapes, furniture and a published workbench'; END IF;
 IF jsonb_typeof(s->'sawing') IS DISTINCT FROM 'array' OR jsonb_array_length(s->'sawing')=0
 THEN RAISE EXCEPTION 'Published Crafting requires authored sawing recipes'; END IF;
 FOR saw IN SELECT value FROM jsonb_array_elements(s->'sawing') LOOP
  IF jsonb_typeof(saw) IS DISTINCT FROM 'object'
   OR length(btrim(COALESCE(saw->>'recipe_id','')))=0 OR length(btrim(COALESCE(saw->>'display_name','')))=0
   OR saw->>'recipe_id' IN ('cut_arrow_shafts','make_headless_arrows','make_bronze_arrows','make_iron_arrows','spin_wool')
   OR saw->>'log_item_id'=saw->>'plank_item_id' OR saw->>'plank_item_id'=s->>'log_item_id'
   OR EXISTS(SELECT 1 FROM jsonb_array_elements(s->'furniture') furniture_row WHERE furniture_row->>'recipe_id'=saw->>'recipe_id')
  THEN RAISE EXCEPTION 'Sawing requires a distinct recipe identity and log/plank pair'; END IF;
  FOREACH k IN ARRAY ARRAY['level','xp_tenths','duration_ms'] LOOP
   IF jsonb_typeof(saw->k) IS DISTINCT FROM 'number' OR (saw->>k) !~ '^[0-9]+$'
   THEN RAISE EXCEPTION 'Sawing requires integer %',k; END IF;
  END LOOP;
  IF (saw->>'level')::numeric NOT BETWEEN 1 AND 99 OR (saw->>'xp_tenths')::numeric NOT BETWEEN 0 AND 10000000
   OR (saw->>'duration_ms')::numeric NOT BETWEEN 1 AND 86400000
  THEN RAISE EXCEPTION 'Invalid sawing level, XP or duration'; END IF;
  FOREACH k IN ARRAY ARRAY['log_item_id','plank_item_id'] LOOP
   IF NOT EXISTS(SELECT 1 FROM item_definitions WHERE item_id=saw->>k AND runtime_enabled AND NOT stackable)
    OR EXISTS(SELECT 1 FROM jsonb_each_text(s) e WHERE e.key LIKE '%item_id' AND e.key<>'log_item_id' AND e.value=saw->>k)
    OR EXISTS(SELECT 1 FROM jsonb_array_elements(s->'furniture') furniture_row WHERE furniture_row->>'item_id'=saw->>k)
    OR EXISTS(SELECT 1 FROM jsonb_each_text(COALESCE(NULLIF(s->'arrows','null'::jsonb),'{}'::jsonb)) e WHERE e.key LIKE '%item_id' AND e.value=saw->>k)
    OR EXISTS(SELECT 1 FROM jsonb_each_text(COALESCE(NULLIF(s->'spinning','null'::jsonb),'{}'::jsonb)) e WHERE e.key LIKE '%item_id' AND e.value=saw->>k)
   THEN RAISE EXCEPTION 'Sawing requires distinct enabled nonstackable materials'; END IF;
  END LOOP;
 END LOOP;
 IF (SELECT count(DISTINCT value->>'recipe_id') FROM jsonb_array_elements(s->'sawing'))<>jsonb_array_length(s->'sawing')
  OR (SELECT count(DISTINCT id) FROM jsonb_array_elements(s->'sawing') row CROSS JOIN LATERAL (VALUES(row->>'log_item_id'),(row->>'plank_item_id')) ids(id))<>2*jsonb_array_length(s->'sawing')
 THEN RAISE EXCEPTION 'Sawing recipe identities and material bindings must be unique'; END IF;
 FOR f IN SELECT value FROM jsonb_array_elements(s->'furniture') LOOP
  IF NOT EXISTS(SELECT 1 FROM item_definitions WHERE item_id=f->>'item_id' AND runtime_enabled AND NOT stackable AND trade_policy='tradeable')
  OR ((f->>'placement_layer'='furniture' AND (f->>'footprint_width_tiles')::int=1 AND (f->>'footprint_height_tiles')::int=1)
   OR (f->>'placement_layer'='floor' AND (f->>'footprint_width_tiles')::int=2 AND (f->>'footprint_height_tiles')::int=1 AND (f->>'blocks_movement')::boolean=false)
   OR (f->>'placement_layer'='wall' AND (f->>'footprint_width_tiles')::int=1 AND (f->>'footprint_height_tiles')::int=1 AND (f->>'blocks_movement')::boolean=false)) IS DISTINCT FROM true
  OR f->>'placement_layer' IS NULL
  OR (f->>'occupies_furniture_space')::boolean IS DISTINCT FROM true OR jsonb_typeof(f->'blocks_movement') IS DISTINCT FROM 'boolean'
  OR EXISTS(SELECT 1 FROM jsonb_each_text(s) e WHERE e.key LIKE '%item_id' AND e.value=f->>'item_id')
  OR EXISTS(SELECT 1 FROM jsonb_each_text(COALESCE(NULLIF(s->'arrows','null'::jsonb),'{}'::jsonb)) e WHERE e.key LIKE '%item_id' AND e.value=f->>'item_id')
  THEN RAISE EXCEPTION 'Furniture requires distinct enabled tradeable items and a 1x1 footprint with explicit collision'; END IF;
  IF f->>'placement_layer'='wall' AND (
   COALESCE(f->>'south_texture_path','') !~ '^res://assets/.+\.png$'
   OR position('..' in f->>'south_texture_path')>0)
  THEN RAISE EXCEPTION 'Wall decorations require a south-facing project PNG'; END IF;
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
CREATE OR REPLACE FUNCTION protect_furniture_footprints() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE s jsonb; f jsonb; other_f jsonb; placed record; w int; h int; ow int; oh int;
BEGIN
 PERFORM id FROM characters WHERE id=NEW.character_id FOR UPDATE;
 SELECT published_settings INTO s FROM crafting_definitions WHERE definition_id=NEW.definition_id;
 SELECT value INTO f FROM jsonb_array_elements(s->'furniture') WHERE value->>'item_id'=NEW.item_id;
 IF f IS NULL OR f->>'placement_layer' NOT IN ('floor','furniture','wall')
  OR (f->>'placement_layer'='wall' AND (NEW.facing<>'south' OR NEW.tile_y<>1 OR NEW.tile_x NOT IN (3,5,6,7)))
  OR (f->>'placement_layer'='floor' AND NEW.facing NOT IN ('east','south'))
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
-- Trigger identity lets Studio health distinguish the wall-capable publication schema.
ALTER TRIGGER home_furniture_footprint_guard ON character_home_furniture RENAME TO home_furniture_wall_footprint_guard;
SET CONSTRAINTS ALL IMMEDIATE;
INSERT INTO schema_migrations(migration_file,migration_number,migration_name,notes)
VALUES('129_wall_decorations.sql',129,'wall_decorations','Nonblocking south-facing decorations on four solid starter-home north-wall slots; existing placement persistence and separate layer occupancy.');
COMMIT;
