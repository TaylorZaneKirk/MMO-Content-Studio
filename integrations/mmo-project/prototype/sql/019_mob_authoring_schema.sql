-- T4 Content Studio integration migration for reusable mob definition authoring.
-- This is a handoff artifact for MMO Project; Content Studio does not apply it
-- automatically and this file must not be treated as a runtime hot-reload path.

CREATE TABLE IF NOT EXISTS mob_factions (
    faction_id TEXT PRIMARY KEY,
    display_name TEXT NOT NULL,
    description TEXT NULL,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT mob_factions_faction_id_format_check
        CHECK (faction_id = LOWER(faction_id) AND faction_id ~ '^[a-z0-9]+(_[a-z0-9]+)*$'),
    CONSTRAINT mob_factions_display_name_nonblank_check
        CHECK (LENGTH(BTRIM(display_name)) > 0)
);

CREATE TABLE IF NOT EXISTS mob_faction_dispositions (
    source_faction_id TEXT NOT NULL REFERENCES mob_factions(faction_id) ON DELETE RESTRICT,
    target_faction_id TEXT NOT NULL REFERENCES mob_factions(faction_id) ON DELETE RESTRICT,
    disposition TEXT NOT NULL,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY (source_faction_id, target_faction_id),
    CONSTRAINT mob_faction_dispositions_no_self_check
        CHECK (source_faction_id <> target_faction_id),
    CONSTRAINT mob_faction_dispositions_disposition_check
        CHECK (disposition IN ('hostile', 'neutral'))
);

CREATE TABLE IF NOT EXISTS mob_definitions (
    mob_definition_id TEXT PRIMARY KEY,
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
    max_health INTEGER NOT NULL,
    movement_speed_tiles_per_second DOUBLE PRECISION NOT NULL DEFAULT 1.25,
    combat_faction_id TEXT NULL REFERENCES mob_factions(faction_id) ON DELETE RESTRICT,
    can_proactively_target_hostile_mobs BOOLEAN NOT NULL DEFAULT FALSE,
    mob_detection_radius_tiles INTEGER NOT NULL DEFAULT 0,
    mob_target_scan_interval_ms INTEGER NOT NULL DEFAULT 0,
    mob_target_scan_candidate_limit INTEGER NOT NULL DEFAULT 0,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT mob_definitions_id_format_check
        CHECK (mob_definition_id = LOWER(mob_definition_id) AND mob_definition_id ~ '^[a-z0-9]+(_[a-z0-9]+)*$'),
    CONSTRAINT mob_definitions_display_name_nonblank_check
        CHECK (LENGTH(BTRIM(display_name)) > 0),
    CONSTRAINT mob_definitions_publication_state_check
        CHECK (publication_state IN ('Draft', 'Published', 'Disabled')),
    CONSTRAINT mob_definitions_visual_texture_path_nonblank_check
        CHECK (LENGTH(BTRIM(visual_texture_path)) > 0),
    CONSTRAINT mob_definitions_source_dimensions_check
        CHECK (source_width > 0 AND source_height > 0),
    CONSTRAINT mob_definitions_visual_numbers_check
        CHECK (
            ISFINITE(visual_anchor_offset_x)
            AND ISFINITE(visual_anchor_offset_y)
            AND ISFINITE(visual_render_scale)
            AND visual_render_scale > 0
        ),
    CONSTRAINT mob_definitions_footprint_check
        CHECK (footprint_width_tiles > 0 AND footprint_height_tiles > 0),
    CONSTRAINT mob_definitions_max_health_check
        CHECK (max_health > 0),
    CONSTRAINT mob_definitions_movement_speed_check
        CHECK (
            ISFINITE(movement_speed_tiles_per_second)
            AND movement_speed_tiles_per_second > 0
        ),
    CONSTRAINT mob_definitions_target_scan_values_check
        CHECK (
            mob_detection_radius_tiles >= 0
            AND mob_target_scan_interval_ms >= 0
            AND mob_target_scan_candidate_limit >= 0
        ),
    CONSTRAINT mob_definitions_proactive_targeting_check
        CHECK (
            can_proactively_target_hostile_mobs = FALSE
            OR (
                combat_faction_id IS NOT NULL
                AND mob_detection_radius_tiles > 0
                AND mob_target_scan_interval_ms > 0
                AND mob_target_scan_candidate_limit > 0
            )
        ),
    CONSTRAINT mob_definitions_timestamp_order_check
        CHECK (created_at <= updated_at)
);

CREATE TABLE IF NOT EXISTS mob_combat_profiles (
    mob_definition_id TEXT PRIMARY KEY REFERENCES mob_definitions(mob_definition_id) ON DELETE CASCADE,
    attack_type TEXT NOT NULL,
    accuracy_style TEXT NULL,
    minimum_range_tiles INTEGER NOT NULL,
    maximum_range_tiles INTEGER NOT NULL,
    attack_speed_units INTEGER NOT NULL,
    attack_level INTEGER NOT NULL DEFAULT 0,
    strength_level INTEGER NOT NULL DEFAULT 0,
    defence_level INTEGER NOT NULL DEFAULT 0,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT mob_combat_profiles_attack_type_check
        CHECK (attack_type IN ('melee')),
    CONSTRAINT mob_combat_profiles_accuracy_style_check
        CHECK (accuracy_style IS NULL OR accuracy_style IN ('thrust', 'slash', 'crush')),
    CONSTRAINT mob_combat_profiles_attack_type_accuracy_style_check
        CHECK (attack_type <> 'melee' OR accuracy_style IS NOT NULL),
    CONSTRAINT mob_combat_profiles_range_check
        CHECK (minimum_range_tiles >= 0 AND maximum_range_tiles >= minimum_range_tiles),
    CONSTRAINT mob_combat_profiles_attack_speed_units_check
        CHECK (attack_speed_units BETWEEN 1 AND 60),
    CONSTRAINT mob_combat_profiles_level_bounds_check
        CHECK (
            attack_level BETWEEN 0 AND 1000000
            AND strength_level BETWEEN 0 AND 1000000
            AND defence_level BETWEEN 0 AND 1000000
        )
);

CREATE TABLE IF NOT EXISTS mob_combat_bonuses (
    mob_definition_id TEXT PRIMARY KEY REFERENCES mob_definitions(mob_definition_id) ON DELETE CASCADE,
    attack_thrust INTEGER NOT NULL DEFAULT 0,
    attack_slash INTEGER NOT NULL DEFAULT 0,
    attack_crush INTEGER NOT NULL DEFAULT 0,
    attack_ranged INTEGER NOT NULL DEFAULT 0,
    attack_magic INTEGER NOT NULL DEFAULT 0,
    strength_melee INTEGER NOT NULL DEFAULT 0,
    strength_ranged INTEGER NOT NULL DEFAULT 0,
    strength_magic INTEGER NOT NULL DEFAULT 0,
    defence_thrust INTEGER NOT NULL DEFAULT 0,
    defence_slash INTEGER NOT NULL DEFAULT 0,
    defence_crush INTEGER NOT NULL DEFAULT 0,
    defence_ranged INTEGER NOT NULL DEFAULT 0,
    defence_magic INTEGER NOT NULL DEFAULT 0,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS mob_drops (
    mob_definition_id TEXT NOT NULL REFERENCES mob_definitions(mob_definition_id) ON DELETE CASCADE,
    drop_order INTEGER NOT NULL,
    item_id TEXT NOT NULL REFERENCES item_definitions(item_id) ON DELETE RESTRICT,
    stack_count INTEGER NOT NULL,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY (mob_definition_id, drop_order),
    CONSTRAINT mob_drops_drop_order_check
        CHECK (drop_order BETWEEN 0 AND 255),
    CONSTRAINT mob_drops_stack_count_check
        CHECK (stack_count BETWEEN 1 AND 1000000),
    CONSTRAINT mob_drops_unique_item_per_mob
        UNIQUE (mob_definition_id, item_id)
);

CREATE INDEX IF NOT EXISTS mob_faction_dispositions_target_idx
    ON mob_faction_dispositions(target_faction_id);

CREATE INDEX IF NOT EXISTS mob_definitions_publication_state_idx
    ON mob_definitions(publication_state);

CREATE INDEX IF NOT EXISTS mob_definitions_combat_faction_id_idx
    ON mob_definitions(combat_faction_id)
    WHERE combat_faction_id IS NOT NULL;

CREATE INDEX IF NOT EXISTS mob_drops_item_id_idx
    ON mob_drops(item_id);
