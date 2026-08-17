-- QV4 typed dialogue choice effects and exact-once settlement foundation.
-- Effects are authored on player choices only. Runtime settlement persists a
-- frozen per-command plan before applying quest, inventory, or XP mutations.

BEGIN;

CREATE EXTENSION IF NOT EXISTS pgcrypto;

ALTER TABLE quest_definitions
ADD COLUMN IF NOT EXISTS publication_token UUID NULL;

ALTER TABLE dialogue_definitions
ADD COLUMN IF NOT EXISTS publication_token UUID NULL;

CREATE TABLE IF NOT EXISTS dialogue_choice_effects (
    dialogue_definition_id TEXT NOT NULL,
    node_id TEXT NOT NULL,
    choice_id TEXT NOT NULL,
    effect_id TEXT NOT NULL,
    effect_order INTEGER NOT NULL,
    effect_type TEXT NOT NULL,
    quest_id TEXT NULL,
    transition_id TEXT NULL,
    item_id TEXT NULL,
    item_quantity INTEGER NULL,
    skill_id TEXT NULL,
    xp_amount BIGINT NULL,
    PRIMARY KEY (dialogue_definition_id, node_id, choice_id, effect_id),
    UNIQUE (dialogue_definition_id, node_id, choice_id, effect_order),
    FOREIGN KEY (dialogue_definition_id, node_id, choice_id)
        REFERENCES dialogue_choices(dialogue_definition_id, node_id, choice_id)
        ON DELETE CASCADE,
    CONSTRAINT dialogue_choice_effects_effect_id_format_check
        CHECK (effect_id = LOWER(effect_id) AND effect_id ~ '^[a-z][a-z0-9]*(_[a-z0-9]+)*$'),
    CONSTRAINT dialogue_choice_effects_effect_order_check
        CHECK (effect_order BETWEEN 0 AND 10000),
    CONSTRAINT dialogue_choice_effects_type_check
        CHECK (effect_type IN ('start_quest', 'advance_quest', 'complete_quest', 'grant_item', 'remove_item', 'grant_experience')),
    CONSTRAINT dialogue_choice_effects_quest_id_format_check
        CHECK (quest_id IS NULL OR (quest_id = LOWER(quest_id) AND quest_id ~ '^[a-z][a-z0-9]*(_[a-z0-9]+)*$')),
    CONSTRAINT dialogue_choice_effects_transition_id_format_check
        CHECK (transition_id IS NULL OR (transition_id = LOWER(transition_id) AND transition_id ~ '^[a-z][a-z0-9]*(_[a-z0-9]+)*$')),
    CONSTRAINT dialogue_choice_effects_item_id_format_check
        CHECK (item_id IS NULL OR (item_id = LOWER(item_id) AND item_id ~ '^[a-z][a-z0-9]*(_[a-z0-9]+)*$')),
    CONSTRAINT dialogue_choice_effects_skill_id_format_check
        CHECK (skill_id IS NULL OR (skill_id = LOWER(skill_id) AND skill_id ~ '^[a-z][a-z0-9]*(_[a-z0-9]+)*$')),
    CONSTRAINT dialogue_choice_effects_shape_check
        CHECK (
            (
                effect_type IN ('start_quest', 'advance_quest', 'complete_quest')
                AND quest_id IS NOT NULL
                AND transition_id IS NOT NULL
                AND item_id IS NULL
                AND item_quantity IS NULL
                AND skill_id IS NULL
                AND xp_amount IS NULL
            )
            OR (
                effect_type IN ('grant_item', 'remove_item')
                AND quest_id IS NULL
                AND transition_id IS NULL
                AND item_id IS NOT NULL
                AND item_quantity IS NOT NULL
                AND item_quantity >= 1
                AND skill_id IS NULL
                AND xp_amount IS NULL
            )
            OR (
                effect_type = 'grant_experience'
                AND quest_id IS NULL
                AND transition_id IS NULL
                AND item_id IS NULL
                AND item_quantity IS NULL
                AND skill_id IS NOT NULL
                AND xp_amount IS NOT NULL
                AND xp_amount > 0
            )
        )
);

CREATE INDEX IF NOT EXISTS dialogue_choice_effects_quest_idx
    ON dialogue_choice_effects(quest_id)
    WHERE quest_id IS NOT NULL;

CREATE INDEX IF NOT EXISTS dialogue_choice_effects_item_idx
    ON dialogue_choice_effects(item_id)
    WHERE item_id IS NOT NULL;

CREATE INDEX IF NOT EXISTS dialogue_choice_effects_skill_idx
    ON dialogue_choice_effects(skill_id)
    WHERE skill_id IS NOT NULL;

DROP TRIGGER IF EXISTS dialogue_choice_effects_touch_definition_updated_at ON dialogue_choice_effects;
CREATE TRIGGER dialogue_choice_effects_touch_definition_updated_at
AFTER INSERT OR UPDATE OR DELETE ON dialogue_choice_effects
FOR EACH ROW
EXECUTE FUNCTION touch_dialogue_definition_updated_at();

CREATE OR REPLACE FUNCTION set_publication_token_for_published_content()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $$
BEGIN
    IF NEW.publication_state = 'Published'
       AND (TG_OP = 'INSERT'
            OR OLD.publication_state <> 'Published'
            OR NEW.publication_token IS NULL) THEN
        NEW.publication_token = gen_random_uuid();
    ELSIF NEW.publication_state <> 'Published' THEN
        NEW.publication_token = NULL;
    END IF;
    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS quest_definitions_publication_token_lifecycle ON quest_definitions;
CREATE TRIGGER quest_definitions_publication_token_lifecycle
BEFORE INSERT OR UPDATE OF publication_state
ON quest_definitions
FOR EACH ROW
EXECUTE FUNCTION set_publication_token_for_published_content();

DROP TRIGGER IF EXISTS dialogue_definitions_publication_token_lifecycle ON dialogue_definitions;
CREATE TRIGGER dialogue_definitions_publication_token_lifecycle
BEFORE INSERT OR UPDATE OF publication_state
ON dialogue_definitions
FOR EACH ROW
EXECUTE FUNCTION set_publication_token_for_published_content();

UPDATE quest_definitions
SET publication_token = gen_random_uuid()
WHERE publication_state = 'Published'
  AND publication_token IS NULL;

UPDATE dialogue_definitions
SET publication_token = gen_random_uuid()
WHERE publication_state = 'Published'
  AND publication_token IS NULL;

CREATE TABLE IF NOT EXISTS character_dialogue_choice_effect_settlements (
    settlement_id UUID PRIMARY KEY,
    character_id UUID NOT NULL REFERENCES characters(id) ON DELETE CASCADE,
    source_event_id TEXT NOT NULL,
    dialogue_id TEXT NOT NULL,
    dialogue_publication_token UUID NOT NULL,
    dialogue_instance_id TEXT NOT NULL,
    source_node_id TEXT NOT NULL,
    choice_id TEXT NOT NULL,
    command_sequence INTEGER NOT NULL,
    target_node_id TEXT NOT NULL,
    settlement_status TEXT NOT NULL DEFAULT 'admitted',
    admitted_at TIMESTAMPTZ NOT NULL,
    settled_at TIMESTAMPTZ NULL,
    UNIQUE (character_id, dialogue_instance_id, source_node_id, choice_id, command_sequence),
    UNIQUE (character_id, source_event_id),
    CONSTRAINT character_dialogue_choice_effect_settlements_status_check
        CHECK (settlement_status IN ('admitted', 'settled'))
);

CREATE TABLE IF NOT EXISTS character_dialogue_choice_effect_plan_rows (
    settlement_id UUID NOT NULL REFERENCES character_dialogue_choice_effect_settlements(settlement_id) ON DELETE CASCADE,
    effect_id TEXT NOT NULL,
    effect_order INTEGER NOT NULL,
    effect_type TEXT NOT NULL,
    quest_id TEXT NULL,
    quest_publication_token UUID NULL,
    transition_id TEXT NULL,
    item_id TEXT NULL,
    item_quantity INTEGER NULL,
    skill_id TEXT NULL,
    xp_amount BIGINT NULL,
    completed_at TIMESTAMPTZ NULL,
    completion_evidence TEXT NULL,
    PRIMARY KEY (settlement_id, effect_id),
    UNIQUE (settlement_id, effect_order),
    CONSTRAINT character_dialogue_choice_effect_plan_rows_order_check
        CHECK (effect_order BETWEEN 0 AND 10000),
    CONSTRAINT character_dialogue_choice_effect_plan_rows_type_check
        CHECK (effect_type IN ('start_quest', 'advance_quest', 'complete_quest', 'grant_item', 'remove_item', 'grant_experience'))
);

CREATE TABLE IF NOT EXISTS character_inventory_mutation_evidence (
    character_id UUID NOT NULL REFERENCES characters(id) ON DELETE CASCADE,
    source_event_id TEXT NOT NULL,
    effect_id TEXT NOT NULL,
    mutation_type TEXT NOT NULL,
    item_id TEXT NOT NULL REFERENCES item_definitions(item_id),
    quantity INTEGER NOT NULL,
    applied_at TIMESTAMPTZ NOT NULL,
    PRIMARY KEY (character_id, source_event_id, effect_id),
    CONSTRAINT character_inventory_mutation_evidence_type_check
        CHECK (mutation_type IN ('grant_item', 'remove_item')),
    CONSTRAINT character_inventory_mutation_evidence_quantity_check
        CHECK (quantity >= 1)
);

CREATE INDEX IF NOT EXISTS character_dialogue_choice_effect_settlements_recovery_idx
    ON character_dialogue_choice_effect_settlements(admitted_at, settlement_id)
    WHERE settlement_status <> 'settled';

COMMIT;
