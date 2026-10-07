-- Alchemy V1 authoring and narrow persisted Attack boost. Prepare only; apply on delivery approval.
BEGIN;
CREATE TABLE alchemy_definitions (
 definition_id text PRIMARY KEY CHECK(definition_id='v1'),
 settings jsonb NOT NULL CHECK(jsonb_typeof(settings)='object'),
 published_settings jsonb,
 publication_state text NOT NULL DEFAULT 'Draft' CHECK(publication_state IN ('Draft','Published')),
 updated_at timestamptz NOT NULL DEFAULT clock_timestamp()
);
CREATE TABLE character_attack_boost (
 character_id uuid PRIMARY KEY REFERENCES characters(id) ON DELETE CASCADE,
 state jsonb NOT NULL CHECK(jsonb_typeof(state)='object')
);
CREATE FUNCTION validate_alchemy_publication() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
 PERFORM i.item_id FROM item_definitions i WHERE i.item_id IN (
  SELECT e.value FROM alchemy_definitions a,LATERAL jsonb_each_text(a.published_settings) e
  WHERE e.key LIKE '%item_id'
 ) ORDER BY i.item_id FOR SHARE;
 PERFORM w.definition_id FROM world_object_definitions w WHERE w.definition_id IN (
  SELECT published_settings->>'station_definition_id' FROM alchemy_definitions
 ) ORDER BY w.definition_id FOR SHARE;
 PERFORM x.definition_id FROM world_object_interactions x WHERE x.definition_id IN (
  SELECT published_settings->>'station_definition_id' FROM alchemy_definitions
 ) ORDER BY x.definition_id,x.interaction_order FOR SHARE;
 IF EXISTS (
  SELECT 1 FROM alchemy_definitions a,LATERAL jsonb_each_text(a.published_settings) e
  WHERE e.key LIKE '%item_id' AND NOT EXISTS (
   SELECT 1 FROM item_definitions i WHERE i.item_id=e.value AND i.runtime_enabled
    AND (e.key NOT IN ('crystal_item_id','bottle_item_id','potion3_item_id','potion2_item_id','potion1_item_id') OR NOT i.stackable)
    AND (e.key NOT IN ('shard_item_id','dust_item_id','petals_item_id') OR i.stackable)
  )
 ) OR EXISTS (
  SELECT 1 FROM alchemy_definitions a WHERE a.published_settings IS NOT NULL AND NOT EXISTS (
   SELECT 1 FROM world_object_definitions w JOIN world_object_interactions x USING(definition_id)
   WHERE w.definition_id=a.published_settings->>'station_definition_id'
    AND w.publication_state='Published' AND x.action_id='alchemy'
  )
 ) THEN RAISE EXCEPTION 'Published Alchemy requires enabled items, correct stackability and a published Alchemy station'; END IF;
 RETURN NULL;
END $$;
CREATE CONSTRAINT TRIGGER alchemy_publication_guard AFTER INSERT OR UPDATE ON alchemy_definitions DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION validate_alchemy_publication();
CREATE CONSTRAINT TRIGGER alchemy_item_guard AFTER UPDATE OR DELETE ON item_definitions DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION validate_alchemy_publication();
CREATE CONSTRAINT TRIGGER alchemy_station_guard AFTER UPDATE OR DELETE ON world_object_definitions DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION validate_alchemy_publication();
CREATE CONSTRAINT TRIGGER alchemy_action_guard AFTER INSERT OR UPDATE OR DELETE ON world_object_interactions DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION validate_alchemy_publication();
INSERT INTO schema_migrations(migration_file,migration_number,migration_name,notes)
VALUES('095_alchemy.sql',95,'alchemy','Alchemy publication and narrow paused Attack boost persistence.');
COMMIT;
