-- Separate arrow production facts from retained furniture identity; all old guards remain.
BEGIN;
CREATE OR REPLACE FUNCTION protect_retained_crafting_furniture() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
 IF EXISTS(SELECT 1 FROM character_home_furniture WHERE definition_id=OLD.definition_id) AND (
  TG_OP='DELETE' OR NEW.published_settings IS NULL OR
  (NEW.published_settings - ARRAY['arrows','saw_level','saw_xp_tenths','saw_duration_ms','chair_level','chair_xp_tenths','chair_duration_ms','chair_planks','chair_nails','placement_level'])
   IS DISTINCT FROM
  (OLD.published_settings - ARRAY['arrows','saw_level','saw_xp_tenths','saw_duration_ms','chair_level','chair_xp_tenths','chair_duration_ms','chair_planks','chair_nails','placement_level'])
 ) THEN RAISE EXCEPTION 'Retained home furniture prevents disabling/deleting or rebinding its published item/geometry/art; pick it up first'; END IF;
 IF TG_OP='DELETE' THEN RETURN OLD; ELSE RETURN NEW; END IF;
END $$;

CREATE FUNCTION validate_arrow_crafting_publication() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE a jsonb; k text; v text; step jsonb;
BEGIN
 SELECT published_settings->'arrows' INTO a FROM crafting_definitions WHERE definition_id='v1';
 IF a IS NULL OR a='null'::jsonb THEN RETURN NULL; END IF;
 IF jsonb_typeof(a)<>'object' OR NOT a ?& ARRAY['knife_item_id','shaft_item_id','headless_item_id','feather_item_id','bronze_head_item_id','iron_head_item_id','bronze_arrow_item_id','iron_arrow_item_id','shafts','headless','bronze','iron']
 THEN RAISE EXCEPTION 'Complete arrow Crafting settings required'; END IF;
 FOR k,v IN SELECT key,value FROM jsonb_each_text(a) WHERE key LIKE '%item_id' LOOP
  PERFORM item_id FROM item_definitions WHERE item_id=v FOR SHARE;
  IF NOT EXISTS(SELECT 1 FROM item_definitions WHERE item_id=v AND runtime_enabled AND stackable=(k<>'knife_item_id'))
  THEN RAISE EXCEPTION 'Published arrow Crafting requires enabled item shapes: %', k; END IF;
 END LOOP;
 IF (SELECT count(DISTINCT value) FROM jsonb_each_text(a) WHERE key LIKE '%item_id')<>8
 OR EXISTS(SELECT 1 FROM crafting_definitions c, jsonb_each_text(c.published_settings) prior, jsonb_each_text(a) n
   WHERE c.definition_id='v1' AND prior.key LIKE '%item_id' AND n.key LIKE '%item_id' AND prior.value=n.value)
 THEN RAISE EXCEPTION 'Arrow bindings must be distinct'; END IF;
 FOREACH k IN ARRAY ARRAY['shafts','headless','bronze','iron'] LOOP
  step=a->k;
  IF jsonb_typeof(step)<>'object' OR NOT step ?& ARRAY['level','xp_tenths','duration_ms']
   OR jsonb_typeof(step->'level') IS DISTINCT FROM 'number' OR jsonb_typeof(step->'xp_tenths') IS DISTINCT FROM 'number' OR jsonb_typeof(step->'duration_ms') IS DISTINCT FROM 'number'
   OR (step->>'level')::int NOT BETWEEN 1 AND 99 OR (step->>'xp_tenths')::int NOT BETWEEN 0 AND 10000000
   OR (step->>'duration_ms')::int NOT BETWEEN 1 AND 86400000
  THEN RAISE EXCEPTION 'Invalid arrow Crafting step %', k; END IF;
 END LOOP;
 RETURN NULL;
END $$;
CREATE CONSTRAINT TRIGGER arrow_crafting_guard AFTER INSERT OR UPDATE OR DELETE ON crafting_definitions DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION validate_arrow_crafting_publication();
CREATE CONSTRAINT TRIGGER arrow_crafting_item_guard AFTER UPDATE OR DELETE ON item_definitions DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION validate_arrow_crafting_publication();
INSERT INTO schema_migrations(migration_file,migration_number,migration_name,notes)
VALUES('112_arrow_crafting.sql',112,'arrow_crafting','Validate inventory arrow recipes independently of retained furniture.');
COMMIT;
