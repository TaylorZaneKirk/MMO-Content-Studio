-- R1 authoring facts. Existing Mob Ranged defence is copied to each new channel.
BEGIN;

ALTER TABLE item_combat_profiles
    ADD COLUMN ranged_damage_type TEXT NULL;
ALTER TABLE item_combat_profiles
    DROP CONSTRAINT item_combat_profiles_attack_type_check,
    DROP CONSTRAINT item_combat_profiles_accuracy_style_check,
    DROP CONSTRAINT item_combat_profiles_attack_type_accuracy_style_check;
ALTER TABLE item_combat_profiles
    ADD CONSTRAINT item_combat_profiles_attack_type_check
        CHECK (attack_type IN ('melee', 'ranged')),
    ADD CONSTRAINT item_combat_profiles_accuracy_style_check
        CHECK (accuracy_style IS NULL OR accuracy_style IN ('thrust', 'slash', 'crush')),
    ADD CONSTRAINT item_combat_profiles_ranged_damage_type_check
        CHECK (ranged_damage_type IS NULL OR ranged_damage_type IN ('light', 'standard', 'heavy')),
    ADD CONSTRAINT item_combat_profiles_attack_type_accuracy_style_check
        CHECK (
            (attack_type = 'melee' AND accuracy_style IS NOT NULL AND ranged_damage_type IS NULL)
            OR (attack_type = 'ranged' AND accuracy_style IS NULL AND ranged_damage_type IS NOT NULL)
        );

ALTER TABLE mob_combat_bonuses
    ADD COLUMN defence_ranged_light INTEGER NOT NULL DEFAULT 0,
    ADD COLUMN defence_ranged_standard INTEGER NOT NULL DEFAULT 0,
    ADD COLUMN defence_ranged_heavy INTEGER NOT NULL DEFAULT 0;
UPDATE mob_combat_bonuses SET
    defence_ranged_light = defence_ranged,
    defence_ranged_standard = defence_ranged,
    defence_ranged_heavy = defence_ranged;
ALTER TABLE mob_combat_bonuses DROP COLUMN defence_ranged;

-- The legacy mapping put bows in the shield hand. The live weapon slot is right_hand.
UPDATE item_definitions SET
    equipment_slot_id = 'right_hand',
    runtime_enabled = TRUE,
    updated_at = NOW()
WHERE item_id = 'inventory_346_wooden_bow';

INSERT INTO item_combat_profiles (
    item_id, profile_id, attack_type, accuracy_style, ranged_damage_type,
    minimum_range_tiles, maximum_range_tiles, attack_speed_units)
SELECT item_id, 'wooden_bow_shortbow', 'ranged', NULL, 'standard', 1, 7, 4
FROM item_definitions WHERE item_id = 'inventory_346_wooden_bow'
ON CONFLICT (item_id) DO UPDATE SET
    profile_id = EXCLUDED.profile_id,
    attack_type = EXCLUDED.attack_type,
    accuracy_style = EXCLUDED.accuracy_style,
    ranged_damage_type = EXCLUDED.ranged_damage_type,
    minimum_range_tiles = EXCLUDED.minimum_range_tiles,
    maximum_range_tiles = EXCLUDED.maximum_range_tiles,
    attack_speed_units = EXCLUDED.attack_speed_units,
    updated_at = NOW();

-- Temporary POC behavior until ammunition owns its proper Ranged Strength contribution.
INSERT INTO item_combat_bonuses (item_id, attack_ranged, strength_ranged)
SELECT item_id, 3, 3 FROM item_definitions WHERE item_id = 'inventory_346_wooden_bow'
ON CONFLICT (item_id) DO UPDATE SET
    attack_ranged = EXCLUDED.attack_ranged,
    strength_ranged = EXCLUDED.strength_ranged,
    updated_at = NOW();

INSERT INTO item_skill_requirements (item_id, skill_id, required_value)
SELECT item_id, 'ranged', 1 FROM item_definitions WHERE item_id = 'inventory_346_wooden_bow'
ON CONFLICT (item_id, skill_id) DO UPDATE SET
    required_value = EXCLUDED.required_value,
    updated_at = NOW();

INSERT INTO schema_migrations (migration_file, migration_number, migration_name, notes)
VALUES ('069_ranged_authoring_facts.sql', 69, 'ranged_authoring_facts',
        'R1 ranged profile facts, Mob defence split and Wooden Bow shortbow POC.');
COMMIT;
