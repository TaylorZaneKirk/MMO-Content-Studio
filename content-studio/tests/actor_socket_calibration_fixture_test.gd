extends SceneTree

const State = preload("res://scripts/actor_socket_calibration_state.gd")
const Canvas = preload("res://scripts/actor_socket_calibration_canvas.gd")
const Editor = preload("res://scripts/actor_socket_calibration_editor.gd")


class FixtureAuthoringHostClient extends AuthoringHostClient:
	var requests: Array = []

	func _request(
		operation: String,
		path: String,
		method: int = HTTPClient.METHOD_GET,
		payload: Dictionary = {}
	) -> void:
		requests.append({"operation": operation, "path": path, "method": method, "payload": payload.duplicate(true)})


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	_verify_state()
	await _verify_canvas()
	await _verify_editor()
	print("[actor-socket-calibration-fixture] passed")
	quit(0)


func _verify_state() -> void:
	var state = State.new()
	state.configure(_rig(), "orc_v1")
	state.load_response({
		"exists": true,
		"catalog_hash": "before",
		"calibration": {
			"calibration_id": "orc_v1",
			"rig_id": "humanoid_v1",
			"sockets": {
				"right_hand_primary": {
					"S": {"1": {"x": 25, "y": 135}, "2": {"x": 31, "y": 136}},
				},
			},
		},
	})
	var overridden := state.resolve_effective_point("right_hand_primary", "S", 1)
	if not bool(overridden.get("is_override", false)) or int((overridden.get("point", {}) as Dictionary).get("x", 0)) != 25:
		_fail("Actor override must take precedence over the rig socket")
		return
	var inherited := state.resolve_effective_point("left_hand_primary", "S", 1)
	if bool(inherited.get("is_override", true)) or int((inherited.get("point", {}) as Dictionary).get("x", -1)) != 14:
		_fail("Missing actor override must use the inherited rig socket")
		return
	state.set_override("left_hand_primary", "S", 1, Vector2i(-100, 500))
	state.set_override("right_hand_primary", "S", 3, Vector2i(44, 55))
	var payload := state.save_payload()
	var sockets := payload.get("socket_overrides", {}) as Dictionary
	if int((((sockets.get("right_hand_primary", {}) as Dictionary).get("S", {}) as Dictionary).get("1", {}) as Dictionary).get("x", 0)) != 25 or not ((sockets.get("left_hand_primary", {}) as Dictionary).has("S")):
		_fail("Saving one pose must retain the complete socket override dictionary")
		return
	if int((state.resolve_effective_point("left_hand_primary", "S", 1).get("point", {}) as Dictionary).get("y", 0)) != 500:
		_fail("Numeric fields must retain signed outside-image coordinates")
		return
	state.revert_override("right_hand_primary", "S", 3)
	if state.has_override("right_hand_primary", "S", 3) or not state.has_override("right_hand_primary", "S", 1):
		_fail("Revert must remove only the selected pose override")
		return
	state.revert_override("left_hand_primary", "S", 1)
	if (state.socket_overrides.get("left_hand_primary", {}) as Dictionary).size() != 0:
		_fail("Revert must clean empty sparse override containers")


func _verify_canvas() -> void:
	var temporary_root := ProjectSettings.globalize_path("user://actor-socket-calibration-fixture")
	DirAccess.make_dir_recursive_absolute(temporary_root)
	var image_path := temporary_root.path_join("actor.png")
	var image: Image = Image.create(13, 7, false, Image.FORMAT_RGBA8)
	image.fill(Color(0.8, 0.6, 0.2, 1))
	image.save_png(image_path)
	var canvas = Canvas.new()
	root.add_child(canvas)
	canvas.set_frame({"file_path": image_path, "source_width": 13, "source_height": 7})
	canvas.set_zoom_scale(8.0)
	canvas.set_marker(Vector2i(2, 3), false, true)
	var preview := canvas.source_to_preview(Vector2(12, 6))
	if not canvas.preview_to_source(preview).is_equal_approx(Vector2(12, 6)):
		_fail("Exact frame dimensions must drive calibration source/preview transforms")
		return
	if not canvas.source_bounds().has_point(Vector2(12, 6)) or canvas.source_bounds().has_point(Vector2(13, 6)):
		_fail("Calibration canvas must reach each visible source-image edge without extending past it")
		return
	var dragged: Array = []
	canvas.marker_dragged.connect(func(point: Vector2i) -> void: dragged.append(point))
	var press := InputEventMouseButton.new()
	press.button_index = MOUSE_BUTTON_LEFT
	press.pressed = true
	press.position = canvas.source_to_preview(Vector2(2, 3))
	canvas._gui_input(press)
	var move := InputEventMouseMotion.new()
	move.button_mask = MOUSE_BUTTON_MASK_LEFT
	move.position = canvas.source_to_preview(Vector2(99, 99))
	canvas._gui_input(move)
	if dragged.is_empty() or dragged.back() != Vector2i(12, 6):
		_fail("Mouse drag must clamp and quantize to the visible source frame")
		return
	canvas.clear_marker(false)
	canvas._gui_input(press)
	if dragged.size() != 1:
		_fail("Unavailable poses must not allow marker dragging")
	canvas.set_frame({})
	image = null
	canvas.queue_free()
	await process_frame


func _verify_editor() -> void:
	var client := FixtureAuthoringHostClient.new()
	var editor = Editor.new()
	editor.configure_client(client)
	root.add_child(editor)
	await process_frame
	editor.configure_context(_context("orc_v1"))
	if client.requests.size() != 2 or str((client.requests[0] as Dictionary).get("operation", "")) != "actor_calibration_frames" or str((client.requests[1] as Dictionary).get("operation", "")) != "actor_calibration":
		_fail("Composite actor context must load exact frames and the referenced calibration")
		return
	client.actor_calibration_frames_received.emit(_frames_response())
	client.actor_calibration_received.emit({
		"exists": true,
		"catalog_hash": "before",
		"calibration": {"calibration_id": "orc_v1", "rig_id": "humanoid_v1", "sockets": {"right_hand_primary": {"S": {"1": {"x": 25, "y": 135}, "2": {"x": 31, "y": 136}}}}},
	})
	editor._state.set_override("right_hand_primary", "S", 1, Vector2i(26, 136))
	editor._refresh_view()
	if not editor._state.is_dirty() or int((editor._state.save_payload().get("socket_overrides", {}) as Dictionary).get("right_hand_primary", {}) .get("S", {}) .get("2", {}) .get("x", 0)) != 31:
		_fail("Editor save payload must preserve unselected loaded socket poses")
		return
	editor._awaiting_save = true
	editor._on_request_failed("actor_calibration_save", "conflict", [{"code": "actor_calibration_catalog_conflict"}])
	if not editor._conflicted or not editor._state.is_dirty() or not editor._save_button.disabled:
		_fail("Calibration conflict must preserve local edits and block automatic overwrite")
		return
	var assigned := ""
	editor.use_calibration_for_actor.connect(func(calibration_id: String) -> void: assigned = calibration_id)
	editor._use_calibration_for_actor()
	if assigned != "orc_v1":
		_fail("Calibration assignment must remain an explicit editor signal")
		return
	editor.queue_free()
	await process_frame


func _context(calibration_id: String) -> Dictionary:
	return {
		"actor_kind": "mob",
		"visual_texture_path": "res://assets/maps/objects/mobs/orc.png",
		"rig_id": "humanoid_v1",
		"calibration_id": calibration_id,
		"rig": _rig(),
		"composite": true,
	}


func _frames_response() -> Dictionary:
	var frames: Array = []
	for direction in ["N", "E", "S", "W"]:
		for frame in [1, 2, 3, 4]:
			frames.append({"direction": direction, "frame": frame, "available": true, "file_path": "", "source_width": 160, "source_height": 200})
	return {"actor_kind": "mob", "visual_texture_path": "res://assets/maps/objects/mobs/orc.png", "frames": frames}


func _rig() -> Dictionary:
	var positions := {}
	for direction in ["N", "E", "S", "W"]:
		positions[direction] = {}
		for frame in [1, 2, 3, 4]:
			positions[direction][str(frame)] = {"x": 14, "y": 18}
	return {
		"rig_id": "humanoid_v1",
		"sockets": [
			{"socket_id": "right_hand_primary", "positions": positions.duplicate(true)},
			{"socket_id": "left_hand_primary", "positions": positions.duplicate(true)},
		],
	}


func _fail(message: String) -> void:
	push_error(message)
	quit(1)
