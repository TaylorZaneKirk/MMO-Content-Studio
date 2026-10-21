-- Prepared only; apply with the matching game/Studio release, never as a source-build step.
BEGIN;
CREATE TABLE fishing_methods (
 action_id text PRIMARY KEY CHECK(action_id ~ '^[a-z][a-z0-9_]*$'),
 draft jsonb NOT NULL CHECK(jsonb_typeof(draft)='object' AND (draft->>'action_id') IS NOT DISTINCT FROM action_id),
 published_method jsonb,
 publication_state text NOT NULL DEFAULT 'Draft' CHECK(publication_state IN ('Draft','Published','Disabled')),
 updated_at timestamptz NOT NULL DEFAULT clock_timestamp()
);
-- Deferred dependency checks preserve independent draft edits and allow a complete
-- publication transaction, while rejecting dangling runtime item/method references.
CREATE FUNCTION validate_fishing_publication() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE m record; s jsonb; c jsonb; k text;
BEGIN
 IF TG_TABLE_NAME='fishing_methods' AND TG_OP='UPDATE' THEN
  IF OLD.action_id IS DISTINCT FROM NEW.action_id THEN
   RAISE EXCEPTION 'Fishing action IDs are immutable';
  END IF;
 END IF;
 -- Lock dependencies before checking them. A concurrent item disable or method
 -- edit must finish before these checks, or wait and recheck after publication.
 PERFORM action_id FROM fishing_methods ORDER BY action_id FOR SHARE;
 PERFORM i.item_id FROM item_definitions i WHERE i.item_id IN (
  SELECT published_method->>'tool_item_id' FROM fishing_methods WHERE published_method IS NOT NULL
  UNION SELECT published_method->>'consumable_item_id' FROM fishing_methods WHERE published_method IS NOT NULL
  UNION SELECT catch_row.value->>'item_id' FROM fishing_methods f,
   LATERAL jsonb_array_elements(f.published_method->'catches') catch_row(value)
 ) ORDER BY i.item_id FOR SHARE;
 PERFORM d.definition_id FROM world_object_definitions d
 WHERE EXISTS(SELECT 1 FROM world_object_interactions i JOIN fishing_methods f ON f.action_id=i.action_id
  WHERE i.definition_id=d.definition_id) ORDER BY d.definition_id FOR SHARE;
 PERFORM i.definition_id FROM world_object_interactions i JOIN fishing_methods f ON f.action_id=i.action_id
 ORDER BY i.definition_id,i.interaction_order FOR SHARE OF i;
 IF TG_TABLE_NAME='fishing_methods' AND TG_OP='DELETE' THEN
  IF OLD.published_method IS NOT NULL OR EXISTS(
   SELECT 1 FROM world_object_interactions i JOIN world_object_definitions d USING(definition_id)
   WHERE i.action_id=OLD.action_id AND d.publication_state='Published')
  THEN RAISE EXCEPTION 'Disable Fishing method and remove published object references before deletion'; END IF;
 END IF;
 FOR m IN SELECT action_id,published_method FROM fishing_methods WHERE published_method IS NOT NULL LOOP
  s:=m.published_method;
  IF m.action_id IN ('alchemy','bank','chop','clear_drain','close','cook','crafting','enter','enter_cellar','enter_home','fill','forge','inspect','leave_cellar','lock_gate','mill','mine','open','pick_wheat','place_crate','repair_cart','restore_concentration','smelt','study_auto','study_manual','use','return','claim','climb')
  THEN RAISE EXCEPTION 'Fishing cannot shadow another world-object command'; END IF;
  IF jsonb_typeof(s) IS DISTINCT FROM 'object' OR (s->>'action_id') IS DISTINCT FROM m.action_id
   OR jsonb_typeof(s->'display_name') IS DISTINCT FROM 'string' OR length(btrim(s->>'display_name'))=0
   OR jsonb_typeof(s->'tool_item_id') IS DISTINCT FROM 'string'
   OR jsonb_typeof(s->'catches') IS DISTINCT FROM 'array'
  THEN RAISE EXCEPTION 'Invalid Fishing method shape %',m.action_id; END IF;
  FOR k IN SELECT unnest(ARRAY['consume_quantity','interval_ms']) LOOP
   IF jsonb_typeof(s->k) IS DISTINCT FROM 'number' OR (s->>k) !~ '^[0-9]+$'
   THEN RAISE EXCEPTION 'Fishing % requires integer %',m.action_id,k; END IF;
  END LOOP;
  IF (s->>'interval_ms')::numeric NOT BETWEEN 600 AND 60000
   OR (s->>'consume_quantity')::numeric NOT BETWEEN 0 AND 1000
   OR NOT (s ? 'consumable_item_id')
   OR NOT EXISTS(SELECT 1 FROM item_definitions WHERE item_id=s->>'tool_item_id' AND runtime_enabled)
  THEN RAISE EXCEPTION 'Invalid Fishing timing, quantity or reusable published tool'; END IF;
  IF s->'consumable_item_id'='null'::jsonb THEN
   IF (s->>'consume_quantity')::int<>0 THEN RAISE EXCEPTION 'No Fishing consumable requires quantity0'; END IF;
  ELSIF jsonb_typeof(s->'consumable_item_id') IS DISTINCT FROM 'string'
   OR s->>'consumable_item_id'=s->>'tool_item_id' OR (s->>'consume_quantity')::int=0
   OR NOT EXISTS(SELECT 1 FROM item_definitions WHERE item_id=s->>'consumable_item_id' AND runtime_enabled AND stackable)
  THEN RAISE EXCEPTION 'Fishing consumable must be a separate published stackable item with positive quantity'; END IF;
  IF jsonb_array_length(s->'catches')=0 OR
   (SELECT count(*) FROM jsonb_array_elements(s->'catches'))<>(SELECT count(DISTINCT value->>'item_id') FROM jsonb_array_elements(s->'catches'))
  THEN RAISE EXCEPTION 'Fishing requires unique ordered catches'; END IF;
  FOR c IN SELECT value FROM jsonb_array_elements(s->'catches') LOOP
   IF jsonb_typeof(c) IS DISTINCT FROM 'object' OR jsonb_typeof(c->'item_id') IS DISTINCT FROM 'string'
    OR c->>'item_id'=s->>'consumable_item_id'
    OR NOT EXISTS(SELECT 1 FROM item_definitions WHERE item_id=c->>'item_id' AND runtime_enabled)
   THEN RAISE EXCEPTION 'Fishing catches require published item identities'; END IF;
   FOR k IN SELECT unnest(ARRAY['required_level','xp','success_low','success_high']) LOOP
    IF jsonb_typeof(c->k) IS DISTINCT FROM 'number' OR (c->>k) !~ '^[0-9]+$'
    THEN RAISE EXCEPTION 'Fishing catch requires integer %',k; END IF;
   END LOOP;
   IF (c->>'required_level')::numeric NOT BETWEEN 1 AND 99 OR (c->>'xp')::numeric NOT BETWEEN 1 AND 100000
    OR (c->>'success_low')::numeric NOT BETWEEN 0 AND 256 OR (c->>'success_high')::numeric NOT BETWEEN (c->>'success_low')::numeric AND 256
   THEN RAISE EXCEPTION 'Invalid Fishing catch level, XP or curve'; END IF;
  END LOOP;
 END LOOP;
 IF EXISTS(SELECT 1 FROM world_object_interactions i JOIN world_object_definitions d USING(definition_id)
   JOIN fishing_methods f ON f.action_id=i.action_id
   WHERE d.publication_state='Published' AND f.published_method IS NULL)
 THEN RAISE EXCEPTION 'Published world-object actions require published Fishing methods'; END IF;
 RETURN NULL;
END $$;
CREATE CONSTRAINT TRIGGER fishing_publication_guard AFTER INSERT OR UPDATE OR DELETE ON fishing_methods
 DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION validate_fishing_publication();
CREATE CONSTRAINT TRIGGER fishing_item_guard AFTER UPDATE OR DELETE ON item_definitions
 DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION validate_fishing_publication();
CREATE CONSTRAINT TRIGGER fishing_interaction_guard AFTER INSERT OR UPDATE OR DELETE ON world_object_interactions
 DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION validate_fishing_publication();
CREATE CONSTRAINT TRIGGER fishing_world_object_guard AFTER INSERT OR UPDATE ON world_object_definitions
 DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION validate_fishing_publication();
-- Seed only existing behavior here. Lure/Trout is the separately guarded content addition.
INSERT INTO fishing_methods(action_id,draft,published_method,publication_state) VALUES ('bait','{"action_id":"bait","display_name":"Bait","tool_item_id":"inventory_332_fishing_pole","consumable_item_id":"fishing_bait","consume_quantity":1,"interval_ms":3000,"catches":[{"item_id":"herring","required_level":10,"xp":30,"success_low":24,"success_high":128},{"item_id":"sardine","required_level":5,"xp":20,"success_low":32,"success_high":192}]}'::jsonb,'{"action_id":"bait","display_name":"Bait","tool_item_id":"inventory_332_fishing_pole","consumable_item_id":"fishing_bait","consume_quantity":1,"interval_ms":3000,"catches":[{"item_id":"herring","required_level":10,"xp":30,"success_low":24,"success_high":128},{"item_id":"sardine","required_level":5,"xp":20,"success_low":32,"success_high":192}]}'::jsonb,'Published');
INSERT INTO fishing_methods(action_id,draft,published_method,publication_state) VALUES ('net','{"action_id":"net","display_name":"Net","tool_item_id":"small_fishing_net","consumable_item_id":null,"consume_quantity":0,"interval_ms":3000,"catches":[{"item_id":"anchovies","required_level":15,"xp":40,"success_low":24,"success_high":128},{"item_id":"inventory_425_raw_fish","required_level":1,"xp":10,"success_low":48,"success_high":256}]}'::jsonb,'{"action_id":"net","display_name":"Net","tool_item_id":"small_fishing_net","consumable_item_id":null,"consume_quantity":0,"interval_ms":3000,"catches":[{"item_id":"anchovies","required_level":15,"xp":40,"success_low":24,"success_high":128},{"item_id":"inventory_425_raw_fish","required_level":1,"xp":10,"success_low":48,"success_high":256}]}'::jsonb,'Published');
INSERT INTO schema_migrations(migration_file,migration_number,migration_name,notes) VALUES('124_fishing_methods.sql',124,'fishing_methods','Independent Fishing drafts/publications; exact existing Net/Bait behavior.');
COMMIT;
