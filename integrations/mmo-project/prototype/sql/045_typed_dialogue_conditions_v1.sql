-- QV3 typed dialogue conditions for entry points and choices.
-- Conditions are normalized authored rows, exported to immutable runtime
-- dialogue catalogs, and evaluated server-side only.

CREATE TABLE IF NOT EXISTS dialogue_entry_conditions (
    dialogue_definition_id TEXT NOT NULL,
    entry_id TEXT NOT NULL,
    condition_order INTEGER NOT NULL,
    condition_type TEXT NOT NULL,
    quest_id TEXT NULL,
    quest_status TEXT NULL,
    quest_step_id TEXT NULL,
    item_id TEXT NULL,
    item_quantity INTEGER NULL,
    PRIMARY KEY (dialogue_definition_id, entry_id, condition_order),
    FOREIGN KEY (dialogue_definition_id, entry_id)
        REFERENCES dialogue_entry_points(dialogue_definition_id, entry_id)
        ON DELETE CASCADE,
    CONSTRAINT dialogue_entry_conditions_condition_order_check
        CHECK (condition_order BETWEEN 0 AND 10000),
    CONSTRAINT dialogue_entry_conditions_type_check
        CHECK (condition_type IN ('quest_status', 'quest_step', 'has_item')),
    CONSTRAINT dialogue_entry_conditions_quest_id_format_check
        CHECK (quest_id IS NULL OR (quest_id = LOWER(quest_id) AND quest_id ~ '^[a-z][a-z0-9]*(_[a-z0-9]+)*$')),
    CONSTRAINT dialogue_entry_conditions_quest_step_id_format_check
        CHECK (quest_step_id IS NULL OR (quest_step_id = LOWER(quest_step_id) AND quest_step_id ~ '^[a-z][a-z0-9]*(_[a-z0-9]+)*$')),
    CONSTRAINT dialogue_entry_conditions_item_id_format_check
        CHECK (item_id IS NULL OR (item_id = LOWER(item_id) AND item_id ~ '^[a-z][a-z0-9]*(_[a-z0-9]+)*$')),
    CONSTRAINT dialogue_entry_conditions_quest_status_check
        CHECK (quest_status IS NULL OR quest_status IN ('not_started', 'active', 'completed')),
    CONSTRAINT dialogue_entry_conditions_shape_check
        CHECK (
            (
                condition_type = 'quest_status'
                AND quest_id IS NOT NULL
                AND quest_status IS NOT NULL
                AND quest_step_id IS NULL
                AND item_id IS NULL
                AND item_quantity IS NULL
            )
            OR (
                condition_type = 'quest_step'
                AND quest_id IS NOT NULL
                AND quest_status IS NULL
                AND quest_step_id IS NOT NULL
                AND item_id IS NULL
                AND item_quantity IS NULL
            )
            OR (
                condition_type = 'has_item'
                AND quest_id IS NULL
                AND quest_status IS NULL
                AND quest_step_id IS NULL
                AND item_id IS NOT NULL
                AND item_quantity IS NOT NULL
                AND item_quantity >= 1
            )
        )
);

CREATE TABLE IF NOT EXISTS dialogue_choice_conditions (
    dialogue_definition_id TEXT NOT NULL,
    node_id TEXT NOT NULL,
    choice_id TEXT NOT NULL,
    condition_order INTEGER NOT NULL,
    condition_type TEXT NOT NULL,
    quest_id TEXT NULL,
    quest_status TEXT NULL,
    quest_step_id TEXT NULL,
    item_id TEXT NULL,
    item_quantity INTEGER NULL,
    PRIMARY KEY (dialogue_definition_id, node_id, choice_id, condition_order),
    FOREIGN KEY (dialogue_definition_id, node_id, choice_id)
        REFERENCES dialogue_choices(dialogue_definition_id, node_id, choice_id)
        ON DELETE CASCADE,
    CONSTRAINT dialogue_choice_conditions_condition_order_check
        CHECK (condition_order BETWEEN 0 AND 10000),
    CONSTRAINT dialogue_choice_conditions_type_check
        CHECK (condition_type IN ('quest_status', 'quest_step', 'has_item')),
    CONSTRAINT dialogue_choice_conditions_quest_id_format_check
        CHECK (quest_id IS NULL OR (quest_id = LOWER(quest_id) AND quest_id ~ '^[a-z][a-z0-9]*(_[a-z0-9]+)*$')),
    CONSTRAINT dialogue_choice_conditions_quest_step_id_format_check
        CHECK (quest_step_id IS NULL OR (quest_step_id = LOWER(quest_step_id) AND quest_step_id ~ '^[a-z][a-z0-9]*(_[a-z0-9]+)*$')),
    CONSTRAINT dialogue_choice_conditions_item_id_format_check
        CHECK (item_id IS NULL OR (item_id = LOWER(item_id) AND item_id ~ '^[a-z][a-z0-9]*(_[a-z0-9]+)*$')),
    CONSTRAINT dialogue_choice_conditions_quest_status_check
        CHECK (quest_status IS NULL OR quest_status IN ('not_started', 'active', 'completed')),
    CONSTRAINT dialogue_choice_conditions_shape_check
        CHECK (
            (
                condition_type = 'quest_status'
                AND quest_id IS NOT NULL
                AND quest_status IS NOT NULL
                AND quest_step_id IS NULL
                AND item_id IS NULL
                AND item_quantity IS NULL
            )
            OR (
                condition_type = 'quest_step'
                AND quest_id IS NOT NULL
                AND quest_status IS NULL
                AND quest_step_id IS NOT NULL
                AND item_id IS NULL
                AND item_quantity IS NULL
            )
            OR (
                condition_type = 'has_item'
                AND quest_id IS NULL
                AND quest_status IS NULL
                AND quest_step_id IS NULL
                AND item_id IS NOT NULL
                AND item_quantity IS NOT NULL
                AND item_quantity >= 1
            )
        )
);

CREATE INDEX IF NOT EXISTS dialogue_entry_conditions_quest_idx
    ON dialogue_entry_conditions(quest_id)
    WHERE quest_id IS NOT NULL;

CREATE INDEX IF NOT EXISTS dialogue_choice_conditions_quest_idx
    ON dialogue_choice_conditions(quest_id)
    WHERE quest_id IS NOT NULL;

CREATE INDEX IF NOT EXISTS dialogue_entry_conditions_item_idx
    ON dialogue_entry_conditions(item_id)
    WHERE item_id IS NOT NULL;

CREATE INDEX IF NOT EXISTS dialogue_choice_conditions_item_idx
    ON dialogue_choice_conditions(item_id)
    WHERE item_id IS NOT NULL;

DROP TRIGGER IF EXISTS dialogue_entry_conditions_touch_definition_updated_at ON dialogue_entry_conditions;
CREATE TRIGGER dialogue_entry_conditions_touch_definition_updated_at
AFTER INSERT OR UPDATE OR DELETE ON dialogue_entry_conditions
FOR EACH ROW
EXECUTE FUNCTION touch_dialogue_definition_updated_at();

DROP TRIGGER IF EXISTS dialogue_choice_conditions_touch_definition_updated_at ON dialogue_choice_conditions;
CREATE TRIGGER dialogue_choice_conditions_touch_definition_updated_at
AFTER INSERT OR UPDATE OR DELETE ON dialogue_choice_conditions
FOR EACH ROW
EXECUTE FUNCTION touch_dialogue_definition_updated_at();
