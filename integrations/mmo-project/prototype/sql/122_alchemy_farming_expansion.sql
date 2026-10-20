-- Source-only migration. Apply with matching game/Studio packages during an authorized release.
BEGIN;
ALTER TABLE character_attack_boost RENAME TO character_combat_boost;
UPDATE character_combat_boost SET state=(state-'Amount') || jsonb_build_object('Attack',state->'Amount','Strength',0,'Defence',0);
CREATE FUNCTION migrate_alchemy_rows(s jsonb) RETURNS jsonb LANGUAGE sql IMMUTABLE AS $$
 SELECT CASE WHEN s IS NULL THEN NULL ELSE
 (s - ARRAY['flower_item_id','petals_item_id','potion3_item_id','potion2_item_id','potion1_item_id','prepare_level','prepare_duration_ms','prepare_xp_tenths','prepare_flower_quantity','prepare_petals_quantity','brew_level','brew_duration_ms','brew_xp_tenths','brew_bottle_quantity','brew_dust_quantity','brew_petals_quantity','brew_potion_quantity','attack_bonus_flat','attack_bonus_percent']) ||
 jsonb_build_object('recipes',jsonb_build_array(
 jsonb_build_object('recipe_id','prepare_petals','display_name','Prepare crushed petals','operation','alchemy','station_definition_id',s->'station_definition_id','required_level',s->'prepare_level','xp_tenths',s->'prepare_xp_tenths','duration_ms',s->'prepare_duration_ms','required_inventory_tool_id',NULL,
 'inputs',jsonb_build_array(jsonb_build_object('item_id',s->'flower_item_id','quantity',s->'prepare_flower_quantity')),
 'outputs',jsonb_build_array(jsonb_build_object('item_id',s->'petals_item_id','minimum',s->'prepare_petals_quantity','maximum',s->'prepare_petals_quantity'))),
 jsonb_build_object('recipe_id','brew_attack','display_name','Brew Attack potion','operation','alchemy','station_definition_id',s->'station_definition_id','required_level',s->'brew_level','xp_tenths',s->'brew_xp_tenths','duration_ms',s->'brew_duration_ms','required_inventory_tool_id',NULL,
 'inputs',jsonb_build_array(jsonb_build_object('item_id',s->'bottle_item_id','quantity',s->'brew_bottle_quantity'),jsonb_build_object('item_id',s->'dust_item_id','quantity',s->'brew_dust_quantity'),jsonb_build_object('item_id',s->'petals_item_id','quantity',s->'brew_petals_quantity')),
 'outputs',jsonb_build_array(jsonb_build_object('item_id',s->'potion3_item_id','minimum',s->'brew_potion_quantity','maximum',s->'brew_potion_quantity')))),
 'potions',jsonb_build_array(jsonb_build_object('stat','attack','potion3_item_id',s->'potion3_item_id','potion2_item_id',s->'potion2_item_id','potion1_item_id',s->'potion1_item_id','bonus_flat',s->'attack_bonus_flat','bonus_percent',s->'attack_bonus_percent'))) END
$$;
UPDATE alchemy_definitions SET settings=migrate_alchemy_rows(settings),published_settings=migrate_alchemy_rows(published_settings),updated_at=clock_timestamp();
DROP FUNCTION migrate_alchemy_rows(jsonb);
CREATE FUNCTION migrate_farming_rows(s jsonb) RETURNS jsonb LANGUAGE sql IMMUTABLE AS $$
 SELECT CASE WHEN s IS NULL THEN NULL ELSE (s-ARRAY['seed_item_id','flower_item_id','growth_seconds','plant_xp','harvest_xp']) || jsonb_build_object('crops',jsonb_build_array(jsonb_build_object('crop_id','flowers','display_name','Flower','seed_item_id',s->'seed_item_id','harvest_item_id',s->'flower_item_id','required_level',1,'growth_seconds',s->'growth_seconds','plant_xp',s->'plant_xp','harvest_xp',s->'harvest_xp'))) END
$$;
UPDATE farming_definitions SET settings=migrate_farming_rows(settings),published_settings=migrate_farming_rows(published_settings),updated_at=clock_timestamp();
DROP FUNCTION migrate_farming_rows(jsonb);
-- Shared reference leaves let deferred guards protect nested authored rows too.
CREATE FUNCTION alchemy_item_references(s jsonb) RETURNS TABLE(item_id text,stackable boolean) LANGUAGE sql IMMUTABLE AS $$
 SELECT s->>'crystal_item_id',false UNION SELECT s->>'bottle_item_id',false
 UNION SELECT s->>'hammer_item_id',NULL UNION SELECT s->>'shard_item_id',true UNION SELECT s->>'dust_item_id',true
 UNION SELECT v #>> '{}',NULL FROM jsonb_path_query(s,'$.recipes[*].inputs[*].item_id') v
 UNION SELECT v #>> '{}',NULL FROM jsonb_path_query(s,'$.recipes[*].outputs[*].item_id') v
 UNION SELECT p->>'potion3_item_id',false FROM jsonb_array_elements(s->'potions') p
 UNION SELECT p->>'potion2_item_id',false FROM jsonb_array_elements(s->'potions') p
 UNION SELECT p->>'potion1_item_id',false FROM jsonb_array_elements(s->'potions') p
$$;
CREATE OR REPLACE FUNCTION validate_alchemy_publication() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
 PERFORM i.item_id FROM item_definitions i WHERE i.item_id IN (SELECT ref.item_id FROM alchemy_definitions a,LATERAL alchemy_item_references(a.published_settings) ref) ORDER BY i.item_id FOR SHARE;
 PERFORM w.definition_id FROM world_object_definitions w WHERE w.definition_id IN (SELECT published_settings->>'station_definition_id' FROM alchemy_definitions) ORDER BY w.definition_id FOR SHARE;
 PERFORM x.definition_id FROM world_object_interactions x WHERE x.definition_id IN (SELECT published_settings->>'station_definition_id' FROM alchemy_definitions) ORDER BY x.definition_id,x.interaction_order FOR SHARE;
 IF EXISTS(SELECT 1 FROM alchemy_definitions a,LATERAL alchemy_item_references(a.published_settings) ref WHERE a.published_settings IS NOT NULL AND NOT EXISTS(SELECT 1 FROM item_definitions i WHERE i.item_id=ref.item_id AND i.runtime_enabled AND (ref.stackable IS NULL OR i.stackable=ref.stackable)))
 OR EXISTS(SELECT 1 FROM alchemy_definitions a WHERE a.published_settings IS NOT NULL AND NOT EXISTS(SELECT 1 FROM world_object_definitions w JOIN world_object_interactions x USING(definition_id) WHERE w.definition_id=a.published_settings->>'station_definition_id' AND w.publication_state='Published' AND x.action_id='alchemy'))
 THEN RAISE EXCEPTION 'Published Alchemy requires enabled ingredients, nonstackable dose items and a published lab'; END IF;
 RETURN NULL;
END $$;
CREATE FUNCTION farming_item_references(s jsonb) RETURNS TABLE(item_id text) LANGUAGE sql IMMUTABLE AS $$
 SELECT s->>'hoe_item_id' UNION SELECT s->>'bucket_item_id'
 UNION SELECT c->>'seed_item_id' FROM jsonb_array_elements(s->'crops') c
 UNION SELECT c->>'harvest_item_id' FROM jsonb_array_elements(s->'crops') c
$$;
CREATE OR REPLACE FUNCTION validate_farming_items() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
 PERFORM i.item_id FROM item_definitions i WHERE i.item_id IN (
 SELECT ref.item_id FROM farming_definitions f,LATERAL farming_item_references(f.published_settings) ref
 UNION SELECT state->'crop'->>'flower_item_id' FROM character_farming_patches WHERE state->'crop'->>'flower_item_id' IS NOT NULL) ORDER BY i.item_id FOR SHARE;
 IF EXISTS(SELECT 1 FROM farming_definitions f,LATERAL farming_item_references(f.published_settings) ref WHERE f.published_settings IS NOT NULL AND NOT EXISTS(SELECT 1 FROM item_definitions i WHERE i.item_id=ref.item_id AND i.runtime_enabled))
 OR EXISTS(SELECT 1 FROM character_farming_patches p WHERE p.state->'crop'->>'flower_item_id' IS NOT NULL AND NOT EXISTS(SELECT 1 FROM item_definitions i WHERE i.item_id=p.state->'crop'->>'flower_item_id' AND i.runtime_enabled))
 THEN RAISE EXCEPTION 'Published Farming or a frozen planted crop references an unavailable item'; END IF;
 RETURN NULL;
END $$;
-- Flush deferred reference checks before changing table constraints.
SET CONSTRAINTS ALL IMMEDIATE;
ALTER TABLE alchemy_definitions ADD CONSTRAINT alchemy_authored_rows CHECK(settings ?& ARRAY['recipes','potions'] AND (published_settings IS NULL OR published_settings ?& ARRAY['recipes','potions']));
ALTER TABLE farming_definitions ADD CONSTRAINT farming_authored_rows CHECK(settings ? 'crops' AND (published_settings IS NULL OR published_settings ? 'crops'));
INSERT INTO schema_migrations(migration_file,migration_number,migration_name,notes) VALUES('122_alchemy_farming_expansion.sql',122,'alchemy_farming_expansion','Authored recipe/crop rows and coexisting paused combat boosts; existing crops unchanged.');
COMMIT;
