-- PREPARED ONLY. Stop old writers, take a fresh recoverable backup, review
-- previews/slime_family_mastery.sql, then apply only after activation approval.
BEGIN;
LOCK TABLE lore_definitions, character_skills, character_lore IN SHARE ROW EXCLUSIVE MODE;
DO $$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM schema_migrations WHERE migration_number=98)
 OR EXISTS(SELECT 1 FROM schema_migrations WHERE migration_number=99)
 OR NOT EXISTS(SELECT 1 FROM lore_definitions WHERE definition_id='v1'
   AND (published_settings->>'accuracy_basis_points')::int=50
   AND (published_settings->>'mastery_level')::int=3
   AND (published_settings->>'mastery_studies')::int=20)
 THEN RAISE EXCEPTION 'Reconcile the live Insight/V1 baseline before migration 099'; END IF;
 IF EXISTS(SELECT 1 FROM character_lore l WHERE NOT EXISTS(SELECT 1 FROM character_skills k WHERE k.character_id=l.character_id AND k.skill_id='discipline'))
 THEN RAISE EXCEPTION 'Missing Insight skill for mastery row'; END IF;
END $$;
CREATE TABLE slime_099_mastery_backup AS SELECT * FROM character_lore;
ALTER TABLE character_lore
 ADD COLUMN mastery_points bigint NOT NULL DEFAULT 0,
 ADD COLUMN unlocked_milestones integer NOT NULL DEFAULT 0 CHECK(unlocked_milestones BETWEEN 0 AND 140),
 ADD COLUMN legacy_accuracy_basis_points integer NOT NULL DEFAULT 0 CHECK(legacy_accuracy_basis_points IN (0,50));
-- Add the cross-column constraint after copying existing nonzero counts.

CREATE FUNCTION lore_eligible_milestones(points bigint, base_level integer) RETURNS integer
 LANGUAGE sql IMMUTABLE STRICT AS $$
 SELECT CASE WHEN base_level<3 THEN 0 ELSE coalesce(max(n),0) END
 FROM generate_series(1,140) AS milestones(n)
 WHERE points>=20::bigint*n+n::bigint*(n-1)/2;
$$;
UPDATE character_lore l SET mastery_points=l.studies,
 unlocked_milestones=lore_eligible_milestones(l.studies,k.base_value),
 legacy_accuracy_basis_points=CASE WHEN l.unlocked THEN 50 ELSE 0 END
 FROM character_skills k WHERE k.character_id=l.character_id AND k.skill_id='discipline';
ALTER TABLE character_lore ADD CONSTRAINT lore_points_cover_studies CHECK(mastery_points>=studies);
-- Legacy benefit is an accuracy floor. It does not manufacture points or awards.
-- Old binaries updating only studies are rejected rather than silently losing points.
CREATE FUNCTION preserve_family_mastery() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
 IF NEW.studies<OLD.studies OR NEW.mastery_points<OLD.mastery_points OR NEW.unlocked_milestones<OLD.unlocked_milestones
 OR NEW.legacy_accuracy_basis_points<>OLD.legacy_accuracy_basis_points OR NEW.unlocked<>OLD.unlocked
 OR (NEW.studies<>OLD.studies AND NEW.mastery_points=OLD.mastery_points)
 THEN RAISE EXCEPTION 'Mastery is monotonic; use a family-mastery-compatible writer'; END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER preserve_family_mastery BEFORE UPDATE ON character_lore FOR EACH ROW EXECUTE FUNCTION preserve_family_mastery();
-- A gained general level releases retained points without requiring another item.
CREATE FUNCTION unlock_level_gated_mastery() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
 IF NEW.skill_id='discipline' THEN
  UPDATE character_lore SET unlocked_milestones=greatest(unlocked_milestones,lore_eligible_milestones(mastery_points,NEW.base_value)) WHERE character_id=NEW.character_id;
 END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER unlock_level_gated_mastery AFTER INSERT OR UPDATE OF base_value,experience ON character_skills
 FOR EACH ROW EXECUTE FUNCTION unlock_level_gated_mastery();
CREATE OR REPLACE FUNCTION validate_lore_publication() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE s jsonb; specimen jsonb; members jsonb;
BEGIN
 SELECT published_settings INTO s FROM lore_definitions WHERE definition_id='v1';
 IF s IS NULL THEN RETURN NULL; END IF;
 IF s ? 'family' THEN
  members := s->'family'->'mob_definition_ids';
  IF s->'family'->>'family_id' IS DISTINCT FROM 'slime'
   OR s->'family'->>'display_name' IS DISTINCT FROM 'Slime'
   OR s->'family'->>'defence_style' IS DISTINCT FROM 'melee'
   OR jsonb_typeof(members) IS DISTINCT FROM 'array'
   OR jsonb_typeof(s->'specimens') IS DISTINCT FROM 'array'
  THEN RAISE EXCEPTION 'Published mastery requires the explicit Slime family and specimen rows'; END IF;
  PERFORM mob_definition_id FROM mob_definitions WHERE mob_definition_id IN (SELECT jsonb_array_elements_text(members)) FOR SHARE;
  IF jsonb_array_length(members)=0 OR NOT members @> '["slime"]'::jsonb
   OR (SELECT count(*) FROM jsonb_array_elements(members))<>(SELECT count(DISTINCT value) FROM jsonb_array_elements(members))
   OR EXISTS(SELECT 1 FROM jsonb_array_elements(members) m WHERE jsonb_typeof(m.value)<>'string')
   OR EXISTS(SELECT 1 FROM jsonb_array_elements_text(members) m(id) WHERE NOT EXISTS(
       SELECT 1 FROM mob_definitions d WHERE d.mob_definition_id=m.id AND d.publication_state='Published'))
  THEN RAISE EXCEPTION 'Family members must be unique published Mob IDs including slime'; END IF;
  IF jsonb_array_length(s->'specimens')=0
   OR (SELECT count(*) FROM jsonb_array_elements(s->'specimens'))<>(SELECT count(DISTINCT value->>'item_id') FROM jsonb_array_elements(s->'specimens'))
  THEN RAISE EXCEPTION 'Unique specimen items are required'; END IF;
  FOR specimen IN SELECT value FROM jsonb_array_elements(s->'specimens') LOOP
   PERFORM item_id FROM item_definitions WHERE item_id=specimen->>'item_id' FOR SHARE;
   IF jsonb_typeof(specimen)<>'object' OR NOT specimen ?& ARRAY['item_id','family_id','required_level','base_xp','mastery_points']
    OR EXISTS(SELECT 1 FROM jsonb_each(specimen) e WHERE e.value='null'::jsonb)
    OR jsonb_typeof(specimen->'required_level') IS DISTINCT FROM 'number'
    OR jsonb_typeof(specimen->'base_xp') IS DISTINCT FROM 'number'
    OR jsonb_typeof(specimen->'mastery_points') IS DISTINCT FROM 'number'
    OR specimen->>'family_id' IS DISTINCT FROM 'slime'
    OR NOT ((specimen->>'required_level')::int BETWEEN 1 AND 99)
    OR NOT ((specimen->>'base_xp')::int BETWEEN 1 AND 100000)
    OR NOT ((specimen->>'mastery_points')::int BETWEEN 1 AND 1000000)
    OR NOT EXISTS(SELECT 1 FROM item_definitions WHERE item_id=specimen->>'item_id' AND runtime_enabled AND NOT stackable AND trade_policy='tradeable')
   THEN RAISE EXCEPTION 'Invalid published specimen or item reference'; END IF;
  END LOOP;
  IF EXISTS(SELECT 1 FROM jsonb_array_elements(s->'specimens') higher,
     jsonb_array_elements(s->'specimens') lower
     WHERE (higher->>'required_level')::int>(lower->>'required_level')::int
       AND ((higher->>'base_xp')::int<=(lower->>'base_xp')::int
         OR (higher->>'mastery_points')::int<=(lower->>'mastery_points')::int))
  THEN RAISE EXCEPTION 'Higher-level specimens must award more XP and points'; END IF;
  IF EXISTS(SELECT 1 FROM unnest(ARRAY['study_duration_ms','station_xp_percent','station_auto_ms','station_manual_ms',
     'focus_drain_ms','focus_reduction_percent','fishing_focus_level','cooking_focus_level','mining_focus_level',
     'blacksmithing_focus_level','woodcutting_focus_level','crafting_focus_level','farming_focus_level','alchemy_focus_level']) key
     WHERE jsonb_typeof(s->key) IS DISTINCT FROM 'number')
  THEN RAISE EXCEPTION 'Study and focus numeric fields must be JSON numbers'; END IF;
  IF NOT (s->'specimens') @> '[{"item_id":"slime_goo","family_id":"slime","required_level":1,"base_xp":5,"mastery_points":1}]'::jsonb
   OR (s->>'study_duration_ms')::int IS DISTINCT FROM 1800
   OR (s->>'station_xp_percent')::int IS DISTINCT FROM 125
   OR (s->>'station_auto_ms')::int IS DISTINCT FROM 1800
   OR (s->>'station_manual_ms')::int IS DISTINCT FROM 600
  THEN RAISE EXCEPTION 'Basic Goo and existing study timing/XP multiplier must be preserved'; END IF;
  RETURN NULL;
 END IF;
 -- The previous published document stays valid until guarded content publication.

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
CREATE FUNCTION prevent_legacy_lore_document() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
 IF (OLD.settings ? 'family' AND NOT coalesce(NEW.settings ? 'family',false))
 OR (OLD.published_settings ? 'family' AND NOT coalesce(NEW.published_settings ? 'family',false))
 THEN RAISE EXCEPTION 'Use a family-mastery-compatible Studio document'; END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER prevent_legacy_lore_document BEFORE UPDATE ON lore_definitions FOR EACH ROW EXECUTE FUNCTION prevent_legacy_lore_document();
INSERT INTO schema_migrations(migration_file,migration_number,migration_name,notes)
VALUES('099_slime_family_mastery.sql',99,'slime_family_mastery','Preserve lifetime studies, weighted points, monotonic milestones and legacy accuracy floors.');
COMMIT;
