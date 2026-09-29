-- Existing spell art remains right-facing; authors may choose another cardinal direction.
BEGIN;
ALTER TABLE magic_combat_spells
    ADD COLUMN projectile_source_facing TEXT NOT NULL DEFAULT 'right'
    CONSTRAINT magic_spells_projectile_source_facing_check
    CHECK (projectile_source_facing IN ('right', 'down', 'left', 'up'));
INSERT INTO schema_migrations(migration_file, migration_number, migration_name, notes)
VALUES ('080_magic_projectile_source_facing.sql', 80, 'magic_projectile_source_facing',
    'Author the direction projectile source art faces before rotating toward travel.');
COMMIT;
