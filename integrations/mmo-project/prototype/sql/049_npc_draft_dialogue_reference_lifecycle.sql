BEGIN;

ALTER TABLE npc_definitions
    DROP CONSTRAINT IF EXISTS npc_definitions_dialogue_reference_check;

ALTER TABLE npc_definitions
    ADD CONSTRAINT npc_definitions_dialogue_reference_check
        CHECK (
            interaction_enabled = FALSE
            OR publication_state <> 'Published'
            OR (
                default_dialogue_id IS NOT NULL
                AND LENGTH(BTRIM(default_dialogue_id)) > 0
            )
        );

COMMIT;
