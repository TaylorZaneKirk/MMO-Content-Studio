-- Moves reusable mob behavior into authored mob definitions.

ALTER TABLE mob_definitions
    ADD COLUMN IF NOT EXISTS movement_behavior TEXT NOT NULL DEFAULT 'static';

ALTER TABLE mob_definitions
    ADD COLUMN IF NOT EXISTS wander_radius_tiles INTEGER NOT NULL DEFAULT 0;

ALTER TABLE mob_definitions
    ADD COLUMN IF NOT EXISTS aggression_mode TEXT NOT NULL DEFAULT 'retaliatory';

ALTER TABLE mob_definitions
    ADD COLUMN IF NOT EXISTS aggression_radius_tiles INTEGER NOT NULL DEFAULT 0;

ALTER TABLE mob_definitions
    ADD COLUMN IF NOT EXISTS leash_radius_tiles INTEGER NOT NULL DEFAULT 6;

ALTER TABLE mob_definitions
    ADD COLUMN IF NOT EXISTS return_home_behavior TEXT NOT NULL DEFAULT 'return_to_spawn';

UPDATE mob_definitions
SET movement_behavior = 'static',
    wander_radius_tiles = 0,
    aggression_mode = 'retaliatory',
    aggression_radius_tiles = 0,
    leash_radius_tiles = 6,
    return_home_behavior = 'return_to_spawn'
WHERE movement_behavior = 'static'
  AND wander_radius_tiles = 0
  AND aggression_radius_tiles = 0
  AND leash_radius_tiles = 6;

ALTER TABLE mob_definitions
    DROP CONSTRAINT IF EXISTS mob_definitions_behavior_values_check;

ALTER TABLE mob_definitions
    ADD CONSTRAINT mob_definitions_behavior_values_check
        CHECK (
            movement_behavior IN ('static', 'random_wander')
            AND aggression_mode IN ('passive', 'retaliatory', 'proactive')
            AND return_home_behavior IN ('none', 'return_to_spawn')
        );

ALTER TABLE mob_definitions
    DROP CONSTRAINT IF EXISTS mob_definitions_behavior_radii_check;

ALTER TABLE mob_definitions
    ADD CONSTRAINT mob_definitions_behavior_radii_check
        CHECK (
            wander_radius_tiles BETWEEN 0 AND 32
            AND aggression_radius_tiles BETWEEN 0 AND 32
            AND leash_radius_tiles BETWEEN 0 AND 64
        );

ALTER TABLE mob_definitions
    DROP CONSTRAINT IF EXISTS mob_definitions_behavior_consistency_check;

ALTER TABLE mob_definitions
    ADD CONSTRAINT mob_definitions_behavior_consistency_check
        CHECK (
            (movement_behavior <> 'static' OR wander_radius_tiles = 0)
            AND (movement_behavior <> 'random_wander' OR wander_radius_tiles > 0)
            AND (aggression_mode <> 'proactive' OR aggression_radius_tiles > 0)
            AND (aggression_mode = 'proactive' OR aggression_radius_tiles = 0)
            AND leash_radius_tiles >= wander_radius_tiles
            AND leash_radius_tiles >= aggression_radius_tiles
        );
