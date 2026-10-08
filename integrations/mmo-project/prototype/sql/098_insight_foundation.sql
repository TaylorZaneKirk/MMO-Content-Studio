-- PREPARED ONLY: stop old consumers before applying. No live application in this phase.
-- Recovery: retained snapshots preserve original fractional XP and Concentration.
-- Do not roll back new earned hundredths into tenths: retain/export new progression.
BEGIN;
LOCK TABLE character_skills, character_stats IN SHARE ROW EXCLUSIVE MODE;
CREATE TABLE insight_098_progression_backup AS SELECT character_id,skill_id,experience,experience_tenths_remainder FROM character_skills;
CREATE TABLE insight_098_concentration_backup AS SELECT character_id,concentration_current,concentration_max FROM character_stats;
ALTER TABLE character_skills ADD COLUMN experience_hundredths_remainder smallint NOT NULL DEFAULT 0
 CHECK(experience_hundredths_remainder BETWEEN 0 AND 99);
UPDATE character_skills SET experience_hundredths_remainder=experience_tenths_remainder*10;
-- Keep the legacy field for recovery, but reject an old binary trying to write it.
CREATE FUNCTION reject_legacy_xp_fraction() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
 IF NEW.experience_tenths_remainder IS DISTINCT FROM OLD.experience_tenths_remainder THEN
  RAISE EXCEPTION 'Legacy tenths writer: deploy the Insight-compatible server';
 END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER insight_reject_legacy_fraction BEFORE UPDATE OF experience_tenths_remainder ON character_skills
 FOR EACH ROW EXECUTE FUNCTION reject_legacy_xp_fraction();
ALTER TABLE character_stats ADD COLUMN concentration_drain_ticks bigint NOT NULL DEFAULT 0 CHECK(concentration_drain_ticks>=0);
-- Floor the old fraction, clamp corrupt out-of-range legacy balances, zero old
-- capacity maps to zero current. No free refill; full remains full. Max 1..99.
UPDATE character_stats s SET concentration_current=CASE WHEN s.concentration_max>0
 THEN floor(least(1::numeric,greatest(0::numeric,s.concentration_current::numeric/s.concentration_max))*k.base_value)::int ELSE 0 END,
 concentration_max=k.base_value
 FROM character_skills k WHERE k.character_id=s.character_id AND k.skill_id='discipline';
DO $$ BEGIN
 IF EXISTS(SELECT 1 FROM character_stats s WHERE NOT EXISTS(SELECT 1 FROM character_skills k WHERE k.character_id=s.character_id AND k.skill_id='discipline'))
 THEN RAISE EXCEPTION 'Missing Insight progression: reconcile before migration'; END IF;
END $$;
ALTER TABLE character_stats ADD CONSTRAINT insight_concentration_bounds CHECK(concentration_max BETWEEN 1 AND 99 AND concentration_current BETWEEN 0 AND concentration_max);
-- Derive initial capacity from progression; new characters without the skill yet
-- start at level 1. Subsequent skill insertion reconciles the same stats row.
CREATE FUNCTION insight_initial_capacity() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
 NEW.concentration_max=coalesce((SELECT base_value FROM character_skills WHERE character_id=NEW.character_id AND skill_id='discipline'),1);
 NEW.concentration_current=NEW.concentration_max; RETURN NEW;
END $$;
CREATE TRIGGER insight_initial_capacity BEFORE INSERT ON character_stats FOR EACH ROW EXECUTE FUNCTION insight_initial_capacity();
CREATE FUNCTION insight_level_capacity() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
 IF NEW.skill_id='discipline' THEN
 UPDATE character_stats SET concentration_current=least(NEW.base_value,concentration_current+greatest(0,NEW.base_value-concentration_max)),
 concentration_max=NEW.base_value WHERE character_id=NEW.character_id;
 END IF; RETURN NEW;
END $$;
CREATE TRIGGER insight_level_capacity AFTER INSERT OR UPDATE OF base_value,experience ON character_skills FOR EACH ROW EXECUTE FUNCTION insight_level_capacity();
UPDATE skill_definitions SET display_name='Insight',updated_at=clock_timestamp() WHERE skill_id='discipline';
CREATE FUNCTION validate_insight_publication() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE s jsonb; key text;
BEGIN
 SELECT published_settings INTO s FROM lore_definitions WHERE definition_id='v1';
 -- Existing V1 remains valid until the separately guarded content upgrade.
 IF s IS NULL OR NOT s ? 'lectern_definition_id' THEN RETURN NULL; END IF;
 PERFORM definition_id FROM world_object_definitions WHERE definition_id=s->>'lectern_definition_id' FOR SHARE;
 IF NOT s ?& ARRAY['lectern_definition_id','station_xp_percent','station_auto_ms','station_manual_ms','focus_drain_ms','focus_reduction_percent','fishing_focus_level','cooking_focus_level','mining_focus_level','blacksmithing_focus_level','woodcutting_focus_level','crafting_focus_level','farming_focus_level','alchemy_focus_level']
 OR EXISTS(SELECT 1 FROM jsonb_each(s) e WHERE e.value='null'::jsonb)
 OR s->>'lectern_definition_id' IS DISTINCT FROM 'study_lectern'
 OR NOT ((s->>'station_xp_percent')::int BETWEEN 100 AND 200)
 OR NOT ((s->>'station_auto_ms')::int BETWEEN 600 AND 60000)
 OR NOT ((s->>'station_manual_ms')::int BETWEEN 600 AND 60000)
 OR NOT ((s->>'focus_drain_ms')::int BETWEEN 1000 AND 60000)
 OR NOT ((s->>'focus_reduction_percent')::int BETWEEN 1 AND 20)
 OR NOT EXISTS(SELECT 1 FROM world_object_definitions WHERE definition_id=s->>'lectern_definition_id' AND publication_state='Published' AND footprint_width_tiles=1 AND footprint_height_tiles=1)
 OR NOT EXISTS(SELECT 1 FROM world_object_interactions WHERE definition_id=s->>'lectern_definition_id' AND action_id='restore_concentration' AND is_default)
 OR (SELECT count(*) FROM world_object_interactions WHERE definition_id=s->>'lectern_definition_id' AND action_id IN ('restore_concentration','study_manual','study_auto'))<>3
 OR EXISTS(SELECT 1 FROM character_stats WHERE concentration_drain_ticks >= (s->>'focus_drain_ms')::bigint*10000)
 THEN RAISE EXCEPTION 'Invalid published Insight foundation settings or lectern'; END IF;
 FOREACH key IN ARRAY ARRAY['fishing_focus_level','cooking_focus_level','mining_focus_level','blacksmithing_focus_level','woodcutting_focus_level','crafting_focus_level','farming_focus_level','alchemy_focus_level'] LOOP
 IF NOT ((s->>key)::int BETWEEN 1 AND 99) THEN RAISE EXCEPTION 'Invalid focus level'; END IF;
 END LOOP;
 RETURN NULL;
END $$;
CREATE CONSTRAINT TRIGGER insight_publication_guard AFTER INSERT OR UPDATE OR DELETE ON lore_definitions DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION validate_insight_publication();
CREATE CONSTRAINT TRIGGER insight_station_guard AFTER UPDATE OR DELETE ON world_object_definitions DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION validate_insight_publication();
CREATE CONSTRAINT TRIGGER insight_interaction_guard AFTER INSERT OR UPDATE OR DELETE ON world_object_interactions DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION validate_insight_publication();
INSERT INTO schema_migrations(migration_file,migration_number,migration_name,notes)
VALUES('098_insight_foundation.sql',98,'insight_foundation','Prepared hundredths XP and proportional Concentration; legacy values retained for recovery.');
COMMIT;
