BEGIN;
ALTER TABLE characters
    ADD COLUMN melee_combat_option_slot SMALLINT NOT NULL DEFAULT 0,
    ADD CONSTRAINT characters_melee_combat_option_slot_check
        CHECK (melee_combat_option_slot BETWEEN 0 AND 3);

CREATE TABLE item_melee_combat_options (
    item_id TEXT NOT NULL REFERENCES item_definitions(item_id) ON DELETE CASCADE,
    option_slot SMALLINT NOT NULL,
    option_id TEXT NOT NULL,
    display_name TEXT NOT NULL,
    combat_style TEXT NOT NULL,
    accuracy_style TEXT NOT NULL,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT item_melee_combat_options_pkey PRIMARY KEY (item_id, option_slot),
    CONSTRAINT item_melee_combat_options_item_id_option_id_key UNIQUE (item_id, option_id),
    CONSTRAINT item_melee_combat_options_slot_check CHECK (option_slot BETWEEN 0 AND 3),
    CONSTRAINT item_melee_combat_options_id_check CHECK (option_id ~ '^[a-z][a-z0-9_]*$'),
    CONSTRAINT item_melee_combat_options_name_check CHECK (length(btrim(display_name)) > 0),
    CONSTRAINT item_melee_combat_options_style_check
        CHECK (combat_style IN ('accurate', 'aggressive', 'defensive', 'controlled')),
    CONSTRAINT item_melee_combat_options_accuracy_check
        CHECK (accuracy_style IN ('thrust', 'slash', 'crush'))
);

-- Preserve every existing profile, including tools authored after seed migrations.
INSERT INTO item_melee_combat_options
    (item_id, option_slot, option_id, display_name, combat_style, accuracy_style)
SELECT item_id, 0, 'attack', 'Attack', 'controlled', accuracy_style
FROM item_combat_profiles WHERE attack_type = 'melee';

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM item_combat_profiles WHERE item_id = 'inventory_125_gold_sword' AND attack_type = 'melee')
        OR NOT EXISTS (SELECT 1 FROM item_combat_profiles WHERE item_id = 'inventory_120_war_hammer' AND attack_type = 'melee') THEN
        RAISE EXCEPTION 'Named CR1 melee items are missing';
    END IF;
END $$;

DELETE FROM item_melee_combat_options WHERE item_id IN ('inventory_125_gold_sword', 'inventory_120_war_hammer');
INSERT INTO item_melee_combat_options
    (item_id, option_slot, option_id, display_name, combat_style, accuracy_style)
VALUES
    ('inventory_125_gold_sword', 0, 'chop', 'Chop', 'accurate', 'slash'),
    ('inventory_125_gold_sword', 1, 'slash', 'Slash', 'aggressive', 'slash'),
    ('inventory_125_gold_sword', 2, 'lunge', 'Lunge', 'controlled', 'thrust'),
    ('inventory_125_gold_sword', 3, 'block', 'Block', 'defensive', 'slash'),
    ('inventory_120_war_hammer', 0, 'pound', 'Pound', 'accurate', 'crush'),
    ('inventory_120_war_hammer', 1, 'pummel', 'Pummel', 'aggressive', 'crush'),
    ('inventory_120_war_hammer', 2, 'block', 'Block', 'defensive', 'crush');

ALTER TABLE item_combat_profiles
    DROP CONSTRAINT item_combat_profiles_accuracy_style_check,
    DROP CONSTRAINT item_combat_profiles_accuracy_style_nonempty,
    DROP CONSTRAINT item_combat_profiles_attack_type_accuracy_style_check,
    DROP COLUMN accuracy_style,
    ADD CONSTRAINT item_combat_profiles_family_shape_check CHECK (
        (attack_type = 'melee' AND ranged_damage_type IS NULL AND ammunition_family IS NULL)
        OR (attack_type = 'ranged' AND ((ammunition_family IS NULL AND ranged_damage_type IS NOT NULL)
            OR (ammunition_family = 'arrow' AND ranged_damage_type IS NULL)))
        OR (attack_type = 'magic' AND ranged_damage_type IS NULL AND ammunition_family IS NULL)
    );

INSERT INTO schema_migrations (migration_file, migration_number, migration_name, notes)
VALUES ('089_unified_combat_options_melee_styles.sql', 89,
    'unified_combat_options_melee_styles', 'Melee options and durable positional preference');

COMMIT;
