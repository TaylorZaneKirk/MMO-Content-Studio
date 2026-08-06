-- A3: optional equipped-visual authoring metadata for unified item aggregates.
-- This migration is a handoff artifact for MMO Project and is not applied
-- automatically by MMO Content Studio.

CREATE TABLE IF NOT EXISTS item_equipped_visuals (
    item_id TEXT PRIMARY KEY
        REFERENCES item_definitions(item_id)
        ON DELETE CASCADE,
    asset_key TEXT NOT NULL DEFAULT '',
    rig_id TEXT NOT NULL DEFAULT '',
    binding_type TEXT NOT NULL DEFAULT '',
    render_layer_id TEXT NOT NULL DEFAULT '',
    socket_id TEXT NULL,
    secondary_socket_id TEXT NULL,
    nudge_x INTEGER NOT NULL DEFAULT 0,
    nudge_y INTEGER NOT NULL DEFAULT 0,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT item_equipped_visuals_asset_key_format_check
        CHECK (
            asset_key = ''
            OR (
                asset_key = LOWER(asset_key)
                AND asset_key ~ '^[a-z][a-z0-9]*(_[a-z0-9]+)*$'
            )
        ),
    CONSTRAINT item_equipped_visuals_binding_type_check
        CHECK (binding_type IN ('', 'rig_layer', 'socket')),
    CONSTRAINT item_equipped_visuals_socket_nonblank_check
        CHECK (socket_id IS NULL OR LENGTH(BTRIM(socket_id)) > 0),
    CONSTRAINT item_equipped_visuals_secondary_socket_nonblank_check
        CHECK (secondary_socket_id IS NULL OR LENGTH(BTRIM(secondary_socket_id)) > 0),
    CONSTRAINT item_equipped_visuals_timestamp_order_check
        CHECK (created_at <= updated_at)
);

CREATE TABLE IF NOT EXISTS item_equipped_visual_pose_anchors (
    item_id TEXT NOT NULL
        REFERENCES item_definitions(item_id)
        ON DELETE CASCADE,
    direction TEXT NOT NULL,
    frame INTEGER NOT NULL,
    grip_anchor_x INTEGER NOT NULL,
    grip_anchor_y INTEGER NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT item_equipped_visual_pose_anchors_pkey
        PRIMARY KEY (item_id, direction, frame),
    CONSTRAINT item_equipped_visual_pose_anchors_direction_check
        CHECK (direction IN ('N', 'E', 'S', 'W')),
    CONSTRAINT item_equipped_visual_pose_anchors_frame_check
        CHECK (frame >= 1 AND frame <= 4),
    CONSTRAINT item_equipped_visual_pose_anchors_timestamp_order_check
        CHECK (created_at <= updated_at)
);

CREATE OR REPLACE FUNCTION touch_item_definition_updated_at()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    affected_item_id TEXT;
BEGIN
    affected_item_id := COALESCE(NEW.item_id, OLD.item_id);
    IF affected_item_id IS NOT NULL THEN
        UPDATE item_definitions
        SET updated_at = NOW()
        WHERE item_id = affected_item_id;
    END IF;
    RETURN COALESCE(NEW, OLD);
END;
$$;

DROP TRIGGER IF EXISTS item_equipped_visuals_touch_item_updated_at
ON item_equipped_visuals;
CREATE TRIGGER item_equipped_visuals_touch_item_updated_at
AFTER INSERT OR UPDATE OR DELETE
ON item_equipped_visuals
FOR EACH ROW
EXECUTE FUNCTION touch_item_definition_updated_at();

DROP TRIGGER IF EXISTS item_equipped_visual_pose_anchors_touch_item_updated_at
ON item_equipped_visual_pose_anchors;
CREATE TRIGGER item_equipped_visual_pose_anchors_touch_item_updated_at
AFTER INSERT OR UPDATE OR DELETE
ON item_equipped_visual_pose_anchors
FOR EACH ROW
EXECUTE FUNCTION touch_item_definition_updated_at();
