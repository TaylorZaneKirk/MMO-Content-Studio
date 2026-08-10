extends RefCounted
class_name ActorSocketCalibrationState

const COORDINATE_LIMIT := 4096

var rig: Dictionary = {}
var calibration_id := ""
var target_calibration_id := ""
var loaded_calibration_id := ""
var catalog_hash := ""
var exists := false
var socket_overrides: Dictionary = {}
var foreground_overlay_overrides: Dictionary = {}
var _saved_socket_overrides: Dictionary = {}
var _saved_foreground_overlay_overrides: Dictionary = {}


func configure(next_rig: Dictionary, next_calibration_id: String) -> void:
	rig = next_rig.duplicate(true)
	target_calibration_id = next_calibration_id.strip_edges()
	calibration_id = target_calibration_id


func clear_loaded_state() -> void:
	loaded_calibration_id = ""
	catalog_hash = ""
	exists = false
	socket_overrides = {}
	foreground_overlay_overrides = {}
	_saved_socket_overrides = {}
	_saved_foreground_overlay_overrides = {}


func begin_load(next_calibration_id: String) -> void:
	target_calibration_id = next_calibration_id.strip_edges()
	calibration_id = target_calibration_id
	clear_loaded_state()


func load_response(payload: Dictionary) -> void:
	exists = bool(payload.get("exists", false))
	catalog_hash = str(payload.get("catalog_hash", ""))
	var calibration := payload.get("calibration", {}) as Dictionary
	if exists and not calibration.is_empty():
		loaded_calibration_id = str(calibration.get("calibration_id", target_calibration_id)).strip_edges()
		calibration_id = loaded_calibration_id
		socket_overrides = _normalize_socket_overrides(calibration.get("sockets", {}) as Dictionary)
		foreground_overlay_overrides = _normalize_foreground_overlay_overrides(calibration.get("foreground_overlays", {}) as Dictionary)
	else:
		loaded_calibration_id = target_calibration_id
		calibration_id = target_calibration_id
		socket_overrides = {}
		foreground_overlay_overrides = {}
	_saved_socket_overrides = socket_overrides.duplicate(true)
	_saved_foreground_overlay_overrides = foreground_overlay_overrides.duplicate(true)


func is_loaded_target(target: String) -> bool:
	return not loaded_calibration_id.is_empty() \
		and loaded_calibration_id == target.strip_edges() \
		and not catalog_hash.is_empty()


func is_dirty() -> bool:
	return JSON.stringify(socket_overrides) != JSON.stringify(_saved_socket_overrides) \
		or JSON.stringify(foreground_overlay_overrides) != JSON.stringify(_saved_foreground_overlay_overrides)


func get_socket_ids() -> Array:
	var ids: Array = []
	for socket_variant in rig.get("sockets", []) as Array:
		var socket := socket_variant as Dictionary
		var socket_id := str(socket.get("socket_id", ""))
		if not socket_id.is_empty():
			ids.append(socket_id)
	return ids


func get_foreground_overlays() -> Array:
	var overlays: Array = []
	for overlay_variant in rig.get("foreground_overlays", []) as Array:
		if overlay_variant is Dictionary:
			overlays.append((overlay_variant as Dictionary).duplicate(true))
	return overlays


func resolve_effective_point(socket_id: String, direction: String, frame: int) -> Dictionary:
	var override := _read_point(socket_overrides, socket_id, direction, frame)
	if not override.is_empty():
		return {"available": true, "point": override, "is_override": true}
	var inherited := _read_base_point(socket_id, direction, frame)
	if not inherited.is_empty():
		return {"available": true, "point": inherited, "is_override": false}
	return {"available": false, "point": {}, "is_override": false}


func resolve_effective_rectangle(overlay_id: String, direction: String, frame: int) -> Dictionary:
	var override := _read_rectangle(foreground_overlay_overrides, overlay_id, direction, frame)
	if not override.is_empty():
		return {"available": true, "rectangle": override, "is_override": true}
	var inherited := _read_base_rectangle(overlay_id, direction, frame)
	if not inherited.is_empty():
		return {"available": true, "rectangle": inherited, "is_override": false}
	return {"available": false, "rectangle": {}, "is_override": false}


func get_overlay(overlay_id: String) -> Dictionary:
	for overlay_variant in get_foreground_overlays():
		var overlay := overlay_variant as Dictionary
		if str(overlay.get("overlay_id", "")) == overlay_id:
			return overlay
	return {}


func has_override(socket_id: String, direction: String, frame: int) -> bool:
	return not _read_point(socket_overrides, socket_id, direction, frame).is_empty()


func has_foreground_overlay_override(overlay_id: String, direction: String, frame: int) -> bool:
	return not _read_rectangle(foreground_overlay_overrides, overlay_id, direction, frame).is_empty()


func set_override(socket_id: String, direction: String, frame: int, point: Vector2i) -> void:
	var directions := socket_overrides.get(socket_id, {}) as Dictionary
	var frames := directions.get(direction, {}) as Dictionary
	frames[str(frame)] = {"x": clampi(point.x, -COORDINATE_LIMIT, COORDINATE_LIMIT), "y": clampi(point.y, -COORDINATE_LIMIT, COORDINATE_LIMIT)}
	directions[direction] = frames
	socket_overrides[socket_id] = directions


func set_foreground_overlay_override(overlay_id: String, direction: String, frame: int, rectangle: Dictionary) -> void:
	var directions := foreground_overlay_overrides.get(overlay_id, {}) as Dictionary
	var frames := directions.get(direction, {}) as Dictionary
	frames[str(frame)] = {
		"x": int(rectangle.get("x", 0)),
		"y": int(rectangle.get("y", 0)),
		"width": int(rectangle.get("width", 1)),
		"height": int(rectangle.get("height", 1)),
	}
	directions[direction] = frames
	foreground_overlay_overrides[overlay_id] = directions


func revert_override(socket_id: String, direction: String, frame: int) -> bool:
	return _revert_sparse_entry(socket_overrides, socket_id, direction, frame)


func revert_foreground_overlay_override(overlay_id: String, direction: String, frame: int) -> bool:
	return _revert_sparse_entry(foreground_overlay_overrides, overlay_id, direction, frame)


func save_payload() -> Dictionary:
	return {
		"expected_catalog_hash": catalog_hash,
		"rig_id": str(rig.get("rig_id", "")),
		"socket_overrides": socket_overrides.duplicate(true),
		"foreground_overlay_overrides": foreground_overlay_overrides.duplicate(true),
	}


func apply_saved_response(payload: Dictionary) -> void:
	load_response(payload)


func discard_changes() -> void:
	socket_overrides = _saved_socket_overrides.duplicate(true)
	foreground_overlay_overrides = _saved_foreground_overlay_overrides.duplicate(true)


func _revert_sparse_entry(overrides: Dictionary, entry_id: String, direction: String, frame: int) -> bool:
	var directions := overrides.get(entry_id, {}) as Dictionary
	var frames := directions.get(direction, {}) as Dictionary
	if not frames.has(str(frame)):
		return false
	frames.erase(str(frame))
	if frames.is_empty():
		directions.erase(direction)
	else:
		directions[direction] = frames
	if directions.is_empty():
		overrides.erase(entry_id)
	else:
		overrides[entry_id] = directions
	return true


func _normalize_socket_overrides(value: Dictionary) -> Dictionary:
	var normalized := value.duplicate(true)
	for socket_id_variant in normalized.keys():
		var directions := normalized.get(socket_id_variant, {}) as Dictionary
		for direction_variant in directions.keys():
			var frames := directions.get(direction_variant, {}) as Dictionary
			for frame_variant in frames.keys():
				var point := frames.get(frame_variant, {}) as Dictionary
				if point.has("x"):
					point["x"] = _normalize_coordinate(point.get("x"))
				if point.has("y"):
					point["y"] = _normalize_coordinate(point.get("y"))
				frames[frame_variant] = point
			directions[direction_variant] = frames
		normalized[socket_id_variant] = directions
	return normalized


func _normalize_foreground_overlay_overrides(value: Dictionary) -> Dictionary:
	var normalized: Dictionary = {}
	for overlay_id_variant in value.keys():
		var overlay := value.get(overlay_id_variant, {}) as Dictionary
		var directions := overlay.get("source_rect_by_direction", {}) as Dictionary
		if directions.is_empty():
			continue
		var normalized_directions := directions.duplicate(true)
		for direction_variant in normalized_directions.keys():
			var frames := normalized_directions.get(direction_variant, {}) as Dictionary
			for frame_variant in frames.keys():
				var rectangle := frames.get(frame_variant, {}) as Dictionary
				for key in ["x", "y", "width", "height"]:
					if rectangle.has(key):
						rectangle[key] = _normalize_coordinate(rectangle.get(key))
				frames[frame_variant] = rectangle
			normalized_directions[direction_variant] = frames
		normalized[str(overlay_id_variant)] = normalized_directions
	return normalized


func _normalize_coordinate(value: Variant) -> Variant:
	if value is float and is_equal_approx(value, round(value)):
		return int(value)
	return value


func _read_base_point(socket_id: String, direction: String, frame: int) -> Dictionary:
	for socket_variant in rig.get("sockets", []) as Array:
		var socket := socket_variant as Dictionary
		if str(socket.get("socket_id", "")) != socket_id:
			continue
		var positions := socket.get("positions", {}) as Dictionary
		return _read_point_from_positions(positions, direction, frame)
	return {}


func _read_base_rectangle(overlay_id: String, direction: String, frame: int) -> Dictionary:
	var overlay := get_overlay(overlay_id)
	if overlay.is_empty():
		return {}
	return _read_rectangle_from_positions(overlay.get("source_rect_by_direction", {}) as Dictionary, direction, frame)


func _read_point(overrides: Dictionary, socket_id: String, direction: String, frame: int) -> Dictionary:
	return _read_point_from_positions(overrides.get(socket_id, {}) as Dictionary, direction, frame)


func _read_rectangle(overrides: Dictionary, overlay_id: String, direction: String, frame: int) -> Dictionary:
	return _read_rectangle_from_positions(overrides.get(overlay_id, {}) as Dictionary, direction, frame)


func _read_point_from_positions(positions: Dictionary, direction: String, frame: int) -> Dictionary:
	var frames := positions.get(direction, {}) as Dictionary
	var point := frames.get(str(frame), {}) as Dictionary
	if not point.has("x") or not point.has("y"):
		return {}
	return {"x": int(point.get("x", 0)), "y": int(point.get("y", 0))}


func _read_rectangle_from_positions(positions: Dictionary, direction: String, frame: int) -> Dictionary:
	var frames := positions.get(direction, {}) as Dictionary
	var rectangle := frames.get(str(frame), {}) as Dictionary
	if not rectangle.has("x") or not rectangle.has("y") or not rectangle.has("width") or not rectangle.has("height"):
		return {}
	return {
		"x": int(rectangle.get("x", 0)),
		"y": int(rectangle.get("y", 0)),
		"width": int(rectangle.get("width", 0)),
		"height": int(rectangle.get("height", 0)),
	}
