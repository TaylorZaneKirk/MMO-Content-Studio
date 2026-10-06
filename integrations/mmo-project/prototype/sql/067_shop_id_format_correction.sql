-- Tighten reusable Shop IDs to the established segmented lower-snake-case convention.
-- If incompatible rows somehow exist, ADD CONSTRAINT fails and this transaction rolls back,
-- preserving both the existing data and the original constraint.
BEGIN;

ALTER TABLE shop_definitions
    DROP CONSTRAINT shop_definitions_shop_definition_id_check;

ALTER TABLE shop_definitions
    ADD CONSTRAINT shop_definitions_shop_definition_id_check
    CHECK (shop_definition_id ~ '^[a-z][a-z0-9]*(_[a-z0-9]+)*$');

INSERT INTO schema_migrations (migration_file, migration_number, migration_name, notes)
VALUES ('067_shop_id_format_correction.sql', 67, 'shop_id_format_correction',
        'Align Shop stable IDs with the established segmented lower-snake-case convention.');

COMMIT;
