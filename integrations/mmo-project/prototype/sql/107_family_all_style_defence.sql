-- PREPARED ONLY: stop/drain old game and Studio writers, back up, apply once,
-- then install matching consumers. Never restore this backup over later earnings.
BEGIN;
LOCK TABLE lore_definitions, character_skills, character_lore IN SHARE ROW EXCLUSIVE MODE;
DO $$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM schema_migrations WHERE migration_number=106)
 OR EXISTS(SELECT 1 FROM schema_migrations WHERE migration_number=107)
 THEN RAISE EXCEPTION 'Migration 107 requires exactly the prepared 106 baseline'; END IF;
END $$;
CREATE TABLE lore_107_progress_backup AS SELECT * FROM character_lore;
CREATE TABLE lore_107_settings_backup AS SELECT * FROM lore_definitions;
CREATE TABLE lore_107_skills_backup AS SELECT * FROM character_skills;

-- Same explicit schedule as FamilyMastery. Existing seven-category thresholds
-- stay fixed; the two defences divide the gap after each melee-defence reward.
CREATE FUNCTION lore_mastery_threshold(milestone integer) RETURNS bigint
 LANGUAGE plpgsql IMMUTABLE STRICT AS $$
DECLARE cycle integer; step integer; original integer; before_points bigint; gap bigint;
BEGIN
 IF milestone NOT BETWEEN 1 AND 180 THEN RAISE EXCEPTION 'Invalid creature milestone'; END IF;
 cycle := (milestone-1)/9; step := (milestone-1)%9+1;
 original := cycle*7 + CASE WHEN step<=4 THEN step WHEN step>=7 THEN step-2 ELSE 4 END;
 before_points := 20::bigint*original+original::bigint*(original-1)/2;
 IF step IN (5,6) THEN
  gap := 20+original;
  RETURN before_points+gap*(step-4)/3;
 END IF;
 RETURN before_points;
END $$;
CREATE OR REPLACE FUNCTION lore_eligible_milestones(points bigint, base_level integer, required_level integer) RETURNS integer
 LANGUAGE sql IMMUTABLE STRICT AS $$
 SELECT CASE WHEN base_level<required_level THEN 0 ELSE coalesce(max(n),0) END
 FROM generate_series(1,180) milestones(n) WHERE points>=lore_mastery_threshold(n);
$$;
ALTER TABLE character_lore DROP CONSTRAINT character_lore_unlocked_milestones_check;
ALTER TABLE character_lore ADD CONSTRAINT character_lore_unlocked_milestones_check CHECK(unlocked_milestones BETWEEN 0 AND 180);

-- Mapping protects unlocked rewards even when current level/tuning would not
-- grant them. Retroactive point eligibility only adds newly earned defence steps.
UPDATE character_lore l SET unlocked_milestones=greatest(
 (l.unlocked_milestones/7)*9 + (l.unlocked_milestones%7) + CASE WHEN l.unlocked_milestones%7>=5 THEN 2 ELSE 0 END,
 lore_eligible_milestones(l.mastery_points,k.base_value,CASE WHEN l.family_id='slime' THEN 3 ELSE 5 END))
FROM character_skills k WHERE k.character_id=l.character_id AND k.skill_id='discipline' AND l.family_id IN ('slime','beasts');
CREATE OR REPLACE FUNCTION validate_lore_publication() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE s jsonb; family jsonb; specimen jsonb; members jsonb; thresholds jsonb;
BEGIN
 SELECT published_settings INTO s FROM lore_definitions WHERE definition_id='v1';
 IF s IS NULL THEN RETURN NULL; END IF;
 IF jsonb_typeof(s->'families') IS DISTINCT FROM 'array' OR jsonb_typeof(s->'specimens') IS DISTINCT FROM 'array'
 THEN RAISE EXCEPTION 'Explicit families and specimens are required'; END IF;
 IF jsonb_array_length(s->'families')=0
 OR (SELECT count(*) FROM jsonb_array_elements(s->'families'))<>(SELECT count(DISTINCT value->>'family_id') FROM jsonb_array_elements(s->'families'))
 THEN RAISE EXCEPTION 'Unique families are required'; END IF;
 FOR family IN SELECT value FROM jsonb_array_elements(s->'families') LOOP
  members:=family->'mob_definition_ids';
  IF family->>'family_id'='flowers' THEN
   thresholds:=family->'milestone_points';
   IF family->>'display_name' IS DISTINCT FROM 'Flowers'
    OR family->>'defence_style' IS DISTINCT FROM 'none'
    OR members IS DISTINCT FROM '[]'::jsonb
    OR jsonb_typeof(family->'required_level') IS DISTINCT FROM 'number'
    OR NOT coalesce((family->>'required_level') ~ '^[0-9]+$',false)
    OR NOT ((family->>'required_level')::int BETWEEN 1 AND 99)
    OR jsonb_typeof(thresholds) IS DISTINCT FROM 'array'
   THEN RAISE EXCEPTION 'Flowers requires no Mobs, no defence and an integer Insight gate 1..99'; END IF;
   IF jsonb_array_length(thresholds)<>5
    OR EXISTS(SELECT 1 FROM jsonb_array_elements(thresholds) p
      WHERE jsonb_typeof(p.value)<>'number' OR NOT (p.value::text ~ '^[0-9]+$'))
   THEN RAISE EXCEPTION 'Flowers requires exactly five integer point thresholds'; END IF;
   IF EXISTS(SELECT 1 FROM jsonb_array_elements_text(thresholds) p
      WHERE p.value::bigint NOT BETWEEN 1 AND 1000000000)
    OR EXISTS(SELECT 1 FROM generate_series(1,4) n
      WHERE (thresholds->>(n-1))::bigint >= (thresholds->>n)::bigint)
   THEN RAISE EXCEPTION 'Flower thresholds must be positive and strictly increasing'; END IF;
   CONTINUE;
  END IF;
  IF family ? 'milestone_points' AND family->'milestone_points'<>'null'::jsonb
  THEN RAISE EXCEPTION 'Only Flowers authors milestone point thresholds'; END IF;
  IF jsonb_typeof(family) IS DISTINCT FROM 'object'
   OR family->>'family_id' IS NULL OR family->>'family_id' NOT IN ('slime','beasts')
   OR family->>'display_name' IS DISTINCT FROM (CASE WHEN family->>'family_id'='slime' THEN 'Slime' ELSE 'Beasts' END)
   OR family->>'defence_style' IS DISTINCT FROM 'all'
   OR jsonb_typeof(family->'required_level') IS DISTINCT FROM 'number'
   OR (family->>'required_level')::int IS DISTINCT FROM (CASE WHEN family->>'family_id'='slime' THEN 3 ELSE 5 END)
   OR jsonb_typeof(members) IS DISTINCT FROM 'array'
  THEN RAISE EXCEPTION 'Slime and Beasts require their approved identity, gate and all three combat defences'; END IF;
  PERFORM mob_definition_id FROM mob_definitions WHERE mob_definition_id IN (SELECT jsonb_array_elements_text(members)) FOR SHARE;
  IF jsonb_array_length(members)=0
   OR EXISTS(SELECT 1 FROM jsonb_array_elements(members) m WHERE jsonb_typeof(m.value)<>'string')
   OR EXISTS(SELECT 1 FROM jsonb_array_elements_text(members) m(id) WHERE NOT EXISTS(
       SELECT 1 FROM mob_definitions d WHERE d.mob_definition_id=m.id AND d.publication_state='Published'))
  THEN RAISE EXCEPTION 'Family members must be published Mob IDs'; END IF;
 END LOOP;
 IF NOT EXISTS(SELECT 1 FROM jsonb_array_elements(s->'families') f WHERE f->>'family_id'='slime'
   AND f->'mob_definition_ids' @> '["slime","cellar_slime"]'::jsonb)
 THEN RAISE EXCEPTION 'Preserve both existing Slime members'; END IF;
 IF EXISTS(SELECT member FROM jsonb_array_elements(s->'families') f,
   LATERAL jsonb_array_elements_text(f->'mob_definition_ids') member GROUP BY member HAVING count(*)>1)
 THEN RAISE EXCEPTION 'A Mob may belong to only one family'; END IF;
 IF jsonb_array_length(s->'specimens')=0
   OR (SELECT count(*) FROM jsonb_array_elements(s->'specimens'))<>(SELECT count(DISTINCT value->>'item_id') FROM jsonb_array_elements(s->'specimens'))
 THEN RAISE EXCEPTION 'Unique specimen items are required'; END IF;
 FOR specimen IN SELECT value FROM jsonb_array_elements(s->'specimens') LOOP
  PERFORM item_id FROM item_definitions WHERE item_id=specimen->>'item_id' FOR SHARE;
  IF jsonb_typeof(specimen) IS DISTINCT FROM 'object'
   OR NOT specimen ?& ARRAY['item_id','family_id','required_level','base_xp','mastery_points']
   OR EXISTS(SELECT 1 FROM jsonb_each(specimen) e WHERE e.value='null'::jsonb)
   OR jsonb_typeof(specimen->'required_level') IS DISTINCT FROM 'number'
   OR jsonb_typeof(specimen->'base_xp') IS DISTINCT FROM 'number'
   OR jsonb_typeof(specimen->'mastery_points') IS DISTINCT FROM 'number'
   OR NOT EXISTS(SELECT 1 FROM jsonb_array_elements(s->'families') f WHERE f->>'family_id'=specimen->>'family_id')
   OR NOT ((specimen->>'required_level')::int BETWEEN 1 AND 99)
   OR NOT ((specimen->>'base_xp')::int BETWEEN 1 AND 100000)
   OR NOT ((specimen->>'mastery_points')::int BETWEEN 1 AND 1000000)
   OR NOT EXISTS(SELECT 1 FROM item_definitions WHERE item_id=specimen->>'item_id' AND runtime_enabled AND NOT stackable AND trade_policy='tradeable')
  THEN RAISE EXCEPTION 'Invalid published specimen or item reference'; END IF;
 END LOOP;
 IF EXISTS(SELECT 1 FROM jsonb_array_elements(s->'specimens') higher, jsonb_array_elements(s->'specimens') lower
  WHERE higher->>'family_id'=lower->>'family_id' AND (higher->>'required_level')::int>(lower->>'required_level')::int
   AND ((higher->>'base_xp')::int<=(lower->>'base_xp')::int OR (higher->>'mastery_points')::int<=(lower->>'mastery_points')::int))
 THEN RAISE EXCEPTION 'Higher-level specimens within a family must award more XP and points'; END IF;
 IF NOT s->'specimens' @> '[{"item_id":"slime_goo","family_id":"slime","required_level":1,"base_xp":5,"mastery_points":1}]'::jsonb
 OR (EXISTS(SELECT 1 FROM jsonb_array_elements(s->'families') f WHERE f->>'family_id'='beasts')
   AND NOT s->'specimens' @> '[{"item_id":"rat_tail","family_id":"beasts","required_level":5,"base_xp":10,"mastery_points":2}]'::jsonb)
 THEN RAISE EXCEPTION 'Preserve Basic Goo and Rat Tail awards'; END IF;
 IF EXISTS(SELECT 1 FROM jsonb_array_elements(s->'families') f WHERE f->>'family_id'='flowers')
 AND (NOT s->'specimens' @> '[{"item_id":"inventory_484_flowers","family_id":"flowers","required_level":1,"base_xp":10,"mastery_points":1}]'::jsonb
  OR EXISTS(SELECT 1 FROM jsonb_array_elements(s->'specimens') p
    WHERE p->>'family_id'='flowers' AND p->>'item_id'<>'inventory_484_flowers'))
 THEN RAISE EXCEPTION 'Flowers studies only the existing flower: Insight 1, 10 XP, 1 point'; END IF;
 IF EXISTS(SELECT 1 FROM unnest(ARRAY['study_duration_ms','station_xp_percent','station_auto_ms','station_manual_ms',
     'focus_drain_ms','focus_reduction_percent','fishing_focus_level','cooking_focus_level','mining_focus_level',
     'blacksmithing_focus_level','woodcutting_focus_level','crafting_focus_level','farming_focus_level','alchemy_focus_level']) key
     WHERE jsonb_typeof(s->key) IS DISTINCT FROM 'number')
 OR (s->>'study_duration_ms')::int IS DISTINCT FROM 1800 OR (s->>'station_xp_percent')::int IS DISTINCT FROM 125
 OR (s->>'station_auto_ms')::int IS DISTINCT FROM 1800 OR (s->>'station_manual_ms')::int IS DISTINCT FROM 600
 THEN RAISE EXCEPTION 'Preserve existing study timings and XP multiplier'; END IF;
 RETURN NULL;
END $$;


-- Preserve draft/published differences and all unrelated settings.
UPDATE lore_definitions SET
 settings=jsonb_set(settings,'{families}',(SELECT jsonb_agg(CASE WHEN f->>'family_id' IN ('slime','beasts') THEN f || '{"defence_style":"all"}'::jsonb ELSE f END ORDER BY ord) FROM jsonb_array_elements(settings->'families') WITH ORDINALITY a(f,ord))),
 published_settings=jsonb_set(published_settings,'{families}',(SELECT jsonb_agg(CASE WHEN f->>'family_id' IN ('slime','beasts') THEN f || '{"defence_style":"all"}'::jsonb ELSE f END ORDER BY ord) FROM jsonb_array_elements(published_settings->'families') WITH ORDINALITY a(f,ord))),
 updated_at=clock_timestamp() WHERE definition_id='v1';
DO $$ BEGIN
 IF (SELECT count(*) FROM character_lore)<>(SELECT count(*) FROM lore_107_progress_backup)
 OR EXISTS(SELECT character_id,family_id,studies,mastery_points,unlocked,legacy_accuracy_basis_points FROM lore_107_progress_backup
 EXCEPT SELECT character_id,family_id,studies,mastery_points,unlocked,legacy_accuracy_basis_points FROM character_lore)
 OR EXISTS(SELECT * FROM lore_107_progress_backup WHERE family_id='flowers' EXCEPT SELECT * FROM character_lore WHERE family_id='flowers')
 OR EXISTS(SELECT * FROM lore_107_skills_backup EXCEPT SELECT * FROM character_skills)
 OR EXISTS(SELECT * FROM character_skills EXCEPT SELECT * FROM lore_107_skills_backup)
 THEN RAISE EXCEPTION 'Migration changed protected studies, points, identity, Flowers or XP'; END IF;
 -- Old categories 0..3 keep positions; strengths 4..6 move to 6..8.
 IF EXISTS(SELECT 1 FROM lore_107_progress_backup old JOIN character_lore live USING(character_id,family_id), generate_series(0,6) category
 WHERE old.family_id IN ('slime','beasts') AND
 greatest(least(20,(old.unlocked_milestones+6-category)/7)*50,CASE WHEN category<3 THEN old.legacy_accuracy_basis_points ELSE 0 END) <>
 greatest(least(20,(live.unlocked_milestones+8-(CASE WHEN category<4 THEN category ELSE category+2 END))/9)*50,CASE WHEN category<3 THEN live.legacy_accuracy_basis_points ELSE 0 END))
 THEN RAISE EXCEPTION 'Migration changed an existing combat bonus'; END IF;
END $$;
INSERT INTO schema_migrations(migration_file,migration_number,migration_name,notes)
VALUES('107_family_all_style_defence.sql',107,'family_all_style_defence','Nine individual rewards per cycle; original rewards, points and 12530-point completion preserved.');
COMMIT;
