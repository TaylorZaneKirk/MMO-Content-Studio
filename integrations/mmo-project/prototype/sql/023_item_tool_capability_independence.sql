-- U1: tool capabilities belong to item definitions and do not require
-- equipability, hand slots, or weapon metadata.

DROP TRIGGER IF EXISTS item_tool_capabilities_hand_slot_guard
ON item_tool_capabilities;

DROP TRIGGER IF EXISTS item_definitions_tool_capability_slot_guard
ON item_definitions;

DROP FUNCTION IF EXISTS ensure_item_tool_capabilities_hand_slot();

DROP FUNCTION IF EXISTS prevent_non_hand_slot_with_tool_capabilities();
