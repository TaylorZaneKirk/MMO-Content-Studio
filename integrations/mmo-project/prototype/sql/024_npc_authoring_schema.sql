-- T5 Content Studio integration migration for reusable NPC definition authoring.
-- This is a handoff artifact for MMO Project; Content Studio does not apply it
-- automatically and this file must not be treated as a runtime hot-reload path.

CREATE TABLE IF NOT EXISTS npc_definitions (
    npc_definition_id TEXT PRIMARY KEY,
    display_name TEXT NOT NULL,
    publication_state TEXT NOT NULL DEFAULT 'Draft',
    visual_texture_path TEXT NOT NULL,
    source_width INTEGER NOT NULL,
    source_height INTEGER NOT NULL,
    visual_anchor_offset_x DOUBLE PRECISION NOT NULL DEFAULT 0,
    visual_anchor_offset_y DOUBLE PRECISION NOT NULL DEFAULT 0,
    visual_render_scale DOUBLE PRECISION NOT NULL DEFAULT 0.25,
    footprint_width_tiles INTEGER NOT NULL DEFAULT 1,
    footprint_height_tiles INTEGER NOT NULL DEFAULT 1,
    movement_behavior TEXT NOT NULL DEFAULT 'static',
    wander_radius_tiles INTEGER NOT NULL DEFAULT 0,
    tick_interval_ms INTEGER NOT NULL DEFAULT 600,
    idle_chance DOUBLE PRECISION NOT NULL DEFAULT 0.15,
    interaction_enabled BOOLEAN NOT NULL DEFAULT FALSE,
    interaction_range_tiles INTEGER NOT NULL DEFAULT 1,
    default_interaction TEXT NOT NULL DEFAULT 'talk',
    default_dialogue_id TEXT NULL,
    notes TEXT NULL,
    created_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT npc_definitions_id_format_check
        CHECK (npc_definition_id = LOWER(npc_definition_id) AND npc_definition_id ~ '^[a-z][a-z0-9]*(_[a-z0-9]+)*$'),
    CONSTRAINT npc_definitions_display_name_nonblank_check
        CHECK (LENGTH(BTRIM(display_name)) > 0),
    CONSTRAINT npc_definitions_publication_state_check
        CHECK (publication_state IN ('Draft', 'Published', 'Disabled')),
    CONSTRAINT npc_definitions_visual_texture_path_nonblank_check
        CHECK (LENGTH(BTRIM(visual_texture_path)) > 0),
    CONSTRAINT npc_definitions_source_dimensions_check
        CHECK (source_width > 0 AND source_height > 0),
    CONSTRAINT npc_definitions_visual_numbers_check
        CHECK (
            visual_anchor_offset_x::TEXT NOT IN ('Infinity', '-Infinity', 'NaN')
            AND visual_anchor_offset_y::TEXT NOT IN ('Infinity', '-Infinity', 'NaN')
            AND visual_render_scale::TEXT NOT IN ('Infinity', '-Infinity', 'NaN')
            AND visual_render_scale > 0
        ),
    CONSTRAINT npc_definitions_footprint_positive_check
        CHECK (footprint_width_tiles > 0 AND footprint_height_tiles > 0),
    CONSTRAINT npc_definitions_initial_footprint_check
        CHECK (footprint_width_tiles = 1 AND footprint_height_tiles = 1),
    CONSTRAINT npc_definitions_movement_behavior_check
        CHECK (movement_behavior IN ('static', 'random_wander')),
    CONSTRAINT npc_definitions_wander_radius_check
        CHECK (wander_radius_tiles >= 0),
    CONSTRAINT npc_definitions_movement_consistency_check
        CHECK (
            (movement_behavior = 'static' AND wander_radius_tiles = 0)
            OR (movement_behavior = 'random_wander' AND wander_radius_tiles > 0)
        ),
    CONSTRAINT npc_definitions_tick_interval_check
        CHECK (tick_interval_ms >= 600),
    CONSTRAINT npc_definitions_idle_chance_check
        CHECK (
            idle_chance::TEXT NOT IN ('Infinity', '-Infinity', 'NaN')
            AND idle_chance >= 0
            AND idle_chance <= 1
        ),
    CONSTRAINT npc_definitions_interaction_range_check
        CHECK (interaction_range_tiles >= 1),
    CONSTRAINT npc_definitions_default_interaction_check
        CHECK (default_interaction = 'talk'),
    CONSTRAINT npc_definitions_dialogue_reference_check
        CHECK (
            interaction_enabled = FALSE
            OR publication_state <> 'Published'
            OR (
                default_dialogue_id IS NOT NULL
                AND LENGTH(BTRIM(default_dialogue_id)) > 0
            )
        ),
    CONSTRAINT npc_definitions_timestamp_order_check
        CHECK (created_at_utc <= updated_at_utc)
);

CREATE INDEX IF NOT EXISTS npc_definitions_publication_state_idx
    ON npc_definitions(publication_state);
