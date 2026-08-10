extends RefCounted
class_name RiggedSpriteVisualPayload


static func canonicalize(descriptor: Dictionary) -> Dictionary:
	if descriptor.is_empty():
		return {}

	var pose_policy := str(descriptor.get("pose_policy", "")).strip_edges()
	var calibration_id: Variant = descriptor.get("calibration_id", null)
	var cosmetic_item_ids: Variant = descriptor.get("cosmetic_item_ids", {})
	var payload := {
		"schema_version": int(descriptor.get("schema_version", 1)),
		"rig_id": str(descriptor.get("rig_id", "")).strip_edges(),
		"calibration_id": calibration_id.strip_edges() if calibration_id is String else null,
		"pose_policy": pose_policy,
		"fixed_direction": null,
		"fixed_frame": null,
		"cosmetic_item_ids": (cosmetic_item_ids as Dictionary).duplicate(true) if cosmetic_item_ids is Dictionary else {},
	}
	if pose_policy == "fixed":
		payload["fixed_direction"] = str(descriptor.get("fixed_direction", "")).strip_edges()
		payload["fixed_frame"] = int(descriptor.get("fixed_frame", 0))
	return payload
