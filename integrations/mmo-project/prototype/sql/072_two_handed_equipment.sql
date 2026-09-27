-- Hand occupancy is authored equipment metadata. Possessions are not rewritten.
BEGIN;
ALTER TABLE item_definitions ADD COLUMN two_handed boolean NOT NULL DEFAULT false;
ALTER TABLE item_definitions ADD CONSTRAINT item_definitions_two_handed_shape
    CHECK (NOT two_handed OR equipment_slot_id IS NOT DISTINCT FROM 'right_hand');
INSERT INTO schema_migrations(migration_file, migration_number, migration_name, notes)
VALUES ('072_two_handed_equipment.sql', 72, 'two_handed_equipment',
        'Author right-hand equipment that excludes the left hand.');
COMMIT;
