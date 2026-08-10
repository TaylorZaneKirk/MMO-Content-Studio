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
var _saved_socket_overrides: Dictionary = {}


func configure(next_rig: Dictionary, next_calibration_id: String) -> void:
	rig = next_rig.duplicate(true)
	target_calibration_id = next_calibration_id.strip_edges()
	calibration_id = target_calibration_id


func clear_loaded_state() -> void:
	loaded_calibration_id = ""
	catalog_hash = ""
	exists = false
	socket_overrides = {}
	_saved_socket_overrides = {}


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
	else:
		loaded_calibration_id = target_calibration_id
		calibration_id = target_calibration_id
		socket_overrides = {}
	_saved_socket_overrides = socket_overrides.duplicate(true)


func is_loaded_target(target: String) -> bool:
	return not loaded_calibration_id.is_empty() \
		and loaded_calibration_id == target.strip_edges() \
		and not catalog_hash.is_empty()


func is_dirty() -> bool:
	return JSON.stringify(socket_overrides) != JSON.stringify(_saved_socket_overrides)


func get_socket_ids() -> Array:
	var ids: Array = []
	for socket_variant in rig.get("sockets", []) as Array:
		var socket := socket_variant as Dictionary
		var socket_id := str(socket.get("socket_id", ""))
		if not socket_id.is_empty():
			ids.append(socket_id)
	return ids


func resolve_effective_point(socket_id: String, direction: String, frame: int) -> Dictionary:
	var override := _read_point(socket_overrides, socket_id, direction, frame)
	if not override.is_empty():
		return {"available": true, "point": override, "is_override": true}
	var inherited := _read_base_point(socket_id, direction, frame)
	if not inherited.is_empty():
		return {"available": true, "point": inherited, "is_override": false}
	return {"available": false, "point": {}, "is_override": false}


func has_override(socket_id: String, direction: String, frame: int) -> bool:
	return not _read_point(socket_overrides, socket_id, direction, frame).is_empty()


func set_override(socket_id: String, direction: String, frame: int, point: Vector2i) -> void:
	var directions := socket_overrides.get(socket_id, {}) as Dictionary
	var frames := directions.get(direction, {}) as Dictionary
	frames[str(frame)] = {"x": clampi(point.x, -COORDINATE_LIMIT, COORDINATE_LIMIT), "y": clampi(point.y, -COORDINATE_LIMIT, COORDINATE_LIMIT)}
	directions[direction] = frames
	socket_overrides[socket_id] = directions


func revert_override(socket_id: String, direction: String, frame: int) -> bool:
	if not has_override(socket_id, direction, frame):
		return false
	var directions := socket_overrides.get(socket_id, {}) as Dictionary
	var frames := directions.get(direction, {}) as Dictionary
	frames.erase(str(frame))
	if frames.is_empty():
		directions.erase(direction)
	else:
		directions[direction] = frames
	if directions.is_empty():
		socket_overrides.erase(socket_id)
	else:
		socket_overrides[socket_id] = directions
	return true


func save_payload() -> Dictionary:
	return {
		"expected_catalog_hash": catalog_hash,
		"rig_id": str(rig.get("rig_id", "")),
		"socket_overrides": socket_overrides.duplicate(true),
	}


func apply_saved_response(payload: Dictionary) -> void:
	load_response(payload)


func discard_changes() -> void:
	socket_overrides = _saved_socket_overrides.duplicate(true)


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


func _read_point(overrides: Dictionary, socket_id: String, direction: String, frame: int) -> Dictionary:
	var directions := overrides.get(socket_id, {}) as Dictionary
	return _read_point_from_positions(directions, direction, frame)


func _read_point_from_positions(positions: Dictionary, direction: String, frame: int) -> Dictionary:
	var frames := positions.get(direction, {}) as Dictionary
	var point := frames.get(str(frame), {}) as Dictionary
	if not point.has("x") or not point.has("y"):
		return {}
	return {"x": int(point.get("x", 0)), "y": int(point.get("y", 0))}
