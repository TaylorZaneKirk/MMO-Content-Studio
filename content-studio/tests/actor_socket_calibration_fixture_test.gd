extends SceneTree

const State = preload("res://scripts/actor_socket_calibration_state.gd")
const Canvas = preload("res://scripts/actor_socket_calibration_canvas.gd")
const Editor = preload("res://scripts/actor_socket_calibration_editor.gd")
const MobEditor = preload("res://scripts/mob_editor.gd")
const NpcEditor = preload("res://scripts/npc_editor.gd")
const RiggedSpriteVisualPayload = preload("res://scripts/rigged_sprite_visual_payload.gd")


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
	_verify_composite_visual_payload()
	_verify_composite_preview_requests()
	_verify_persisted_composite_previews()
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
	mob._form_editable = true
	mob._status = Label.new()
	mob._sync_preview_pose_controls()
	client.request_failed.connect(mob._on_request_failed)
	mob._preview()
	if client.requests.size() != 1 or str((client.requests[0] as Dictionary).get("operation", "")) != "mob_preview":
		_fail("Mob preview payload construction must issue only its mob_preview request")
		return
	var mob_request := (client.requests[0] as Dictionary).get("payload", {}) as Dictionary
	if str(mob_request.get("preview_direction", "")) != "S" or int(mob_request.get("preview_frame", 0)) != 1 or not mob._preview_facing.disabled or not mob._preview_frame.disabled:
		_fail("Fixed Mob previews must synchronize and lock the effective S/F1 pose")
		return
	var mob_payload := ((client.requests[0] as Dictionary).get("payload", {}) as Dictionary).get("composite_visual", {}) as Dictionary
	if mob_payload != descriptor or mob_payload == mob._composite_visual:
		_fail("Mob composite payload must be a detached authored descriptor without UI side effects")
		return
	client.active_operation = ""

	var npc := FixtureNpcEditor.new()
	npc._client = client
	npc._npc_id = LineEdit.new()
	npc._npc_id.text = "test_npc"
	npc._operation = _option("save_draft")
	npc._preview_facing = _option("south")
	npc._preview_frame = _option(2)
	npc._visual_mode = _option("composite_rig")
	npc._composite_visual = descriptor.duplicate(true)
	npc._form_editable = true
	npc._status = Label.new()
	npc._sync_preview_pose_controls()
	npc._preview()
	if client.requests.size() != 2 or str((client.requests[1] as Dictionary).get("operation", "")) != "npc_preview":
		_fail("NPC preview payload construction must issue only its npc_preview request")
		return
	var npc_request := (client.requests[1] as Dictionary).get("payload", {}) as Dictionary
	if str(npc_request.get("preview_direction", "")) != "S" or int(npc_request.get("preview_frame", 0)) != 1 or not npc._preview_facing.disabled or not npc._preview_frame.disabled:
		_fail("Fixed NPC previews must synchronize and lock the effective S/F1 pose")
		return
	client.active_operation = ""

	var actor_pose_descriptor := descriptor.duplicate(true)
	actor_pose_descriptor["schema_version"] = 1.0
	actor_pose_descriptor["pose_policy"] = "actor_pose"
	actor_pose_descriptor["fixed_direction"] = "S"
	actor_pose_descriptor["fixed_frame"] = 1.0
	mob._composite_visual = actor_pose_descriptor
	mob._preview_facing = _option("N")
	mob._preview_frame = _option(2)
	mob._sync_preview_pose_controls()
	mob._preview()
	var actor_pose_request := (client.requests[2] as Dictionary).get("payload", {}) as Dictionary
	var actor_pose_payload := actor_pose_request.get("composite_visual", {}) as Dictionary
	if JSON.stringify(actor_pose_payload).contains("\"schema_version\":1.0") or actor_pose_payload.get("fixed_direction", "not-null") != null or actor_pose_payload.get("fixed_frame", "not-null") != null or str(actor_pose_request.get("preview_direction", "")) != "N" or int(actor_pose_request.get("preview_frame", 0)) != 2:
		_fail("Actor Pose preview payloads must use integer schema versions, null fixed fields, and user-selected preview controls")
		return
	if mob._preview_facing.disabled or mob._preview_frame.disabled:
		_fail("Actor Pose Mob preview selectors must remain editable")
		return
	client.active_operation = ""

	var npc_actor_pose := actor_pose_descriptor.duplicate(true)
	npc._composite_visual = npc_actor_pose
	npc._preview_facing = _option("north")
	npc._preview_frame = _option(2)
	npc._sync_preview_pose_controls()
	npc._preview()
	var npc_actor_pose_request := (client.requests[3] as Dictionary).get("payload", {}) as Dictionary
	var npc_actor_pose_payload := npc_actor_pose_request.get("composite_visual", {}) as Dictionary
	if JSON.stringify(npc_actor_pose_payload).contains("\"schema_version\":1.0") or npc_actor_pose_payload.get("fixed_direction", "not-null") != null or npc_actor_pose_payload.get("fixed_frame", "not-null") != null or str(npc_actor_pose_request.get("preview_direction", "")) != "N" or int(npc_actor_pose_request.get("preview_frame", 0)) != 2:
		_fail("Actor Pose NPC previews must use canonical descriptors and user-selected preview controls")
		return
	if npc._preview_facing.disabled or npc._preview_frame.disabled:
		_fail("Actor Pose NPC preview selectors must remain editable")
		return
	_verify_fixed_npc_preview_direction_selectors(descriptor)
	_verify_actor_pose_npc_preview_direction_selectors(actor_pose_descriptor)

	_verify_fixed_pose_rows(mob, npc)

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


func _verify_fixed_npc_preview_direction_selectors(base_descriptor: Dictionary) -> void:
	for fixture in [
		{"fixed_direction": "S", "selector_metadata": "south"},
		{"fixed_direction": "W", "selector_metadata": "west"},
		{"fixed_direction": "E", "selector_metadata": "east"},
		{"fixed_direction": "N", "selector_metadata": "north"},
	]:
		var client := FixtureAuthoringHostClient.new()
		var npc := FixtureNpcEditor.new()
		npc._client = client
		npc._npc_id = LineEdit.new()
		npc._npc_id.text = "fixed_npc_%s" % str(fixture.get("fixed_direction", ""))
		npc._operation = _option("save_draft")
		npc._preview_facing = _direction_option("north")
		npc._preview_frame = _option(2)
		npc._visual_mode = _option("composite_rig")
		npc._composite_visual = base_descriptor.duplicate(true)
		npc._composite_visual["fixed_direction"] = fixture.get("fixed_direction", "S")
		npc._composite_visual["fixed_frame"] = 1
		npc._form_editable = true
		npc._status = Label.new()
		npc._sync_preview_pose_controls()
		if _selected_metadata(npc._preview_facing) != str(fixture.get("selector_metadata", "")):
			_fail("Fixed NPC preview selector must use its full direction metadata")
			return
		if not npc._preview_facing.disabled or not npc._preview_frame.disabled or int(_selected_metadata(npc._preview_frame)) != 1:
			_fail("Fixed NPC preview controls must synchronize and remain disabled")
			return
		npc._preview()
		var request := (client.requests.back() as Dictionary).get("payload", {}) as Dictionary
		if str(request.get("preview_direction", "")) != str(fixture.get("fixed_direction", "")) or int(request.get("preview_frame", 0)) != 1:
			_fail("Fixed NPC preview requests must retain canonical fixed pose values")
			return


func _verify_actor_pose_npc_preview_direction_selectors(base_descriptor: Dictionary) -> void:
	for fixture in [
		{"selector_metadata": "south", "direction": "S"},
		{"selector_metadata": "west", "direction": "W"},
		{"selector_metadata": "east", "direction": "E"},
		{"selector_metadata": "north", "direction": "N"},
	]:
		var client := FixtureAuthoringHostClient.new()
		var npc := FixtureNpcEditor.new()
		npc._client = client
		npc._npc_id = LineEdit.new()
		npc._npc_id.text = "actor_pose_npc_%s" % str(fixture.get("direction", ""))
		npc._operation = _option("save_draft")
		npc._preview_facing = _direction_option(str(fixture.get("selector_metadata", "")))
		npc._preview_frame = _option(2)
		npc._visual_mode = _option("composite_rig")
		npc._composite_visual = base_descriptor.duplicate(true)
		npc._form_editable = true
		npc._status = Label.new()
		npc._sync_preview_pose_controls()
		if npc._preview_facing.disabled or npc._preview_frame.disabled:
			_fail("Actor Pose NPC preview controls must remain editable")
			return
		npc._preview()
		var request := (client.requests.back() as Dictionary).get("payload", {}) as Dictionary
		if str(request.get("preview_direction", "")) != str(fixture.get("direction", "")) or int(request.get("preview_frame", 0)) != 2:
			_fail("Actor Pose NPC preview requests must use the selected canonical pose")
			return


func _verify_composite_visual_payload() -> void:
	var parsed := JSON.new()
	if parsed.parse('''
		{
			"schema_version": 1.0,
			"rig_id": "humanoid_v1",
			"calibration_id": "orc_v1",
			"pose_policy": "actor_pose",
			"fixed_direction": "S",
			"fixed_frame": 1.0,
			"cosmetic_item_ids": {"right_hand": "inventory_154_axe"}
		}
	''') != OK:
		_fail("Composite visual JSON fixture must parse")
		return
	var payload := RiggedSpriteVisualPayload.canonicalize(parsed.data as Dictionary)
	var serialized := JSON.stringify(payload)
	if serialized.contains("\"schema_version\":1.0") or payload.get("fixed_direction", "not-null") != null or payload.get("fixed_frame", "not-null") != null:
		_fail("Actor Pose descriptors must serialize integral schema_version and null fixed fields")
		return
	payload = RiggedSpriteVisualPayload.canonicalize({
		"schema_version": 1.0,
		"rig_id": "humanoid_v1",
		"calibration_id": null,
		"pose_policy": "fixed",
		"fixed_direction": "S",
		"fixed_frame": 1.0,
		"cosmetic_item_ids": {},
	})
	if int(payload.get("schema_version", 0)) != 1 or str(payload.get("fixed_direction", "")) != "S" or int(payload.get("fixed_frame", 0)) != 1:
		_fail("Fixed descriptors must serialize typed fixed pose values")


func _verify_persisted_composite_previews() -> void:
	var temporary_root := ProjectSettings.globalize_path("user://persisted-composite-preview-fixture")
	DirAccess.make_dir_recursive_absolute(temporary_root)
	var base_path := temporary_root.path_join("orc.png")
	var cosmetic_path := temporary_root.path_join("axe.png")
	_write_fixture_png(base_path, Vector2i(160, 192), Color(0.2, 0.4, 0.6, 1))
	_write_fixture_png(cosmetic_path, Vector2i(24, 32), Color(1.0, 0.75, 0.15, 1))
	var rigged_preview := {
		"base_file_path": base_path,
		"source_width": 160,
		"source_height": 192,
		"direction": "S",
		"frame": 1,
		"cosmetics": [{
			"item_id": "inventory_154_axe",
			"file_path": cosmetic_path,
			"x": 25,
			"y": 135,
			"z_index": 10,
			"flip_x": false,
		}],
		"foreground_overlays": [],
	}
	var fixed_descriptor := {
		"schema_version": 1,
		"rig_id": "humanoid_v1",
		"calibration_id": "orc_v1",
		"pose_policy": "fixed",
		"fixed_direction": "S",
		"fixed_frame": 1,
		"cosmetic_item_ids": {"right_hand": "inventory_154_axe"},
	}
	var mob_client := FixtureAuthoringHostClient.new()
	var mob := FixtureMobEditor.new()
	mob._client = mob_client
	mob._visual_preview = MobEditor.MobVisualPreview.new()
	mob._presentation_semantics = Label.new()
	mob._visual_path = LineEdit.new()
	mob._visual_path.text = "res://assets/maps/objects/mobs/orc.png"
	mob._visual_mode = _option("composite_rig")
	mob._preview_facing = _option("S")
	mob._preview_frame = _option(1)
	mob._fixed_direction_label = Label.new()
	mob._fixed_frame_label = Label.new()
	mob._composite_visual = fixed_descriptor.duplicate(true)
	mob._apply_button = Button.new()
	mob._apply_button.disabled = true
	mob._apply_persisted_rigged_preview({"rigged_sprite_preview": rigged_preview})
	mob._update_presentation_semantics()
	if not mob._has_persisted_rigged_preview or mob._visual_preview.rigged_cosmetics.size() != 1 or mob._visual_preview.rigged_draw_list.filter(func(entry: Dictionary) -> bool: return str(entry.get("kind", "")) == "cosmetic").size() != 1:
		_fail("Loaded Mob presentation must immediately retain and draw the saved Axe manifest")
		return
	if not mob_client.requests.is_empty() or not mob._apply_button.disabled:
		_fail("Loading persisted Mob presentation must not run a mutation preview or enable Apply")
		return
	if not mob._presentation_semantics.text.contains("Static base presentation") or not mob._presentation_semantics.text.contains("Attachment pose: South / F1") or mob._fixed_direction_label.text != "Attachment direction":
		_fail("Static fixed Mob UI must distinguish the base image from its attachment pose")
		return
	mob._clear_rigged_preview_for_unsaved_change()
	mob._update_presentation_semantics()
	if not mob._visual_preview.rigged_sprite_preview.is_empty() or not mob._presentation_semantics.text.contains("Validate changes to preview the unsaved composition"):
		_fail("Unsaved Mob appearance changes must clear the saved composite manifest")
		return

	var npc_client := FixtureAuthoringHostClient.new()
	var npc := FixtureNpcEditor.new()
	npc._client = npc_client
	npc._visual_preview = NpcEditor.NpcVisualPreview.new()
	npc._presentation_semantics = Label.new()
	npc._visual_path = LineEdit.new()
	npc._visual_path.text = "res://assets/actors/npcs/Chars_139_200-F2-S.png"
	npc._visual_mode = _option("composite_rig")
	npc._preview_facing = _direction_option("north")
	npc._preview_frame = _option(2)
	npc._fixed_direction_label = Label.new()
	npc._fixed_frame_label = Label.new()
	npc._composite_visual = {
		"schema_version": 1,
		"rig_id": "humanoid_v1",
		"calibration_id": null,
		"pose_policy": "actor_pose",
		"fixed_direction": null,
		"fixed_frame": null,
		"cosmetic_item_ids": {"right_hand": "inventory_154_axe"},
	}
	npc._apply_button = Button.new()
	npc._apply_button.disabled = true
	npc._apply_persisted_rigged_preview({"rigged_sprite_preview": rigged_preview})
	npc._update_presentation_semantics()
	if not npc._has_persisted_rigged_preview or npc._visual_preview.rigged_cosmetics.size() != 1 or _selected_metadata(npc._preview_facing) != "south" or int(_selected_metadata(npc._preview_frame)) != 1:
		_fail("Loaded actor-pose NPC presentation must immediately retain the saved Axe manifest and effective S/F1 pose")
		return
	if not npc_client.requests.is_empty() or not npc._apply_button.disabled or npc._presentation_semantics.text.contains("Static base presentation") or not npc._presentation_semantics.text.contains("Actor-pose presentation"):
		_fail("Actor-pose NPC presentation must remain literal without a static-base explanation or mutation preview")


func _verify_fixed_pose_rows(mob: FixtureMobEditor, npc: FixtureNpcEditor) -> void:
	mob._fixed_direction_label = Label.new()
	mob._fixed_direction = _option("S")
	mob._fixed_frame_label = Label.new()
	mob._fixed_frame = _option(1)
	mob._set_fixed_pose_rows_visible(false)
	if mob._fixed_direction_label.visible or mob._fixed_direction.visible or mob._fixed_frame_label.visible or mob._fixed_frame.visible:
		_fail("Actor Pose Mob controls must hide both fixed-pose labels and selectors")
		return
	mob._set_fixed_pose_rows_visible(true)
	if not mob._fixed_direction_label.visible or not mob._fixed_direction.visible or not mob._fixed_frame_label.visible or not mob._fixed_frame.visible:
		_fail("Fixed Mob controls must restore both fixed-pose rows")
		return
	npc._fixed_direction_label = Label.new()
	npc._fixed_direction = _option("S")
	npc._fixed_frame_label = Label.new()
	npc._fixed_frame = _option(1)
	npc._set_fixed_pose_rows_visible(false)
	if npc._fixed_direction_label.visible or npc._fixed_direction.visible or npc._fixed_frame_label.visible or npc._fixed_frame.visible:
		_fail("Actor Pose NPC controls must hide both fixed-pose labels and selectors")
		return
	npc._set_fixed_pose_rows_visible(true)
	if not npc._fixed_direction_label.visible or not npc._fixed_direction.visible or not npc._fixed_frame_label.visible or not npc._fixed_frame.visible:
		_fail("Fixed NPC controls must restore both fixed-pose rows")


func _option(metadata: Variant) -> OptionButton:
	var option := OptionButton.new()
	option.add_item(str(metadata))
	option.set_item_metadata(0, metadata)
	option.select(0)
	return option


func _direction_option(selected_metadata: String) -> OptionButton:
	var option := OptionButton.new()
	for metadata in ["south", "west", "east", "north"]:
		option.add_item(metadata.capitalize())
		option.set_item_metadata(option.item_count - 1, metadata)
		if metadata == selected_metadata:
			option.select(option.item_count - 1)
	return option


func _selected_metadata(control: OptionButton) -> String:
	if control == null or control.selected < 0:
		return ""
	return str(control.get_item_metadata(control.selected))


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
