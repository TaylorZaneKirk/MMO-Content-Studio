-- Mirror of MMO Project migration 033_a6_composite_actor_visuals.sql.
-- Existing authored NPCs and mobs remain flat_sprite until explicitly changed.

ALTER TABLE npc_definitions
    ADD COLUMN IF NOT EXISTS visual_mode TEXT NOT NULL DEFAULT 'flat_sprite',
    ADD COLUMN IF NOT EXISTS composite_visual JSONB NULL;

ALTER TABLE npc_definitions
    DROP CONSTRAINT IF EXISTS npc_definitions_visual_mode_check;

ALTER TABLE npc_definitions
    ADD CONSTRAINT npc_definitions_visual_mode_check
        CHECK (visual_mode IN ('flat_sprite', 'composite_rig'));

ALTER TABLE npc_definitions
    DROP CONSTRAINT IF EXISTS npc_definitions_visual_texture_path_nonblank_check;

ALTER TABLE npc_definitions
    ADD CONSTRAINT npc_definitions_visual_contract_check
        CHECK (visual_mode = 'composite_rig' OR LENGTH(BTRIM(visual_texture_path)) > 0);

ALTER TABLE mob_definitions
    ADD COLUMN IF NOT EXISTS visual_mode TEXT NOT NULL DEFAULT 'flat_sprite',
    ADD COLUMN IF NOT EXISTS composite_visual JSONB NULL;

ALTER TABLE mob_definitions
    DROP CONSTRAINT IF EXISTS mob_definitions_visual_mode_check;

ALTER TABLE mob_definitions
    ADD CONSTRAINT mob_definitions_visual_mode_check
        CHECK (visual_mode IN ('flat_sprite', 'composite_rig'));

ALTER TABLE mob_definitions
    DROP CONSTRAINT IF EXISTS mob_definitions_visual_texture_path_nonblank_check;

ALTER TABLE mob_definitions
    ADD CONSTRAINT mob_definitions_visual_contract_check
        CHECK (visual_mode = 'composite_rig' OR LENGTH(BTRIM(visual_texture_path)) > 0);
