-- T2 Content Studio integration migration for declarative consumable authoring.
-- Apply this migration to the MMO Project development database before using
-- the unified Items workspace. Existing installations also need migration 050.

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
    amount INTEGER NOT NULL,
    PRIMARY KEY (item_id, effect_index),
    CONSTRAINT item_consumable_effects_identity_key
        UNIQUE (item_id, effect_type, target_id),
    CONSTRAINT item_consumable_effects_index_check
        CHECK (effect_index BETWEEN 0 AND 15),
    CONSTRAINT item_consumable_effects_type_check
        CHECK (effect_type IN ('restore_resource')),
    CONSTRAINT item_consumable_effects_resource_check
        CHECK (target_id IN ('health', 'concentration', 'special')),
    CONSTRAINT item_consumable_effects_amount_check
        CHECK (amount BETWEEN 1 AND 1000000)
);

CREATE INDEX IF NOT EXISTS item_consumable_profiles_result_item_id_idx
    ON item_consumable_profiles(result_item_id)
    WHERE result_item_id IS NOT NULL;

CREATE INDEX IF NOT EXISTS item_consumable_requirements_target_id_idx
    ON item_consumable_requirements(target_id);


-- Consumable values are intentionally authored in Studio; no food balance is seeded.

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
