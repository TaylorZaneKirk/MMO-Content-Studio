extends RefCounted

static func initial_state_for_rig(_rig_id: String, _rig_catalog: Dictionary) -> Dictionary:
	return {
		"base_layers": {},
		"cosmetics": {},
	}
