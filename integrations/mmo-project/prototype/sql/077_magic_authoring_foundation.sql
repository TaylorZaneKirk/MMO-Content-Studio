-- Establishes ordinary combat-spell authoring and immutable Magic facts.
-- Casting and fractional XP settlement remain separate runtime consumers.
BEGIN;
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM item_combat_bonuses WHERE strength_magic <> 0)
       OR EXISTS (SELECT 1 FROM mob_combat_bonuses WHERE strength_magic <> 0) THEN
        RAISE EXCEPTION 'Nonzero legacy Magic Strength requires an explicit content decision';
    END IF;
END $$;
ALTER TABLE item_combat_bonuses RENAME COLUMN strength_magic TO magic_damage_percent;
ALTER TABLE mob_combat_bonuses DROP COLUMN strength_magic;
ALTER TABLE item_combat_profiles
    DROP CONSTRAINT item_combat_profiles_attack_type_check,
    DROP CONSTRAINT item_combat_profiles_attack_type_accuracy_style_check,
    ADD CONSTRAINT item_combat_profiles_attack_type_check CHECK (attack_type IN ('melee','ranged','magic')),
    ADD CONSTRAINT item_combat_profiles_attack_type_accuracy_style_check CHECK (
        (attack_type = 'melee' AND accuracy_style IS NOT NULL AND ranged_damage_type IS NULL AND ammunition_family IS NULL)
        OR (attack_type = 'ranged' AND accuracy_style IS NULL AND (
            (ammunition_family IS NULL AND ranged_damage_type IS NOT NULL)
            OR (ammunition_family IS NOT NULL AND ranged_damage_type IS NULL)))
        OR (attack_type = 'magic' AND accuracy_style IS NULL AND ranged_damage_type IS NULL
            AND ammunition_family IS NULL AND maximum_ammunition_tier IS NULL)
    );
ALTER TABLE mob_combat_profiles
    ADD COLUMN magic_level INTEGER NOT NULL DEFAULT 0,
    ADD CONSTRAINT mob_combat_profiles_magic_level_check CHECK (magic_level BETWEEN 0 AND 1000000);
ALTER TABLE character_skills
    ADD COLUMN experience_tenths_remainder SMALLINT NOT NULL DEFAULT 0,
    ADD CONSTRAINT character_skills_experience_tenths_remainder_check CHECK (experience_tenths_remainder BETWEEN 0 AND 9);
CREATE TABLE magic_combat_spells (
    spell_id TEXT PRIMARY KEY,
    display_name TEXT NOT NULL,
    publication_state TEXT NOT NULL DEFAULT 'Draft',
    tier INTEGER NOT NULL,
    element TEXT NOT NULL,
    required_magic_level INTEGER NOT NULL,
    shard_cost INTEGER NOT NULL,
    successful_hit_min_damage INTEGER NOT NULL,
    base_max_hit INTEGER NOT NULL,
    base_cast_xp_tenths INTEGER NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT magic_combat_spells_id_check CHECK (spell_id ~ '^[a-z][a-z0-9]*(_[a-z0-9]+)*$'),
    CONSTRAINT magic_combat_spells_name_check CHECK (length(btrim(display_name)) > 0),
    CONSTRAINT magic_combat_spells_publication_state_check CHECK (publication_state IN ('Draft','Published','Disabled')),
    CONSTRAINT magic_combat_spells_tier_check CHECK (tier BETWEEN 1 AND 4),
    CONSTRAINT magic_combat_spells_element_check CHECK (element IN ('air','earth','fire','water')),
    CONSTRAINT magic_combat_spells_required_magic_level_check CHECK (required_magic_level BETWEEN 1 AND 99),
    CONSTRAINT magic_combat_spells_shard_cost_check CHECK (shard_cost > 0),
    CONSTRAINT magic_combat_spells_min_damage_check CHECK (successful_hit_min_damage >= 0),
    CONSTRAINT magic_combat_spells_max_hit_check CHECK (base_max_hit >= successful_hit_min_damage),
    CONSTRAINT magic_combat_spells_cast_xp_check CHECK (base_cast_xp_tenths >= 0),
    CONSTRAINT magic_combat_spells_timestamp_order_check CHECK (created_at <= updated_at)
);
INSERT INTO schema_migrations(migration_file,migration_number,migration_name,notes)
VALUES ('077_magic_authoring_foundation.sql',77,'magic_authoring_foundation',
        'Combat spells, standard Magic focuses, Mob Magic defence and durable XP tenths remainder; no casting.');
COMMIT;
