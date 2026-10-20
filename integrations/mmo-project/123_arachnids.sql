-- Prepared only: no live application without separate release authorization.
-- Extend the existing family guards; no progress or authored settings are rewritten.
BEGIN;
ALTER TABLE character_lore DROP CONSTRAINT lore_known_family;
ALTER TABLE character_lore ADD CONSTRAINT lore_known_family CHECK(family_id IN ('slime','beasts','flowers','arachnids'));
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
   OR family->>'family_id' IS NULL OR family->>'family_id' NOT IN ('slime','beasts','arachnids')
   OR family->>'display_name' IS DISTINCT FROM (CASE WHEN family->>'family_id'='slime' THEN 'Slime' WHEN family->>'family_id'='beasts' THEN 'Beasts' ELSE 'Arachnids' END)
   OR family->>'defence_style' IS DISTINCT FROM 'all'
   OR jsonb_typeof(family->'required_level') IS DISTINCT FROM 'number'
   OR (family->>'required_level')::int IS DISTINCT FROM (CASE WHEN family->>'family_id'='slime' THEN 3 WHEN family->>'family_id'='beasts' THEN 5 ELSE 7 END)
   OR jsonb_typeof(members) IS DISTINCT FROM 'array'
  THEN RAISE EXCEPTION 'Slime, Beasts and Arachnids require their approved identity, gate and all three combat defences'; END IF;
  PERFORM mob_definition_id FROM mob_definitions WHERE mob_definition_id IN (SELECT jsonb_array_elements_text(members)) FOR SHARE;
  IF jsonb_array_length(members)=0
   OR EXISTS(SELECT 1 FROM jsonb_array_elements(members) m WHERE jsonb_typeof(m.value)<>'string')
   OR EXISTS(SELECT 1 FROM jsonb_array_elements_text(members) m(id) WHERE NOT EXISTS(
       SELECT 1 FROM mob_definitions d WHERE d.mob_definition_id=m.id AND d.publication_state='Published'))
  THEN RAISE EXCEPTION 'Family members must be published Mob IDs'; END IF;
  IF family->>'family_id'='arachnids' AND members IS DISTINCT FROM '["giant_spider"]'::jsonb
  THEN RAISE EXCEPTION 'Arachnids contains only Giant Spider'; END IF;
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
   AND NOT s->'specimens' @> '[{"item_id":"rat_tail","family_id":"beasts","required_level":1,"base_xp":10,"mastery_points":2}]'::jsonb)
 THEN RAISE EXCEPTION 'Preserve Basic Goo and Rat Tail awards'; END IF;
 IF EXISTS(SELECT 1 FROM jsonb_array_elements(s->'families') f WHERE f->>'family_id'='arachnids')
 AND NOT s->'specimens' @> '[{"item_id":"spider_fang","family_id":"arachnids","required_level":1,"base_xp":20,"mastery_points":4}]'::jsonb
 THEN RAISE EXCEPTION 'Spider Fang requires Insight 1, 20 XP and 4 Arachnids points'; END IF;
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

INSERT INTO schema_migrations(migration_file,migration_number,migration_name,notes)
VALUES('123_arachnids.sql',123,'arachnids','Allow approved Arachnids family without altering existing progress or independent drafts.');
COMMIT;
