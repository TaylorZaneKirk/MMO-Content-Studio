-- Additive compatibility repair for consumable child rows authored through
-- Content Studio's unified item Save Draft path.

ALTER TABLE item_consumable_requirements
ADD COLUMN IF NOT EXISTS updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW();

ALTER TABLE item_consumable_effects
ADD COLUMN IF NOT EXISTS updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW();
