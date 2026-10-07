-- PREPARED ONLY. Apply only during separately authorized delivery.
BEGIN;
CREATE TABLE lore_definitions (
 definition_id text PRIMARY KEY CHECK(definition_id='v1'),
 settings jsonb NOT NULL CHECK(jsonb_typeof(settings)='object'),
 published_settings jsonb CHECK(published_settings IS NULL OR jsonb_typeof(published_settings)='object'),
 publication_state text NOT NULL DEFAULT 'Draft' CHECK(publication_state IN ('Draft','Published')),
 updated_at timestamptz NOT NULL DEFAULT clock_timestamp()
);
CREATE TABLE character_lore (
 character_id uuid PRIMARY KEY REFERENCES characters(id) ON DELETE CASCADE,
 studies bigint NOT NULL DEFAULT 0 CHECK(studies>=0),
 unlocked boolean NOT NULL DEFAULT false
);
-- Display rename only: character XP, requirements, modifiers and wire identity survive.
UPDATE skill_definitions SET display_name='Lore',updated_at=clock_timestamp() WHERE skill_id='discipline';
CREATE FUNCTION validate_lore_publication() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE s jsonb;
BEGIN
 SELECT published_settings INTO s FROM lore_definitions WHERE definition_id='v1';
 IF s IS NULL THEN RETURN NULL; END IF;
 PERFORM item_id FROM item_definitions WHERE item_id=s->>'goo_item_id' FOR SHARE;
 PERFORM mob_definition_id FROM mob_definitions WHERE mob_definition_id=s->>'mob_definition_id' FOR SHARE;
 IF NOT s ?& ARRAY['goo_item_id','mob_definition_id','study_level','study_duration_ms','study_xp','mastery_studies','mastery_level','accuracy_basis_points']
 OR EXISTS(SELECT 1 FROM jsonb_each(s) e WHERE e.value='null'::jsonb)
 OR (s->>'goo_item_id') IS DISTINCT FROM 'slime_goo' OR (s->>'mob_definition_id') IS DISTINCT FROM 'slime'
 OR NOT EXISTS(SELECT 1 FROM item_definitions WHERE item_id=s->>'goo_item_id' AND runtime_enabled AND NOT stackable AND trade_policy='tradeable')
 OR NOT EXISTS(SELECT 1 FROM mob_definitions WHERE mob_definition_id=s->>'mob_definition_id' AND publication_state='Published')
 OR NOT ((s->>'study_level')::int BETWEEN 1 AND 99)
 OR NOT ((s->>'mastery_level')::int BETWEEN (s->>'study_level')::int AND 99)
 OR NOT ((s->>'study_duration_ms')::int BETWEEN 600 AND 60000)
 OR NOT ((s->>'study_xp')::int BETWEEN 1 AND 100000)
 OR NOT ((s->>'mastery_studies')::int BETWEEN 1 AND 1000000)
 OR NOT ((s->>'accuracy_basis_points')::int BETWEEN 1 AND 100)
 THEN RAISE EXCEPTION 'Published Lore requires valid bounded Slime/Goo rules, an enabled nonstackable tradeable Goo and published Slime'; END IF;
 RETURN NULL;
END $$;
CREATE CONSTRAINT TRIGGER lore_publication_guard AFTER INSERT OR UPDATE OR DELETE ON lore_definitions DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION validate_lore_publication();
CREATE CONSTRAINT TRIGGER lore_item_guard AFTER UPDATE OR DELETE ON item_definitions DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION validate_lore_publication();
CREATE CONSTRAINT TRIGGER lore_mob_guard AFTER UPDATE OR DELETE ON mob_definitions DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION validate_lore_publication();
INSERT INTO schema_migrations(migration_file,migration_number,migration_name,notes)
VALUES('097_lore.sql',97,'lore','Lore presentation, bounded study rules and durable Slime mastery.');
COMMIT;
