extends RefCounted

const DEFAULT_HUMANOID_BASE_LAYERS := {
	"head": "head1",
	"body": "defbod",
	"legs": "defbod",
}


static func initial_state_for_rig(rig_id: String, rig_catalog: Dictionary) -> Dictionary:
	var base_layers := {}
	if rig_id == "humanoid_v1" and _catalog_has_rig(rig_catalog, rig_id):
		base_layers = DEFAULT_HUMANOID_BASE_LAYERS.duplicate(true)
	return {
		"base_layers": base_layers,
		"cosmetics": {},
	}


static func _catalog_has_rig(rig_catalog: Dictionary, rig_id: String) -> bool:
	for rig_variant in rig_catalog.get("rigs", []) as Array:
		if rig_variant is Dictionary and str((rig_variant as Dictionary).get("rig_id", "")) == rig_id:
			return true
	return false
