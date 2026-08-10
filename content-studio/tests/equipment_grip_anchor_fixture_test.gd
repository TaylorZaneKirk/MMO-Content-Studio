extends SceneTree

const PaperDollPreview = preload("res://scripts/paper_doll_preview.gd")

var _drag_result: Array = []


func _initialize() -> void:
	call_deferred("_run_fixture")


func _run_fixture() -> void:
	var preview := PaperDollPreview.new()
	var stage := Control.new()
	stage.size = PaperDollPreview.STAGE_SIZE
	root.add_child(stage)
	var status := Label.new()
	preview.bind(stage, status)
	preview.game_client_assets_root = _create_fixture_assets()
	preview.configure_rig_catalog(_rig_catalog())

	var socket_visual := _socket_visual()
	var presentation_state := preview.update(true, "right_hand", "axe", "W", 1, ["right_hand"], socket_visual)
	_expect(str(presentation_state.get("resolved_asset_path", "")).ends_with("axe-F3-W.png"), "Ordinary preview must retain its same-direction frame fallback")

	var unavailable_state := preview.update(true, "right_hand", "axe", "W", 1, ["right_hand"], socket_visual, true)
	_expect(not bool(unavailable_state.get("selected_item_pose_available", true)), "Grip-anchor authoring must reject fallback item art")
	_expect(not preview._socket_marker.visible and not preview._grip_marker.visible, "Unavailable exact item art must hide attachment markers")
	_expect(str(unavailable_state.get("status", "")).contains("Item art unavailable for W/F1"), "Unavailable exact item art must identify its direction and frame")

	_write_fixture_png(preview.game_client_assets_root.path_join("actors/player/right_hand/axe-F1-W.png"), Vector2i(20, 12))
	preview.clear_cache()
	var exact_state := preview.update(true, "right_hand", "axe", "W", 1, ["right_hand"], socket_visual, true)
	_expect(bool(exact_state.get("selected_item_pose_available", false)), "Exact selected item art must enable grip calibration")
	_expect(preview._socket_marker.visible and preview._grip_marker.visible, "Exact socket-bound item art must expose read-only socket and draggable grip markers")

	_drag_result.clear()
	preview.grip_anchor_changed.connect(_on_grip_anchor_changed)
	var selected_rect := preview._selected_layer_rect()
	var start_mouse := selected_rect.position + (selected_rect.size * 0.5)
	preview._begin_drag(start_mouse)
	preview._apply_drag_position(start_mouse + Vector2(-1000, 1000))
	_expect(_drag_result == ["W", 1, 19, 0], "Grip-anchor canvas dragging must clamp to exact item-art source bounds")
	preview._end_drag()

	var flipped_visual := socket_visual.duplicate(true)
	flipped_visual["flip_x"] = {"W": {"1": true}}
	preview.update(true, "right_hand", "axe", "W", 1, ["right_hand"], flipped_visual, true)
	_expect(preview.resolve_effective_grip_anchor(Vector2i(16, 6), 20, true) == Vector2i(3, 6), "Flip must mirror effective grip X without changing the stored anchor")

	var hidden_visual := socket_visual.duplicate(true)
	hidden_visual["hidden_poses"] = {"W": {"1": true}}
	var hidden_state := preview.update(true, "right_hand", "axe", "W", 1, ["right_hand"], hidden_visual, true)
	_expect(bool(hidden_state.get("selected_item_pose_hidden", false)), "Hidden pose state must be reported to the item editor")
	_expect(not preview._socket_marker.visible and not preview._grip_marker.visible, "Hidden socket pose must not expose calibration markers")

	preview.update(true, "right_hand", "axe", "W", 1, ["right_hand"], _rig_layer_visual())
	_expect(not preview._socket_marker.visible and not preview._grip_marker.visible, "Rig-layer preview must not fabricate grip-anchor markers")

	stage.free()
	status.free()
	print("[equipment-grip-anchor-fixture] passed")
	quit(0)


func _expect(condition: bool, message: String) -> void:
	if condition:
		return
	push_error(message)
	quit(1)


func _on_grip_anchor_changed(direction: String, frame: int, x: int, y: int) -> void:
	_drag_result = [direction, frame, x, y]


func _create_fixture_assets() -> String:
	var root := ProjectSettings.globalize_path("user://equipment-grip-anchor-fixture")
	DirAccess.make_dir_recursive_absolute(root.path_join("actors/player/right_hand"))
	DirAccess.remove_absolute(root.path_join("actors/player/right_hand/axe-F1-W.png"))
	_write_fixture_png(root.path_join("actors/player/right_hand/axe-F3-W.png"), Vector2i(20, 12))
	return root


func _write_fixture_png(path: String, size: Vector2i) -> void:
	var image := Image.create(size.x, size.y, false, Image.FORMAT_RGBA8)
	image.fill(Color(1, 0.6, 0.2, 1))
	image.save_png(path)


func _rig_catalog() -> Dictionary:
	return {
		"available": true,
		"source_path": "/tmp/catalog_v1.json",
		"rigs": [{
			"rig_id": "humanoid_v1",
			"layers": [{
				"layer_id": "right_hand",
				"binding_type": "rig_layer",
				"default_render_plane": "front",
				"z_index_by_direction": {"N": 40, "E": 40, "S": 40, "W": 40},
			}],
			"sockets": [{
				"socket_id": "right_hand_primary",
				"positions": {
					"N": {"1": {"x": 16, "y": 16}},
					"E": {"1": {"x": 16, "y": 16}},
					"S": {"1": {"x": 16, "y": 16}},
					"W": {"1": {"x": 16, "y": 16}},
				},
			}],
		}],
	}


func _socket_visual() -> Dictionary:
	return {
		"asset_key": "axe",
		"rig_id": "humanoid_v1",
		"binding_type": "socket",
		"render_layer_id": "right_hand",
		"socket_id": "right_hand_primary",
		"secondary_socket_id": null,
		"nudge": {"x": 0, "y": 0},
		"grip_anchors": {"W": {"1": {"x": 16, "y": 6}}},
	}


func _rig_layer_visual() -> Dictionary:
	return {
		"asset_key": "axe",
		"rig_id": "humanoid_v1",
		"binding_type": "rig_layer",
		"render_layer_id": "right_hand",
		"socket_id": null,
		"secondary_socket_id": null,
		"nudge": {"x": 0, "y": 0},
		"grip_anchors": {},
	}
