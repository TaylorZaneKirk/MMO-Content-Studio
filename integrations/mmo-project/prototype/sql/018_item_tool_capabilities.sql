CREATE TABLE IF NOT EXISTS item_tool_capabilities (
    item_id TEXT NOT NULL REFERENCES item_definitions(item_id) ON DELETE CASCADE,
    capability_id TEXT NOT NULL,
    capability_order INTEGER NOT NULL,
    power_tier INTEGER NOT NULL DEFAULT 1,
    action_animation_id TEXT,
    effect_resource_id TEXT,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY (item_id, capability_id),
    CONSTRAINT item_tool_capabilities_capability_id_nonempty
        CHECK (length(btrim(capability_id)) > 0),
    CONSTRAINT item_tool_capabilities_order_check
        CHECK (capability_order BETWEEN 0 AND 63),
    CONSTRAINT item_tool_capabilities_power_tier_check
        CHECK (power_tier BETWEEN 1 AND 1000),
    CONSTRAINT item_tool_capabilities_action_animation_id_nonempty
        CHECK (action_animation_id IS NULL OR length(btrim(action_animation_id)) > 0),
    CONSTRAINT item_tool_capabilities_effect_resource_id_nonempty
        CHECK (effect_resource_id IS NULL OR length(btrim(effect_resource_id)) > 0),
    CONSTRAINT item_tool_capabilities_item_order_key
        UNIQUE (item_id, capability_order)
);

CREATE INDEX IF NOT EXISTS item_tool_capabilities_capability_id_idx
ON item_tool_capabilities(capability_id);

CREATE OR REPLACE FUNCTION ensure_item_tool_capabilities_hand_slot()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $$
DECLARE
    item_slot TEXT;
BEGIN
    SELECT equipment_slot_id
    INTO item_slot
    FROM item_definitions
    WHERE item_id = NEW.item_id;

    IF item_slot NOT IN ('right_hand', 'left_hand') THEN
        RAISE EXCEPTION
            'Item % must be right_hand or left_hand equipment before tool capabilities can be authored.',
            NEW.item_id
            USING ERRCODE = '23514';
    END IF;

    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS item_tool_capabilities_hand_slot_guard
ON item_tool_capabilities;

CREATE TRIGGER item_tool_capabilities_hand_slot_guard
BEFORE INSERT OR UPDATE ON item_tool_capabilities
FOR EACH ROW
EXECUTE FUNCTION ensure_item_tool_capabilities_hand_slot();

INSERT INTO item_tool_capabilities (
    item_id,
    capability_id,
    capability_order,
    power_tier
)
SELECT
    item_definitions.item_id,
    'mining',
    0,
    1
FROM item_definitions
WHERE item_definitions.item_id = 'inventory_17_mining_hammer'
  AND item_definitions.equipment_slot_id IN ('right_hand', 'left_hand')
ON CONFLICT (item_id, capability_id) DO UPDATE
SET
    capability_order = EXCLUDED.capability_order,
    power_tier = EXCLUDED.power_tier,
    updated_at = NOW();
