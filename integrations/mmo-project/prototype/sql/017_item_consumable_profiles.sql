-- T2 Content Studio integration migration for declarative consumable authoring.
-- Apply this migration to the MMO Project development database before using
-- the Consumables workspace.

CREATE TABLE IF NOT EXISTS item_consumable_profiles (
    item_id TEXT PRIMARY KEY REFERENCES item_definitions(item_id) ON DELETE CASCADE,
    use_action TEXT NOT NULL,
    consume_quantity INTEGER NOT NULL DEFAULT 1,
    result_item_id TEXT NULL REFERENCES item_definitions(item_id),
    success_message TEXT NULL,
    usable_in_combat BOOLEAN NOT NULL DEFAULT TRUE,
    cooldown_ms INTEGER NOT NULL DEFAULT 0,
    use_animation_id TEXT NULL,
    use_sound_resource_path TEXT NULL,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT item_consumable_profiles_use_action_check
        CHECK (use_action IN ('eat', 'drink', 'use')),
    CONSTRAINT item_consumable_profiles_consume_quantity_check
        CHECK (consume_quantity BETWEEN 1 AND 999),
    CONSTRAINT item_consumable_profiles_cooldown_ms_check
        CHECK (cooldown_ms BETWEEN 0 AND 86400000),
    CONSTRAINT item_consumable_profiles_result_not_self_check
        CHECK (result_item_id IS NULL OR result_item_id <> item_id)
);

CREATE TABLE IF NOT EXISTS item_consumable_requirements (
    item_id TEXT NOT NULL REFERENCES item_consumable_profiles(item_id) ON DELETE CASCADE,
    requirement_index INTEGER NOT NULL,
    requirement_type TEXT NOT NULL,
    target_id TEXT NOT NULL,
    minimum_value INTEGER NOT NULL,
    PRIMARY KEY (item_id, requirement_index),
    CONSTRAINT item_consumable_requirements_skill_id_fkey
        FOREIGN KEY (target_id) REFERENCES skill_definitions(skill_id),
    CONSTRAINT item_consumable_requirements_identity_key
        UNIQUE (item_id, requirement_type, target_id),
    CONSTRAINT item_consumable_requirements_index_check
        CHECK (requirement_index BETWEEN 0 AND 15),
    CONSTRAINT item_consumable_requirements_type_check
        CHECK (requirement_type IN ('skill_minimum')),
    CONSTRAINT item_consumable_requirements_minimum_value_check
        CHECK (minimum_value BETWEEN 1 AND 1000000)
);

CREATE TABLE IF NOT EXISTS item_consumable_effects (
    item_id TEXT NOT NULL REFERENCES item_consumable_profiles(item_id) ON DELETE CASCADE,
    effect_index INTEGER NOT NULL,
    effect_type TEXT NOT NULL,
    target_id TEXT NOT NULL,
    minimum_amount INTEGER NOT NULL,
    maximum_amount INTEGER NOT NULL,
    PRIMARY KEY (item_id, effect_index),
    CONSTRAINT item_consumable_effects_identity_key
        UNIQUE (item_id, effect_type, target_id),
    CONSTRAINT item_consumable_effects_index_check
        CHECK (effect_index BETWEEN 0 AND 15),
    CONSTRAINT item_consumable_effects_type_check
        CHECK (effect_type IN ('restore_resource')),
    CONSTRAINT item_consumable_effects_resource_check
        CHECK (target_id IN ('health', 'concentration', 'special')),
    CONSTRAINT item_consumable_effects_amount_range_check
        CHECK (
            minimum_amount BETWEEN 1 AND 1000000
            AND maximum_amount BETWEEN minimum_amount AND 1000000
        )
);

CREATE INDEX IF NOT EXISTS item_consumable_profiles_result_item_id_idx
    ON item_consumable_profiles(result_item_id)
    WHERE result_item_id IS NOT NULL;

CREATE INDEX IF NOT EXISTS item_consumable_requirements_target_id_idx
    ON item_consumable_requirements(target_id);


-- Preserve the current hard-coded food behavior as initial declarative data.
-- The existing runtime computes BaseHealthRestore + Random.Next(1, Inclusive + 1),
-- so the authored minimum/maximum values below are the exact resulting ranges.
WITH legacy_food (
    display_name,
    use_action,
    minimum_amount,
    maximum_amount,
    success_message
) AS (
    VALUES
        ('Apple', 'eat', 2, 4, 'You eat an apple.'),
        ('Pie', 'eat', 6, 10, 'You eat a pie.'),
        ('Fish', 'eat', 6, 9, 'You eat some raw fish. *yeck*'),
        ('Corn', 'eat', 5, 7, 'You eat some corn.'),
        ('Watermelon', 'eat', 7, 10, 'You eat the watermelon.'),
        ('Ale', 'drink', 2, 4, 'You drink the ale. *burp*'),
        ('Orc Meat', 'eat', 4, 6, 'You eat some raw orc meat. You''re disgusting.'),
        ('Cyclops Meat', 'eat', 4, 6, 'You eat some raw cyclops meat. You''re disgusting.'),
        ('Yeti Meat', 'eat', 4, 6, 'You eat some raw yeti meat. You''re disgusting.'),
        ('Raw Fish', 'eat', 2, 3, 'You eat some raw fish. You''re disgusting.'),
        ('Fish Sticks', 'eat', 7, 11, 'You eat some fish sticks.'),
        ('Orc Burger', 'eat', 10, 15, 'You eat an orc burger.'),
        ('Cyclops Burger', 'eat', 13, 19, 'You eat a cyclops burger.'),
        ('Yeti Burger', 'eat', 18, 25, 'You eat a yeti burger.'),
        ('Orc Pot Pie', 'eat', 10, 14, 'You eat an orc pot pie.'),
        ('Trout', 'eat', 3, 5, 'You eat the trout.'),
        ('Catfish', 'eat', 4, 6, 'You eat the catfish.'),
        ('Swordfish', 'eat', 5, 7, 'You eat the swordfish.'),
        ('Squid', 'eat', 6, 8, 'You eat the squid.'),
        ('Trout Fillet', 'eat', 4, 6, 'You eat the trout fillet.'),
        ('Catfish Sandwich', 'eat', 5, 8, 'You eat the catfish sandwich.'),
        ('Swordfish Steak', 'eat', 8, 11, 'You eat the swordfish steak.'),
        ('Squid Platter', 'eat', 9, 13, 'You eat the squid platter.'),
        ('Cooked Pig', 'eat', 21, 28, 'You eat the cooked pig.')
),
inserted_profiles AS (
    INSERT INTO item_consumable_profiles (
        item_id,
        use_action,
        consume_quantity,
        result_item_id,
        success_message,
        usable_in_combat,
        cooldown_ms,
        use_animation_id,
        use_sound_resource_path
    )
    SELECT
        item.item_id,
        legacy.use_action,
        1,
        NULL,
        legacy.success_message,
        TRUE,
        0,
        NULL,
        NULL
    FROM legacy_food legacy
    JOIN item_definitions item ON item.item_name = legacy.display_name
    ON CONFLICT (item_id) DO NOTHING
    RETURNING item_id
)
INSERT INTO item_consumable_effects (
    item_id,
    effect_index,
    effect_type,
    target_id,
    minimum_amount,
    maximum_amount
)
SELECT
    item.item_id,
    0,
    'restore_resource',
    'health',
    legacy.minimum_amount,
    legacy.maximum_amount
FROM legacy_food legacy
JOIN item_definitions item ON item.item_name = legacy.display_name
JOIN inserted_profiles inserted ON inserted.item_id = item.item_id
ON CONFLICT (item_id, effect_index) DO NOTHING;

CREATE OR REPLACE FUNCTION enforce_consumable_result_publication_on_item()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    IF NEW.runtime_enabled = TRUE AND OLD.runtime_enabled = FALSE THEN
        IF EXISTS (
            SELECT 1
            FROM item_consumable_profiles profile
            JOIN item_definitions result_item
              ON result_item.item_id = profile.result_item_id
            WHERE profile.item_id = NEW.item_id
              AND result_item.runtime_enabled = FALSE
        ) THEN
            RAISE EXCEPTION 'Cannot publish consumable % while its result item is runtime-disabled.', NEW.item_id;
        END IF;
    END IF;

    IF NEW.runtime_enabled = FALSE AND OLD.runtime_enabled = TRUE THEN
        IF EXISTS (
            SELECT 1
            FROM item_consumable_profiles profile
            JOIN item_definitions source_item
              ON source_item.item_id = profile.item_id
            WHERE profile.result_item_id = OLD.item_id
              AND source_item.runtime_enabled = TRUE
        ) THEN
            RAISE EXCEPTION 'Cannot disable result item % while a published consumable references it.', OLD.item_id;
        END IF;
    END IF;

    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS item_definitions_consumable_result_publication_guard
    ON item_definitions;
CREATE TRIGGER item_definitions_consumable_result_publication_guard
BEFORE UPDATE OF runtime_enabled ON item_definitions
FOR EACH ROW
EXECUTE FUNCTION enforce_consumable_result_publication_on_item();

CREATE OR REPLACE FUNCTION enforce_published_consumable_profile_result()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    IF NEW.result_item_id IS NOT NULL
       AND EXISTS (
           SELECT 1
           FROM item_definitions source_item
           WHERE source_item.item_id = NEW.item_id
             AND source_item.runtime_enabled = TRUE
       )
       AND NOT EXISTS (
           SELECT 1
           FROM item_definitions result_item
           WHERE result_item.item_id = NEW.result_item_id
             AND result_item.runtime_enabled = TRUE
       ) THEN
        RAISE EXCEPTION 'Cannot assign runtime-disabled result item % to published consumable %.',
            NEW.result_item_id,
            NEW.item_id;
    END IF;

    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS item_consumable_profiles_result_publication_guard
    ON item_consumable_profiles;
CREATE TRIGGER item_consumable_profiles_result_publication_guard
BEFORE INSERT OR UPDATE OF result_item_id ON item_consumable_profiles
FOR EACH ROW
EXECUTE FUNCTION enforce_published_consumable_profile_result();
