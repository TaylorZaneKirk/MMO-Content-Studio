extends RefCounted
class_name PaperDollPreview

const LAYER_ORDER := ["cape", "right_hand", "legs", "boots", "body", "left_hand", "gloves", "head"]
const DEFAULT_VISUAL_KEYS := {"head": "head1", "body": "defbod", "legs": "defbod"}
const DEFAULT_DIRECTION := "N"
const DEFAULT_FRAME := 3
const STAGE_SIZE := Vector2(180, 180)
const STAGE_PADDING := 8.0
const ANCHOR_OFFSET := Vector2(-7, -7)

var game_client_assets_root := ""

var _stage: Control
var _status: Label
var _layers: Dictionary = {}
var _file_cache: Dictionary = {}
var _texture_cache: Dictionary = {}


func bind(stage: Control, status: Label) -> void:
	_stage = stage
	_status = status
	_layers.clear()
	for slot_name in LAYER_ORDER:
		var layer := TextureRect.new()
		layer.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
		layer.stretch_mode = TextureRect.STRETCH_SCALE
		layer.mouse_filter = Control.MOUSE_FILTER_IGNORE
		stage.add_child(layer)
		_layers[slot_name] = layer


func clear_cache() -> void:
	_file_cache.clear()
	_texture_cache.clear()


func update(
	equippable: bool,
	slot_id: String,
	visual_key: String,
	direction: String,
	frame: int,
	visible_slots: Array
) -> void:
	if _stage == null or _layers.is_empty():
		return
	for slot_name in _layers:
		var layer := _layers[slot_name] as TextureRect
		layer.texture = null
		layer.visible = false
		layer.position = Vector2.ZERO
		layer.size = Vector2.ZERO

	if game_client_assets_root.is_empty() or not DirAccess.dir_exists_absolute(game_client_assets_root):
		_status.text = "The configured game_client_assets directory is unavailable."
		return

	var selected_direction := direction if not direction.is_empty() else DEFAULT_DIRECTION
	var selected_frame := frame if frame > 0 else DEFAULT_FRAME
	var keys := DEFAULT_VISUAL_KEYS.duplicate(true)
	if equippable and slot_id in visible_slots and not visual_key.is_empty() and visual_key != "None":
		keys[slot_id] = visual_key

	var loaded_selected := not equippable or slot_id.is_empty() or slot_id == "ring"
	var loaded_layers := []
	for slot_name in LAYER_ORDER:
		var asset_key := str(keys.get(slot_name, ""))
		if asset_key.is_empty():
			continue
		var texture := _load_texture(slot_name, asset_key, selected_frame, selected_direction)
		if texture == null:
			continue
		var layer := _layers[slot_name] as TextureRect
		loaded_layers.append({"slot_name": slot_name, "texture": texture, "layer": layer})
		if slot_name == slot_id:
			loaded_selected = true

	if not loaded_layers.is_empty():
		var source_bounds := _source_bounds(loaded_layers)
		var preview_scale := _preview_scale(source_bounds.size)
		for layer_entry_variant: Variant in loaded_layers:
			var layer_entry: Dictionary = layer_entry_variant
			var slot_name := str(layer_entry.get("slot_name", ""))
			var texture := layer_entry.get("texture") as Texture2D
			var layer := layer_entry.get("layer") as TextureRect
			if texture == null or layer == null:
				continue
			layer.texture = texture
			layer.visible = true
			layer.z_index = _z_index(slot_name, selected_direction)
			_place_layer(layer, texture, slot_name, source_bounds, preview_scale)

	if not equippable:
		_status.text = "Not equippable: showing the default player layers only."
	elif slot_id == "ring":
		_status.text = "Ring is gameplay equipment but has no visible paper-doll layer in the current client."
	elif loaded_selected:
		_status.text = "%s • frame %d • visual key %s" % [selected_direction, selected_frame, visual_key]
	else:
		_status.text = "No player-layer PNG matched %s in slot %s for %s frame %d." % [visual_key, slot_id, selected_direction, selected_frame]


func normalize_visual_key(value: String) -> String:
	var normalized := value.to_lower().replace("'", "").replace("’", "")
	for separator in [" ", "-", "/"]:
		normalized = normalized.replace(separator, "_")
	while normalized.contains("__"):
		normalized = normalized.replace("__", "_")
	return normalized.trim_prefix("_").trim_suffix("_")


func _load_texture(slot_name: String, asset_key: String, frame: int, direction: String) -> Texture2D:
	for fallback_frame in _frame_fallbacks(frame, direction):
		var file_path := _find_file(slot_name, asset_key, int(fallback_frame), direction)
		if file_path.is_empty():
			continue
		if _texture_cache.has(file_path):
			return _texture_cache[file_path] as Texture2D
		var image := Image.load_from_file(file_path)
		if image == null or image.is_empty():
			continue
		var texture := ImageTexture.create_from_image(image)
		_texture_cache[file_path] = texture
		return texture
	return null


func _source_bounds(loaded_layers: Array) -> Rect2:
	var has_bounds := false
	var source_bounds := Rect2(ANCHOR_OFFSET, Vector2.ZERO)
	for layer_entry_variant: Variant in loaded_layers:
		var layer_entry: Dictionary = layer_entry_variant
		var texture := layer_entry.get("texture") as Texture2D
		if texture == null:
			continue
		var slot_name := str(layer_entry.get("slot_name", ""))
		var layer_rect := Rect2(ANCHOR_OFFSET + _layer_offset(slot_name), texture.get_size())
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


func _place_layer(layer: TextureRect, texture: Texture2D, slot_name: String, source_bounds: Rect2, preview_scale: float) -> void:
	var display_size := texture.get_size() * preview_scale
	var group_size := source_bounds.size * preview_scale
	var group_origin := (STAGE_SIZE - group_size) * 0.5
	var source_position := ANCHOR_OFFSET + _layer_offset(slot_name)
	layer.size = display_size
	layer.position = group_origin + ((source_position - source_bounds.position) * preview_scale)


func _layer_offset(slot_name: String) -> Vector2:
	if slot_name == "right_hand":
		return Vector2.ZERO
	return Vector2.ZERO


func _find_file(slot_name: String, asset_key: String, frame: int, direction: String) -> String:
	var cache_key := "%s|%s|%d|%s" % [slot_name, asset_key, frame, direction]
	if _file_cache.has(cache_key):
		return str(_file_cache[cache_key])
	var directory_path := game_client_assets_root.path_join("actors").path_join("player").path_join(slot_name)
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
	if not values.has(DEFAULT_FRAME):
		values.append(DEFAULT_FRAME)
	return values


func _z_index(slot_name: String, direction: String) -> int:
	match slot_name:
		"right_hand":
			return 0 if direction == "W" else 30
		"cape":
			return 10
		"legs", "boots", "body":
			return 20
		"left_hand", "gloves", "head":
			return 30
		_:
			return 20
