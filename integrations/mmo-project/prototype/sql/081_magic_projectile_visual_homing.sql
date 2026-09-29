-- Optional visual homing; existing spell projectiles keep frozen-endpoint presentation.
BEGIN;
ALTER TABLE magic_combat_spells
    ADD COLUMN projectile_homing_enabled BOOLEAN NOT NULL DEFAULT FALSE,
    ADD COLUMN projectile_homing_strength DOUBLE PRECISION NOT NULL DEFAULT 1.0
        CONSTRAINT magic_spells_projectile_homing_strength_check
        CHECK (projectile_homing_strength > 0 AND projectile_homing_strength <= 1
            AND projectile_homing_strength < 'Infinity'::double precision);
INSERT INTO schema_migrations(migration_file, migration_number, migration_name, notes)
VALUES ('081_magic_projectile_visual_homing.sql', 81, 'magic_projectile_visual_homing',
    'Optional presentation-only Magic projectile homing with authored bend strength.');
COMMIT;
