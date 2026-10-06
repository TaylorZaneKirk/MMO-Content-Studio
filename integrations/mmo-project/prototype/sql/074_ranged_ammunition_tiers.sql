-- Nulls permit the existing profiles to be repaired through Studio after deploy.
-- Supported authoring and runtime startup require explicit positive tiers.
BEGIN;
ALTER TABLE item_ammunition_profiles
    ADD COLUMN ammunition_tier integer,
    ADD CONSTRAINT item_ammunition_profiles_ammunition_tier_check
        CHECK (ammunition_tier IS NULL OR ammunition_tier > 0);
ALTER TABLE item_combat_profiles
    ADD COLUMN maximum_ammunition_tier integer,
    ADD CONSTRAINT item_combat_profiles_maximum_ammunition_tier_check
        CHECK (maximum_ammunition_tier IS NULL OR maximum_ammunition_tier > 0),
    ADD CONSTRAINT item_combat_profiles_ammunition_tier_shape_check
        CHECK (maximum_ammunition_tier IS NULL OR
            (attack_type = 'ranged' AND ammunition_family IS NOT NULL AND ranged_damage_type IS NULL));
INSERT INTO schema_migrations(migration_file, migration_number, migration_name, notes)
VALUES ('074_ranged_ammunition_tiers.sql', 74, 'ranged_ammunition_tiers',
        'Explicit ammunition tier and weapon ceiling; author existing profiles through Studio before game startup.');
COMMIT;
