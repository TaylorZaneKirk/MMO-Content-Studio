extends SceneTree

const EXPECTED_API_VERSION := "1"
const AuthoringWorkspaceSupport = preload("res://scripts/authoring_workspace_support.gd")
const AuthoringHttpTransport = preload("res://scripts/http_json_client.gd")
const PaperDollPreview = preload("res://scripts/paper_doll_preview.gd")
const RiggedSpritePreviewLayout = preload("res://scripts/rigged_sprite_preview_layout.gd")


class FixtureAuthoringHostClient extends AuthoringHostClient:
	var catalog_searches: Array = []
	var requested_item_ids: Array = []
	var preview_requests: Array = []
	var actor_calibration_requests: Array = []

	func search_item_catalog(search: String = "") -> void:
		catalog_searches.append(search)

	func load_item_definition(item_id: String) -> void:
		requested_item_ids.append(item_id)

	func preview_item_operation(item_id: String, payload: Dictionary) -> void:
		preview_requests.append({
			"item_id": item_id,
			"payload": payload.duplicate(true),
		})

	func _request(
		operation: String,
		path: String,
		method: int = HTTPClient.METHOD_GET,
		payload: Dictionary = {}
	) -> void:
		if operation.begins_with("actor_calibration"):
			actor_calibration_requests.append({
				"operation": operation,
				"path": path,
				"method": method,
				"payload": payload.duplicate(true),
			})


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
	await _verify_existing_item_icon_preservation(main_scene)
	await _verify_grip_anchor_payload_normalization(main_scene)
	await _verify_per_pose_flip_payload_and_preview_math(main_scene)
	await _verify_post_save_reload_uses_fresh_item_version(main_scene)
	await _verify_mob_preview_preserves_unsaved_rigged_state(main_scene)
	_verify_non_json_transport_failure()
	_verify_mutation_transport_timeout_policy()
	await _verify_paper_doll_preview_layers()
	await _verify_paper_doll_preview_interactions()
	await _verify_paper_doll_preview_camera_and_zoom()
	await _verify_paper_doll_foreground_overlays()
	_verify_rigged_sprite_preview_layout()
	_verify_actor_calibration_host_client_operations()
	await _verify_live_runtime_catalog_if_configured()

	print("[content-studio-contract-fixture] passed")
	quit(0)


func _verify_rigged_sprite_preview_layout() -> void:
	var manifest := {
		"source_width": 128,
		"source_height": 160,
		"cosmetics": [
			{"item_id": "behind", "z_index": -1},
			{"item_id": "over_grip", "z_index": 11},
		],
		"foreground_overlays": [
			{"overlay_id": "right_hand_primary_grip", "z_index": 10, "source_rect": {"x": 24, "y": 104, "width": 24, "height": 20}},
		],
	}
	var draw_list := RiggedSpritePreviewLayout.build_draw_list(manifest)
	if draw_list.size() != 4 or str((draw_list[0] as Dictionary).get("id", "")) != "behind" or str((draw_list[1] as Dictionary).get("kind", "")) != "base" or str((draw_list[2] as Dictionary).get("kind", "")) != "overlay" or str((draw_list[3] as Dictionary).get("id", "")) != "over_grip":
		_fail("Rigged preview draw order must support behind-base, grip, and item-over-grip depth")
		return
	var scale := RiggedSpritePreviewLayout.fit_scale(Vector2(128, 160), Vector2(280, 220))
	if not is_equal_approx(scale, min(248.0 / 128.0, 188.0 / 160.0)):
		_fail("Rigged preview fit must use one uniform source-art scale")
		return
	var canvas := Vector2(128, 160)
	var pane := Vector2(280, 220)
	var transform := RiggedSpritePreviewLayout.preview_transform(canvas, pane)
	var origin := transform.get("origin", Vector2.ZERO) as Vector2
	if not RiggedSpritePreviewLayout.source_to_preview(Vector2.ZERO, canvas, pane).is_equal_approx(origin):
		_fail("Source origin must map to the preview origin")
		return
	var bottom_right := RiggedSpritePreviewLayout.source_to_preview(canvas, canvas, pane)
	if not bottom_right.is_equal_approx(origin + canvas * float(transform.get("scale", 1.0))):
		_fail("Source canvas bottom-right must map through the preview transform")
		return
	var source_point := Vector2(-12.25, 178.5)
	var round_trip := RiggedSpritePreviewLayout.preview_to_source(
		RiggedSpritePreviewLayout.source_to_preview(source_point, canvas, pane, 24.0),
		canvas,
		pane,
		24.0)
	if not round_trip.is_equal_approx(source_point):
		_fail("Preview/source conversion must be inverse across padded aspect ratios")
		return
	var portrait_round_trip := RiggedSpritePreviewLayout.preview_to_source(
		RiggedSpritePreviewLayout.source_to_preview(Vector2(127.75, -8.5), canvas, Vector2(180, 420), 12.0),
		canvas,
		Vector2(180, 420),
		12.0)
	if not portrait_round_trip.is_equal_approx(Vector2(127.75, -8.5)):
		_fail("Preview/source conversion must support multiple pane aspect ratios")
		return
	if RiggedSpritePreviewLayout.quantize_source_pixel(1.5) != 2 or RiggedSpritePreviewLayout.quantize_source_pixel(-1.5) != -2 or RiggedSpritePreviewLayout.quantize_source_pixel(-0.49) != 0 or RiggedSpritePreviewLayout.quantize_source_pixel(5.49) != 5:
		_fail("Source coordinate quantization must round deterministically without clamping")


func _verify_actor_calibration_host_client_operations() -> void:
	var client := FixtureAuthoringHostClient.new()
	client.load_actor_calibration("orc_v1")
	client.save_actor_calibration("orc_v1", {"expected_catalog_hash": "hash"})
	client.load_actor_calibration_frames({"actor_kind": "mob", "visual_texture_path": "res://assets/maps/objects/mobs/orc.png"})
	if client.actor_calibration_requests.size() != 3:
		_fail("Actor calibration host-client operations must issue their narrow requests")
		return
	var load_request := client.actor_calibration_requests[0] as Dictionary
	var save_request := client.actor_calibration_requests[1] as Dictionary
	var frames_request := client.actor_calibration_requests[2] as Dictionary
	if str(load_request.get("path", "")) != "/api/v1/actor-appearance/calibrations/orc_v1" or int(save_request.get("method", -1)) != HTTPClient.METHOD_PUT or str(frames_request.get("path", "")) != "/api/v1/actor-appearance/calibration-frames":
		_fail("Actor calibration host-client routes must preserve the API contract")


func _verify_mob_preview_preserves_unsaved_rigged_state(main_scene: PackedScene) -> void:
	var scene := main_scene.instantiate()
	root.add_child(scene)
	await process_frame
	var mobs = scene.get_node("Margin/Root/Tabs/Mobs")
	if mobs == null:
		_fail("Rigged mob fixture could not locate the Mob workspace")
		return
	mobs._on_mob_options_received(_mob_rigged_options())
	mobs._start_new_mob()
	mobs._load_composite_visual({
		"visual_mode": "composite_rig",
		"composite_visual": {
			"schema_version": 1,
			"rig_id": "humanoid_v1",
			"calibration_id": "orc_v1",
			"pose_policy": "fixed",
			"fixed_direction": "S",
			"fixed_frame": 1,
			"cosmetic_item_ids": {"right_hand": "inventory_154_axe"},
		},
	})
	mobs._on_mob_preview_received({
		"target_operation": "save_draft",
		"valid_for_draft": true,
		"valid_for_publication": true,
		"messages": [],
		"changes": [],
		"asset_preview_file_path": "",
		"preview_signature": "fixture",
		"rigged_sprite_preview": {"base_file_path": "", "source_width": 32, "source_height": 32, "direction": "S", "frame": 1, "cosmetics": [], "foreground_overlays": []},
	})
	var descriptor := mobs._composite_visual as Dictionary
	if mobs._selected_metadata(mobs._visual_mode) != "composite_rig" or str(descriptor.get("rig_id", "")) != "humanoid_v1" or str(descriptor.get("calibration_id", "")) != "orc_v1" or str((descriptor.get("cosmetic_item_ids", {}) as Dictionary).get("right_hand", "")) != "inventory_154_axe":
		_fail("Mob preview response must not replace unsaved rigged authoring state")
		return
	var payload: Dictionary = mobs._payload()
	var serialized: Dictionary = payload.get("composite_visual", {}) as Dictionary
	if str(serialized.get("rig_id", "")) != "humanoid_v1" or str(serialized.get("calibration_id", "")) != "orc_v1" or str((serialized.get("cosmetic_item_ids", {}) as Dictionary).get("right_hand", "")) != "inventory_154_axe":
		_fail("Mob payload must preserve the rigged descriptor after preview")
	scene.queue_free()
	await process_frame


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
	items._display_name.text = "Small Shield"
	items._equipable.button_pressed = true
	items._select_option(items._equipment_slot, "left_hand")
	items._update_contextual_sections()
	items._appearance_enabled.button_pressed = true
	items._on_appearance_enabled_toggled(true)
	if items._selected_metadata(items._appearance_binding) != "socket":
		_fail("New left-hand authored appearance should default to socket binding")
		return
	if items._selected_metadata(items._appearance_render_layer) != "left_hand":
		_fail("New left-hand authored appearance should default render_layer_id to left_hand")
		return
	if items._selected_metadata(items._appearance_socket) != "left_hand_primary":
		_fail("New left-hand authored appearance should default socket_id to left_hand_primary")
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

	items._paper_doll_preview.set_fit_zoom_percent(200)
	items._refresh_preview_zoom_controls()
	if items._appearance_zoom_label.text != "Zoom 200%":
		_fail("Preview zoom label should reflect transient fit zoom changes")
		return
	items._paper_doll_preview._drag_state = {"active": true}
	items._on_visual_preview_changed()
	if bool(items._paper_doll_preview._drag_state.get("active", false)):
		_fail("Changing pose should cancel stale paper-doll drag state")
		return
	if items._paper_doll_preview.get_fit_zoom_percent() != 200:
		_fail("Changing pose should not corrupt the preview zoom state")
		return
	items._paper_doll_preview._drag_state = {"active": true}
	items._on_appearance_binding_changed()
	if bool(items._paper_doll_preview._drag_state.get("active", false)):
		_fail("Changing binding should cancel stale paper-doll drag state")
		return
	if items._paper_doll_preview.get_fit_zoom_percent() != 200:
		_fail("Changing binding should not corrupt the preview zoom state")
		return
	items._paper_doll_preview._drag_state = {"active": true}
	items._start_new()
	if bool(items._paper_doll_preview._drag_state.get("active", false)):
		_fail("Changing item/new item should cancel stale paper-doll drag state")
		return
	if items._paper_doll_preview.get_fit_zoom_percent() != 200:
		_fail("Changing item/new item should not corrupt the preview zoom state")
		return

	scene.queue_free()
	await process_frame


func _verify_existing_item_icon_preservation(main_scene: PackedScene) -> void:
	var scene := main_scene.instantiate()
	root.add_child(scene)
	await process_frame

	var items := scene.get_node("Margin/Root/Tabs/Items")
	if items == null:
		_fail("Unified item editor fixture could not locate the Items workspace for icon preservation")
		return

	items._on_options_received(_available_rig_catalog())
	items._on_assets_received({"assets": []})
	items._on_definition_received({
		"item_id": "inventory_154_axe",
		"display_name": "Axe",
		"icon_texture_path": "res://assets/items/Inventory_154_Axe.png",
		"publication_state": "Draft",
		"classification_label": "Equipment",
		"authoring_kind": "Unified",
		"updated_at_utc": "2026-08-07T00:00:00+00:00",
		"equipment": {
			"equipment_slot_id": "right_hand",
			"required_strength": 1,
			"requirements": [],
			"skill_modifiers": [],
			"combat_bonuses": {},
			"weapon_profile": null,
			"equipped_visual": null,
		},
		"consumable_behavior": null,
		"tool_capabilities": [],
	})
	items._appearance_enabled.button_pressed = true
	items._on_appearance_enabled_toggled(true)
	var payload: Dictionary = items._payload()
	if str(payload.get("icon_texture_path", "")) != "res://assets/items/Inventory_154_Axe.png":
		_fail("Appearance-only editing must retain an unavailable persisted icon path")
		return

	scene.queue_free()
	await process_frame


func _verify_grip_anchor_payload_normalization(main_scene: PackedScene) -> void:
	var scene := main_scene.instantiate()
	root.add_child(scene)
	await process_frame


	var items := scene.get_node("Margin/Root/Tabs/Items")
	if items == null:
		_fail("Unified item editor fixture could not locate the Items workspace for grip-anchor normalization")
		return

	items._on_options_received(_available_rig_catalog())
	items._start_new()
	items._equipable.button_pressed = true
	items._select_option(items._equipment_slot, "right_hand")
	items._update_contextual_sections()
	items._appearance_enabled.button_pressed = true
	items._on_appearance_enabled_toggled(true)
	items._equipped_visual_grip_anchors["E"] = {
		"1": {"x": 5000000000, "y": -5000000000},
	}
	var payload: Dictionary = items._payload()
	var equipment := payload.get("equipment", {}) as Dictionary
	var equipped_visual := equipment.get("equipped_visual", {}) as Dictionary
	var grip_anchors := equipped_visual.get("grip_anchors", {}) as Dictionary
	var east := grip_anchors.get("E", {}) as Dictionary
	var anchor := east.get("1", {}) as Dictionary
	if int(anchor.get("x", 0)) != 4096 or int(anchor.get("y", 0)) != -4096:
		_fail("Grip-anchor payloads must clamp invalid transient values to the attachment contract")
		return

	scene.queue_free()
	await process_frame


func _verify_per_pose_flip_payload_and_preview_math(main_scene: PackedScene) -> void:
	var scene := main_scene.instantiate()
	root.add_child(scene)
	await process_frame

	var items := scene.get_node("Margin/Root/Tabs/Items")
	if items == null:
		_fail("Unified item editor fixture could not locate the Items workspace for per-pose flip authoring")
		return

	items._on_options_received(_available_rig_catalog())
	items._start_new()
	items._equipable.button_pressed = true
	items._select_option(items._equipment_slot, "right_hand")
	items._update_contextual_sections()
	items._appearance_enabled.button_pressed = true
	items._on_appearance_enabled_toggled(true)
	items._select_option(items._preview_direction, "N")
	items._preview_frame.value = 1
	items._update_grip_pose_controls()
	items._appearance_flip_x.button_pressed = true
	items._on_appearance_flip_x_toggled(true)

	var socket_payload: Dictionary = (items._equipped_visual_payload() as Dictionary)
	var flip_x := socket_payload.get("flip_x", {}) as Dictionary
	if not bool((flip_x.get("N", {}) as Dictionary).get("1", false)):
		_fail("Flip horizontally must persist only the selected socket pose")
		return
	items._select_option(items._preview_direction, "E")
	items._update_grip_pose_controls()
	if items._appearance_flip_x.button_pressed:
		_fail("Changing pose must restore the selected pose flip state")
		return

	var preview := PaperDollPreview.new()
	if preview.resolve_effective_grip_anchor(Vector2i(-24, 72), 80, true) != Vector2i(103, 72):
		_fail("Studio socket flip math must preserve signed virtual anchors")
		return
	if preview.resolve_effective_grip_anchor(Vector2i(73, 43), 80, false) != Vector2i(73, 43):
		_fail("Missing flip metadata must retain the authored socket anchor")
		return
	var dragged_anchor: Array = []
	preview.grip_anchor_changed.connect(func(_direction: String, _frame: int, x: int, y: int) -> void:
		dragged_anchor = [x, y])
	preview._current_pose_context = {"preview_scale": 1.0}
	preview._drag_state = {
		"mouse_start_position": Vector2.ZERO,
		"start_grip_anchor": Vector2(10, 20),
		"texture_size": Vector2(80, 60),
		"flip_x": true,
		"direction": "N",
		"frame": 1,
	}
	preview._apply_drag_position_from_active_drag(Vector2(4, -3))
	if dragged_anchor != [14, 23]:
		_fail("Flipped socket dragging must update the authored anchor in the mirrored horizontal direction")
		return

	items._select_option(items._appearance_binding, "rig_layer")
	items._select_option(items._appearance_render_layer, "left_hand")
	items._select_option(items._preview_direction, "W")
	items._preview_frame.value = 4
	items._update_grip_pose_controls()
	items._appearance_flip_x.button_pressed = true
	items._on_appearance_flip_x_toggled(true)
	var shield_payload: Dictionary = (items._equipped_visual_payload() as Dictionary)
	var shield_flip_x := shield_payload.get("flip_x", {}) as Dictionary
	if not bool((shield_flip_x.get("W", {}) as Dictionary).get("4", false)):
		_fail("Rig-layer visuals must persist selected pose flip metadata without a socket anchor")
		return
	items._select_option(items._appearance_binding, "socket")
	items._on_appearance_binding_changed()
	items._appearance_visible_in_pose.button_pressed = false
	items._on_appearance_visible_in_pose_toggled(false)
	var hidden_payload: Dictionary = (items._equipped_visual_payload() as Dictionary)
	var hidden_poses := hidden_payload.get("hidden_poses", {}) as Dictionary
	if not bool((hidden_poses.get("W", {}) as Dictionary).get("4", false)):
		_fail("Visible in this pose must persist only explicitly hidden poses")
		return
	if items._appearance_grip_x.editable or not items._appearance_flip_x.disabled or not items._appearance_item_over_grip.disabled:
		_fail("Hidden poses must disable attachment, flip, and item-over-grip editing")
		return
	items._appearance_visible_in_pose.button_pressed = true
	items._on_appearance_visible_in_pose_toggled(true)
	if not items._appearance_grip_x.editable or items._appearance_flip_x.disabled or items._appearance_item_over_grip.disabled:
		_fail("Revealing a pose must restore normal pose editing")
		return
	items._appearance_item_over_grip.button_pressed = true
	items._on_appearance_item_over_grip_toggled(true)
	var item_over_grip_payload: Dictionary = (items._equipped_visual_payload() as Dictionary)
	var item_over_grip := item_over_grip_payload.get("item_over_grip", {}) as Dictionary
	if not bool((item_over_grip.get("W", {}) as Dictionary).get("4", false)):
		_fail("Render in front of hand must persist only the selected pose")
		return
	items._select_option(items._preview_direction, "N")
	items._update_grip_pose_controls()
	if items._appearance_item_over_grip.button_pressed:
		_fail("Changing pose must restore the selected pose item-over-grip state")
		return

	scene.queue_free()
	await process_frame


func _verify_post_save_reload_uses_fresh_item_version(main_scene: PackedScene) -> void:
	var scene := main_scene.instantiate()
	root.add_child(scene)
	await process_frame

	var items := scene.get_node("Margin/Root/Tabs/Items")
	if items == null:
		_fail("Unified item editor fixture could not locate the Items workspace for post-save concurrency")
		return

	items._on_options_received(_available_rig_catalog())
	items._on_assets_received({"assets": []})
	items._on_definition_received({
		"item_id": "inventory_154_axe",
		"display_name": "Axe",
		"icon_texture_path": "res://assets/items/Inventory_154_Axe.png",
		"publication_state": "Draft",
		"classification_label": "Equipment",
		"authoring_kind": "Unified",
		"updated_at_utc": "2026-08-07T00:00:00+00:00",
		"equipment": {
			"equipment_slot_id": "right_hand",
			"required_strength": 1,
			"requirements": [],
			"skill_modifiers": [],
			"combat_bonuses": {},
			"weapon_profile": null,
			"equipped_visual": null,
		},
		"consumable_behavior": null,
		"tool_capabilities": [],
	})
	var client := FixtureAuthoringHostClient.new()
	items._client = client
	var saved_item := (items._current_item as Dictionary).duplicate(true)
	saved_item["display_name"] = "Axe Mk II"
	saved_item["updated_at_utc"] = "2026-08-07T00:05:00+00:00"
	items._on_mutation_completed({
		"operation": "save_draft",
		"item": saved_item,
		"messages": [],
	})
	if client.catalog_searches.size() != 1:
		_fail("Successful item mutations should immediately request a catalog refresh")
		return
	if str(items._current_item.get("updated_at_utc", "")) != "2026-08-07T00:05:00+00:00":
		_fail("Successful item mutations should adopt the authoritative updated_at_utc before reload completes")
		return
	if str(items._payload().get("expected_updated_at_utc", "")) != "2026-08-07T00:05:00+00:00":
		_fail("Preview payloads should use the authoritative post-save updated_at_utc")
		return
	if not items._preview_button.disabled or not items._operation.disabled:
		_fail("Item preview controls must stay disabled while the authoritative reload is pending")
		return
	if items._updated.text != "2026-08-07T00:05:00+00:00":
		_fail("The item editor should surface the fresh post-save timestamp immediately")
		return

	items._on_definition_received(saved_item)
	if items._preview_button.disabled or items._operation.disabled:
		_fail("Item preview controls should re-enable after the authoritative reload completes")
		return
	items._select_option(items._operation, "publish")
	items._preview()
	if client.preview_requests.size() != 1:
		_fail("Preview publish should not require a manual refresh after a successful save")
		return
	var preview_request := client.preview_requests[0] as Dictionary
	var preview_payload := preview_request.get("payload", {}) as Dictionary
	if str(preview_request.get("item_id", "")) != "inventory_154_axe":
		_fail("Preview publish should continue using the current item after a successful save")
		return
	if str(preview_payload.get("target_operation", "")) != "publish":
		_fail("Post-save preview should still target publish when requested")
		return
	if str(preview_payload.get("expected_updated_at_utc", "")) != "2026-08-07T00:05:00+00:00":
		_fail("Immediate preview publish must use the fresh post-save updated_at_utc")
		return

	scene.queue_free()
	await process_frame


func _verify_non_json_transport_failure() -> void:
	var transport := AuthoringHttpTransport.new()
	var emitted: Array = []
	transport.request_failed.connect(func(operation: String, message: String, _errors: Array) -> void:
		emitted.append_array([operation, message])
	)
	transport._operation = "preview_item"
	transport._on_request_completed(
		HTTPRequest.RESULT_SUCCESS,
		400,
		PackedStringArray(),
		"Microsoft.AspNetCore.Http.BadHttpRequestException".to_utf8_buffer())
	if emitted.size() != 2 or emitted[0] != "preview_item" or not str(emitted[1]).contains("HTTP 400"):
		_fail("Non-JSON host failures must become a readable request failure")


func _verify_mutation_transport_timeout_policy() -> void:
	var transport := AuthoringHttpTransport.new()
	var preview_timeout := transport._timeout_seconds_for_operation("item_preview")
	var publish_timeout := transport._timeout_seconds_for_operation("item_publish")
	if publish_timeout <= preview_timeout:
		_fail("Mutation requests must allow more time than normal preview requests")
		return
	var timeout_message := transport._transport_failure_message("item_publish", HTTPRequest.RESULT_TIMEOUT)
	if not timeout_message.contains("reload before retrying"):
		_fail("Mutation timeout messaging must tell the user to reload before retrying")


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
	var selected_rect := preview._selected_layer_rect()
	var preview_scale := float(preview._current_pose_context.get("preview_scale", 0.0))
	var start_mouse := selected_rect.position + (selected_rect.size * 0.5)
	_stage_mouse_press(preview, selected_rect.position + selected_rect.size + Vector2(8, 8))
	if bool(preview._drag_state.get("active", false)):
		_fail("Clicking outside the rendered socket-bound layer must not begin dragging")
		return
	_stage_mouse_press(preview, start_mouse)
	if not bool(preview._drag_state.get("active", false)):
		_fail("Valid socket-bound preview must begin dragging when the equipment sprite is grabbed")
		return

	_stage_mouse_motion(preview, start_mouse + Vector2(5.0 * preview_scale, 0))
	if emitted.size() != 4 or emitted[2] != 11 or emitted[3] != 16:
		_fail("Dragging right by +5 source pixels must decrease grip X by 5")
		return
	_stage_mouse_motion(preview, start_mouse + Vector2(-5.0 * preview_scale, 0))
	if emitted[2] != 21 or emitted[3] != 16:
		_fail("Dragging left by -5 source pixels must increase grip X by 5")
		return
	_stage_mouse_motion(preview, start_mouse + Vector2(0, 5.0 * preview_scale))
	if emitted[2] != 16 or emitted[3] != 11:
		_fail("Dragging down by +5 source pixels must decrease grip Y by 5")
		return
	_stage_mouse_motion(preview, start_mouse + Vector2(0, -5.0 * preview_scale))
	if emitted[2] != 16 or emitted[3] != 21:
		_fail("Dragging up by -5 source pixels must increase grip Y by 5")
		return
	_stage_mouse_motion(preview, start_mouse + Vector2(4000.0 * preview_scale, 0))
	if emitted[2] != -3984:
		_fail("Grip drag must allow negative attachment anchors beyond the PNG bounds")
		return
	_stage_mouse_motion(preview, start_mouse + Vector2(-4000.0 * preview_scale, -4000.0 * preview_scale))
	if emitted[2] != 4016 or emitted[3] != 4016:
		_fail("Grip drag must allow positive attachment anchors beyond the PNG bounds")
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
	_stage_mouse_motion(preview, start_mouse + Vector2(10.0 * preview_scale, 0))
	if emitted[2] != 6:
		_fail("Active drag must survive redraws caused by grip-anchor updates")
		return

	_stage_mouse_release(preview, start_mouse + Vector2(10.0 * preview_scale, 0))
	if bool(preview._drag_state.get("active", false)):
		_fail("Mouse release/end drag must cancel the active grip drag")
		return

	var flipped_visual := _make_socket_visual(Vector2i(16, 16))
	flipped_visual["flip_x"] = {"N": {"1": true}}
	preview.update(true, "right_hand", "axe", "N", 1, visible_slots, flipped_visual)
	var flipped_rect := preview._selected_layer_rect()
	var flipped_scale := float(preview._current_pose_context.get("preview_scale", 0.0))
	var flipped_start_mouse := flipped_rect.position + (flipped_rect.size * 0.5)
	_stage_mouse_press(preview, flipped_start_mouse)
	if not bool(preview._drag_state.get("active", false)):
		_fail("Flipped socket-bound preview must begin dragging from the live rendered equipment layer")
		return
	_stage_mouse_motion(preview, flipped_start_mouse + Vector2(5.0 * flipped_scale, 0))
	if emitted.size() != 4 or emitted[2] != 21 or emitted[3] != 16:
		_fail("Dragging a flipped socket-bound preview must move its authored anchor in the mirrored horizontal direction")
		return
	var flipped_source_before := (preview._layers.get("right_hand", null) as TextureRect).position
	var updated_flipped_visual := _make_socket_visual(Vector2i(int(emitted[2]), int(emitted[3])))
	updated_flipped_visual["flip_x"] = {"N": {"1": true}}
	preview.update(true, "right_hand", "axe", "N", 1, visible_slots, updated_flipped_visual)
	var flipped_source_after := (preview._layers.get("right_hand", null) as TextureRect).position
	if flipped_source_after.x <= flipped_source_before.x:
		_fail("Dragging a flipped socket-bound preview right must move the rendered sprite right after refresh")
		return
	_stage_mouse_release(preview, flipped_start_mouse + Vector2(5.0 * flipped_scale, 0))
	preview.cancel_drag()
	if bool(preview._drag_state.get("active", false)):
		_fail("Explicit drag cancellation must clear drag state")
		return

	stage.free()
	status.free()


func _verify_paper_doll_preview_camera_and_zoom() -> void:
	var preview_fixture := _build_preview_fixture()
	var preview: PaperDollPreview = preview_fixture.preview
	var stage: Control = preview_fixture.stage
	var status: Label = preview_fixture.status
	var visible_slots := ["head", "body", "legs", "right_hand"]
	var valid_west_visual := _make_socket_visual(Vector2i(16, 16), "right_hand_primary", "W")
	var valid_east_visual := _make_socket_visual(Vector2i(16, 16), "right_hand_primary", "E")

	preview.reset_fit_view()
	preview.update(true, "right_hand", "axe", "W", 1, visible_slots, valid_west_visual)
	var body_layer := preview._layers.get("body", null) as TextureRect
	if body_layer == null:
		_fail("Preview camera fixture expected a visible body layer")
		return
	var west_body_position := body_layer.position
	var west_body_size := body_layer.size
	var west_view_state := preview._last_view_state
	var actor_bounds := preview._variant_to_rect2(west_view_state.get("actor_bounds_source", Rect2()), Rect2())
	var view_bounds := preview._variant_to_rect2(west_view_state.get("view_bounds_source", Rect2()), Rect2())
	var padding_source := float(west_view_state.get("padding_source", 0.0))
	if absf((actor_bounds.position.x - view_bounds.position.x) - padding_source) > 0.01:
		_fail("Stable edit view should keep the configured left-side source padding around the actor")
		return
	if absf(((view_bounds.position.x + view_bounds.size.x) - (actor_bounds.position.x + actor_bounds.size.x)) - padding_source) > 0.01:
		_fail("Stable edit view should keep the configured right-side source padding around the actor")
		return
	if absf((actor_bounds.position.y - view_bounds.position.y) - padding_source) > 0.01:
		_fail("Stable edit view should keep the configured top-side source padding around the actor")
		return
	if absf(((view_bounds.position.y + view_bounds.size.y) - (actor_bounds.position.y + actor_bounds.size.y)) - padding_source) > 0.01:
		_fail("Stable edit view should keep the configured bottom-side source padding around the actor")
		return
	if west_body_position.x <= 50.0:
		_fail("West-facing pose should begin with meaningful left-side edit margin")
		return

	var fit_scale := float(preview._current_pose_context.get("preview_scale", 0.0))
	var fit_selected_rect := preview._variant_to_rect2(preview._current_pose_context.get("selected_rect", Rect2()), Rect2())
	var fit_source_position := preview._variant_to_vector2(preview._current_pose_context.get("layer_position_source", Vector2.ZERO), Vector2.ZERO)
	var fit_socket_marker_position := preview._socket_marker.position
	var fit_grip_marker_position := preview._grip_marker.position
	if fit_socket_marker_position != fit_grip_marker_position:
		_fail("Socket and grip markers should stay coincident in the fitted edit view")
		return

	preview.update(true, "right_hand", "axe", "W", 1, visible_slots, _make_socket_visual(Vector2i(11, 16), "right_hand_primary", "W"))
	var west_body_position_after_drag := (preview._layers.get("body", null) as TextureRect).position
	if west_body_position_after_drag != west_body_position:
		_fail("Dragging selected equipment must not recenter the actor in the stable edit view")
		return

	preview.update(true, "right_hand", "axe", "E", 1, visible_slots, valid_east_visual)
	body_layer = preview._layers.get("body", null) as TextureRect
	var east_right_margin := stage.size.x - (body_layer.position.x + body_layer.size.x)
	if east_right_margin <= 50.0:
		_fail("East-facing pose should begin with meaningful right-side edit margin")
		return

	preview.reset_fit_view()
	preview.update(true, "right_hand", "axe", "W", 1, visible_slots, valid_west_visual)
	var deterministic_body_position := (preview._layers.get("body", null) as TextureRect).position
	var deterministic_scale := float(preview._current_pose_context.get("preview_scale", 0.0))
	preview.update(true, "right_hand", "axe", "W", 1, visible_slots, valid_west_visual)
	if (preview._layers.get("body", null) as TextureRect).position != deterministic_body_position or absf(float(preview._current_pose_context.get("preview_scale", 0.0)) - deterministic_scale) > 0.0001:
		_fail("Fit view should be deterministic for the same actor pose and selected equipment")
		return

	preview.set_fit_zoom_percent(200)
	preview.update(true, "right_hand", "axe", "W", 1, visible_slots, valid_west_visual)
	var zoomed_scale := float(preview._current_pose_context.get("preview_scale", 0.0))
	var zoomed_selected_rect := preview._variant_to_rect2(preview._current_pose_context.get("selected_rect", Rect2()), Rect2())
	if zoomed_scale <= fit_scale or zoomed_selected_rect.size.x <= fit_selected_rect.size.x:
		_fail("Increasing fit zoom should enlarge the rendered attachment in stage space")
		return
	if preview._variant_to_vector2(preview._current_pose_context.get("layer_position_source", Vector2.ZERO), Vector2.ZERO) != fit_source_position:
		_fail("Changing zoom must not change the authored source-space attachment position")
		return
	if preview._socket_marker.position != preview._grip_marker.position:
		_fail("Socket and grip markers should stay coincident at higher zoom levels")
		return

	var emitted: Array = []
	preview.grip_anchor_changed.connect(func(direction: String, frame: int, x: int, y: int) -> void:
		emitted.clear()
		emitted.append_array([direction, frame, x, y])
	, CONNECT_ONE_SHOT)
	var selected_rect := preview._variant_to_rect2(preview._current_pose_context.get("selected_rect", Rect2()), Rect2())
	var start_mouse := selected_rect.position + (selected_rect.size * 0.5)
	_stage_mouse_press(preview, start_mouse)
	_stage_mouse_motion(preview, start_mouse + Vector2(5.0 * zoomed_scale, 0))
	if emitted.size() != 4 or emitted[2] != 11 or emitted[3] != 16:
		_fail("Drag delta conversion must stay correct at higher zoom levels")
		return
	_stage_mouse_release(preview, start_mouse + Vector2(5.0 * zoomed_scale, 0))

	preview.set_fit_zoom_percent(50)
	preview.update(true, "right_hand", "axe", "N", 1, visible_slots, _make_socket_visual(Vector2i(16, 16)))
	var low_zoom_scale := float(preview._current_pose_context.get("preview_scale", 0.0))
	var low_zoom_selected_rect := preview._variant_to_rect2(preview._current_pose_context.get("selected_rect", Rect2()), Rect2())
	if low_zoom_scale >= fit_scale or low_zoom_selected_rect.size.x >= fit_selected_rect.size.x:
		_fail("Decreasing fit zoom should shrink the rendered attachment in stage space")
		return
	if preview._socket_marker.position != preview._grip_marker.position:
		_fail("Socket and grip markers should stay coincident at lower zoom levels")
		return

	emitted.clear()
	preview.grip_anchor_changed.connect(func(direction: String, frame: int, x: int, y: int) -> void:
		emitted.clear()
		emitted.append_array([direction, frame, x, y])
	, CONNECT_ONE_SHOT)
	selected_rect = preview._selected_layer_rect()
	start_mouse = selected_rect.position + (selected_rect.size * 0.5)
	_stage_mouse_press(preview, start_mouse)
	_stage_mouse_motion(preview, start_mouse + Vector2(5.0 * low_zoom_scale, 0))
	if emitted.size() != 4 or emitted[2] != 11 or emitted[3] != 16:
		_fail("Drag delta conversion must stay correct at lower zoom levels")
		return
	_stage_mouse_release(preview, start_mouse + Vector2(5.0 * low_zoom_scale, 0))

	preview.set_actual_scale_enabled(true)
	preview.update(true, "right_hand", "axe", "W", 1, visible_slots, valid_west_visual)
	var actual_scale := float(preview._current_pose_context.get("preview_scale", 0.0))
	if absf(actual_scale - PaperDollPreview.ACTUAL_GAME_SCALE) > 0.0001:
		_fail("Actual scale preset should preserve the canonical game render scale")
		return
	if preview._variant_to_vector2(preview._current_pose_context.get("layer_position_source", Vector2.ZERO), Vector2.ZERO) != fit_source_position:
		_fail("Actual scale preset must not change the authored source-space attachment position")
		return
	if preview._socket_marker.position != preview._grip_marker.position:
		_fail("Socket and grip markers should stay coincident in actual scale mode")
		return

	var actual_flipped_visual := _make_socket_visual(Vector2i(-20, 24), "right_hand_primary", "W")
	actual_flipped_visual["flip_x"] = {"W": {"1": true}}
	preview.update(true, "right_hand", "axe", "W", 1, visible_slots, actual_flipped_visual)
	var actual_scale_rect := preview._selected_layer_rect()
	var actual_scale_start_mouse := actual_scale_rect.position + (actual_scale_rect.size * 0.5)
	var actual_scale_layer_before := (preview._layers.get("right_hand", null) as TextureRect).position
	emitted.clear()
	preview.grip_anchor_changed.connect(func(direction: String, frame: int, x: int, y: int) -> void:
		emitted.clear()
		emitted.append_array([direction, frame, x, y])
	, CONNECT_ONE_SHOT)
	_stage_mouse_press(preview, actual_scale_rect.position + actual_scale_rect.size + Vector2(8, 8))
	if bool(preview._drag_state.get("active", false)):
		_fail("Clicking outside the actual-scale rendered layer must not begin dragging")
		return
	_stage_mouse_press(preview, actual_scale_start_mouse)
	if not bool(preview._drag_state.get("active", false)):
		_fail("Actual-scale flipped socket previews must begin dragging from a stage-local click on the rendered layer")
		return
	_stage_mouse_motion(preview, actual_scale_start_mouse + Vector2(5.0 * actual_scale, 0))
	if emitted.size() != 4 or emitted[2] != -15 or emitted[3] != 24:
		_fail("Actual-scale flipped socket dragging must preserve mirrored authored-anchor movement for virtual anchors")
		return
	var updated_actual_flipped_visual := _make_socket_visual(Vector2i(int(emitted[2]), int(emitted[3])), "right_hand_primary", "W")
	updated_actual_flipped_visual["flip_x"] = {"W": {"1": true}}
	preview.update(true, "right_hand", "axe", "W", 1, visible_slots, updated_actual_flipped_visual)
	var actual_scale_layer_after := (preview._layers.get("right_hand", null) as TextureRect).position
	if actual_scale_layer_after.x <= actual_scale_layer_before.x:
		_fail("Actual-scale flipped socket dragging must move the rendered sprite in the requested visual direction")
		return
	_stage_mouse_release(preview, actual_scale_start_mouse + Vector2(5.0 * actual_scale, 0))

	var axe_wall_visual := _make_socket_visual(Vector2i(71, 16), "right_hand_primary", "W", 4, "axe")
	preview.set_actual_scale_enabled(false)
	preview.reset_fit_view()
	preview.update(true, "right_hand", "axe", "W", 4, ["right_hand"], axe_wall_visual)
	var axe_selected_rect := preview._variant_to_rect2(preview._current_pose_context.get("selected_rect", Rect2()), Rect2())
	var axe_scale := float(preview._current_pose_context.get("preview_scale", 0.0))
	var axe_start_mouse := axe_selected_rect.position + (axe_selected_rect.size * 0.5)
	var axe_emitted: Array = []
	preview.grip_anchor_changed.connect(func(direction: String, frame: int, x: int, y: int) -> void:
		axe_emitted.clear()
		axe_emitted.append_array([direction, frame, x, y])
	, CONNECT_ONE_SHOT)
	preview._begin_drag(axe_start_mouse)
	preview._apply_drag_position(axe_start_mouse + Vector2(-9.0 * axe_scale, 0))
	if axe_emitted.size() != 4 or axe_emitted[2] != 80 or axe_emitted[3] != 16:
		_fail("West-facing 72px axe drag must continue past the previous width-1 wall at X=71")
		return
	preview._end_drag()

	preview.update(true, "right_hand", "axe", "W", 1, visible_slots, _make_socket_visual(Vector2i(-20, 24), "right_hand_primary", "W"))
	if preview._socket_marker.position != preview._grip_marker.position:
		_fail("Socket and grip markers should stay coincident for negative attachment anchors")
		return
	var negative_source_position := preview._variant_to_vector2(preview._current_pose_context.get("layer_position_source", Vector2.ZERO), Vector2.ZERO)
	preview.update(true, "right_hand", "axe", "W", 1, visible_slots, _make_socket_visual(Vector2i(-10, 24), "right_hand_primary", "W"))
	var shifted_negative_source_position := preview._variant_to_vector2(preview._current_pose_context.get("layer_position_source", Vector2.ZERO), Vector2.ZERO)
	if int(round(shifted_negative_source_position.x - negative_source_position.x)) != -10:
		_fail("Increasing the attachment anchor by +10 must move the visual -10 source pixels")
		return

	preview.update(true, "right_hand", "axe", "S", 1, visible_slots, _make_socket_visual(Vector2i(24, 100), "right_hand_primary", "S"))
	if preview._socket_marker.position != preview._grip_marker.position:
		_fail("Socket and grip markers should stay coincident for positive out-of-bounds Y anchors")
		return

	stage.free()
	status.free()


func _verify_paper_doll_foreground_overlays() -> void:
	var preview_fixture := _build_preview_fixture()
	var preview: PaperDollPreview = preview_fixture.preview
	var stage: Control = preview_fixture.stage
	var status: Label = preview_fixture.status
	var visible_slots := ["head", "body", "legs", "right_hand"]
	var socket_visual := _make_socket_visual(Vector2i(16, 16), "right_hand_primary", "N", 1)

	preview.update(true, "right_hand", "dark_sword", "N", 1, visible_slots, socket_visual)
	var foreground_overlay := preview._foreground_overlays.get("right_hand_primary_grip", null) as TextureRect
	var held_item := preview._layers.get("right_hand", null) as TextureRect
	if foreground_overlay == null or not foreground_overlay.visible:
		_fail("Valid socket-bound preview must render the canonical foreground grip overlay")
		return
	if foreground_overlay.z_index <= held_item.z_index:
		_fail("Foreground grip overlay must render above the held item")
		return
	var item_over_grip_visual := socket_visual.duplicate(true)
	item_over_grip_visual["item_over_grip"] = {"N": {"1": true}}
	preview.update(true, "right_hand", "dark_sword", "N", 1, visible_slots, item_over_grip_visual)
	foreground_overlay = preview._foreground_overlays.get("right_hand_primary_grip", null) as TextureRect
	held_item = preview._layers.get("right_hand", null) as TextureRect
	if held_item == null or foreground_overlay == null or held_item.z_index <= foreground_overlay.z_index:
		_fail("Item-over-grip preview poses must render above the applicable rig-owned grip overlay")
		return
	if foreground_overlay.mouse_filter != Control.MOUSE_FILTER_IGNORE:
		_fail("Foreground grip overlay must remain non-interactive")
		return
	if preview._layers.has("right_hand_primary_grip"):
		_fail("Foreground grip overlay must not be registered as an item-owned preview layer")
		return
	var overlay_texture := foreground_overlay.texture as AtlasTexture
	if overlay_texture == null or overlay_texture.region != Rect2(8, 8, 8, 8):
		_fail("Foreground grip overlay must preserve canonical source crop metadata")
		return
	var fit_overlay_position := foreground_overlay.position
	var fit_overlay_size := foreground_overlay.size
	var fit_anchor_position := preview._variant_to_vector2(preview._current_pose_context.get("layer_position_source", Vector2.ZERO), Vector2.ZERO)

	preview.set_fit_zoom_percent(200)
	preview.update(true, "right_hand", "dark_sword", "N", 1, visible_slots, socket_visual)
	foreground_overlay = preview._foreground_overlays.get("right_hand_primary_grip", null) as TextureRect
	if foreground_overlay.position == fit_overlay_position or foreground_overlay.size.x <= fit_overlay_size.x:
		_fail("Fit zoom must preserve foreground-overlay registration while scaling its stage transform")
		return
	if preview._variant_to_vector2(preview._current_pose_context.get("layer_position_source", Vector2.ZERO), Vector2.ZERO) != fit_anchor_position:
		_fail("Foreground overlay rendering must not change attachment anchor calculations")
		return
	preview.update(true, "right_hand", "dark_sword", "W", 1, visible_slots, _make_socket_visual(Vector2i(-20, 24), "right_hand_primary", "W"))
	foreground_overlay = preview._foreground_overlays.get("right_hand_primary_grip", null) as TextureRect
	if foreground_overlay == null or not foreground_overlay.visible or preview._socket_marker.position != preview._grip_marker.position:
		_fail("Foreground overlays must preserve signed virtual attachment-anchor registration")
		return

	preview.set_actual_scale_enabled(true)
	preview.update(true, "right_hand", "dark_sword", "N", 1, visible_slots, socket_visual)
	foreground_overlay = preview._foreground_overlays.get("right_hand_primary_grip", null) as TextureRect
	if foreground_overlay == null or not foreground_overlay.visible or absf(foreground_overlay.size.x - 2.0) > 0.0001:
		_fail("Actual scale must preserve foreground-overlay registration at canonical source-art scale")
		return

	var rig := (preview._rigs_by_id.get("humanoid_v1", {}) as Dictionary).duplicate(true)
	var overlays: Array = rig.get("foreground_overlays", []) as Array
	var optional_overlay := (overlays[0] as Dictionary).duplicate(true)
	var rectangles: Dictionary = (optional_overlay.get("source_rect_by_direction", {}) as Dictionary).duplicate(true)
	var north_rectangles: Dictionary = (rectangles.get("N", {}) as Dictionary).duplicate(true)
	north_rectangles["1"] = null
	rectangles["N"] = north_rectangles
	optional_overlay["source_rect_by_direction"] = rectangles
	rig["foreground_overlays"] = [optional_overlay]
	rig["foreground_overlays_by_id"] = {"right_hand_primary_grip": optional_overlay}
	preview._rigs_by_id["humanoid_v1"] = rig
	preview.set_actual_scale_enabled(false)
	preview.reset_fit_view()
	preview.update(true, "right_hand", "dark_sword", "N", 1, visible_slots, socket_visual)
	foreground_overlay = preview._foreground_overlays.get("right_hand_primary_grip", null) as TextureRect
	if foreground_overlay == null or foreground_overlay.visible:
		_fail("Optional missing foreground-overlay pose must render nothing")
		return
	if not held_item.visible:
		_fail("Optional missing foreground-overlay pose must leave the held item visible")
		return

	var unavailable_source_overlay := (overlays[0] as Dictionary).duplicate(true)
	unavailable_source_overlay["source_layer_id"] = "unavailable_source_layer"
	rig["foreground_overlays"] = [unavailable_source_overlay]
	rig["foreground_overlays_by_id"] = {"right_hand_primary_grip": unavailable_source_overlay}
	preview._rigs_by_id["humanoid_v1"] = rig
	preview.update(true, "right_hand", "dark_sword", "N", 1, visible_slots, socket_visual)
	foreground_overlay = preview._foreground_overlays.get("right_hand_primary_grip", null) as TextureRect
	if foreground_overlay == null or foreground_overlay.visible:
		_fail("Unavailable foreground-overlay source art must degrade safely")
		return

	preview.update(true, "right_hand", "dark_sword", "N", 1, visible_slots, _make_rig_layer_visual())
	if foreground_overlay.visible:
		_fail("Rig-layer preview must not activate socket-owned foreground overlays")
		return

	var left_visible_slots := ["head", "body", "legs", "left_hand"]
	var left_socket_visual := _make_socket_visual(Vector2i(16, 16), "left_hand_primary", "N", 1, "flowers", "left_hand")
	preview.update(true, "left_hand", "flowers", "N", 1, left_visible_slots, left_socket_visual)
	var left_foreground_overlay := preview._foreground_overlays.get("left_hand_primary_grip", null) as TextureRect
	var left_held_item := preview._layers.get("left_hand", null) as TextureRect
	if left_foreground_overlay == null or not left_foreground_overlay.visible:
		_fail("Left-hand socket-bound preview must render its rig-owned foreground grip overlay")
		return
	if left_held_item == null or left_foreground_overlay.z_index <= left_held_item.z_index:
		_fail("Left-hand foreground grip overlay must render above its socket-bound item")
		return
	if left_foreground_overlay.mouse_filter != Control.MOUSE_FILTER_IGNORE:
		_fail("Left-hand foreground grip overlay must remain non-interactive")
		return
	preview.update(true, "left_hand", "small_shield", "N", 1, left_visible_slots, _make_rig_layer_visual("small_shield", "left_hand"))
	if left_foreground_overlay.visible:
		_fail("Left-hand rig-layer preview must not activate a socket-owned foreground overlay")
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
	stage.size = PaperDollPreview.STAGE_SIZE
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


func _write_fixture_png(path: String, color: Color, size: Vector2i = Vector2i(32, 32)) -> void:
	var image := Image.create(size.x, size.y, false, Image.FORMAT_RGBA8)
	image.fill(color)
	image.save_png(path)


func _stage_mouse_press(preview: PaperDollPreview, position: Vector2) -> void:
	var event := InputEventMouseButton.new()
	event.button_index = MOUSE_BUTTON_LEFT
	event.pressed = true
	event.position = position
	preview._on_stage_gui_input(event)


func _stage_mouse_motion(preview: PaperDollPreview, position: Vector2) -> void:
	var event := InputEventMouseMotion.new()
	event.position = position
	event.button_mask = MOUSE_BUTTON_MASK_LEFT
	preview._on_stage_gui_input(event)


func _stage_mouse_release(preview: PaperDollPreview, position: Vector2) -> void:
	var event := InputEventMouseButton.new()
	event.button_index = MOUSE_BUTTON_LEFT
	event.pressed = false
	event.position = position
	preview._on_stage_gui_input(event)


func _build_preview_fixture() -> Dictionary:
	var preview := PaperDollPreview.new()
	var stage := Control.new()
	stage.size = PaperDollPreview.STAGE_SIZE
	var status := Label.new()
	preview.bind(stage, status)

	var temp_root := ProjectSettings.globalize_path("user://contract-fixture-paper-doll")
	DirAccess.make_dir_recursive_absolute(temp_root.path_join("actors/player/head"))
	DirAccess.make_dir_recursive_absolute(temp_root.path_join("actors/player/body"))
	DirAccess.make_dir_recursive_absolute(temp_root.path_join("actors/player/legs"))
	DirAccess.make_dir_recursive_absolute(temp_root.path_join("actors/player/right_hand"))
	DirAccess.make_dir_recursive_absolute(temp_root.path_join("actors/player/left_hand"))
	for direction in ["N", "S", "E", "W"]:
		_write_fixture_png(temp_root.path_join("actors/player/head/head1-F1-%s.png" % direction), Color(1, 0, 0, 1))
		_write_fixture_png(temp_root.path_join("actors/player/body/defbod-F1-%s.png" % direction), Color(0, 1, 0, 1))
		_write_fixture_png(temp_root.path_join("actors/player/legs/defbod-F1-%s.png" % direction), Color(0, 0, 1, 1))
		_write_fixture_png(temp_root.path_join("actors/player/right_hand/dark_sword-F1-%s.png" % direction), Color(1, 1, 0, 1))
		_write_fixture_png(temp_root.path_join("actors/player/left_hand/flowers-F1-%s.png" % direction), Color(1, 0.5, 1, 1))
		_write_fixture_png(temp_root.path_join("actors/player/left_hand/small_shield-F1-%s.png" % direction), Color(0.5, 0.7, 0.8, 1))
	_write_fixture_png(temp_root.path_join("actors/player/right_hand/axe-F4-W.png"), Color(1, 0.6, 0.2, 1), Vector2i(72, 32))

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
						{
							"layer_id": "left_hand",
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
								"E": {"1": {"x": 40, "y": 16}, "2": {"x": 40, "y": 16}, "3": {"x": 40, "y": 16}, "4": {"x": 40, "y": 16}},
								"S": {"1": {"x": 16, "y": 16}, "2": {"x": 16, "y": 16}, "3": {"x": 16, "y": 16}, "4": {"x": 16, "y": 16}},
								"W": {"1": {"x": 0, "y": 16}, "2": {"x": 0, "y": 16}, "3": {"x": 0, "y": 16}, "4": {"x": 0, "y": 16}},
							},
						},
						{
							"socket_id": "left_hand_primary",
							"positions": {
								"N": {"1": {"x": 16, "y": 16}, "2": {"x": 16, "y": 16}, "3": {"x": 16, "y": 16}, "4": {"x": 16, "y": 16}},
								"E": {"1": {"x": 0, "y": 16}, "2": {"x": 0, "y": 16}, "3": {"x": 0, "y": 16}, "4": {"x": 0, "y": 16}},
								"S": {"1": {"x": 16, "y": 16}, "2": {"x": 16, "y": 16}, "3": {"x": 16, "y": 16}, "4": {"x": 16, "y": 16}},
								"W": {"1": {"x": 40, "y": 16}, "2": {"x": 40, "y": 16}, "3": {"x": 40, "y": 16}, "4": {"x": 40, "y": 16}},
							},
						},
					],
					"foreground_overlays": [
						{
							"overlay_id": "right_hand_primary_grip",
							"socket_id": "right_hand_primary",
							"source_layer_id": "body",
							"z_index_by_direction": {"N": 50, "E": 50, "S": 50, "W": 50},
							"source_rect_by_direction": {
								"N": {"1": {"x": 8, "y": 8, "width": 8, "height": 8}},
								"E": {"1": {"x": 8, "y": 8, "width": 8, "height": 8}},
								"S": {"1": {"x": 8, "y": 8, "width": 8, "height": 8}},
								"W": {"1": {"x": 8, "y": 8, "width": 8, "height": 8}},
							},
						},
						{
							"overlay_id": "left_hand_primary_grip",
							"socket_id": "left_hand_primary",
							"source_layer_id": "body",
							"z_index_by_direction": {"N": 50, "E": 50, "S": 50, "W": 50},
							"source_rect_by_direction": {
								"N": {"1": {"x": 8, "y": 8, "width": 8, "height": 8}},
								"E": {"1": {"x": 8, "y": 8, "width": 8, "height": 8}},
								"S": {"1": {"x": 8, "y": 8, "width": 8, "height": 8}},
								"W": {"1": {"x": 8, "y": 8, "width": 8, "height": 8}},
							},
						},
					],
				},
			],
		},
	}


func _mob_rigged_options() -> Dictionary:
	return {
		"defaults": {},
		"supported_limits": {},
		"publication_states": [],
		"attack_types": [],
		"accuracy_styles": [],
		"movement_behaviors": [],
		"aggression_modes": [],
		"return_home_behaviors": [],
		"faction_dispositions": [],
		"combat_bonus_fields": [],
		"factions": [],
		"published_drop_items": [],
		"visual_assets": {},
		"actor_appearance": {
			"visual_modes": [{"id": "flat_sprite", "display_name": "Flat Sprite"}, {"id": "composite_rig", "display_name": "Rigged Sprite"}],
			"rigs": [{"rig_id": "humanoid_v1", "layers": [{"layer_id": "right_hand"}]}],
			"calibrations": [{"calibration_id": "orc_v1", "rig_id": "humanoid_v1"}],
			"equipped_visuals": [{"item_id": "inventory_154_axe", "rig_id": "humanoid_v1", "binding_type": "socket", "render_layer_id": "right_hand"}],
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


func _make_socket_visual(anchor: Vector2i, socket_id: String = "right_hand_primary", direction: String = "N", frame: int = 1, asset_key: String = "dark_sword", render_layer_id: String = "right_hand") -> Dictionary:
	return {
		"asset_key": asset_key,
		"rig_id": "humanoid_v1",
		"binding_type": "socket",
		"render_layer_id": render_layer_id,
		"socket_id": socket_id,
		"secondary_socket_id": null,
		"nudge": {"x": 0, "y": 0},
		"grip_anchors": {
			direction: {
				str(frame): {"x": anchor.x, "y": anchor.y},
			},
		},
	}


func _make_rig_layer_visual(asset_key: String = "dark_sword", render_layer_id: String = "right_hand") -> Dictionary:
	return {
		"asset_key": asset_key,
		"rig_id": "humanoid_v1",
		"binding_type": "rig_layer",
		"render_layer_id": render_layer_id,
		"socket_id": null,
		"secondary_socket_id": null,
		"nudge": {"x": 0, "y": 0},
		"grip_anchors": {},
	}


func _fail(message: String) -> void:
	push_error(message)
	quit(1)
