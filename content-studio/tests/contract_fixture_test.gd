extends SceneTree

const EXPECTED_API_VERSION := "1"
const AuthoringWorkspaceSupport = preload("res://scripts/authoring_workspace_support.gd")
const PaperDollPreview = preload("res://scripts/paper_doll_preview.gd")


func _initialize() -> void:
	call_deferred("_run_fixture")


func _run_fixture() -> void:
	var main_scene := load("res://scenes/Main.tscn") as PackedScene
	if main_scene == null:
		_fail("T3A main scene or one of its scripts failed to parse")
		return

	var workspace_support := AuthoringWorkspaceSupport.new()
	var apply_button := Button.new()
	workspace_support.accept_preview(
		"save_draft",
		"fixture-signature",
		true,
		apply_button,
		"Apply: Save Draft"
	)
	if not workspace_support.can_apply("save_draft", "fixture-signature"):
		_fail("Shared workspace preview gate fixture mismatch")
		return
	if workspace_support.operation_name("save_draft") != "Save Draft":
		_fail("Shared workspace operation-name fixture mismatch")
		return

	var envelope := {
		"api_version": "1",
		"request_id": "fixture",
		"success": true,
		"data": {
			"target_operation": "save_draft",
			"valid_for_draft": true,
			"valid_for_publication": true,
			"messages": [],
			"changes": [
				{"field": "equippable", "before": "True", "after": "False"},
			],
		},
		"errors": [],
	}

	if envelope.api_version != EXPECTED_API_VERSION:
		_fail("API version fixture mismatch")
		return

	if not envelope.data.valid_for_draft or envelope.data.changes.size() != 1:
		_fail("T3A equipment-preview fixture mismatch")
		return

	var preview := PaperDollPreview.new()
	var offset := preview.resolve_source_pixel_offset(Vector2i(80, 60), Vector2i(30, 20), Vector2i(1, -2))
	if offset != Vector2(51, 38):
		_fail("Actor attachment offset fixture mismatch")
		return

	await _verify_item_editor_rig_catalog_behavior(main_scene)
	await _verify_item_editor_default_initialization(main_scene)
	await _verify_paper_doll_preview_layers()
	await _verify_paper_doll_preview_interactions()
	await _verify_live_runtime_catalog_if_configured()

	print("[content-studio-contract-fixture] passed")
	quit(0)


func _verify_item_editor_rig_catalog_behavior(main_scene: PackedScene) -> void:
	var scene := main_scene.instantiate()
	root.add_child(scene)
	await process_frame

	var items := scene.get_node("Margin/Root/Tabs/Items")
	if items == null:
		_fail("Unified item editor fixture could not locate the Items workspace")
		return

	items._on_options_received(_missing_rig_catalog())
	items._equipable.button_pressed = true
	items._appearance_enabled.button_pressed = true
	items._update_contextual_sections()
	if items._appearance_rig.item_count != 0:
		_fail("Unavailable actor rig catalog must not fabricate humanoid_v1")
		return
	if not items._appearance_rig.disabled or not items._appearance_binding.disabled:
		_fail("Unavailable actor rig catalog must disable rig-dependent attachment editing")
		return
	if not items._appearance_rig_status.text.contains("unavailable") or not items._appearance_rig_status.text.contains("/tmp/missing/catalog_v1.json"):
		_fail("Unavailable actor rig catalog must report the real diagnostic path")
		return

	items._on_options_received(_available_rig_catalog())
	items._update_contextual_sections()
	if items._appearance_rig.item_count != 1:
		_fail("Available actor rig catalog should populate humanoid_v1")
		return
	if str(items._appearance_rig.get_item_metadata(0)) != "humanoid_v1":
		_fail("Available actor rig catalog should keep humanoid_v1 as the first rig")
		return
	if not items._appearance_rig_status.text.contains("Loaded humanoid_v1") or not items._appearance_rig_status.text.contains("/tmp/catalog_v1.json"):
		_fail("Available actor rig catalog should report the loaded rig and source path")
		return

	scene.queue_free()
	await process_frame


func _verify_item_editor_default_initialization(main_scene: PackedScene) -> void:
	var scene := main_scene.instantiate()
	root.add_child(scene)
	await process_frame

	var items := scene.get_node("Margin/Root/Tabs/Items")
	if items == null:
		_fail("Unified item editor fixture could not locate the Items workspace for defaults")
		return

	items._on_options_received(_available_rig_catalog())
	items._start_new()
	items._display_name.text = "Axe"
	items._equipable.button_pressed = true
	items._select_option(items._equipment_slot, "right_hand")
	items._update_contextual_sections()
	items._update_paper_doll_preview()
	if items._appearance_asset_path.text != "Legacy preview visual: axe":
		_fail("Legacy preview status should expose the normalized visual key before authored metadata is enabled")
		return
	items._appearance_enabled.button_pressed = true
	items._on_appearance_enabled_toggled(true)
	if items._selected_metadata(items._appearance_rig) != "humanoid_v1":
		_fail("New right-hand authored appearance should default to humanoid_v1")
		return
	if items._selected_metadata(items._appearance_binding) != "socket":
		_fail("New right-hand authored appearance should default to socket binding")
		return
	if items._selected_metadata(items._appearance_render_layer) != "right_hand":
		_fail("New right-hand authored appearance should default render_layer_id to right_hand")
		return
	if items._selected_metadata(items._appearance_socket) != "right_hand_primary":
		_fail("New right-hand authored appearance should default socket_id to right_hand_primary")
		return
	if items._appearance_asset_key.text != "axe":
		_fail("New right-hand authored appearance should default asset_key to axe")
		return
	if int(items._appearance_nudge_x.value) != 0 or int(items._appearance_nudge_y.value) != 0:
		_fail("New authored appearance should initialize nudge to 0,0")
		return

	items._start_new()
	items._display_name.text = "Plate Armor"
	items._equipable.button_pressed = true
	items._select_option(items._equipment_slot, "body")
	items._update_contextual_sections()
	items._appearance_enabled.button_pressed = true
	items._on_appearance_enabled_toggled(true)
	if items._selected_metadata(items._appearance_binding) != "rig_layer":
		_fail("Non-hand authored appearance should remain rig_layer by default")
		return
	if items._selected_metadata(items._appearance_render_layer) != "body":
		_fail("Non-hand authored appearance should default render_layer_id to the gameplay slot")
		return
	if items._appearance_asset_key.text != "plate_armor":
		_fail("Non-hand authored appearance should still derive the normalized asset key")
		return

	items._start_new()
	items._display_name.text = "Axe"
	items._equipable.button_pressed = true
	items._select_option(items._equipment_slot, "right_hand")
	items._update_contextual_sections()
	items._apply_equipped_visual({
		"asset_key": "persisted_axe",
		"rig_id": "humanoid_v1",
		"binding_type": "rig_layer",
		"render_layer_id": "body",
		"socket_id": null,
		"nudge": {"x": 3, "y": -2},
		"grip_anchors": {},
	})
	items._on_appearance_enabled_toggled(true)
	if items._appearance_asset_key.text != "persisted_axe":
		_fail("Persisted equipped visual metadata must win over right-hand initialization defaults")
		return
	if items._selected_metadata(items._appearance_binding) != "rig_layer":
		_fail("Persisted equipped visual binding must not be overwritten by defaults")
		return
	if items._selected_metadata(items._appearance_render_layer) != "body":
		_fail("Persisted equipped visual render layer must not be overwritten by defaults")
		return

	items._paper_doll_preview._drag_state = {"active": true}
	items._on_visual_preview_changed()
	if bool(items._paper_doll_preview._drag_state.get("active", false)):
		_fail("Changing pose should cancel stale paper-doll drag state")
		return
	items._paper_doll_preview._drag_state = {"active": true}
	items._on_appearance_binding_changed()
	if bool(items._paper_doll_preview._drag_state.get("active", false)):
		_fail("Changing binding should cancel stale paper-doll drag state")
		return
	items._paper_doll_preview._drag_state = {"active": true}
	items._start_new()
	if bool(items._paper_doll_preview._drag_state.get("active", false)):
		_fail("Changing item/new item should cancel stale paper-doll drag state")
		return

	scene.queue_free()
	await process_frame


func _verify_paper_doll_preview_layers() -> void:
	var preview_fixture := _build_preview_fixture()
	var preview: PaperDollPreview = preview_fixture.preview
	var stage: Control = preview_fixture.stage
	var status: Label = preview_fixture.status
	var preview_state := preview.update(
		true,
		"right_hand",
		"dark_sword",
		"N",
		1,
		["head", "body", "legs", "right_hand"]
	)
	if str(preview_state.get("resolved_asset_path", "")).is_empty():
		stage.free()
		status.free()
		_fail("Paper doll preview fixture should resolve the selected equipment asset path")
		return
	if preview._layers.is_empty() or not (preview._layers.get("head", null) as TextureRect).visible:
		stage.free()
		status.free()
		_fail("Paper doll preview fixture should populate default humanoid layers")
		return
	stage.free()
	status.free()


func _verify_paper_doll_preview_interactions() -> void:
	var preview_fixture := _build_preview_fixture()
	var preview: PaperDollPreview = preview_fixture.preview
	var stage: Control = preview_fixture.stage
	var status: Label = preview_fixture.status
	var visible_slots := ["head", "body", "legs", "right_hand"]
	var emitted: Array = []
	preview.grip_anchor_changed.connect(func(direction: String, frame: int, x: int, y: int) -> void:
		emitted.clear()
		emitted.append_array([direction, frame, x, y])
	)

	preview.update(true, "right_hand", "axe", "N", 1, visible_slots)
	preview._begin_drag(Vector2(90, 90))
	if bool(preview._drag_state.get("active", false)):
		_fail("Legacy preview must not begin a grip drag")
		return
	if preview._socket_marker.visible or preview._grip_marker.visible:
		_fail("Legacy preview must not show socket or grip markers")
		return

	preview.update(true, "right_hand", "axe", "N", 1, visible_slots, _make_rig_layer_visual())
	preview._begin_drag(Vector2(90, 90))
	if bool(preview._drag_state.get("active", false)):
		_fail("Rig-layer preview must not begin a grip drag")
		return
	if preview._socket_marker.visible or preview._grip_marker.visible:
		_fail("Rig-layer preview must not show socket or grip markers")
		return

	preview.update(true, "right_hand", "axe", "N", 1, visible_slots, _make_socket_visual(Vector2i(16, 16), "missing_socket"))
	preview._begin_drag(Vector2(90, 90))
	if bool(preview._drag_state.get("active", false)):
		_fail("Invalid socket preview must not begin a grip drag")
		return
	if preview._socket_marker.visible or preview._grip_marker.visible:
		_fail("Invalid socket preview must not show socket or grip markers")
		return

	var valid_visual := _make_socket_visual(Vector2i(16, 16))
	preview.update(true, "right_hand", "axe", "N", 1, visible_slots, valid_visual)
	if not preview._socket_marker.visible or not preview._grip_marker.visible:
		_fail("Valid socket-bound preview must show socket and grip markers")
		return
	var selected_rect := preview._variant_to_rect2(preview._current_pose_context.get("selected_rect", Rect2()), Rect2())
	var preview_scale := float(preview._current_pose_context.get("preview_scale", 0.0))
	var start_mouse := selected_rect.position + (selected_rect.size * 0.5)
	preview._begin_drag(start_mouse)
	if not bool(preview._drag_state.get("active", false)):
		_fail("Valid socket-bound preview must begin dragging when the equipment sprite is grabbed")
		return

	preview._apply_drag_position(start_mouse + Vector2(5.0 * preview_scale, 0))
	if emitted.size() != 4 or emitted[2] != 11 or emitted[3] != 16:
		_fail("Dragging right by +5 source pixels must decrease grip X by 5")
		return
	preview._apply_drag_position(start_mouse + Vector2(-5.0 * preview_scale, 0))
	if emitted[2] != 21 or emitted[3] != 16:
		_fail("Dragging left by -5 source pixels must increase grip X by 5")
		return
	preview._apply_drag_position(start_mouse + Vector2(0, 5.0 * preview_scale))
	if emitted[2] != 16 or emitted[3] != 11:
		_fail("Dragging down by +5 source pixels must decrease grip Y by 5")
		return
	preview._apply_drag_position(start_mouse + Vector2(0, -5.0 * preview_scale))
	if emitted[2] != 16 or emitted[3] != 21:
		_fail("Dragging up by -5 source pixels must increase grip Y by 5")
		return
	preview._apply_drag_position(start_mouse + Vector2(4000.0 * preview_scale, 0))
	if emitted[2] != 0:
		_fail("Grip drag must clamp X within the texture bounds")
		return
	preview._apply_drag_position(start_mouse + Vector2(-4000.0 * preview_scale, -4000.0 * preview_scale))
	if emitted[2] != 31 or emitted[3] != 31:
		_fail("Grip drag must clamp both axes to the texture dimensions")
		return

	var source_before := preview._variant_to_vector2(preview._current_pose_context.get("layer_position_source", Vector2.ZERO), Vector2.ZERO)
	var redrawn_visual := _make_socket_visual(Vector2i(11, 16))
	preview.update(true, "right_hand", "axe", "N", 1, visible_slots, redrawn_visual)
	if not bool(preview._drag_state.get("active", false)):
		_fail("Preview redraw during an active drag must not cancel the drag")
		return
	var source_after := preview._variant_to_vector2(preview._current_pose_context.get("layer_position_source", Vector2.ZERO), Vector2.ZERO)
	if int(round(source_after.x - source_before.x)) != 5:
		_fail("Updating a socket-bound grip anchor must immediately change the selected equipment source_position")
		return
	preview._apply_drag_position(start_mouse + Vector2(10.0 * preview_scale, 0))
	if emitted[2] != 6:
		_fail("Active drag must survive redraws caused by grip-anchor updates")
		return

	preview._end_drag()
	if bool(preview._drag_state.get("active", false)):
		_fail("Mouse release/end drag must cancel the active grip drag")
		return
	preview.cancel_drag()
	if bool(preview._drag_state.get("active", false)):
		_fail("Explicit drag cancellation must clear drag state")
		return

	stage.free()
	status.free()


func _verify_live_runtime_catalog_if_configured() -> void:
	if not OS.has_environment("CONTENT_STUDIO_LIVE_OPTIONS_PATH"):
		return
	if not OS.has_environment("CONTENT_STUDIO_GAME_CLIENT_ASSETS"):
		_fail("Live runtime fixture requires CONTENT_STUDIO_GAME_CLIENT_ASSETS")
		return

	var options_path := OS.get_environment("CONTENT_STUDIO_LIVE_OPTIONS_PATH")
	if not FileAccess.file_exists(options_path):
		_fail("Live runtime fixture could not find the captured item options JSON")
		return

	var payload = JSON.parse_string(FileAccess.get_file_as_string(options_path))
	if not (payload is Dictionary):
		_fail("Live runtime fixture could not parse the captured item options JSON")
		return

	var data := (payload as Dictionary).get("data", {}) as Dictionary
	var catalog := data.get("actor_rig_catalog", {}) as Dictionary
	if not bool(catalog.get("available", false)):
		_fail("Live runtime fixture expected actor_rig_catalog.available = true")
		return
	if not str(catalog.get("source_path", "")).contains("catalog_v1.json"):
		_fail("Live runtime fixture expected the canonical rig catalog path")
		return

	var preview := PaperDollPreview.new()
	var stage := Control.new()
	stage.size = Vector2(180, 180)
	var status := Label.new()
	preview.bind(stage, status)
	preview.game_client_assets_root = OS.get_environment("CONTENT_STUDIO_GAME_CLIENT_ASSETS")
	preview.configure_rig_catalog(catalog)
	if not preview._rigs_by_id.has("humanoid_v1"):
		stage.free()
		status.free()
		_fail("Live runtime fixture expected PaperDollPreview to receive humanoid_v1")
		return

	var preview_state := preview.update(
		true,
		"right_hand",
		"gold_sword",
		"N",
		1,
		["head", "cape", "body", "legs", "boots", "gloves", "right_hand", "left_hand"]
	)
	if str(preview_state.get("resolved_asset_path", "")).is_empty():
		stage.free()
		status.free()
		_fail("Live runtime fixture expected a non-empty equipment preview asset path")
		return
	if preview._layers.is_empty() or not (preview._layers.get("head", null) as TextureRect).visible:
		stage.free()
		status.free()
		_fail("Live runtime fixture expected the humanoid head layer to render")
		return
	stage.free()
	status.free()


func _write_fixture_png(path: String, color: Color) -> void:
	var image := Image.create(32, 32, false, Image.FORMAT_RGBA8)
	image.fill(color)
	image.save_png(path)


func _build_preview_fixture() -> Dictionary:
	var preview := PaperDollPreview.new()
	var stage := Control.new()
	stage.size = Vector2(180, 180)
	var status := Label.new()
	preview.bind(stage, status)

	var temp_root := ProjectSettings.globalize_path("user://contract-fixture-paper-doll")
	DirAccess.make_dir_recursive_absolute(temp_root.path_join("actors/player/head"))
	DirAccess.make_dir_recursive_absolute(temp_root.path_join("actors/player/body"))
	DirAccess.make_dir_recursive_absolute(temp_root.path_join("actors/player/legs"))
	DirAccess.make_dir_recursive_absolute(temp_root.path_join("actors/player/right_hand"))
	_write_fixture_png(temp_root.path_join("actors/player/head/head1-F1-N.png"), Color(1, 0, 0, 1))
	_write_fixture_png(temp_root.path_join("actors/player/body/defbod-F1-N.png"), Color(0, 1, 0, 1))
	_write_fixture_png(temp_root.path_join("actors/player/legs/defbod-F1-N.png"), Color(0, 0, 1, 1))
	_write_fixture_png(temp_root.path_join("actors/player/right_hand/dark_sword-F1-N.png"), Color(1, 1, 0, 1))

	preview.game_client_assets_root = temp_root
	preview.configure_rig_catalog(_available_rig_catalog().actor_rig_catalog)
	return {
		"preview": preview,
		"stage": stage,
		"status": status,
	}


func _available_rig_catalog() -> Dictionary:
	return {
		"actor_rig_catalog": {
			"available": true,
			"source_path": "/tmp/catalog_v1.json",
			"message": null,
			"rigs": [
				{
					"rig_id": "humanoid_v1",
					"schema_version": 1,
					"layers": [
						{
							"layer_id": "head",
							"binding_type": "rig_layer",
							"default_render_plane": "front",
							"z_index_by_direction": {"N": 30, "E": 30, "S": 30, "W": 30},
						},
						{
							"layer_id": "body",
							"binding_type": "rig_layer",
							"default_render_plane": "base",
							"z_index_by_direction": {"N": 20, "E": 20, "S": 20, "W": 20},
						},
						{
							"layer_id": "legs",
							"binding_type": "rig_layer",
							"default_render_plane": "base",
							"z_index_by_direction": {"N": 10, "E": 10, "S": 10, "W": 10},
						},
						{
							"layer_id": "right_hand",
							"binding_type": "rig_layer",
							"default_render_plane": "front",
							"z_index_by_direction": {"N": 40, "E": 40, "S": 40, "W": 40},
						},
					],
					"sockets": [
						{
							"socket_id": "right_hand_primary",
							"positions": {
								"N": {"1": {"x": 16, "y": 16}, "2": {"x": 16, "y": 16}, "3": {"x": 16, "y": 16}, "4": {"x": 16, "y": 16}},
								"E": {"1": {"x": 16, "y": 16}, "2": {"x": 16, "y": 16}, "3": {"x": 16, "y": 16}, "4": {"x": 16, "y": 16}},
								"S": {"1": {"x": 16, "y": 16}, "2": {"x": 16, "y": 16}, "3": {"x": 16, "y": 16}, "4": {"x": 16, "y": 16}},
								"W": {"1": {"x": 16, "y": 16}, "2": {"x": 16, "y": 16}, "3": {"x": 16, "y": 16}, "4": {"x": 16, "y": 16}},
							},
						},
					],
				},
			],
		},
	}


func _missing_rig_catalog() -> Dictionary:
	return {
		"actor_rig_catalog": {
			"available": false,
			"source_path": "/tmp/missing/catalog_v1.json",
			"message": "The canonical actor rig catalog is unavailable at the configured path.",
			"rigs": [],
		},
	}


func _make_socket_visual(anchor: Vector2i, socket_id: String = "right_hand_primary") -> Dictionary:
	return {
		"asset_key": "dark_sword",
		"rig_id": "humanoid_v1",
		"binding_type": "socket",
		"render_layer_id": "right_hand",
		"socket_id": socket_id,
		"secondary_socket_id": null,
		"nudge": {"x": 0, "y": 0},
		"grip_anchors": {
			"N": {
				"1": {"x": anchor.x, "y": anchor.y},
			},
		},
	}


func _make_rig_layer_visual() -> Dictionary:
	return {
		"asset_key": "dark_sword",
		"rig_id": "humanoid_v1",
		"binding_type": "rig_layer",
		"render_layer_id": "right_hand",
		"socket_id": null,
		"secondary_socket_id": null,
		"nudge": {"x": 0, "y": 0},
		"grip_anchors": {},
	}


func _fail(message: String) -> void:
	push_error(message)
	quit(1)
