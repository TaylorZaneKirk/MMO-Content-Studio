-- Reusable Shop content only. No current stock, currency or runtime deadlines.
BEGIN;
CREATE TABLE shop_definitions (
    shop_definition_id TEXT PRIMARY KEY CHECK (shop_definition_id ~ '^[a-z][a-z0-9_]*$'),
    display_name TEXT NOT NULL CHECK (btrim(display_name) <> ''),
    publication_state TEXT NOT NULL DEFAULT 'Draft' CHECK (publication_state IN ('Draft', 'Published', 'Disabled')),
    buys_unstocked_items BOOLEAN NOT NULL DEFAULT FALSE,
    price_change_per_stock_percent INTEGER NOT NULL CHECK (price_change_per_stock_percent BETWEEN 0 AND 100),
    notes TEXT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp()
);
CREATE TABLE shop_stock_items (
    shop_definition_id TEXT NOT NULL REFERENCES shop_definitions ON DELETE CASCADE,
    stock_order INTEGER NOT NULL CHECK (stock_order >= 0),
    item_id TEXT NOT NULL REFERENCES item_definitions,
    default_stock INTEGER NOT NULL CHECK (default_stock >= 0),
    restock_ticks INTEGER NOT NULL CHECK (restock_ticks > 0),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp(),
    PRIMARY KEY (shop_definition_id, item_id),
    UNIQUE (shop_definition_id, stock_order)
);
ALTER TABLE npc_definitions ADD COLUMN shop_definition_id TEXT NULL REFERENCES shop_definitions;
CREATE INDEX npc_shop_reference_idx ON npc_definitions(shop_definition_id) WHERE shop_definition_id IS NOT NULL;
CREATE INDEX shop_stock_item_reference_idx ON shop_stock_items(item_id);

-- This predicate expresses item economic policy, not Shop-local prices.
CREATE FUNCTION shop_stock_item_is_valid(enabled BOOLEAN, policy TEXT, buy_price BIGINT, sell_price BIGINT, quantity INTEGER)
RETURNS BOOLEAN LANGUAGE sql IMMUTABLE AS $$
    SELECT enabled AND CASE WHEN quantity > 0
        THEN policy IN ('npc_sells', 'npc_buys_and_sells') AND sell_price IS NOT NULL AND sell_price >= 0
        ELSE policy IN ('npc_buys', 'npc_buys_and_sells') AND buy_price IS NOT NULL AND buy_price >= 0 END;
$$;

CREATE FUNCTION validate_shop_lifecycle() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE stock RECORD;
BEGIN
    IF TG_OP = 'DELETE' THEN
        IF OLD.publication_state <> 'Disabled' THEN
            RAISE EXCEPTION 'Disable Shop % before deleting it.', OLD.shop_definition_id;
        END IF;
        RETURN OLD; -- NPC and stock item references retain their ordinary foreign keys.
    END IF;
    IF NEW.publication_state <> 'Published' AND EXISTS (
        SELECT 1 FROM npc_definitions WHERE shop_definition_id = NEW.shop_definition_id AND publication_state = 'Published') THEN
        RAISE EXCEPTION 'Published NPCs reference Shop %; unpublish or change those NPCs first.', NEW.shop_definition_id;
    END IF;
    IF NEW.publication_state = 'Published' THEN
        FOR stock IN SELECT i.*, s.default_stock FROM shop_stock_items s JOIN item_definitions i USING(item_id)
                     WHERE s.shop_definition_id = NEW.shop_definition_id ORDER BY i.item_id FOR SHARE OF i LOOP
            IF NOT shop_stock_item_is_valid(stock.runtime_enabled, stock.shop_policy, stock.npc_buy_price, stock.npc_sell_price, stock.default_stock) THEN
                RAISE EXCEPTION 'Shop % has incompatible or unpublished stock item %.', NEW.shop_definition_id, stock.item_id;
            END IF;
        END LOOP;
    END IF;
    RETURN NEW;
END;
$$;
CREATE TRIGGER shop_lifecycle_guard BEFORE INSERT OR UPDATE OR DELETE ON shop_definitions
FOR EACH ROW EXECUTE FUNCTION validate_shop_lifecycle();

CREATE FUNCTION validate_shop_stock_row() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE state TEXT; item RECORD;
BEGIN
    SELECT publication_state INTO state FROM shop_definitions WHERE shop_definition_id = NEW.shop_definition_id FOR UPDATE;
    IF state = 'Published' THEN
        SELECT * INTO item FROM item_definitions WHERE item_id = NEW.item_id FOR SHARE;
        IF NOT shop_stock_item_is_valid(item.runtime_enabled, item.shop_policy, item.npc_buy_price, item.npc_sell_price, NEW.default_stock) THEN
            RAISE EXCEPTION 'Published Shop stock item % is incompatible or unpublished.', NEW.item_id;
        END IF;
    END IF;
    RETURN NEW;
END;
$$;
CREATE TRIGGER shop_stock_row_guard BEFORE INSERT OR UPDATE ON shop_stock_items
FOR EACH ROW EXECUTE FUNCTION validate_shop_stock_row();

CREATE FUNCTION protect_published_shop_items() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF EXISTS (SELECT 1 FROM shop_stock_items s JOIN shop_definitions d USING(shop_definition_id)
               WHERE s.item_id = NEW.item_id AND d.publication_state = 'Published'
                 AND NOT shop_stock_item_is_valid(NEW.runtime_enabled, NEW.shop_policy, NEW.npc_buy_price, NEW.npc_sell_price, s.default_stock)) THEN
        RAISE EXCEPTION 'Published Shop stock depends on item %; fix or unpublish the Shop first.', NEW.item_id;
    END IF;
    RETURN NEW;
END;
$$;
CREATE TRIGGER published_shop_item_guard BEFORE UPDATE ON item_definitions
FOR EACH ROW EXECUTE FUNCTION protect_published_shop_items();

CREATE FUNCTION validate_npc_shop_reference() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE state TEXT;
BEGIN
    IF NEW.shop_definition_id IS NOT NULL AND NEW.publication_state = 'Published' THEN
        SELECT publication_state INTO state FROM shop_definitions WHERE shop_definition_id = NEW.shop_definition_id FOR SHARE;
        IF state IS DISTINCT FROM 'Published' OR NOT NEW.interaction_enabled THEN
            RAISE EXCEPTION 'Published Shopkeeper NPC % requires interaction_enabled and a Published Shop.', NEW.npc_definition_id;
        END IF;
    END IF;
    RETURN NEW;
END;
$$;
CREATE TRIGGER npc_shop_reference_guard BEFORE INSERT OR UPDATE ON npc_definitions
FOR EACH ROW EXECUTE FUNCTION validate_npc_shop_reference();

INSERT INTO schema_migrations (migration_file, migration_number, migration_name, notes)
VALUES ('066_shop_authoring_schema.sql', 66, 'shop_authoring_schema',
        'Reusable Shops, ordered equilibrium stock, NPC capability and published dependency guards; no Shop runtime.');
COMMIT;
