-- Item Economy Policy V1: immutable item-definition economic facts only.
-- This migration deliberately does not mutate player possessions or execute
-- protection, destruction, transformation, reclaim, shop, or trade behavior.

ALTER TABLE item_definitions
ADD COLUMN IF NOT EXISTS reference_value BIGINT NOT NULL DEFAULT 0,
ADD COLUMN IF NOT EXISTS trade_policy TEXT NOT NULL DEFAULT 'tradeable',
ADD COLUMN IF NOT EXISTS death_behavior TEXT NOT NULL DEFAULT 'ordinary',
ADD COLUMN IF NOT EXISTS death_transform_item_id TEXT NULL,
ADD COLUMN IF NOT EXISTS shop_policy TEXT NOT NULL DEFAULT 'not_shop_traded',
ADD COLUMN IF NOT EXISTS npc_buy_price BIGINT NULL,
ADD COLUMN IF NOT EXISTS npc_sell_price BIGINT NULL,
ADD COLUMN IF NOT EXISTS reclaim_policy TEXT NOT NULL DEFAULT 'none',
ADD COLUMN IF NOT EXISTS reclaim_value BIGINT NULL,
ADD COLUMN IF NOT EXISTS condition_policy_id TEXT NULL,
ADD COLUMN IF NOT EXISTS repair_policy_id TEXT NULL;

ALTER TABLE item_definitions
DROP CONSTRAINT IF EXISTS item_definitions_reference_value_check,
ADD CONSTRAINT item_definitions_reference_value_check CHECK (reference_value >= 0),
DROP CONSTRAINT IF EXISTS item_definitions_trade_policy_check,
ADD CONSTRAINT item_definitions_trade_policy_check CHECK (trade_policy IN ('tradeable', 'untradeable')),
DROP CONSTRAINT IF EXISTS item_definitions_death_behavior_check,
ADD CONSTRAINT item_definitions_death_behavior_check CHECK (death_behavior IN ('ordinary', 'always_keep', 'always_destroy', 'transform', 'reclaim')),
DROP CONSTRAINT IF EXISTS item_definitions_shop_policy_check,
ADD CONSTRAINT item_definitions_shop_policy_check CHECK (shop_policy IN ('not_shop_traded', 'npc_buys', 'npc_sells', 'npc_buys_and_sells')),
DROP CONSTRAINT IF EXISTS item_definitions_npc_buy_price_check,
ADD CONSTRAINT item_definitions_npc_buy_price_check CHECK (npc_buy_price IS NULL OR npc_buy_price >= 0),
DROP CONSTRAINT IF EXISTS item_definitions_npc_sell_price_check,
ADD CONSTRAINT item_definitions_npc_sell_price_check CHECK (npc_sell_price IS NULL OR npc_sell_price >= 0),
DROP CONSTRAINT IF EXISTS item_definitions_shop_price_policy_check,
ADD CONSTRAINT item_definitions_shop_price_policy_check CHECK ((shop_policy = 'not_shop_traded' AND npc_buy_price IS NULL AND npc_sell_price IS NULL) OR (shop_policy = 'npc_buys' AND npc_buy_price IS NOT NULL AND npc_sell_price IS NULL) OR (shop_policy = 'npc_sells' AND npc_buy_price IS NULL AND npc_sell_price IS NOT NULL) OR (shop_policy = 'npc_buys_and_sells' AND npc_buy_price IS NOT NULL AND npc_sell_price IS NOT NULL)),
DROP CONSTRAINT IF EXISTS item_definitions_death_transform_policy_check,
ADD CONSTRAINT item_definitions_death_transform_policy_check CHECK ((death_behavior = 'transform' AND death_transform_item_id IS NOT NULL AND death_transform_item_id <> item_id) OR (death_behavior <> 'transform' AND death_transform_item_id IS NULL)),
DROP CONSTRAINT IF EXISTS item_definitions_reclaim_policy_check,
ADD CONSTRAINT item_definitions_reclaim_policy_check CHECK (reclaim_policy IN ('none', 'fixed_cost')),
DROP CONSTRAINT IF EXISTS item_definitions_reclaim_value_check,
ADD CONSTRAINT item_definitions_reclaim_value_check CHECK (reclaim_value IS NULL OR reclaim_value >= 0),
DROP CONSTRAINT IF EXISTS item_definitions_death_reclaim_policy_check,
ADD CONSTRAINT item_definitions_death_reclaim_policy_check CHECK ((death_behavior = 'reclaim' AND reclaim_policy = 'fixed_cost' AND reclaim_value IS NOT NULL) OR (death_behavior <> 'reclaim' AND reclaim_policy = 'none' AND reclaim_value IS NULL)),
DROP CONSTRAINT IF EXISTS item_definitions_condition_policy_id_format_check,
ADD CONSTRAINT item_definitions_condition_policy_id_format_check CHECK (condition_policy_id IS NULL OR condition_policy_id ~ '^[a-z0-9]+(_[a-z0-9]+)*$'),
DROP CONSTRAINT IF EXISTS item_definitions_repair_policy_id_format_check,
ADD CONSTRAINT item_definitions_repair_policy_id_format_check CHECK (repair_policy_id IS NULL OR repair_policy_id ~ '^[a-z0-9]+(_[a-z0-9]+)*$');

ALTER TABLE item_definitions
DROP CONSTRAINT IF EXISTS item_definitions_death_transform_item_id_fkey,
ADD CONSTRAINT item_definitions_death_transform_item_id_fkey FOREIGN KEY (death_transform_item_id) REFERENCES item_definitions(item_id) ON DELETE RESTRICT;

CREATE OR REPLACE FUNCTION validate_runtime_item_economy_policy()
RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF NEW.runtime_enabled = TRUE THEN
        IF NEW.condition_policy_id IS NOT NULL OR NEW.repair_policy_id IS NOT NULL THEN RAISE EXCEPTION 'Cannot runtime-enable item % with unsupported condition or repair policy metadata.', NEW.item_id; END IF;
        IF NEW.death_behavior = 'transform' AND NOT EXISTS (SELECT 1 FROM item_definitions target WHERE target.item_id = NEW.death_transform_item_id AND target.runtime_enabled = TRUE) THEN RAISE EXCEPTION 'Cannot runtime-enable transform item % because target % is not runtime-enabled.', NEW.item_id, NEW.death_transform_item_id; END IF;
    END IF;
    IF OLD.runtime_enabled = TRUE AND NEW.runtime_enabled = FALSE AND EXISTS (SELECT 1 FROM item_definitions source WHERE source.runtime_enabled = TRUE AND source.death_behavior = 'transform' AND source.death_transform_item_id = OLD.item_id) THEN RAISE EXCEPTION 'Cannot disable runtime item % while a runtime-enabled transform source references it.', OLD.item_id; END IF;
    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS item_definitions_runtime_economy_policy_guard ON item_definitions;
CREATE TRIGGER item_definitions_runtime_economy_policy_guard BEFORE INSERT OR UPDATE OF runtime_enabled, death_behavior, death_transform_item_id, condition_policy_id, repair_policy_id ON item_definitions FOR EACH ROW EXECUTE FUNCTION validate_runtime_item_economy_policy();

CREATE OR REPLACE FUNCTION prevent_runtime_transform_target_delete()
RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF EXISTS (SELECT 1 FROM item_definitions source WHERE source.runtime_enabled = TRUE AND source.death_behavior = 'transform' AND source.death_transform_item_id = OLD.item_id) THEN RAISE EXCEPTION 'Cannot delete item % while a runtime-enabled transform source references it.', OLD.item_id; END IF;
    RETURN OLD;
END;
$$;

DROP TRIGGER IF EXISTS item_definitions_runtime_transform_target_delete_guard ON item_definitions;
CREATE TRIGGER item_definitions_runtime_transform_target_delete_guard BEFORE DELETE ON item_definitions FOR EACH ROW EXECUTE FUNCTION prevent_runtime_transform_target_delete();
