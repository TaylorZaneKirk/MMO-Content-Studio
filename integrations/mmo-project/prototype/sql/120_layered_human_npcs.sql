BEGIN;
-- Source only. Apply with the matching exporter in a later authorized release.
ALTER TABLE npc_definitions ADD COLUMN layer_asset_keys JSONB NULL;
ALTER TABLE npc_definitions DROP CONSTRAINT npc_definitions_visual_mode_check;
ALTER TABLE npc_definitions ADD CONSTRAINT npc_definitions_visual_mode_check
    CHECK (visual_mode IN ('flat_sprite', 'composite_rig', 'layered_humanoid'));
ALTER TABLE npc_definitions ADD CONSTRAINT npc_definitions_layered_human_check CHECK (
    (visual_mode = 'layered_humanoid' AND layer_asset_keys IS NOT NULL
        AND jsonb_typeof(layer_asset_keys) = 'object' AND composite_visual IS NULL
        AND COALESCE(length(layer_asset_keys->>'head'), 0) > 0
        AND COALESCE(length(layer_asset_keys->>'body'), 0) > 0
        AND COALESCE(length(layer_asset_keys->>'legs'), 0) > 0)
    OR (visual_mode <> 'layered_humanoid' AND layer_asset_keys IS NULL));
-- Retain a real fallback texture under the existing visual contract.
-- Publication validates slot names, safe asset keys and idle resources.
INSERT INTO schema_migrations(migration_file,migration_number,migration_name,notes) VALUES('120_layered_human_npcs.sql',120,'layered_human_npcs','NPC layered appearance keys; no placements or gameplay formula changes.');
COMMIT;
