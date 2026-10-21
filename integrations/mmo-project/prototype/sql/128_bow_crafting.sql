-- Optional workbench bow recipes; existing publication and production owners remain.
BEGIN;
CREATE FUNCTION validate_bow_crafting_publication() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE s jsonb; bows jsonb; b jsonb; k text; id text; ids text[];
BEGIN
 SELECT published_settings INTO s FROM crafting_definitions WHERE definition_id='v1';
 bows=COALESCE(NULLIF(s->'bows','null'::jsonb),'[]'::jsonb);
 IF jsonb_typeof(bows)<>'array' THEN RAISE EXCEPTION 'Bow recipes must be an array'; END IF;
 FOR b IN SELECT value FROM jsonb_array_elements(bows) LOOP
  IF jsonb_typeof(b)<>'object' OR NOT b ?& ARRAY['recipe_id','display_name','item_id','log_item_id','yarn_item_id','knife_item_id','level','xp_tenths','duration_ms']
   OR COALESCE(length(btrim(b->>'recipe_id')),0)=0 OR COALESCE(length(btrim(b->>'display_name')),0)=0
   OR jsonb_typeof(b->'level') IS DISTINCT FROM 'number' OR (b->>'level')::int NOT BETWEEN 1 AND 99
   OR jsonb_typeof(b->'xp_tenths') IS DISTINCT FROM 'number' OR (b->>'xp_tenths')::int NOT BETWEEN 0 AND 10000000
   OR jsonb_typeof(b->'duration_ms') IS DISTINCT FROM 'number' OR (b->>'duration_ms')::int NOT BETWEEN 1 AND 86400000
  THEN RAISE EXCEPTION 'Complete bow recipe, level, XP and duration required'; END IF;
  ids=ARRAY[b->>'item_id',b->>'log_item_id',b->>'yarn_item_id',b->>'knife_item_id'];
  PERFORM item_id FROM item_definitions WHERE item_id=ANY(ids) ORDER BY item_id FOR SHARE;
  IF (SELECT count(DISTINCT x) FROM unnest(ids) x)<>4 OR EXISTS(SELECT 1 FROM unnest(ids) x WHERE NOT EXISTS(
   SELECT 1 FROM item_definitions WHERE item_id=x AND runtime_enabled AND NOT stackable))
  THEN RAISE EXCEPTION 'Bow needs four distinct enabled nonstackable item bindings'; END IF;
  IF EXISTS(SELECT 1 FROM jsonb_array_elements(s->'furniture') f WHERE f->>'item_id'=b->>'item_id' OR f->>'recipe_id'=b->>'recipe_id')
   OR EXISTS(SELECT 1 FROM jsonb_array_elements(s->'sawing') f WHERE f->>'recipe_id'=b->>'recipe_id' OR f->>'plank_item_id'=b->>'item_id' OR f->>'log_item_id'=b->>'item_id')
   OR EXISTS(SELECT 1 FROM jsonb_each_text(s) f WHERE f.key LIKE '%item_id' AND f.value=b->>'item_id')
   OR EXISTS(SELECT 1 FROM jsonb_each_text(COALESCE(NULLIF(s->'arrows','null'::jsonb),'{}'::jsonb)) f WHERE f.key LIKE '%item_id' AND f.value=b->>'item_id')
   OR b->>'recipe_id'=ANY(ARRAY['cut_arrow_shafts','make_headless_arrows','make_bronze_arrows','make_iron_arrows','make_steel_arrows','spin_wool'])
  THEN RAISE EXCEPTION 'Bow output and recipe identities must be distinct from existing Crafting'; END IF;
 END LOOP;
 IF (SELECT count(DISTINCT entry->>'recipe_id') FROM jsonb_array_elements(bows) entry)<>jsonb_array_length(bows)
 OR (SELECT count(DISTINCT entry->>'item_id') FROM jsonb_array_elements(bows) entry)<>jsonb_array_length(bows)
 THEN RAISE EXCEPTION 'Bow recipe IDs and outputs must be unique'; END IF;
 RETURN NULL;
END $$;
CREATE CONSTRAINT TRIGGER bow_crafting_guard AFTER INSERT OR UPDATE OR DELETE ON crafting_definitions DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION validate_bow_crafting_publication();
CREATE CONSTRAINT TRIGGER bow_crafting_item_guard AFTER UPDATE OR DELETE ON item_definitions DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION validate_bow_crafting_publication();
SET CONSTRAINTS ALL IMMEDIATE;
INSERT INTO schema_migrations(migration_file,migration_number,migration_name,notes)
VALUES('128_bow_crafting.sql',128,'bow_crafting','Optional workbench bow recipes using existing Crafting lifecycle.');
COMMIT;
