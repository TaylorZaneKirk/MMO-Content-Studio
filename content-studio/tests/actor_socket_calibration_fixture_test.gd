extends SceneTree

const State = preload("res://scripts/actor_socket_calibration_state.gd")
const Canvas = preload("res://scripts/actor_socket_calibration_canvas.gd")
const Editor = preload("res://scripts/actor_socket_calibration_editor.gd")
const MobEditor = preload("res://scripts/mob_editor.gd")
const NpcEditor = preload("res://scripts/npc_editor.gd")


class FixtureAuthoringHostClient extends AuthoringHostClient:
	var requests: Array = []
	var active_operation := ""
	var reject_requests := false

	func _request(
		operation: String,
		path: String,
		method: int = HTTPClient.METHOD_GET,
		payload: Dictionary = {}
	) -> void:
		if reject_requests:
			request_failed.emit(operation, "Another host request is still in progress.", [])
			return
		if not active_operation.is_empty():
			request_failed.emit(operation, "Another host request is still in progress.", [])
			return
		active_operation = operation
		requests.append({"operation": operation, "path": path, "method": method, "payload": payload.duplicate(true)})

	func complete_calibration(payload: Dictionary) -> void:
		active_operation = ""
		actor_calibration_received.emit(payload)

	func complete_frames(payload: Dictionary) -> void:
		active_operation = ""
		actor_calibration_frames_received.emit(payload)


class FixtureMobEditor extends MobEditor:
	func _payload() -> Dictionary:
		return {"composite_visual": _build_composite_visual()}


class FixtureNpcEditor extends NpcEditor:
	func _payload() -> Dictionary:
		return {"composite_visual": _build_composite_visual(), "preview_signature": null}


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	_verify_state()
	await _verify_canvas()
	await _verify_editor()
	await _verify_rigged_mob_preview()
	_verify_composite_preview_requests()
	print("[actor-socket-calibration-fixture] passed")
	quit(0)


func _verify_state() -> void:
	var state = State.new()
	state.configure(_rig(), "orc_v1")
	var parsed := JSON.new()
	if parsed.parse('''
		{
			"exists": true,
			"catalog_hash": "before",
			"calibration": {
				"calibration_id": "orc_v1",
				"rig_id": "humanoid_v1",
				"sockets": {
					"right_hand_primary": {
						"S": {"1": {"x": 25, "y": 135}, "2": {"x": 31, "y": 136}}
					}
				}
			}
		}
	''') != OK:
		_fail("Fixture calibration JSON must parse")
		return
	state.load_response(parsed.data as Dictionary)
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
	if JSON.stringify(payload).contains("25.0") or JSON.stringify(payload).contains("135.0"):
		_fail("Loaded integral socket coordinates must remain integer JSON values when resaved")
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
	if client.requests.size() != 1 or str((client.requests[0] as Dictionary).get("operation", "")) != "actor_calibration":
		_fail("Composite actor context must load the referenced calibration before requesting exact frames")
		return
	client.complete_calibration({
		"exists": true,
		"catalog_hash": "before",
		"calibration": {"calibration_id": "orc_v1", "rig_id": "humanoid_v1", "sockets": {"right_hand_primary": {"S": {"1": {"x": 25, "y": 135}, "2": {"x": 31, "y": 136}}}}},
	})
	if client.requests.size() != 2 or str((client.requests[1] as Dictionary).get("operation", "")) != "actor_calibration_frames":
		_fail("Exact frames must be requested only after the calibration request completes")
		return
	client.complete_frames(_frames_response())
	if editor._state.loaded_calibration_id != "orc_v1" or not editor._state.is_loaded_target("orc_v1"):
		_fail("The editor must retain an explicit successfully loaded calibration target")
		return
	if editor._status.text == "Selected exact pose is unavailable. No compatibility frame is used.":
		_fail("The exact-pose unavailable status must clear once the requested frame is available")
		return
	editor.configure_context(_context("orc_v1"))
	if client.requests.size() != 2:
		_fail("Reapplying an identical calibration context must not reload calibration or exact frames")
		return
	editor._state.set_override("right_hand_primary", "S", 1, Vector2i(26, 136))
	editor._refresh_view()
	if not editor._state.is_dirty() or int((editor._state.save_payload().get("socket_overrides", {}) as Dictionary).get("right_hand_primary", {}) .get("S", {}) .get("2", {}) .get("x", 0)) != 31:
		_fail("Editor save payload must preserve unselected loaded socket poses")
		return
	editor._awaiting_save = true
	editor._active_operation = "save"
	editor._on_request_failed("actor_calibration_save", "conflict", [{"code": "actor_calibration_catalog_conflict"}])
	if not editor._conflicted or not editor._state.is_dirty() or not editor._save_button.disabled:
		_fail("Calibration conflict must preserve local edits and block automatic overwrite")
		return
	editor._reload_calibration()
	editor._reload_calibration()
	if client.requests.size() != 3 or str((client.requests[2] as Dictionary).get("operation", "")) != "actor_calibration":
		_fail("Conflict reload must issue a fresh calibration request")
		return
	client.complete_calibration({
		"exists": true,
		"catalog_hash": "after",
		"calibration": {"calibration_id": "orc_v1", "rig_id": "humanoid_v1", "sockets": {"right_hand_primary": {"S": {"1": {"x": 25, "y": 135}}}}},
	})
	if editor._conflicted or editor._state.catalog_hash != "after" or not editor._state.is_loaded_target("orc_v1"):
		_fail("A successful conflict reload must restore editability against the new catalog hash")
		return
	client.complete_frames(_frames_response())
	editor._state.set_override("right_hand_primary", "S", 1, Vector2i(26, 136))
	editor._calibration_id.text = "new_actor"
	if editor._can_save():
		_fail("Typing a different calibration ID must not retarget the loaded socket dictionary")
		return
	editor._load_calibration()
	if client.requests.size() != 3:
		_fail("Dirty calibration target changes must not issue a new request")
		return
	editor.queue_free()
	await process_frame


func _verify_composite_preview_requests() -> void:
	var descriptor := {
		"schema_version": 1,
		"rig_id": "humanoid_v1",
		"calibration_id": "orc_v1",
		"pose_policy": "fixed",
		"fixed_direction": "S",
		"fixed_frame": 1,
		"cosmetic_item_ids": {"right_hand": "inventory_154_axe"},
	}
	var client := FixtureAuthoringHostClient.new()
	var mob := FixtureMobEditor.new()
	mob._client = client
	mob._mob_id = LineEdit.new()
	mob._mob_id.text = "orc_001"
	mob._operation = _option("save_draft")
	mob._preview_facing = _option("N")
	mob._preview_frame = _option(2)
	mob._visual_mode = _option("composite_rig")
	mob._composite_visual = descriptor.duplicate(true)
	mob._status = Label.new()
	client.request_failed.connect(mob._on_request_failed)
	mob._preview()
	if client.requests.size() != 1 or str((client.requests[0] as Dictionary).get("operation", "")) != "mob_preview":
		_fail("Mob preview payload construction must issue only its mob_preview request")
		return
	var mob_payload := ((client.requests[0] as Dictionary).get("payload", {}) as Dictionary).get("composite_visual", {}) as Dictionary
	if mob_payload != descriptor or mob_payload == mob._composite_visual:
		_fail("Mob composite payload must be a detached authored descriptor without UI side effects")
		return

	var npc := FixtureNpcEditor.new()
	npc._client = client
	npc._npc_id = LineEdit.new()
	npc._npc_id.text = "test_npc"
	npc._operation = _option("save_draft")
	npc._preview_facing = _option("south")
	npc._preview_frame = _option(2)
	npc._visual_mode = _option("composite_rig")
	npc._composite_visual = descriptor.duplicate(true)
	npc._status = Label.new()
	npc._preview()
	if client.requests.size() != 2 or str((client.requests[1] as Dictionary).get("operation", "")) != "npc_preview":
		_fail("NPC preview payload construction must issue only its npc_preview request")
		return

	var rejecting_client := FixtureAuthoringHostClient.new()
	rejecting_client.reject_requests = true
	var rejecting_mob := FixtureMobEditor.new()
	rejecting_mob._client = rejecting_client
	rejecting_mob._mob_id = LineEdit.new()
	rejecting_mob._mob_id.text = "orc_001"
	rejecting_mob._operation = _option("save_draft")
	rejecting_mob._preview_facing = _option("N")
	rejecting_mob._preview_frame = _option(2)
	rejecting_mob._visual_mode = _option("composite_rig")
	rejecting_mob._composite_visual = descriptor.duplicate(true)
	rejecting_mob._status = Label.new()
	rejecting_client.request_failed.connect(rejecting_mob._on_request_failed)
	rejecting_mob._preview()
	if rejecting_mob._status.text != "Another host request is still in progress.":
		_fail("A synchronous Mob preview failure must not be overwritten by calculating status")


func _option(metadata: Variant) -> OptionButton:
	var option := OptionButton.new()
	option.add_item(str(metadata))
	option.set_item_metadata(0, metadata)
	option.select(0)
	return option


func _verify_rigged_mob_preview() -> void:
	var temporary_root := ProjectSettings.globalize_path("user://actor-socket-calibration-preview-fixture")
	DirAccess.make_dir_recursive_absolute(temporary_root)
	var base_path := temporary_root.path_join("base.png")
	var cosmetic_path := temporary_root.path_join("axe.png")
	_write_fixture_png(base_path, Vector2i(160, 192), Color(0.2, 0.4, 0.6, 1))
	_write_fixture_png(cosmetic_path, Vector2i(24, 32), Color(1.0, 0.75, 0.15, 1))
	var viewport := SubViewport.new()
	viewport.size = Vector2i(240, 220)
	viewport.transparent_bg = true
	viewport.render_target_update_mode = SubViewport.UPDATE_ALWAYS
	root.add_child(viewport)
	var preview = MobEditor.MobVisualPreview.new()
	preview.size = Vector2(240, 220)
	viewport.add_child(preview)
	preview.set_rigged_sprite_preview({
		"source_width": 160,
		"source_height": 192,
		"cosmetics": [{
			"item_id": "inventory_154_axe",
			"file_path": cosmetic_path,
			"x": 25,
			"y": 135,
			"z_index": 10,
			"flip_x": false,
		}],
		"foreground_overlays": [],
	})
	if preview.rigged_cosmetics.size() != 1:
		_fail("A rigged Mob preview must load the host-resolved socket cosmetic")
		return
	if preview.rigged_draw_list.filter(func(entry: Dictionary) -> bool: return str(entry.get("kind", "")) == "cosmetic").size() != 1:
		_fail("A rigged Mob preview must retain the socket cosmetic in its ordered draw list")
		return
	var base_image := Image.load_from_file(base_path)
	preview.set_payload(
		ImageTexture.create_from_image(base_image),
		160,
		192,
		0.0,
		0.0,
		0.25,
		1,
		1,
		"fixture"
	)
	await RenderingServer.frame_post_draw
	var rendered := viewport.get_texture().get_image()
	var expected := Vector2i(65, 153)
	if rendered.get_pixelv(expected).r < 0.9 or rendered.get_pixelv(expected).g < 0.6:
		_fail("A rigged Mob preview must draw the socket cosmetic above its base actor")
		return
	preview.queue_free()
	viewport.queue_free()
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


func _write_fixture_png(path: String, size: Vector2i, color: Color) -> void:
	var image := Image.create(size.x, size.y, false, Image.FORMAT_RGBA8)
	image.fill(color)
	image.save_png(path)


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
