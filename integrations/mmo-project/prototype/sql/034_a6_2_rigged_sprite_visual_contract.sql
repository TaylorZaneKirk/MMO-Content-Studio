-- A6.2: NPC and mob rigged-sprite presentation retains the authoritative full base visual.

ALTER TABLE npc_definitions
    DROP CONSTRAINT IF EXISTS npc_definitions_visual_contract_check;

ALTER TABLE npc_definitions
    ADD CONSTRAINT npc_definitions_visual_texture_path_nonblank_check
        CHECK (LENGTH(BTRIM(visual_texture_path)) > 0);

ALTER TABLE mob_definitions
    DROP CONSTRAINT IF EXISTS mob_definitions_visual_contract_check;

ALTER TABLE mob_definitions
    ADD CONSTRAINT mob_definitions_visual_texture_path_nonblank_check
        CHECK (LENGTH(BTRIM(visual_texture_path)) > 0);
