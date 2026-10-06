-- Optional authored Air mastery. Fixed-force techniques remain valid; no content is seeded.
BEGIN;
ALTER TABLE magic_combat_spells
    ADD COLUMN force_mastery_magic_levels_per_step INTEGER NULL
        CONSTRAINT magic_spells_force_mastery_magic_levels_per_step_check CHECK (force_mastery_magic_levels_per_step IS NULL OR force_mastery_magic_levels_per_step > 0),
    ADD COLUMN force_mastery_force_per_step INTEGER NULL
        CONSTRAINT magic_spells_force_mastery_force_per_step_check CHECK (force_mastery_force_per_step IS NULL OR force_mastery_force_per_step > 0),
    ADD COLUMN force_mastery_max_force INTEGER NULL
        CONSTRAINT magic_spells_force_mastery_max_force_check CHECK (force_mastery_max_force IS NULL OR force_mastery_max_force > 0),
    ADD COLUMN displacement_mastery_magic_levels_per_step INTEGER NULL
        CONSTRAINT magic_spells_displacement_mastery_magic_levels_per_step_check CHECK (displacement_mastery_magic_levels_per_step IS NULL OR displacement_mastery_magic_levels_per_step > 0),
    ADD COLUMN displacement_mastery_tiles_per_step INTEGER NULL
        CONSTRAINT magic_spells_displacement_mastery_tiles_per_step_check CHECK (displacement_mastery_tiles_per_step IS NULL OR displacement_mastery_tiles_per_step > 0),
    ADD COLUMN displacement_mastery_max_tiles INTEGER NULL
        CONSTRAINT magic_spells_displacement_mastery_max_tiles_check CHECK (displacement_mastery_max_tiles IS NULL OR displacement_mastery_max_tiles > 0),
    ADD CONSTRAINT magic_spells_air_mastery_shape_check CHECK (
        (force_mastery_magic_levels_per_step IS NULL AND force_mastery_force_per_step IS NULL AND force_mastery_max_force IS NULL AND displacement_mastery_magic_levels_per_step IS NULL AND displacement_mastery_tiles_per_step IS NULL AND displacement_mastery_max_tiles IS NULL)
        OR (COALESCE(impact_effect = 'air_displacement', false)
            AND force_mastery_magic_levels_per_step IS NOT NULL AND force_mastery_force_per_step IS NOT NULL AND force_mastery_max_force IS NOT NULL AND displacement_mastery_magic_levels_per_step IS NOT NULL AND displacement_mastery_tiles_per_step IS NOT NULL AND displacement_mastery_max_tiles IS NOT NULL
            AND force_mastery_max_force >= force
            AND displacement_mastery_max_tiles >= max_displacement_tiles));
INSERT INTO schema_migrations(migration_file, migration_number, migration_name, notes)
VALUES ('084_magic_air_force_mastery.sql', 84, 'magic_air_force_mastery',
    'Optional complete Air force and displacement mastery scalars; no spell content changes.');
COMMIT;
