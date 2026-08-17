-- Quest Definition Authoring V1.
-- Adds draft/published/disabled quest-definition authoring tables used by
-- Content Studio and MapPublisher export. Runtime quest state remains owned by
-- character_quests and character_quest_transition_evidence.

BEGIN;

CREATE TABLE IF NOT EXISTS quest_definitions (
    quest_id TEXT PRIMARY KEY,
    display_name TEXT NOT NULL,
    publication_state TEXT NOT NULL DEFAULT 'Draft',
    schema_version INTEGER NOT NULL DEFAULT 1,
    created_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

ALTER TABLE quest_definitions
DROP CONSTRAINT IF EXISTS quest_definitions_id_format_check,
ADD CONSTRAINT quest_definitions_id_format_check
    CHECK (quest_id ~ '^[a-z][a-z0-9]*(_[a-z0-9]+)*$'),
DROP CONSTRAINT IF EXISTS quest_definitions_display_name_nonblank_check,
ADD CONSTRAINT quest_definitions_display_name_nonblank_check
    CHECK (length(btrim(display_name)) > 0),
DROP CONSTRAINT IF EXISTS quest_definitions_publication_state_check,
ADD CONSTRAINT quest_definitions_publication_state_check
    CHECK (publication_state IN ('Draft', 'Published', 'Disabled')),
DROP CONSTRAINT IF EXISTS quest_definitions_schema_version_check,
ADD CONSTRAINT quest_definitions_schema_version_check
    CHECK (schema_version = 1);

CREATE TABLE IF NOT EXISTS quest_steps (
    quest_id TEXT NOT NULL REFERENCES quest_definitions(quest_id) ON DELETE CASCADE,
    step_id TEXT NOT NULL,
    display_name TEXT NOT NULL,
    step_order INTEGER NOT NULL,
    PRIMARY KEY (quest_id, step_id)
);

ALTER TABLE quest_steps
DROP CONSTRAINT IF EXISTS quest_steps_step_id_format_check,
ADD CONSTRAINT quest_steps_step_id_format_check
    CHECK (step_id ~ '^[a-z][a-z0-9]*(_[a-z0-9]+)*$'),
DROP CONSTRAINT IF EXISTS quest_steps_display_name_nonblank_check,
ADD CONSTRAINT quest_steps_display_name_nonblank_check
    CHECK (length(btrim(display_name)) > 0),
DROP CONSTRAINT IF EXISTS quest_steps_order_nonnegative_check,
ADD CONSTRAINT quest_steps_order_nonnegative_check
    CHECK (step_order >= 0);

CREATE UNIQUE INDEX IF NOT EXISTS quest_steps_order_unique_idx
ON quest_steps(quest_id, step_order);

CREATE TABLE IF NOT EXISTS quest_transitions (
    quest_id TEXT NOT NULL REFERENCES quest_definitions(quest_id) ON DELETE CASCADE,
    transition_id TEXT NOT NULL,
    source_status TEXT NOT NULL,
    source_step_id TEXT NULL,
    target_status TEXT NOT NULL,
    target_step_id TEXT NULL,
    transition_order INTEGER NOT NULL,
    PRIMARY KEY (quest_id, transition_id),
    FOREIGN KEY (quest_id, source_step_id) REFERENCES quest_steps(quest_id, step_id) ON DELETE CASCADE,
    FOREIGN KEY (quest_id, target_step_id) REFERENCES quest_steps(quest_id, step_id) ON DELETE CASCADE
);

ALTER TABLE quest_transitions
DROP CONSTRAINT IF EXISTS quest_transitions_transition_id_format_check,
ADD CONSTRAINT quest_transitions_transition_id_format_check
    CHECK (transition_id ~ '^[a-z][a-z0-9]*(_[a-z0-9]+)*$'),
DROP CONSTRAINT IF EXISTS quest_transitions_source_status_check,
ADD CONSTRAINT quest_transitions_source_status_check
    CHECK (source_status IN ('not_started', 'active')),
DROP CONSTRAINT IF EXISTS quest_transitions_target_status_check,
ADD CONSTRAINT quest_transitions_target_status_check
    CHECK (target_status IN ('active', 'completed')),
DROP CONSTRAINT IF EXISTS quest_transitions_source_shape_check,
ADD CONSTRAINT quest_transitions_source_shape_check
    CHECK (
        (source_status = 'not_started' AND source_step_id IS NULL)
        OR (source_status = 'active' AND source_step_id IS NOT NULL)
    ),
DROP CONSTRAINT IF EXISTS quest_transitions_target_shape_check,
ADD CONSTRAINT quest_transitions_target_shape_check
    CHECK (
        (target_status = 'active' AND target_step_id IS NOT NULL)
        OR (target_status = 'completed' AND target_step_id IS NULL)
    ),
DROP CONSTRAINT IF EXISTS quest_transitions_order_nonnegative_check,
ADD CONSTRAINT quest_transitions_order_nonnegative_check
    CHECK (transition_order >= 0);

CREATE UNIQUE INDEX IF NOT EXISTS quest_transitions_order_unique_idx
ON quest_transitions(quest_id, transition_order);

CREATE OR REPLACE FUNCTION touch_quest_definition_updated_at()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $$
BEGIN
    NEW.updated_at_utc = NOW();
    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS quest_definitions_touch_updated_at
ON quest_definitions;

CREATE TRIGGER quest_definitions_touch_updated_at
BEFORE UPDATE
ON quest_definitions
FOR EACH ROW
EXECUTE FUNCTION touch_quest_definition_updated_at();

CREATE OR REPLACE FUNCTION touch_quest_definition_from_child()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $$
BEGIN
    UPDATE quest_definitions
    SET updated_at_utc = NOW()
    WHERE quest_id = COALESCE(NEW.quest_id, OLD.quest_id);
    RETURN COALESCE(NEW, OLD);
END;
$$;

DROP TRIGGER IF EXISTS quest_steps_touch_definition_updated_at
ON quest_steps;
CREATE TRIGGER quest_steps_touch_definition_updated_at
AFTER INSERT OR UPDATE OR DELETE
ON quest_steps
FOR EACH ROW
EXECUTE FUNCTION touch_quest_definition_from_child();

DROP TRIGGER IF EXISTS quest_transitions_touch_definition_updated_at
ON quest_transitions;
CREATE TRIGGER quest_transitions_touch_definition_updated_at
AFTER INSERT OR UPDATE OR DELETE
ON quest_transitions
FOR EACH ROW
EXECUTE FUNCTION touch_quest_definition_from_child();

COMMIT;
