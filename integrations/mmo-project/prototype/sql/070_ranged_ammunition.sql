-- Real Ammo equipment quantities and explicit weapon/ammunition ownership.
-- POC items are authored through Content Studio after this schema is installed.
BEGIN;
INSERT INTO equipment_slot_definitions(slot_id, display_name, sort_order)
VALUES ('ammo', 'Ammo', 75);
ALTER TABLE character_equipment
    ADD COLUMN stack_count INTEGER NOT NULL DEFAULT 1,
    ADD CONSTRAINT character_equipment_stack_count_check CHECK (stack_count > 0),
    ADD CONSTRAINT character_equipment_slot_quantity_check CHECK (slot_id = 'ammo' OR stack_count = 1);

CREATE TABLE item_ammunition_profiles (
    item_id TEXT PRIMARY KEY REFERENCES item_definitions(item_id) ON DELETE CASCADE,
    ammunition_family TEXT NOT NULL CHECK (ammunition_family = 'arrow'),
    ranged_damage_type TEXT NOT NULL CHECK (ranged_damage_type IN ('light','standard','heavy')),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
ALTER TABLE item_combat_profiles
    ADD COLUMN ammunition_family TEXT NULL,
    ADD CONSTRAINT item_combat_profiles_ammunition_family_check CHECK (ammunition_family IS NULL OR ammunition_family = 'arrow'),
    DROP CONSTRAINT item_combat_profiles_attack_type_accuracy_style_check;
ALTER TABLE item_combat_profiles
    ADD CONSTRAINT item_combat_profiles_attack_type_accuracy_style_check CHECK (
        (attack_type = 'melee' AND accuracy_style IS NOT NULL AND ranged_damage_type IS NULL AND ammunition_family IS NULL)
        OR (attack_type = 'ranged' AND accuracy_style IS NULL AND (
            (ammunition_family IS NULL AND ranged_damage_type IS NOT NULL)
            OR (ammunition_family IS NOT NULL AND ranged_damage_type IS NULL)))
    );

CREATE OR REPLACE FUNCTION validate_equipment_stackability()
RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE item_stackable BOOLEAN; item_slot TEXT;
BEGIN
    -- Spending retained ammunition cannot introduce a new possession, even if
    -- a later Draft edit changed its authoring facts after shot acceptance.
    IF TG_OP = 'UPDATE' AND NEW.character_id = OLD.character_id AND NEW.slot_id = OLD.slot_id
       AND NEW.item_id = OLD.item_id AND NEW.stack_count < OLD.stack_count THEN
        RETURN NEW;
    END IF;
    SELECT stackable, equipment_slot_id INTO item_stackable, item_slot
    FROM item_definitions WHERE item_id = NEW.item_id FOR SHARE;
    IF NEW.slot_id = 'ammo' AND (NOT item_stackable OR item_slot IS DISTINCT FROM 'ammo') THEN
        RAISE EXCEPTION 'Ammo equipment requires a stackable Ammo item: %.', NEW.item_id;
    END IF;
    RETURN NEW;
END;
$$;
CREATE TRIGGER equipment_stackability_guard BEFORE INSERT OR UPDATE OF item_id, slot_id, stack_count
ON character_equipment FOR EACH ROW EXECUTE FUNCTION validate_equipment_stackability();

CREATE OR REPLACE FUNCTION validate_item_stackability_publication()
RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF NEW.runtime_enabled AND NOT NEW.stackable AND
       (EXISTS (SELECT 1 FROM character_inventory WHERE item_id = NEW.item_id AND stack_count <> 1) OR
        EXISTS (SELECT 1 FROM ground_items WHERE item_id = NEW.item_id AND stack_count <> 1) OR
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
INSERT INTO schema_migrations(migration_file,migration_number,migration_name,notes)
VALUES ('070_ranged_ammunition.sql',70,'ranged_ammunition','Quantity-aware Ammo slot, explicit arrow profiles and weapon requirements; content authored through Studio.');
COMMIT;
