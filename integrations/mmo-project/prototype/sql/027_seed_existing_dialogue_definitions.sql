-- Idempotently backfills the current runtime dialogue catalog entry into authoring tables.
-- Existing authored dialogue aggregates are preserved exactly.

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM dialogue_definitions
        WHERE dialogue_definition_id = 'test_npc_greeting'
    ) THEN
        INSERT INTO dialogue_definitions (
            dialogue_definition_id,
            display_name,
            publication_state,
            schema_version,
            metadata_description,
            notes
        ) VALUES (
            'test_npc_greeting',
            'Test NPC Greeting',
            'Published',
            1,
            'Starter NPC prototype dialogue.',
            'Seeded from prototype/shared/dialogues/catalog.json for D4 runtime export.'
        );

        INSERT INTO dialogue_entry_points (
            dialogue_definition_id,
            entry_id,
            node_id,
            priority,
            entry_order
        ) VALUES
            ('test_npc_greeting', 'default', 'welcome', 0, 0),
            ('test_npc_greeting', 'fallback', 'welcome', -10, 1);

        INSERT INTO dialogue_nodes (
            dialogue_definition_id,
            node_id,
            node_type,
            speaker,
            text,
            next_node_id,
            dismissible,
            canvas_x,
            canvas_y,
            node_order
        ) VALUES
            ('test_npc_greeting', 'welcome', 'speaker_text', 'Test NPC', 'Welcome to the prototype.', 'question', TRUE, 0, 0, 0),
            ('test_npc_greeting', 'question', 'player_choice', 'Test NPC', 'What would you like to know?', NULL, TRUE, 320, 0, 1),
            ('test_npc_greeting', 'where_answer', 'speaker_text', 'Test NPC', 'This is the starter region. It is small, but it is awake.', 'end', TRUE, 640, -96, 2),
            ('test_npc_greeting', 'goodbye', 'speaker_text', 'Test NPC', 'Safe travels.', 'end', TRUE, 640, 96, 3),
            ('test_npc_greeting', 'end', 'end', 'Test NPC', 'Come back if you need anything.', NULL, TRUE, 960, 0, 4);

        INSERT INTO dialogue_choices (
            dialogue_definition_id,
            node_id,
            choice_id,
            text,
            target_node_id,
            choice_order
        ) VALUES
            ('test_npc_greeting', 'question', 'where_am_i', 'Where am I?', 'where_answer', 0),
            ('test_npc_greeting', 'question', 'goodbye', 'Goodbye.', 'goodbye', 1);
    END IF;
END
$$;
