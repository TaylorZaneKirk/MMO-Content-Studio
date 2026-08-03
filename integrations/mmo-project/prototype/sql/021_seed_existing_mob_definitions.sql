-- Idempotently backfills current runtime mob catalog entries into authoring tables.
-- Existing authored rows are preserved.

INSERT INTO mob_factions (faction_id, display_name, description, updated_at)
VALUES
    ('goblins', 'Goblins', 'Hostile training goblin faction.', NOW()),
    ('town_guard', 'Town Guard', 'Starter-region guard faction.', NOW())
ON CONFLICT (faction_id) DO NOTHING;

INSERT INTO mob_faction_dispositions (
    source_faction_id,
    target_faction_id,
    disposition,
    updated_at
)
VALUES
    ('goblins', 'town_guard', 'hostile', NOW()),
    ('town_guard', 'goblins', 'hostile', NOW())
ON CONFLICT (source_faction_id, target_faction_id) DO NOTHING;

INSERT INTO mob_definitions (
    mob_definition_id,
    display_name,
    publication_state,
    visual_texture_path,
    source_width,
    source_height,
    visual_anchor_offset_x,
    visual_anchor_offset_y,
    visual_render_scale,
    footprint_width_tiles,
    footprint_height_tiles,
    max_health,
    movement_speed_tiles_per_second,
    defeated_hold_duration_ms,
    respawn_delay_ms,
    combat_faction_id,
    can_proactively_target_hostile_mobs,
    mob_detection_radius_tiles,
    mob_target_scan_interval_ms,
    mob_target_scan_candidate_limit,
    created_at,
    updated_at
)
VALUES
    (
        'training_goblin',
        'Training Goblin',
        'Published',
        'res://assets/maps/objects/mobs/slime.png',
        128,
        88,
        0,
        0,
        0.25,
        1,
        1,
        8,
        1.25,
        1000,
        5000,
        'goblins',
        TRUE,
        6,
        1200,
        4,
        NOW(),
        NOW()
    ),
    (
        'slime',
        'Slime',
        'Published',
        'res://assets/maps/objects/mobs/slime.png',
        128,
        88,
        0,
        0,
        0.25,
        1,
        1,
        5,
        1.25,
        1000,
        5000,
        NULL,
        FALSE,
        0,
        0,
        0,
        NOW(),
        NOW()
    ),
    (
        'training_guard',
        'Training Guard',
        'Published',
        'res://assets/maps/objects/mobs/slime.png',
        128,
        88,
        0,
        0,
        0.25,
        1,
        1,
        10,
        1.25,
        1000,
        5000,
        'town_guard',
        TRUE,
        6,
        1200,
        4,
        NOW(),
        NOW()
    )
ON CONFLICT (mob_definition_id) DO NOTHING;

INSERT INTO mob_combat_profiles (
    mob_definition_id,
    attack_type,
    accuracy_style,
    minimum_range_tiles,
    maximum_range_tiles,
    attack_speed_units,
    attack_level,
    strength_level,
    defence_level,
    updated_at
)
VALUES
    ('training_goblin', 'melee', 'slash', 1, 1, 4, 3, 3, 2, NOW()),
    ('slime', 'melee', 'crush', 1, 1, 4, 1, 1, 1, NOW()),
    ('training_guard', 'melee', 'thrust', 1, 1, 4, 4, 3, 3, NOW())
ON CONFLICT (mob_definition_id) DO NOTHING;

INSERT INTO mob_combat_bonuses (
    mob_definition_id,
    attack_thrust,
    attack_slash,
    attack_crush,
    attack_ranged,
    attack_magic,
    strength_melee,
    strength_ranged,
    strength_magic,
    defence_thrust,
    defence_slash,
    defence_crush,
    defence_ranged,
    defence_magic,
    updated_at
)
VALUES
    ('training_goblin', 0, 2, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, NOW()),
    ('slime', 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, NOW()),
    ('training_guard', 2, 0, 0, 0, 0, 1, 0, 0, 1, 0, 0, 0, 0, NOW())
ON CONFLICT (mob_definition_id) DO NOTHING;

INSERT INTO mob_drops (
    mob_definition_id,
    drop_order,
    item_id,
    stack_count,
    updated_at
)
SELECT 'training_goblin', 0, 'inventory_2_apple', 1, NOW()
WHERE EXISTS (
    SELECT 1
    FROM item_definitions
    WHERE item_id = 'inventory_2_apple'
)
ON CONFLICT (mob_definition_id, drop_order) DO NOTHING;
