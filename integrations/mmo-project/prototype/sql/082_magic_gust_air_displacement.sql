-- Authored one-shot Air displacement and Mob resistance; no content is seeded.
BEGIN;
ALTER TABLE magic_combat_spells
    ADD COLUMN cast_mode TEXT NOT NULL DEFAULT 'selected_combat'
        CONSTRAINT magic_spells_cast_mode_check CHECK (cast_mode IN ('selected_combat', 'explicit_technique')),
    ADD COLUMN impact_effect TEXT NULL
        CONSTRAINT magic_spells_impact_effect_check CHECK (impact_effect IS NULL OR impact_effect = 'air_displacement'),
    ADD COLUMN force INTEGER NULL
        CONSTRAINT magic_spells_force_check CHECK (force IS NULL OR force > 0),
    ADD COLUMN force_falloff_per_tile INTEGER NULL
        CONSTRAINT magic_spells_force_falloff_check CHECK (force_falloff_per_tile IS NULL OR force_falloff_per_tile >= 0),
    ADD COLUMN max_displacement_tiles INTEGER NULL
        CONSTRAINT magic_spells_max_displacement_check CHECK (max_displacement_tiles IS NULL OR max_displacement_tiles > 0),
    ADD CONSTRAINT magic_spells_force_shape_check CHECK (
        (impact_effect IS NOT NULL AND impact_effect = 'air_displacement'
            AND force IS NOT NULL AND force_falloff_per_tile IS NOT NULL AND max_displacement_tiles IS NOT NULL)
        OR (impact_effect IS NULL AND force IS NULL AND force_falloff_per_tile IS NULL AND max_displacement_tiles IS NULL));
ALTER TABLE mob_combat_profiles
    ADD COLUMN physical_weight INTEGER NOT NULL DEFAULT 100
        CONSTRAINT mob_combat_profiles_physical_weight_check CHECK (physical_weight BETWEEN 1 AND 1000000);
INSERT INTO schema_migrations(migration_file, migration_number, migration_name, notes)
VALUES ('082_magic_gust_air_displacement.sql', 82, 'magic_gust_air_displacement',
    'Authored cast mode, Air displacement force and Mob physical weight.');
COMMIT;
