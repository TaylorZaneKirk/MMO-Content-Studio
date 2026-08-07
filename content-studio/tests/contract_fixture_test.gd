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

	var drag_result := []
	preview.grip_anchor_changed.connect(func(direction: String, frame: int, x: int, y: int) -> void:
		drag_result.clear()
		drag_result.append_array([direction, frame, x, y])
	)
	preview._current_pose_context = {
		"direction": "E",
		"frame": 2,
		"layer_position": Vector2.ZERO,
		"preview_scale": 1.0,
		"texture_size": Vector2(80, 60),
	}
	preview._apply_drag_position(Vector2(12.4, 18.6))
	if drag_result.size() != 4 or drag_result[0] != "E" or drag_result[1] != 2 or drag_result[2] != 12 or drag_result[3] != 19:
		_fail("Actor attachment drag fixture mismatch")
		return

	await _verify_item_editor_rig_catalog_behavior(main_scene)
	await _verify_paper_doll_preview_layers()
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

	items._on_options_received({
		"actor_rig_catalog": {
			"available": false,
			"source_path": "/tmp/missing/catalog_v1.json",
			"message": "The canonical actor rig catalog is unavailable at the configured path.",
			"rigs": [],
		},
	})
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

	items._on_options_received({
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
					],
					"sockets": [
						{
							"socket_id": "right_hand_primary",
							"positions": {
								"N": {"1": {"x": 32, "y": 32}, "2": {"x": 32, "y": 32}, "3": {"x": 32, "y": 32}, "4": {"x": 32, "y": 32}},
								"E": {"1": {"x": 32, "y": 32}, "2": {"x": 32, "y": 32}, "3": {"x": 32, "y": 32}, "4": {"x": 32, "y": 32}},
								"S": {"1": {"x": 32, "y": 32}, "2": {"x": 32, "y": 32}, "3": {"x": 32, "y": 32}, "4": {"x": 32, "y": 32}},
								"W": {"1": {"x": 32, "y": 32}, "2": {"x": 32, "y": 32}, "3": {"x": 32, "y": 32}, "4": {"x": 32, "y": 32}},
							},
						},
					],
				},
			],
		},
	})
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


func _verify_paper_doll_preview_layers() -> void:
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
	preview.configure_rig_catalog({
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
	})
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
	var image := Image.create(8, 8, false, Image.FORMAT_RGBA8)
	image.fill(color)
	image.save_png(path)


func _fail(message: String) -> void:
	push_error(message)
	quit(1)
