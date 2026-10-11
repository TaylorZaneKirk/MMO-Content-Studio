-- PREPARED ONLY: requires stopped consumers, a fresh backup and separate activation.
BEGIN;
LOCK TABLE mob_combat_profiles IN SHARE ROW EXCLUSIVE MODE;
DO $$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM schema_migrations WHERE migration_number=107)
 OR EXISTS(SELECT 1 FROM schema_migrations WHERE migration_number=108)
 THEN RAISE EXCEPTION 'Migration 108 requires the reviewed mastery migration 107'; END IF;
END $$;
CREATE TABLE mob_108_profiles_backup AS SELECT * FROM mob_combat_profiles;
ALTER TABLE mob_combat_profiles
 ADD COLUMN ranged_level integer NOT NULL DEFAULT 0 CHECK(ranged_level BETWEEN 0 AND 1000000),
 ADD COLUMN spell_max_hit integer NOT NULL DEFAULT 0 CHECK(spell_max_hit BETWEEN 0 AND 1000000),
 ADD COLUMN projectile_visual text NOT NULL DEFAULT '',
 DROP CONSTRAINT mob_combat_profiles_attack_type_check,
 DROP CONSTRAINT mob_combat_profiles_accuracy_style_check,
 DROP CONSTRAINT mob_combat_profiles_attack_type_accuracy_style_check,
 ADD CONSTRAINT mob_combat_profiles_attack_type_check CHECK(attack_type IN ('melee','ranged','magic')),
 ADD CONSTRAINT mob_combat_profiles_accuracy_style_check CHECK(accuracy_style IN ('thrust','slash','crush','ranged','magic')),
 ADD CONSTRAINT mob_combat_profiles_attack_type_accuracy_style_check CHECK(
  (attack_type='melee' AND accuracy_style IN ('thrust','slash','crush')) OR
  (attack_type IN ('ranged','magic') AND accuracy_style=attack_type)),
 ADD CONSTRAINT mob_projectile_profile_check CHECK(
  (attack_type='melee' AND minimum_range_tiles=1 AND maximum_range_tiles=1 AND projectile_visual='' AND spell_max_hit=0) OR
  (attack_type='ranged' AND minimum_range_tiles>=1 AND maximum_range_tiles<=10 AND ranged_level>0 AND projectile_visual='seed' AND spell_max_hit=0) OR
  (attack_type='magic' AND minimum_range_tiles>=1 AND maximum_range_tiles<=10 AND magic_level>0 AND projectile_visual='arcane' AND spell_max_hit>0));
DO $$ BEGIN
 IF EXISTS(SELECT 1 FROM mob_combat_profiles p JOIN mob_108_profiles_backup b USING(mob_definition_id)
 WHERE to_jsonb(p)-ARRAY['ranged_level','spell_max_hit','projectile_visual'] <> to_jsonb(b))
 THEN RAISE EXCEPTION 'Existing Mob profile changed'; END IF;
END $$;
INSERT INTO schema_migrations(migration_file,migration_number,migration_name,notes)
VALUES('108_mob_projectile_attacks.sql',108,'mob_projectile_attacks','Add bounded single-style Mob Ranged/Magic profiles; preserve existing melee facts.');
COMMIT;
