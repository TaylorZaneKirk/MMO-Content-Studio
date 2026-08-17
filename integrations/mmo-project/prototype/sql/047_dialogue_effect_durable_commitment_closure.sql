-- QV4 durable commitment closure.
-- Frozen dialogue effect plans must carry quest transition semantics after
-- admission, and quest effects reserve their mutation order until fulfilled.

BEGIN;

ALTER TABLE character_dialogue_choice_effect_plan_rows
ADD COLUMN IF NOT EXISTS quest_source_status TEXT NULL,
ADD COLUMN IF NOT EXISTS quest_source_step_id TEXT NULL,
ADD COLUMN IF NOT EXISTS quest_target_status TEXT NULL,
ADD COLUMN IF NOT EXISTS quest_target_step_id TEXT NULL;

UPDATE character_dialogue_choice_effect_plan_rows plan
SET quest_source_status = transition.source_status,
    quest_source_step_id = transition.source_step_id,
    quest_target_status = transition.target_status,
    quest_target_step_id = transition.target_step_id
FROM quest_transitions transition
WHERE plan.quest_id = transition.quest_id
  AND plan.transition_id = transition.transition_id
  AND plan.effect_type IN ('start_quest', 'advance_quest', 'complete_quest')
  AND plan.quest_source_status IS NULL
  AND plan.quest_target_status IS NULL;

ALTER TABLE character_dialogue_choice_effect_plan_rows
DROP CONSTRAINT IF EXISTS character_dialogue_choice_effect_plan_rows_frozen_quest_shape_check;

ALTER TABLE character_dialogue_choice_effect_plan_rows
ADD CONSTRAINT character_dialogue_choice_effect_plan_rows_frozen_quest_shape_check
CHECK (
    (
        effect_type IN ('start_quest', 'advance_quest', 'complete_quest')
        AND quest_id IS NOT NULL
        AND transition_id IS NOT NULL
        AND quest_source_status IN ('not_started', 'active')
        AND quest_target_status IN ('active', 'completed')
    )
    OR (
        effect_type NOT IN ('start_quest', 'advance_quest', 'complete_quest')
        AND quest_source_status IS NULL
        AND quest_source_step_id IS NULL
        AND quest_target_status IS NULL
        AND quest_target_step_id IS NULL
    )
) NOT VALID;

CREATE TABLE IF NOT EXISTS character_quest_transition_reservations (
    character_id UUID NOT NULL REFERENCES characters(id) ON DELETE CASCADE,
    quest_id TEXT NOT NULL,
    settlement_id UUID NOT NULL REFERENCES character_dialogue_choice_effect_settlements(settlement_id) ON DELETE CASCADE,
    effect_id TEXT NOT NULL,
    source_event_id TEXT NOT NULL,
    transition_id TEXT NOT NULL,
    effect_order INTEGER NOT NULL,
    source_status TEXT NOT NULL,
    source_step_id TEXT NULL,
    target_status TEXT NOT NULL,
    target_step_id TEXT NULL,
    reservation_status TEXT NOT NULL DEFAULT 'pending',
    created_at TIMESTAMPTZ NOT NULL,
    fulfilled_at TIMESTAMPTZ NULL,
    PRIMARY KEY (character_id, quest_id, settlement_id, effect_id),
    UNIQUE (character_id, quest_id, source_event_id),
    FOREIGN KEY (settlement_id, effect_id)
        REFERENCES character_dialogue_choice_effect_plan_rows(settlement_id, effect_id)
        ON DELETE CASCADE,
    CONSTRAINT character_quest_transition_reservations_order_check
        CHECK (effect_order BETWEEN 0 AND 10000),
    CONSTRAINT character_quest_transition_reservations_status_check
        CHECK (reservation_status IN ('pending', 'fulfilled')),
    CONSTRAINT character_quest_transition_reservations_source_status_check
        CHECK (source_status IN ('not_started', 'active')),
    CONSTRAINT character_quest_transition_reservations_target_status_check
        CHECK (target_status IN ('active', 'completed')),
    CONSTRAINT character_quest_transition_reservations_fulfilled_at_check
        CHECK (
            (reservation_status = 'pending' AND fulfilled_at IS NULL)
            OR
            (reservation_status = 'fulfilled' AND fulfilled_at IS NOT NULL)
        )
);

INSERT INTO character_quest_transition_reservations (
    character_id,
    quest_id,
    settlement_id,
    effect_id,
    source_event_id,
    transition_id,
    effect_order,
    source_status,
    source_step_id,
    target_status,
    target_step_id,
    reservation_status,
    created_at,
    fulfilled_at
)
SELECT
    settlement.character_id,
    plan.quest_id,
    plan.settlement_id,
    plan.effect_id,
    settlement.source_event_id || '|' || plan.effect_id,
    plan.transition_id,
    plan.effect_order,
    plan.quest_source_status,
    plan.quest_source_step_id,
    plan.quest_target_status,
    plan.quest_target_step_id,
    CASE WHEN plan.completed_at IS NULL THEN 'pending' ELSE 'fulfilled' END,
    settlement.admitted_at,
    plan.completed_at
FROM character_dialogue_choice_effect_settlements settlement
JOIN character_dialogue_choice_effect_plan_rows plan
  ON plan.settlement_id = settlement.settlement_id
WHERE plan.effect_type IN ('start_quest', 'advance_quest', 'complete_quest')
  AND plan.quest_id IS NOT NULL
  AND plan.transition_id IS NOT NULL
  AND plan.quest_source_status IS NOT NULL
  AND plan.quest_target_status IS NOT NULL
ON CONFLICT (character_id, quest_id, settlement_id, effect_id) DO NOTHING;

CREATE INDEX IF NOT EXISTS character_quest_transition_reservations_pending_order_idx
    ON character_quest_transition_reservations(character_id, quest_id, created_at, settlement_id, effect_order, effect_id)
    WHERE reservation_status = 'pending';

CREATE INDEX IF NOT EXISTS character_quest_transition_reservations_unsettled_quest_idx
    ON character_quest_transition_reservations(quest_id)
    WHERE reservation_status = 'pending';

CREATE INDEX IF NOT EXISTS character_dialogue_choice_effect_plan_rows_unsettled_item_idx
    ON character_dialogue_choice_effect_plan_rows(item_id)
    WHERE item_id IS NOT NULL
      AND completed_at IS NULL;

COMMIT;
