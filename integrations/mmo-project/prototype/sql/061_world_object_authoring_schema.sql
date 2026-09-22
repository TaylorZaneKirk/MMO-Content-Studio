-- Reusable World Object authoring only. Tiled continues to own placements.
BEGIN;
CREATE TABLE world_object_definitions (
    definition_id TEXT PRIMARY KEY CHECK (definition_id ~ '^[a-z][a-z0-9_]*$'),
    display_name TEXT NOT NULL CHECK (btrim(display_name) <> ''),
    publication_state TEXT NOT NULL DEFAULT 'Draft' CHECK (publication_state IN ('Draft', 'Published', 'Disabled')),
    blocks_movement BOOLEAN NOT NULL DEFAULT FALSE,
    footprint_width_tiles INTEGER NOT NULL CHECK (footprint_width_tiles > 0),
    footprint_height_tiles INTEGER NOT NULL CHECK (footprint_height_tiles > 0),
    visual_texture_path TEXT NOT NULL CHECK (btrim(visual_texture_path) <> ''),
    source_width INTEGER NOT NULL CHECK (source_width > 0),
    source_height INTEGER NOT NULL CHECK (source_height > 0),
    visual_anchor_offset_x DOUBLE PRECISION NOT NULL CHECK (visual_anchor_offset_x > '-Infinity'::float8 AND visual_anchor_offset_x < 'Infinity'::float8),
    visual_anchor_offset_y DOUBLE PRECISION NOT NULL CHECK (visual_anchor_offset_y > '-Infinity'::float8 AND visual_anchor_offset_y < 'Infinity'::float8),
    visual_render_scale DOUBLE PRECISION NOT NULL CHECK (visual_render_scale > 0 AND visual_render_scale < 'Infinity'::float8),
    visual_animation_fps DOUBLE PRECISION NULL CHECK (visual_animation_fps > 0 AND visual_animation_fps < 'Infinity'::float8),
    created_at_utc TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp(),
    updated_at_utc TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp()
);
CREATE TABLE world_object_interactions (
    definition_id TEXT NOT NULL REFERENCES world_object_definitions ON DELETE CASCADE,
    interaction_order INTEGER NOT NULL CHECK (interaction_order >= 0),
    action_id TEXT NOT NULL CHECK (action_id ~ '^[a-z][a-z0-9_]*$'),
    label TEXT NOT NULL CHECK (btrim(label) <> ''),
    is_default BOOLEAN NOT NULL DEFAULT FALSE,
    PRIMARY KEY (definition_id, interaction_order),
    UNIQUE (definition_id, action_id)
);
CREATE UNIQUE INDEX world_object_one_default_interaction
    ON world_object_interactions (definition_id) WHERE is_default;
CREATE TABLE world_object_animation_frames (
    definition_id TEXT NOT NULL REFERENCES world_object_definitions ON DELETE CASCADE,
    frame_order INTEGER NOT NULL CHECK (frame_order >= 0),
    texture_path TEXT NOT NULL CHECK (btrim(texture_path) <> ''),
    PRIMARY KEY (definition_id, frame_order)
);
INSERT INTO schema_migrations (migration_file, migration_number, migration_name, notes)
VALUES ('061_world_object_authoring_schema.sql', 61, 'world_object_authoring_schema',
        'Reusable definition lifecycle with ordered public interactions and visual frames.');
COMMIT;
