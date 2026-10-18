-- Existing characters are returning players. Only registration grants first-login home admission.
BEGIN;
ALTER TABLE characters ADD COLUMN first_login_pending boolean NOT NULL DEFAULT false;
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
  OR (f->>'occupies_furniture_space')::boolean IS DISTINCT FROM true OR jsonb_typeof(f->'blocks_movement') IS DISTINCT FROM 'boolean'
  OR EXISTS(SELECT 1 FROM jsonb_each_text(s) e WHERE e.key LIKE '%item_id' AND e.value=f->>'item_id')
  OR EXISTS(SELECT 1 FROM jsonb_each_text(COALESCE(NULLIF(s->'arrows','null'::jsonb),'{}'::jsonb)) e WHERE e.key LIKE '%item_id' AND e.value=f->>'item_id')
  THEN RAISE EXCEPTION 'Furniture requires distinct enabled tradeable items and a 1x1 footprint with explicit collision'; END IF;
 END LOOP;
 IF (SELECT count(DISTINCT value->>'item_id') FROM jsonb_array_elements(s->'furniture'))<>jsonb_array_length(s->'furniture')
 OR (SELECT count(DISTINCT value->>'recipe_id') FROM jsonb_array_elements(s->'furniture'))<>jsonb_array_length(s->'furniture')
 OR EXISTS(SELECT 1 FROM character_home_furniture p WHERE NOT EXISTS(
  SELECT 1 FROM jsonb_array_elements(s->'furniture') entry WHERE entry->>'item_id'=p.item_id))
 THEN RAISE EXCEPTION 'Furniture identities must be unique and retain every placed item'; END IF;
 RETURN NULL;
END $$;
INSERT INTO schema_migrations(migration_file,migration_number,migration_name,notes)
VALUES('117_blocking_furniture_and_home_login.sql',117,'blocking_furniture_and_home_login','Definition-driven blocking furniture; explicit one-time new-character home login.');
COMMIT;
