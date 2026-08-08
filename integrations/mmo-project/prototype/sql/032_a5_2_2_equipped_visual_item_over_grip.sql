-- A5.2.2: sparse per-pose equipped-item foreground depth metadata.

ALTER TABLE item_equipped_visual_pose_anchors
    ADD COLUMN IF NOT EXISTS item_over_grip BOOLEAN NOT NULL DEFAULT FALSE;
