-- A5.2: optional per-pose horizontal mirror metadata for equipped visuals.

ALTER TABLE item_equipped_visual_pose_anchors
    ALTER COLUMN grip_anchor_x DROP NOT NULL,
    ALTER COLUMN grip_anchor_y DROP NOT NULL,
    ADD COLUMN IF NOT EXISTS flip_x BOOLEAN NOT NULL DEFAULT FALSE;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'item_equipped_visual_pose_anchors_grip_anchor_pair_check'
    ) THEN
        ALTER TABLE item_equipped_visual_pose_anchors
            ADD CONSTRAINT item_equipped_visual_pose_anchors_grip_anchor_pair_check
            CHECK (
                (grip_anchor_x IS NULL AND grip_anchor_y IS NULL)
                OR (grip_anchor_x IS NOT NULL AND grip_anchor_y IS NOT NULL)
            );
    END IF;
END;
$$;
