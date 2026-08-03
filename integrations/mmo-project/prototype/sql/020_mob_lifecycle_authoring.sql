-- Adds authored mob lifecycle timing for defeated hold and future respawn.

ALTER TABLE mob_definitions
    ADD COLUMN IF NOT EXISTS defeated_hold_duration_ms INTEGER NOT NULL DEFAULT 1000;

ALTER TABLE mob_definitions
    ADD COLUMN IF NOT EXISTS respawn_delay_ms INTEGER NOT NULL DEFAULT 5000;

ALTER TABLE mob_definitions
    DROP CONSTRAINT IF EXISTS mob_definitions_lifecycle_durations_check;

ALTER TABLE mob_definitions
    ADD CONSTRAINT mob_definitions_lifecycle_durations_check
        CHECK (
            defeated_hold_duration_ms BETWEEN 0 AND 86400000
            AND respawn_delay_ms BETWEEN 0 AND 86400000
        );
