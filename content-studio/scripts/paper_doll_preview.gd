extends RefCounted
class_name PaperDollPreview

signal grip_anchor_changed(direction: String, frame: int, x: int, y: int)

const DEFAULT_VISUAL_KEYS := {"head": "head1", "body": "defbod", "legs": "defbod"}
const DEFAULT_RIG_ID := "humanoid_v1"
const STAGE_SIZE := Vector2(180, 180)
const STAGE_PADDING := 8.0
const ANCHOR_OFFSET := Vector2(-7, -7)
const ACTUAL_GAME_SCALE := 0.25
const SOCKET_MARKER_SIZE := Vector2(8, 8)
const GRIP_MARKER_SIZE := Vector2(8, 8)
const MISSING_ASSET_HINT_LIMIT := 2

var game_client_assets_root := ""

var _actual_scale_enabled := false
var _rig_catalog_status := "Actor rig metadata has not been loaded yet."
var _rigs_by_id: Dictionary = {}
var _stage: Control
var _status: Label
var _layer_root: Control
var _overlay_root: Control
var _socket_marker: ColorRect
var _grip_marker: ColorRect
var _layers: Dictionary = {}
var _file_cache: Dictionary = {}
var _texture_cache: Dictionary = {}
var _current_pose_context: Dictionary = {}
var _drag_state: Dictionary = {}
var _asset_resolution_diagnostics: Array = []
var _last_resolved_asset_path := ""


func bind(stage: Control, status: Label) -> void:
	_stage = stage
	_status = status
	for child in stage.get_children():
		stage.remove_child(child)
		child.queue_free()
	_layers.clear()
	_layer_root = Control.new()
	_layer_root.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_layer_root.set_anchors_preset(Control.PRESET_FULL_RECT)
	stage.add_child(_layer_root)
	_overlay_root = Control.new()
	_overlay_root.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_overlay_root.set_anchors_preset(Control.PRESET_FULL_RECT)
	stage.add_child(_overlay_root)
	_socket_marker = _build_marker(Color(0.95, 0.82, 0.36, 1), SOCKET_MARKER_SIZE)
	_overlay_root.add_child(_socket_marker)
	_grip_marker = _build_marker(Color(0.96, 0.44, 0.74, 1), GRIP_MARKER_SIZE)
	_overlay_root.add_child(_grip_marker)
	if not stage.gui_input.is_connected(_on_stage_gui_input):
		stage.gui_input.connect(_on_stage_gui_input)


func clear_cache() -> void:
	_file_cache.clear()
	_texture_cache.clear()


func configure_rig_catalog(catalog: Dictionary) -> void:
	_rigs_by_id.clear()
	var available := bool(catalog.get("available", false))
	var source_path := str(catalog.get("source_path", ""))
	_rig_catalog_status = str(catalog.get("message", ""))
	if not available:
		if _rig_catalog_status.is_empty():
			_rig_catalog_status = "The canonical actor rig catalog is unavailable."
		if not source_path.is_empty():
			_rig_catalog_status = "%s Path: %s" % [_rig_catalog_status, source_path]
		return
	var rigs_variant: Variant = catalog.get("rigs", [])
	if not (rigs_variant is Array):
		_rig_catalog_status = "The actor rig catalog payload is malformed."
		return
	var first_rig_id := ""
	for rig_variant: Variant in rigs_variant:
		if not (rig_variant is Dictionary):
			continue
		var rig := (rig_variant as Dictionary).duplicate(true)
		var rig_id := str(rig.get("rig_id", ""))
		if rig_id.is_empty():
			continue
		if first_rig_id.is_empty():
			first_rig_id = rig_id
		var layers_by_id: Dictionary = {}
		for layer_variant: Variant in rig.get("layers", []) as Array:
			if layer_variant is Dictionary:
				var layer := layer_variant as Dictionary
				layers_by_id[str(layer.get("layer_id", ""))] = layer
		var sockets_by_id: Dictionary = {}
		for socket_variant: Variant in rig.get("sockets", []) as Array:
			if socket_variant is Dictionary:
				var socket := socket_variant as Dictionary
				sockets_by_id[str(socket.get("socket_id", ""))] = socket
		rig["layers_by_id"] = layers_by_id
		rig["sockets_by_id"] = sockets_by_id
		_rigs_by_id[rig_id] = rig
	if _rigs_by_id.is_empty():
		_rig_catalog_status = "The canonical actor rig catalog is empty."
	elif _rig_catalog_status.is_empty():
		_rig_catalog_status = "Loaded %s from %s." % [first_rig_id, source_path] if not source_path.is_empty() else "Loaded %s." % first_rig_id


func set_actual_scale_enabled(enabled: bool) -> void:
	_actual_scale_enabled = enabled


func cancel_drag() -> void:
	_drag_state.clear()
	_current_pose_context.clear()
	_hide_markers()


func get_last_resolved_asset_path() -> String:
	return _last_resolved_asset_path


func update(
	equippable: bool,
	slot_id: String,
	legacy_visual_key: String,
	direction: String,
	frame: int,
	visible_slots: Array,
	equipped_visual: Dictionary = {}
) -> Dictionary:
	_last_resolved_asset_path = ""
	_current_pose_context.clear()
	_asset_resolution_diagnostics.clear()
	_reset_layers()
	_hide_markers()

	if _stage == null or _status == null:
		return {"status": "Preview stage is not available.", "resolved_asset_path": ""}

	if game_client_assets_root.is_empty() or not DirAccess.dir_exists_absolute(game_client_assets_root):
		_status.text = "The configured game_client_assets directory is unavailable."
		return {"status": _status.text, "resolved_asset_path": ""}

	var selected_direction := direction if not direction.is_empty() else "N"
	var selected_frame := clampi(frame, 1, 4)
	var rig_id := str(equipped_visual.get("rig_id", DEFAULT_RIG_ID))
	if rig_id.is_empty():
		rig_id = DEFAULT_RIG_ID
	var rig := (_rigs_by_id.get(rig_id, {}) as Dictionary)
	if rig.is_empty():
		var diagnostic := _rig_catalog_status
		if diagnostic.is_empty():
			diagnostic = "Rig '%s' is not available in the canonical actor rig catalog." % rig_id
		_status.text = diagnostic
		return {"status": diagnostic, "resolved_asset_path": ""}

	var layer_entries := _resolve_loaded_layers(
		rig,
		equippable,
		slot_id,
		legacy_visual_key,
		selected_direction,
		selected_frame,
		visible_slots,
		equipped_visual)
	if layer_entries.is_empty():
		if bool(_drag_state.get("active", false)):
			cancel_drag()
		var diagnostic := "No preview PNGs could be resolved from the configured game_client_assets root."
		var first_missing := _first_asset_resolution_hint()
		if not first_missing.is_empty():
			diagnostic += " First missing asset hint: %s" % first_missing
		_status.text = diagnostic
		return {"status": _status.text, "resolved_asset_path": ""}

	var source_bounds := _source_bounds(layer_entries)
	var preview_scale := ACTUAL_GAME_SCALE if _actual_scale_enabled else _preview_scale(source_bounds.size)
	var group_origin := (STAGE_SIZE - (source_bounds.size * preview_scale)) * 0.5
	var selected_entry: Dictionary = {}
	for layer_entry_variant: Variant in layer_entries:
		var layer_entry: Dictionary = layer_entry_variant
		var texture := layer_entry.get("texture") as Texture2D
		if texture == null:
			continue
		var layer_id := str(layer_entry.get("layer_id", ""))
		var layer := _ensure_layer(layer_id)
		var source_position := _variant_to_vector2(layer_entry.get("source_position", ANCHOR_OFFSET), ANCHOR_OFFSET)
		layer.texture = texture
		layer.visible = true
		layer.z_index = int(layer_entry.get("z_index", 0))
		layer.size = texture.get_size() * preview_scale
		layer.position = group_origin + ((source_position - source_bounds.position) * preview_scale)
		if bool(layer_entry.get("selected_visual", false)):
			selected_entry = layer_entry
			_last_resolved_asset_path = str(layer_entry.get("resolved_asset_path", ""))

	var valid_socket_attachment := _is_valid_socket_attachment(equipped_visual, selected_entry)
	if valid_socket_attachment:
		_update_markers(rig, equipped_visual, selected_direction, selected_frame, selected_entry, source_bounds, group_origin, preview_scale)
	elif bool(_drag_state.get("active", false)):
		cancel_drag()

	_status.text = _status_text(equippable, slot_id, rig_id, selected_direction, selected_frame, selected_entry, equipped_visual)
	return {
		"status": _status.text,
		"resolved_asset_path": _last_resolved_asset_path,
		"current_pose_has_anchor": _current_pose_context.get("authored_anchor", false),
	}


func normalize_visual_key(value: String) -> String:
	var normalized := value.to_lower().replace("'", "").replace("’", "")
	for separator in [" ", "-", "/"]:
		normalized = normalized.replace(separator, "_")
	while normalized.contains("__"):
		normalized = normalized.replace("__", "_")
	return normalized.trim_prefix("_").trim_suffix("_")


func resolve_source_pixel_offset(socket_position: Vector2i, grip_anchor: Vector2i, nudge: Vector2i = Vector2i.ZERO) -> Vector2:
	return Vector2(
		socket_position.x - grip_anchor.x + nudge.x,
		socket_position.y - grip_anchor.y + nudge.y)


func _resolve_loaded_layers(
	rig: Dictionary,
	equippable: bool,
	slot_id: String,
	legacy_visual_key: String,
	direction: String,
	frame: int,
	visible_slots: Array,
	equipped_visual: Dictionary
) -> Array:
	var entries: Array = []
	var layers_variant: Variant = rig.get("layers", [])
	if not (layers_variant is Array):
		return entries
	var visible_lookup: Dictionary = {}
	for variant in visible_slots:
		visible_lookup[str(variant)] = true
	var active_asset_key := str(equipped_visual.get("asset_key", legacy_visual_key))
	if active_asset_key.is_empty():
		active_asset_key = legacy_visual_key
	var active_render_layer_id := str(equipped_visual.get("render_layer_id", slot_id))
	var active_binding_type := str(equipped_visual.get("binding_type", ""))
	for layer_variant: Variant in layers_variant:
		if not (layer_variant is Dictionary):
			continue
		var layer := layer_variant as Dictionary
		var layer_id := str(layer.get("layer_id", ""))
		var asset_key := str(DEFAULT_VISUAL_KEYS.get(layer_id, ""))
		var selected_visual := false
		if equippable and visible_lookup.has(slot_id):
			if not equipped_visual.is_empty() and active_render_layer_id == layer_id and not active_asset_key.is_empty():
				asset_key = active_asset_key
				selected_visual = true
			elif equipped_visual.is_empty() and slot_id == layer_id and not legacy_visual_key.is_empty():
				asset_key = legacy_visual_key
				selected_visual = true
			elif slot_id == "ring":
				selected_visual = false
			elif not legacy_visual_key.is_empty() and slot_id == layer_id:
				selected_visual = true
		if asset_key.is_empty():
			continue
		var load_result := _load_texture(layer_id, asset_key, frame, direction)
		if load_result.is_empty():
			var asset_path := _expected_layer_asset_path(layer_id, asset_key, frame, direction)
			if not _asset_resolution_diagnostics.has(asset_path):
				_asset_resolution_diagnostics.append(asset_path)
			continue
		var texture := load_result.get("texture") as Texture2D
		if texture == null:
			continue
		var pose := _resolve_layer_pose(rig, equipped_visual, layer_id, direction, frame)
		entries.append({
			"layer_id": layer_id,
			"texture": texture,
			"source_position": pose.position,
			"z_index": _resolve_layer_z_index(rig, layer_id, direction),
			"selected_visual": selected_visual and (active_binding_type != "rig_layer" or active_render_layer_id == layer_id),
			"asset_key": asset_key,
			"resolved_asset_path": str(load_result.get("path", "")),
			"used_attachment": bool(pose.used_attachment),
			"grip_anchor": pose.grip_anchor,
			"socket_position": pose.socket_position,
			"authored_anchor": bool(pose.authored_anchor),
		})
	return entries


func _resolve_layer_pose(
	rig: Dictionary,
	equipped_visual: Dictionary,
	layer_id: String,
	direction: String,
	frame: int
) -> Dictionary:
	var pose := {
		"position": ANCHOR_OFFSET,
		"used_attachment": false,
		"grip_anchor": Vector2i.ZERO,
		"socket_position": Vector2i.ZERO,
		"authored_anchor": false,
	}
	if equipped_visual.is_empty():
		return pose
	if str(equipped_visual.get("binding_type", "")) != "socket":
		return pose
	if str(equipped_visual.get("render_layer_id", "")) != layer_id:
		return pose
	var socket_id := str(equipped_visual.get("socket_id", ""))
	var socket_position = _resolve_rig_socket_position(rig, socket_id, direction, frame)
	if socket_position == null:
		return pose
	var grip_anchor := _resolve_preview_grip_anchor(
		equipped_visual.get("grip_anchors", null),
		direction,
		frame,
		layer_id,
		str(equipped_visual.get("asset_key", "")))
	var authored_anchor: Variant = _resolve_directional_point(
		equipped_visual.get("grip_anchors", null),
		direction,
		frame)
	var nudge := _resolve_optional_point(equipped_visual.get("nudge", null))
	pose.position = ANCHOR_OFFSET + resolve_source_pixel_offset(socket_position, grip_anchor, nudge)
	pose.used_attachment = true
	pose.grip_anchor = grip_anchor
	pose.socket_position = socket_position
	pose.authored_anchor = authored_anchor != null
	return pose


func _resolve_preview_grip_anchor(
	grip_points_variant: Variant,
	direction: String,
	frame: int,
	layer_id: String,
	asset_key: String) -> Vector2i:
	var authored_anchor = _resolve_directional_point(grip_points_variant, direction, frame)
	if authored_anchor != null:
		return authored_anchor
	var neighboring_frame := clampi(frame - 1, 1, 4)
	var previous_anchor = _resolve_directional_point(grip_points_variant, direction, neighboring_frame)
	if previous_anchor != null:
		return previous_anchor
	var file_path := _find_file(layer_id, asset_key, frame, direction)
	if file_path.is_empty() and neighboring_frame != frame:
		file_path = _find_file(layer_id, asset_key, neighboring_frame, direction)
	if file_path.is_empty():
		return Vector2i.ZERO
	var texture := _texture_cache.get(file_path, null) as Texture2D
	if texture == null:
		return Vector2i.ZERO
	return Vector2i(int(texture.get_width() * 0.5), int(texture.get_height() * 0.5))


func _resolve_rig_socket_position(rig: Dictionary, socket_id: String, direction: String, frame: int):
	var sockets_by_id: Dictionary = rig.get("sockets_by_id", {})
	var socket := sockets_by_id.get(socket_id, {}) as Dictionary
	if socket.is_empty():
		return null
	return _resolve_directional_point(socket.get("positions", null), direction, frame)


func _resolve_directional_point(points_variant: Variant, direction: String, frame: int):
	if not (points_variant is Dictionary):
		return null
	var points := points_variant as Dictionary
	var frames_variant: Variant = points.get(direction, null)
	if not (frames_variant is Dictionary):
		return null
	var frames := frames_variant as Dictionary
	var point_variant: Variant = frames.get(str(clampi(frame, 1, 4)), null)
	return _resolve_optional_point(point_variant)


func _resolve_optional_point(point_variant: Variant) -> Vector2i:
	if not (point_variant is Dictionary):
		return Vector2i.ZERO
	var point := point_variant as Dictionary
	return Vector2i(
		int(point.get("x", 0)),
		int(point.get("y", 0)))


func _resolve_layer_z_index(rig: Dictionary, layer_id: String, direction: String) -> int:
	var layers_by_id: Dictionary = rig.get("layers_by_id", {})
	var layer := layers_by_id.get(layer_id, {}) as Dictionary
	if layer.is_empty():
		return 0
	var z_indexes := layer.get("z_index_by_direction", {}) as Dictionary
	return int(z_indexes.get(direction, 0))


func _load_texture(layer_id: String, asset_key: String, frame: int, direction: String) -> Dictionary:
	for fallback_frame in _frame_fallbacks(frame, direction):
		var file_path := _find_file(layer_id, asset_key, int(fallback_frame), direction)
		if file_path.is_empty():
			continue
		if _texture_cache.has(file_path):
			return {"texture": _texture_cache[file_path], "path": file_path}
		var image := Image.load_from_file(file_path)
		if image == null or image.is_empty():
			continue
		var texture := ImageTexture.create_from_image(image)
		_texture_cache[file_path] = texture
		return {"texture": texture, "path": file_path}
	return {}


func _source_bounds(loaded_layers: Array) -> Rect2:
	var has_bounds := false
	var source_bounds := Rect2(ANCHOR_OFFSET, Vector2.ZERO)
	for layer_entry_variant: Variant in loaded_layers:
		var layer_entry: Dictionary = layer_entry_variant
		var texture := layer_entry.get("texture") as Texture2D
		if texture == null:
			continue
		var source_position := _variant_to_vector2(layer_entry.get("source_position", ANCHOR_OFFSET), ANCHOR_OFFSET)
		var layer_rect := Rect2(source_position, texture.get_size())
		if not has_bounds:
			source_bounds = layer_rect
			has_bounds = true
		else:
			source_bounds = source_bounds.merge(layer_rect)
	return source_bounds


func _preview_scale(source_size: Vector2) -> float:
	if source_size.x <= 0.0 or source_size.y <= 0.0:
		return 1.0
	var available_size := STAGE_SIZE - (Vector2.ONE * STAGE_PADDING * 2.0)
	return minf(available_size.x / source_size.x, available_size.y / source_size.y)


func _build_marker(color: Color, marker_size: Vector2) -> ColorRect:
	var marker := ColorRect.new()
	marker.color = color
	marker.size = marker_size
	marker.visible = false
	marker.mouse_filter = Control.MOUSE_FILTER_IGNORE
	return marker


func _ensure_layer(layer_id: String) -> TextureRect:
	if _layers.has(layer_id):
		return _layers[layer_id] as TextureRect
	var layer := TextureRect.new()
	layer.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	layer.stretch_mode = TextureRect.STRETCH_SCALE
	layer.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_layer_root.add_child(layer)
	_layers[layer_id] = layer
	return layer


func _reset_layers() -> void:
	for layer_variant: Variant in _layers.values():
		var layer := layer_variant as TextureRect
		if layer == null:
			continue
		layer.texture = null
		layer.visible = false
		layer.position = Vector2.ZERO
		layer.size = Vector2.ZERO


func _hide_markers() -> void:
	if _socket_marker != null:
		_socket_marker.visible = false
	if _grip_marker != null:
		_grip_marker.visible = false


func _update_markers(
	rig: Dictionary,
	equipped_visual: Dictionary,
	direction: String,
	frame: int,
	selected_entry: Dictionary,
	source_bounds: Rect2,
	group_origin: Vector2,
	preview_scale: float
) -> void:
	var socket_position := _variant_to_vector2i(selected_entry.get("socket_position", Vector2i.ZERO), Vector2i.ZERO)
	var grip_anchor := _variant_to_vector2i(selected_entry.get("grip_anchor", Vector2i.ZERO), Vector2i.ZERO)
	var source_position := _variant_to_vector2(selected_entry.get("source_position", ANCHOR_OFFSET), ANCHOR_OFFSET)
	var used_attachment := bool(selected_entry.get("used_attachment", false))
	var socket_stage := group_origin + (((ANCHOR_OFFSET + Vector2(socket_position)) - source_bounds.position) * preview_scale)
	var grip_stage := group_origin + (((source_position + Vector2(grip_anchor)) - source_bounds.position) * preview_scale)
	_socket_marker.visible = true
	_grip_marker.visible = true
	_socket_marker.position = socket_stage - (_socket_marker.size * 0.5)
	_grip_marker.position = grip_stage - (_grip_marker.size * 0.5)
	var texture := selected_entry.get("texture") as Texture2D
	if texture != null:
		var layer_position := group_origin + ((source_position - source_bounds.position) * preview_scale)
		_current_pose_context = {
			"direction": direction,
			"frame": frame,
			"layer_position": layer_position,
			"layer_position_source": source_position,
			"preview_scale": preview_scale,
			"texture_size": texture.get_size(),
			"authored_anchor": bool(selected_entry.get("authored_anchor", false)),
			"used_attachment": used_attachment,
			"can_drag": true,
			"grip_anchor": Vector2(grip_anchor),
			"selected_rect": Rect2(layer_position, texture.get_size() * preview_scale),
		}


func _status_text(
	equippable: bool,
	slot_id: String,
	rig_id: String,
	direction: String,
	frame: int,
	selected_entry: Dictionary,
	equipped_visual: Dictionary) -> String:
	if not equippable:
		return "Not equippable: showing the default player layers only."
	if slot_id == "ring":
		return "Ring is gameplay equipment but has no visible paper-doll layer in the current client."
	if selected_entry.is_empty():
		return "No preview PNG matched the selected item visual."
	var binding_type := str(equipped_visual.get("binding_type", "legacy"))
	var authored_anchor := bool(selected_entry.get("authored_anchor", false))
	var used_attachment := bool(selected_entry.get("used_attachment", false))
	var socket_id := str(equipped_visual.get("socket_id", ""))
	if binding_type == "socket":
		if not used_attachment:
			return "%s • %s frame %d • %s anchor preview fallback (socket '%s' not available)" % [
				rig_id,
				direction,
				frame,
				str(selected_entry.get("asset_key", "")),
				socket_id
			]
		return "%s • %s frame %d • %s anchor %s" % [
			rig_id,
			direction,
			frame,
			str(selected_entry.get("asset_key", "")),
			"authored" if authored_anchor else "preview fallback"
		]
	return "%s • %s frame %d • %s" % [rig_id, direction, frame, str(selected_entry.get("asset_key", ""))]


func _find_file(layer_id: String, asset_key: String, frame: int, direction: String) -> String:
	var cache_key := "%s|%s|%d|%s" % [layer_id, asset_key, frame, direction]
	if _file_cache.has(cache_key):
		return str(_file_cache[cache_key])
	var directory_path := game_client_assets_root.path_join("actors").path_join("player").path_join(layer_id)
	var canonical_path := directory_path.path_join("%s-F%d-%s.png" % [asset_key, frame, direction])
	if FileAccess.file_exists(canonical_path):
		_file_cache[cache_key] = canonical_path
		return canonical_path
	var directory := DirAccess.open(directory_path)
	if directory == null:
		_file_cache[cache_key] = ""
		return ""
	var suffix := "-F%d-%s.png" % [frame, direction]
	directory.list_dir_begin()
	while true:
		var file_name := directory.get_next()
		if file_name.is_empty():
			break
		if directory.current_is_dir() or not file_name.ends_with(suffix):
			continue
		if _legacy_visual_key(file_name.trim_suffix(suffix)) == asset_key:
			directory.list_dir_end()
			var matched_path := directory_path.path_join(file_name)
			_file_cache[cache_key] = matched_path
			return matched_path
	directory.list_dir_end()
	_file_cache[cache_key] = ""
	return ""


func _legacy_visual_key(base_name: String) -> String:
	var prefix_expression := RegEx.new()
	prefix_expression.compile("^Characters_[0-9]+_")
	return normalize_visual_key(prefix_expression.sub(base_name, "", false))


func _frame_fallbacks(frame: int, direction: String) -> Array:
	var values: Array = [frame]
	if direction == "N" and not values.has(4):
		values.append(4)
	if not values.has(3):
		values.append(3)
	return values


func _on_stage_gui_input(event: InputEvent) -> void:
	if event is InputEventMouseButton:
		var mouse_button := event as InputEventMouseButton
		if mouse_button.button_index == MOUSE_BUTTON_LEFT and mouse_button.pressed:
			_begin_drag(mouse_button.position)
		elif mouse_button.button_index == MOUSE_BUTTON_LEFT and not mouse_button.pressed:
			_end_drag()
	elif event is InputEventMouseMotion:
		var mouse_motion := event as InputEventMouseMotion
		if mouse_motion.button_mask & MOUSE_BUTTON_MASK_LEFT and bool(_drag_state.get("active", false)):
			_apply_drag_position(mouse_motion.position)


func _apply_drag_position(local_position: Vector2) -> void:
	if not bool(_drag_state.get("active", false)):
		return
	_apply_drag_position_from_active_drag(local_position)


func _begin_drag(local_position: Vector2) -> void:
	if _current_pose_context.is_empty():
		return
	var can_drag := bool(_current_pose_context.get("can_drag", false))
	if not can_drag:
		return
	var selected_rect := _variant_to_rect2(_current_pose_context.get("selected_rect", Rect2()), Rect2())
	if selected_rect.size.x <= 0.0 or selected_rect.size.y <= 0.0 or not selected_rect.has_point(local_position):
		return
	var preview_scale := float(_current_pose_context.get("preview_scale", 0.0))
	if preview_scale <= 0.0:
		return
	var texture_size := _variant_to_vector2(_current_pose_context.get("texture_size", Vector2.ZERO), Vector2.ZERO)
	if texture_size.x <= 0.0 or texture_size.y <= 0.0:
		return
	var grip_anchor := _variant_to_vector2(_current_pose_context.get("grip_anchor", Vector2.ZERO), Vector2.ZERO)
	_drag_state = {
		"active": true,
		"mouse_start_position": local_position,
		"start_grip_anchor": grip_anchor,
		"preview_scale": preview_scale,
		"texture_size": texture_size,
		"direction": str(_current_pose_context.get("direction", "N")),
		"frame": int(_current_pose_context.get("frame", 1)),
	}


func _apply_drag_position_from_active_drag(local_position: Vector2) -> void:
	var preview_scale := float(_current_pose_context.get("preview_scale", 0.0))
	if preview_scale <= 0.0:
		return
	var texture_size := _variant_to_vector2(_drag_state.get("texture_size", Vector2.ZERO), Vector2.ZERO)
	if texture_size.x <= 0.0 or texture_size.y <= 0.0:
		return
	var start_mouse := _variant_to_vector2(_drag_state.get("mouse_start_position", Vector2.ZERO), Vector2.ZERO)
	var start_grip_anchor := _variant_to_vector2(_drag_state.get("start_grip_anchor", Vector2.ZERO), Vector2.ZERO)
	var drag_delta_source := (local_position - start_mouse) / preview_scale
	var grip_source := start_grip_anchor - drag_delta_source
	var max_x := int(maxf(texture_size.x - 1.0, 0.0))
	var max_y := int(maxf(texture_size.y - 1.0, 0.0))
	var x := clampi(int(round(grip_source.x)), 0, max_x)
	var y := clampi(int(round(grip_source.y)), 0, max_y)
	grip_anchor_changed.emit(
		str(_drag_state.get("direction", "N")),
		int(_drag_state.get("frame", 1)),
		x,
		y)


func _end_drag() -> void:
	_drag_state.clear()


func _is_valid_socket_attachment(equipped_visual: Dictionary, selected_entry: Dictionary) -> bool:
	if equipped_visual.is_empty() or selected_entry.is_empty():
		return false
	if str(equipped_visual.get("binding_type", "")) != "socket":
		return false
	if str(equipped_visual.get("render_layer_id", "")).is_empty():
		return false
	if str(equipped_visual.get("socket_id", "")).is_empty():
		return false
	if not bool(selected_entry.get("selected_visual", false)):
		return false
	if not bool(selected_entry.get("used_attachment", false)):
		return false
	return not str(selected_entry.get("resolved_asset_path", "")).is_empty()


func _expected_layer_asset_path(layer_id: String, asset_key: String, frame: int, direction: String) -> String:
	var directory_path := game_client_assets_root.path_join("actors").path_join("player").path_join(layer_id)
	return directory_path.path_join("%s-F%d-%s.png" % [asset_key, frame, direction])


func _first_asset_resolution_hint() -> String:
	if _asset_resolution_diagnostics.size() == 0:
		return ""
	var max_index := mini(_asset_resolution_diagnostics.size(), MISSING_ASSET_HINT_LIMIT)
	var hints: Array = []
	for index in range(max_index):
		hints.append(str(_asset_resolution_diagnostics[index]))
	return ", ".join(PackedStringArray(hints))


func _variant_to_vector2(value: Variant, fallback: Vector2) -> Vector2:
	if value is Vector2:
		var vector_value: Vector2 = value
		return vector_value
	if value is Vector2i:
		var vector_value_i: Vector2i = value
		return Vector2(vector_value_i)
	return fallback


func _variant_to_vector2i(value: Variant, fallback: Vector2i) -> Vector2i:
	if value is Vector2i:
		var vector_value_i: Vector2i = value
		return vector_value_i
	if value is Vector2:
		var vector_value: Vector2 = value
		return Vector2i(vector_value)
	return fallback


func _variant_to_rect2(value: Variant, fallback: Rect2) -> Rect2:
	if value is Rect2:
		var rect_value: Rect2 = value
		return rect_value
	return fallback
