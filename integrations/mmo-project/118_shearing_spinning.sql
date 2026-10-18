-- Optional spinning recipe uses the existing Crafting publication and item facts.
BEGIN;
CREATE FUNCTION validate_spinning_publication() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE s jsonb; spin jsonb;
BEGIN
 SELECT published_settings INTO s FROM crafting_definitions WHERE definition_id='v1';
 spin=s->'spinning';
 IF spin IS NULL OR spin='null'::jsonb THEN RETURN NULL; END IF;
 IF jsonb_typeof(spin) IS DISTINCT FROM 'object'
 OR spin->>'wool_item_id' IS DISTINCT FROM 'raw_wool'
 OR spin->>'station_definition_id' IS DISTINCT FROM 'spinning_wheel'
 OR NOT spin ?& ARRAY['wool_item_id','yarn_item_id','station_definition_id','level','xp_tenths','duration_ms']
 OR jsonb_typeof(spin->'level') IS DISTINCT FROM 'number'
 OR jsonb_typeof(spin->'xp_tenths') IS DISTINCT FROM 'number'
 OR jsonb_typeof(spin->'duration_ms') IS DISTINCT FROM 'number'
 OR (spin->>'level')::int NOT BETWEEN 1 AND 99
 OR (spin->>'xp_tenths')::int NOT BETWEEN 0 AND 10000000
 OR (spin->>'duration_ms')::int NOT BETWEEN 1 AND 86400000
 THEN RAISE EXCEPTION 'Invalid spinning recipe'; END IF;
 PERFORM item_id FROM item_definitions WHERE item_id IN ('shears',spin->>'wool_item_id',spin->>'yarn_item_id') ORDER BY item_id FOR SHARE;
 IF (SELECT count(*) FROM item_definitions WHERE item_id IN ('shears',spin->>'wool_item_id',spin->>'yarn_item_id')
     AND runtime_enabled AND NOT stackable AND trade_policy='tradeable')<>3
 OR EXISTS(SELECT 1 FROM jsonb_each_text(s) e WHERE e.key LIKE '%item_id' AND e.value IN (spin->>'wool_item_id',spin->>'yarn_item_id'))
 OR EXISTS(SELECT 1 FROM jsonb_array_elements(s->'furniture') f WHERE f->>'item_id' IN (spin->>'wool_item_id',spin->>'yarn_item_id'))
 OR EXISTS(SELECT 1 FROM jsonb_each_text(COALESCE(NULLIF(s->'arrows','null'::jsonb),'{}'::jsonb)) e WHERE e.key LIKE '%item_id' AND e.value IN (spin->>'wool_item_id',spin->>'yarn_item_id'))
 THEN RAISE EXCEPTION 'Spinning requires distinct enabled nonstackable tradeable items and shears'; END IF;
 PERFORM definition_id FROM world_object_definitions WHERE definition_id='spinning_wheel' FOR SHARE;
 IF NOT EXISTS(SELECT 1 FROM world_object_definitions w JOIN world_object_interactions i USING(definition_id)
 WHERE w.definition_id='spinning_wheel' AND w.publication_state='Published' AND w.blocks_movement
 AND w.footprint_width_tiles=1 AND w.footprint_height_tiles=1 AND i.action_id='crafting')
 THEN RAISE EXCEPTION 'Publish a 1x1 spinning wheel with Crafting action'; END IF;
 RETURN NULL;
END $$;
CREATE CONSTRAINT TRIGGER spinning_crafting_guard AFTER INSERT OR UPDATE OR DELETE ON crafting_definitions DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION validate_spinning_publication();
CREATE CONSTRAINT TRIGGER spinning_item_guard AFTER UPDATE OR DELETE ON item_definitions DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION validate_spinning_publication();
CREATE CONSTRAINT TRIGGER spinning_station_guard AFTER UPDATE OR DELETE ON world_object_definitions DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION validate_spinning_publication();
CREATE CONSTRAINT TRIGGER spinning_action_guard AFTER UPDATE OR DELETE ON world_object_interactions DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION validate_spinning_publication();
INSERT INTO schema_migrations(migration_file,migration_number,migration_name,notes)
VALUES('118_shearing_spinning.sql',118,'shearing_spinning','Protect published spinning recipe, item shapes and station dependencies.');
COMMIT;
