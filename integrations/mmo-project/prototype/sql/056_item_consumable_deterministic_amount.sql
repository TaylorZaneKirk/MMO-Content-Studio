-- Replace the old T2 restore range with the intentionally authored fixed amount.
-- Corrected fresh installations already have this shape and need no conversion.
DO $$
DECLARE
    ranged_effect RECORD;
BEGIN
    -- Keep authoring writes out between the content check and the schema change.
    -- The entire block rolls back if any effect still needs a content decision.
    LOCK TABLE item_consumable_effects IN ACCESS EXCLUSIVE MODE;

    IF EXISTS (
        SELECT 1 FROM pg_attribute
        WHERE attrelid = 'item_consumable_effects'::regclass
          AND attname = 'minimum_amount' AND NOT attisdropped
    ) THEN
        SELECT item_id, effect_index, minimum_amount, maximum_amount
        INTO ranged_effect
        FROM item_consumable_effects
        WHERE minimum_amount <> maximum_amount
        ORDER BY item_id, effect_index
        LIMIT 1;

        IF FOUND THEN
            RAISE EXCEPTION 'Consumable item % effect % has a true restore range (% to %).',
                ranged_effect.item_id, ranged_effect.effect_index,
                ranged_effect.minimum_amount, ranged_effect.maximum_amount
                USING HINT = 'Intentionally reauthor every true range to a fixed value before retrying migration 056. No amount was chosen automatically.';
        END IF;

        -- Equality proves this rename is lossless. No row update means existing
        -- identity, order, targets, and timestamps remain exactly as authored.
        ALTER TABLE item_consumable_effects
            DROP CONSTRAINT item_consumable_effects_amount_range_check;
        ALTER TABLE item_consumable_effects RENAME COLUMN minimum_amount TO amount;
        ALTER TABLE item_consumable_effects DROP COLUMN maximum_amount;
        ALTER TABLE item_consumable_effects
            ADD CONSTRAINT item_consumable_effects_amount_check
                CHECK (amount BETWEEN 1 AND 1000000);
    END IF;
END;
$$;
