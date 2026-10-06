-- Ordinary floor items belong to this world's RAM lifetime, not possession SQL.
-- Restart clears floor instances; authored spawn definitions remain content.
BEGIN;
CREATE OR REPLACE FUNCTION validate_item_stackability_publication()
RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF NEW.runtime_enabled AND NOT NEW.stackable AND
       (EXISTS (SELECT 1 FROM character_inventory WHERE item_id = NEW.item_id AND stack_count <> 1) OR
        EXISTS (SELECT 1 FROM character_equipment WHERE item_id = NEW.item_id AND (stack_count <> 1 OR slot_id = 'ammo'))) THEN
        RAISE EXCEPTION 'Cannot publish non-stackable item % with incompatible retained possession quantities.', NEW.item_id;
    END IF;
    IF NEW.runtime_enabled AND EXISTS (SELECT 1 FROM character_equipment
        WHERE item_id = NEW.item_id AND slot_id = 'ammo') AND NEW.equipment_slot_id IS DISTINCT FROM 'ammo' THEN
        RAISE EXCEPTION 'Cannot change equipped Ammo item % to another slot.', NEW.item_id;
    END IF;
    RETURN NEW;
END;
$$;
-- DROP TABLE removes its own indexes, constraints and triggers. Shared item
-- publication/stackability functions remain for inventory and equipment.
DROP TABLE ground_items;
INSERT INTO schema_migrations(migration_file, migration_number, migration_name, notes)
VALUES ('071_ephemeral_ground_items.sql', 71, 'ephemeral_ground_items',
        'Retire ordinary floor persistence; keep inventory and equipment publication guards.');
COMMIT;
