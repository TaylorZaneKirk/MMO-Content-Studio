-- Preserve the pre-M0 production catalog. Re-running never overwrites authored edits.
BEGIN;
CREATE TEMP TABLE seeded_world_objects (definition_id TEXT PRIMARY KEY) ON COMMIT DROP;
WITH inserted AS (
INSERT INTO world_object_definitions (definition_id, display_name, publication_state, blocks_movement, footprint_width_tiles, footprint_height_tiles, visual_texture_path, source_width, source_height, visual_anchor_offset_x, visual_anchor_offset_y, visual_render_scale, visual_animation_fps)
VALUES
    ('homeward_gate', 'Homeward Gate', 'Published', FALSE, 1, 1, 'res://assets/maps/objects/world_objects/MapGFX_245_EA.png', 32, 32, 0, 0, 1, NULL),
    ('s3_proof_exterior_door', 'S3 Proof Door', 'Published', FALSE, 1, 1, 'res://assets/maps/objects/world_objects/MapGFX_245_EA.png', 32, 32, 0, 0, 1, NULL),
    ('s4_theater_entry', 'S4 Theater Entry', 'Published', FALSE, 1, 1, 'res://assets/maps/objects/world_objects/MapGFX_245_EA.png', 32, 32, 0, 0, 1, NULL),
    ('s5_elevation_entry', 'S5 Elevation Entry', 'Published', FALSE, 1, 1, 'res://assets/maps/objects/world_objects/MapGFX_245_EA.png', 32, 32, 0, 0, 1, NULL),
    ('test_sign', 'Test Sign', 'Published', FALSE, 1, 1, 'res://assets/maps/objects/world_objects/MapGFX_245_EA.png', 32, 32, 0, 0, 1, NULL),
    ('starter_stove', 'Stove', 'Published', TRUE, 1, 1, 'res://assets/maps/objects/world_objects/Inventory_410_Stove.png', 32, 32, 0, 0, 1, NULL),
    ('exit_connector', 'Exit', 'Published', FALSE, 1, 1, 'res://assets/maps/objects/world_objects/Inventory_39_Exit.png', 32, 32, 0, 0, 1, NULL),
    ('exit_connector_alt', 'Exit', 'Published', FALSE, 1, 1, 'res://assets/maps/objects/world_objects/Inventory_42_Exit2.png', 32, 32, 0, 0, 1, NULL),
    ('starter_normal_tree', 'Tree', 'Published', TRUE, 1, 1, 'res://assets/maps/objects/world_objects/MapGFX_55_S3.png', 32, 32, 0, 0, 1, NULL),
    ('starter_normal_tree_stump', 'Tree Stump', 'Published', FALSE, 1, 1, 'res://assets/maps/objects/world_objects/MapGFX_54_P3.png', 32, 32, 0, 0, 1, NULL),
    ('starter_oak_tree', 'Oak', 'Published', TRUE, 1, 1, 'res://assets/maps/objects/world_objects/MapGFX_53_T3.png', 32, 32, 0, 0, 1, NULL),
    ('starter_oak_tree_stump', 'Oak Stump', 'Published', FALSE, 1, 1, 'res://assets/maps/objects/world_objects/MapGFX_54_P3.png', 32, 32, 0, 0, 1, NULL),
    ('starter_fishing_spot', 'Fishing Spot', 'Published', FALSE, 1, 1, 'res://assets/maps/objects/world_objects/Fishing_Spot_004.png', 32, 32, 0, 0, 1, 4)
ON CONFLICT (definition_id) DO NOTHING
RETURNING definition_id
)
INSERT INTO seeded_world_objects SELECT definition_id FROM inserted;
INSERT INTO world_object_interactions (definition_id, interaction_order, action_id, label, is_default)
SELECT 'homeward_gate', 0, 'enter_home', 'Enter Home', TRUE
WHERE EXISTS (SELECT 1 FROM seeded_world_objects WHERE definition_id = 'homeward_gate');
INSERT INTO world_object_interactions (definition_id, interaction_order, action_id, label, is_default)
SELECT 'homeward_gate', 1, 'inspect', 'Inspect', FALSE
WHERE EXISTS (SELECT 1 FROM seeded_world_objects WHERE definition_id = 'homeward_gate');
INSERT INTO world_object_interactions (definition_id, interaction_order, action_id, label, is_default)
SELECT 's3_proof_exterior_door', 0, 'enter', 'Enter', TRUE
WHERE EXISTS (SELECT 1 FROM seeded_world_objects WHERE definition_id = 's3_proof_exterior_door');
INSERT INTO world_object_interactions (definition_id, interaction_order, action_id, label, is_default)
SELECT 's3_proof_exterior_door', 1, 'inspect', 'Inspect', FALSE
WHERE EXISTS (SELECT 1 FROM seeded_world_objects WHERE definition_id = 's3_proof_exterior_door');
INSERT INTO world_object_interactions (definition_id, interaction_order, action_id, label, is_default)
SELECT 's4_theater_entry', 0, 'enter', 'Enter', TRUE
WHERE EXISTS (SELECT 1 FROM seeded_world_objects WHERE definition_id = 's4_theater_entry');
INSERT INTO world_object_interactions (definition_id, interaction_order, action_id, label, is_default)
SELECT 's4_theater_entry', 1, 'inspect', 'Inspect', FALSE
WHERE EXISTS (SELECT 1 FROM seeded_world_objects WHERE definition_id = 's4_theater_entry');
INSERT INTO world_object_interactions (definition_id, interaction_order, action_id, label, is_default)
SELECT 's5_elevation_entry', 0, 'enter', 'Enter', TRUE
WHERE EXISTS (SELECT 1 FROM seeded_world_objects WHERE definition_id = 's5_elevation_entry');
INSERT INTO world_object_interactions (definition_id, interaction_order, action_id, label, is_default)
SELECT 's5_elevation_entry', 1, 'inspect', 'Inspect', FALSE
WHERE EXISTS (SELECT 1 FROM seeded_world_objects WHERE definition_id = 's5_elevation_entry');
INSERT INTO world_object_interactions (definition_id, interaction_order, action_id, label, is_default)
SELECT 'test_sign', 0, 'inspect', 'Inspect Wooden Sign', TRUE
WHERE EXISTS (SELECT 1 FROM seeded_world_objects WHERE definition_id = 'test_sign');
INSERT INTO world_object_interactions (definition_id, interaction_order, action_id, label, is_default)
SELECT 'starter_stove', 0, 'cook', 'Cook', TRUE
WHERE EXISTS (SELECT 1 FROM seeded_world_objects WHERE definition_id = 'starter_stove');
INSERT INTO world_object_interactions (definition_id, interaction_order, action_id, label, is_default)
SELECT 'exit_connector', 0, 'use', 'Use', TRUE
WHERE EXISTS (SELECT 1 FROM seeded_world_objects WHERE definition_id = 'exit_connector');
INSERT INTO world_object_interactions (definition_id, interaction_order, action_id, label, is_default)
SELECT 'exit_connector_alt', 0, 'use', 'Use', TRUE
WHERE EXISTS (SELECT 1 FROM seeded_world_objects WHERE definition_id = 'exit_connector_alt');
INSERT INTO world_object_interactions (definition_id, interaction_order, action_id, label, is_default)
SELECT 'starter_normal_tree', 0, 'chop', 'Chop', TRUE
WHERE EXISTS (SELECT 1 FROM seeded_world_objects WHERE definition_id = 'starter_normal_tree');
INSERT INTO world_object_interactions (definition_id, interaction_order, action_id, label, is_default)
SELECT 'starter_oak_tree', 0, 'chop', 'Chop', TRUE
WHERE EXISTS (SELECT 1 FROM seeded_world_objects WHERE definition_id = 'starter_oak_tree');
INSERT INTO world_object_interactions (definition_id, interaction_order, action_id, label, is_default)
SELECT 'starter_fishing_spot', 0, 'net', 'Net', TRUE
WHERE EXISTS (SELECT 1 FROM seeded_world_objects WHERE definition_id = 'starter_fishing_spot');
INSERT INTO world_object_interactions (definition_id, interaction_order, action_id, label, is_default)
SELECT 'starter_fishing_spot', 1, 'bait', 'Bait', FALSE
WHERE EXISTS (SELECT 1 FROM seeded_world_objects WHERE definition_id = 'starter_fishing_spot');
INSERT INTO world_object_animation_frames (definition_id, frame_order, texture_path)
SELECT 'starter_fishing_spot', 0, 'res://assets/maps/objects/world_objects/Fishing_Spot_001.png'
WHERE EXISTS (SELECT 1 FROM seeded_world_objects WHERE definition_id = 'starter_fishing_spot');
INSERT INTO world_object_animation_frames (definition_id, frame_order, texture_path)
SELECT 'starter_fishing_spot', 1, 'res://assets/maps/objects/world_objects/Fishing_Spot_002.png'
WHERE EXISTS (SELECT 1 FROM seeded_world_objects WHERE definition_id = 'starter_fishing_spot');
INSERT INTO world_object_animation_frames (definition_id, frame_order, texture_path)
SELECT 'starter_fishing_spot', 2, 'res://assets/maps/objects/world_objects/Fishing_Spot_003.png'
WHERE EXISTS (SELECT 1 FROM seeded_world_objects WHERE definition_id = 'starter_fishing_spot');
INSERT INTO world_object_animation_frames (definition_id, frame_order, texture_path)
SELECT 'starter_fishing_spot', 3, 'res://assets/maps/objects/world_objects/Fishing_Spot_004.png'
WHERE EXISTS (SELECT 1 FROM seeded_world_objects WHERE definition_id = 'starter_fishing_spot');
INSERT INTO schema_migrations (migration_file, migration_number, migration_name, notes)
VALUES ('062_world_object_authoring_seed.sql', 62, 'world_object_authoring_seed',
        'Seed all 13 pre-M0 shared production definitions, preserving ordered interactions and Fishing animation.')
ON CONFLICT (migration_file) DO NOTHING;
COMMIT;
