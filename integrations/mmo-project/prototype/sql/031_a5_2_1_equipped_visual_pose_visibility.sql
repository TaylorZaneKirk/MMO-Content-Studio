-- A5.2.1: explicit sparse per-pose visibility metadata for equipped visuals.

ALTER TABLE item_equipped_visual_pose_anchors
    ADD COLUMN IF NOT EXISTS hidden BOOLEAN NOT NULL DEFAULT FALSE;
