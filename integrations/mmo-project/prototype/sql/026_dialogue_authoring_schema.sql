-- D2 Content Studio integration migration for reusable dialogue definition authoring.
-- This is a handoff artifact for MMO Project; Content Studio does not apply it
-- automatically and this file must not be treated as a runtime hot-reload path.

CREATE TABLE IF NOT EXISTS dialogue_definitions (
    dialogue_definition_id TEXT PRIMARY KEY,
    display_name TEXT NOT NULL,
    publication_state TEXT NOT NULL DEFAULT 'Draft',
    schema_version INTEGER NOT NULL DEFAULT 1,
    metadata_description TEXT NULL,
    notes TEXT NULL,
    created_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT dialogue_definitions_id_format_check
        CHECK (dialogue_definition_id = LOWER(dialogue_definition_id) AND dialogue_definition_id ~ '^[a-z][a-z0-9]*(_[a-z0-9]+)*$'),
    CONSTRAINT dialogue_definitions_display_name_nonblank_check
        CHECK (LENGTH(BTRIM(display_name)) > 0),
    CONSTRAINT dialogue_definitions_publication_state_check
        CHECK (publication_state IN ('Draft', 'Published', 'Disabled')),
    CONSTRAINT dialogue_definitions_schema_version_positive_check
        CHECK (schema_version > 0),
    CONSTRAINT dialogue_definitions_current_schema_version_check
        CHECK (schema_version = 1),
    CONSTRAINT dialogue_definitions_timestamp_order_check
        CHECK (created_at_utc <= updated_at_utc)
);

CREATE TABLE IF NOT EXISTS dialogue_nodes (
    dialogue_definition_id TEXT NOT NULL REFERENCES dialogue_definitions(dialogue_definition_id) ON DELETE CASCADE,
    node_id TEXT NOT NULL,
    node_type TEXT NOT NULL,
    speaker TEXT NULL,
    text TEXT NULL,
    next_node_id TEXT NULL,
    dismissible BOOLEAN NOT NULL DEFAULT TRUE,
    canvas_x DOUBLE PRECISION NOT NULL DEFAULT 0,
    canvas_y DOUBLE PRECISION NOT NULL DEFAULT 0,
    editor_notes TEXT NULL,
    node_order INTEGER NOT NULL,
    PRIMARY KEY (dialogue_definition_id, node_id),
    CONSTRAINT dialogue_nodes_id_format_check
        CHECK (node_id = LOWER(node_id) AND node_id ~ '^[a-z][a-z0-9]*(_[a-z0-9]+)*$'),
    CONSTRAINT dialogue_nodes_supported_node_type_check
        CHECK (node_type IN ('speaker_text', 'player_choice', 'end')),
    CONSTRAINT dialogue_nodes_canvas_finite_check
        CHECK (
            canvas_x::TEXT NOT IN ('Infinity', '-Infinity', 'NaN')
            AND canvas_y::TEXT NOT IN ('Infinity', '-Infinity', 'NaN')
        ),
    CONSTRAINT dialogue_nodes_node_order_check
        CHECK (node_order BETWEEN 0 AND 10000),
    CONSTRAINT dialogue_nodes_next_node_id_format_check
        CHECK (next_node_id IS NULL OR (next_node_id = LOWER(next_node_id) AND next_node_id ~ '^[a-z][a-z0-9]*(_[a-z0-9]+)*$'))
);

CREATE TABLE IF NOT EXISTS dialogue_entry_points (
    dialogue_definition_id TEXT NOT NULL REFERENCES dialogue_definitions(dialogue_definition_id) ON DELETE CASCADE,
    entry_id TEXT NOT NULL,
    node_id TEXT NOT NULL,
    priority INTEGER NOT NULL,
    entry_order INTEGER NOT NULL,
    PRIMARY KEY (dialogue_definition_id, entry_id),
    CONSTRAINT dialogue_entry_points_entry_id_format_check
        CHECK (entry_id = LOWER(entry_id) AND entry_id ~ '^[a-z][a-z0-9]*(_[a-z0-9]+)*$'),
    CONSTRAINT dialogue_entry_points_node_id_format_check
        CHECK (node_id = LOWER(node_id) AND node_id ~ '^[a-z][a-z0-9]*(_[a-z0-9]+)*$'),
    CONSTRAINT dialogue_entry_points_priority_check
        CHECK (priority BETWEEN -10000 AND 10000),
    CONSTRAINT dialogue_entry_points_entry_order_check
        CHECK (entry_order BETWEEN 0 AND 10000)
);

CREATE TABLE IF NOT EXISTS dialogue_choices (
    dialogue_definition_id TEXT NOT NULL,
    node_id TEXT NOT NULL,
    choice_id TEXT NOT NULL,
    text TEXT NOT NULL,
    target_node_id TEXT NOT NULL,
    choice_order INTEGER NOT NULL,
    PRIMARY KEY (dialogue_definition_id, node_id, choice_id),
    FOREIGN KEY (dialogue_definition_id, node_id)
        REFERENCES dialogue_nodes(dialogue_definition_id, node_id)
        ON DELETE CASCADE,
    CONSTRAINT dialogue_choices_choice_id_format_check
        CHECK (choice_id = LOWER(choice_id) AND choice_id ~ '^[a-z][a-z0-9]*(_[a-z0-9]+)*$'),
    CONSTRAINT dialogue_choices_target_node_id_format_check
        CHECK (target_node_id = LOWER(target_node_id) AND target_node_id ~ '^[a-z][a-z0-9]*(_[a-z0-9]+)*$'),
    CONSTRAINT dialogue_choices_text_nonblank_check
        CHECK (LENGTH(BTRIM(text)) > 0),
    CONSTRAINT dialogue_choices_choice_order_check
        CHECK (choice_order BETWEEN 0 AND 10000)
);

CREATE UNIQUE INDEX IF NOT EXISTS dialogue_entry_points_order_idx
    ON dialogue_entry_points(dialogue_definition_id, priority DESC, entry_order, entry_id);

CREATE UNIQUE INDEX IF NOT EXISTS dialogue_nodes_order_idx
    ON dialogue_nodes(dialogue_definition_id, node_order, node_id);

CREATE UNIQUE INDEX IF NOT EXISTS dialogue_choices_order_idx
    ON dialogue_choices(dialogue_definition_id, node_id, choice_order, choice_id);

CREATE INDEX IF NOT EXISTS dialogue_definitions_publication_state_idx
    ON dialogue_definitions(publication_state);

CREATE INDEX IF NOT EXISTS dialogue_entry_points_node_idx
    ON dialogue_entry_points(dialogue_definition_id, node_id);

CREATE INDEX IF NOT EXISTS dialogue_nodes_next_node_idx
    ON dialogue_nodes(dialogue_definition_id, next_node_id)
    WHERE next_node_id IS NOT NULL;

CREATE INDEX IF NOT EXISTS dialogue_choices_target_node_idx
    ON dialogue_choices(dialogue_definition_id, target_node_id);

CREATE OR REPLACE FUNCTION touch_dialogue_definition_updated_at()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    touched_dialogue_definition_id TEXT;
BEGIN
    touched_dialogue_definition_id := COALESCE(NEW.dialogue_definition_id, OLD.dialogue_definition_id);
    UPDATE dialogue_definitions
    SET updated_at_utc = NOW()
    WHERE dialogue_definition_id = touched_dialogue_definition_id;
    RETURN COALESCE(NEW, OLD);
END;
$$;

DROP TRIGGER IF EXISTS dialogue_entry_points_touch_definition_updated_at ON dialogue_entry_points;
CREATE TRIGGER dialogue_entry_points_touch_definition_updated_at
AFTER INSERT OR UPDATE OR DELETE ON dialogue_entry_points
FOR EACH ROW
EXECUTE FUNCTION touch_dialogue_definition_updated_at();

DROP TRIGGER IF EXISTS dialogue_nodes_touch_definition_updated_at ON dialogue_nodes;
CREATE TRIGGER dialogue_nodes_touch_definition_updated_at
AFTER INSERT OR UPDATE OR DELETE ON dialogue_nodes
FOR EACH ROW
EXECUTE FUNCTION touch_dialogue_definition_updated_at();

DROP TRIGGER IF EXISTS dialogue_choices_touch_definition_updated_at ON dialogue_choices;
CREATE TRIGGER dialogue_choices_touch_definition_updated_at
AFTER INSERT OR UPDATE OR DELETE ON dialogue_choices
FOR EACH ROW
EXECUTE FUNCTION touch_dialogue_definition_updated_at();
