# Authors one reusable World Object through the existing host client. This workspace
# owns form/preview state; the host validates and commits, and Tiled owns placements.
extends HBoxContainer
class_name WorldObjectEditor

const WORKSPACE_SUPPORT := preload("res://scripts/authoring_workspace_support.gd")
const NUMBER_FIELDS := [
	["footprint_width_tiles", "Footprint width", 1, 1024, 1],
	["footprint_height_tiles", "Footprint height", 1, 1024, 1],
	["source_width", "Source width", 1, 65536, 1],
	["source_height", "Source height", 1, 65536, 1],
	["visual_anchor_offset_x", "Anchor X", -65536, 65536, 0.1],
	["visual_anchor_offset_y", "Anchor Y", -65536, 65536, 0.1],
	["visual_render_scale", "Render scale", 0.001, 1024, 0.001],
	["visual_animation_fps", "Animation FPS (0 = none)", 0, 1000, 0.1],
]

@onready var _client: AuthoringHostClient = %AuthoringHostClient
var _support := WORKSPACE_SUPPORT.new()
var _options: Dictionary = {}
var _current: Dictionary = {}
var _fields: Dictionary = {}
var _list: ItemList
var _search: LineEdit
var _definition_id: LineEdit
var _state: Label
var _actions: VBoxContainer
var _frames: VBoxContainer
var _changes: VBoxContainer
var _validation: VBoxContainer
var _apply: Button
var _status: Label
var _visual: TextureRect
var _animation := Timer.new()
var _preview_textures: Array[Texture2D] = []
var _frame_index := 0
var _form: VBoxContainer
var _loading := false
var _preview_request: Dictionary = {}
var _pending_definition_id := ""


func _ready() -> void:
	_build_ui()
	_client.world_object_options_received.connect(_on_options)
	_client.world_object_catalog_received.connect(_on_catalog)
	_client.world_object_definition_received.connect(_on_definition)
	_client.world_object_preview_received.connect(_on_preview)
	_client.world_object_mutation_completed.connect(_on_mutation)
	_client.request_failed.connect(_on_failed)
	add_child(_animation)
	_animation.timeout.connect(_advance_frame)
	_form.visible = false


func _build_ui() -> void:
	var catalog := VBoxContainer.new()
	catalog.custom_minimum_size.x = 250
	add_child(catalog)
	_search = LineEdit.new()
	_search.placeholder_text = "Search World Objects"
	catalog.add_child(_search)
	_search.text_submitted.connect(func(value: String): _client.load_world_objects(value))
	_button(catalog, "Search / Refresh", func(): _client.load_world_objects(_search.text))
	_button(catalog, "New", _new_definition)
	_list = ItemList.new()
	_list.size_flags_vertical = Control.SIZE_EXPAND_FILL
	catalog.add_child(_list)
	_list.item_selected.connect(func(index: int):
		_invalidate()
		_client.load_world_object(str(_list.get_item_metadata(index))))
	var scroll := ScrollContainer.new()
	scroll.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	add_child(scroll)
	_form = VBoxContainer.new()
	_form.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	scroll.add_child(_form)
	_label(_form, "Reusable definitions only. Place objects in Tiled.")
	_definition_id = _text_field("Definition ID", _form)
	_state = _label(_form, "Draft")
	_fields["display_name"] = _text_field("Display name", _form)
	var blocks := CheckBox.new()
	blocks.text = "Blocks movement"
	_form.add_child(blocks)
	blocks.toggled.connect(_invalidate)
	_fields["blocks_movement"] = blocks
	_fields["visual_texture_path"] = _text_field("Texture path", _form)
	var grid := GridContainer.new()
	grid.columns = 2
	_form.add_child(grid)
	for spec: Array in NUMBER_FIELDS:
		_label(grid, str(spec[1]))
		var number := SpinBox.new()
		number.min_value = float(spec[2])
		number.max_value = float(spec[3])
		number.step = float(spec[4])
		number.allow_greater = true
		number.allow_lesser = str(spec[0]).begins_with("visual_anchor")
		grid.add_child(number)
		number.value_changed.connect(_invalidate)
		_fields[str(spec[0])] = number
	_label(_form, "Public interactions (top to bottom is menu order)")
	_actions = VBoxContainer.new()
	_form.add_child(_actions)
	_button(_form, "Add interaction", func(): _add_action({}); _invalidate())
	_label(_form, "Animation frames (top to bottom is playback order)")
	_frames = VBoxContainer.new()
	_form.add_child(_frames)
	_button(_form, "Add frame", func(): _add_frame(""); _invalidate())
	_button(_form, "Refresh visual preview", _refresh_visual)
	_visual = TextureRect.new()
	_visual.custom_minimum_size = Vector2(240, 160)
	_visual.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	_visual.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED
	_form.add_child(_visual)
	var operations := HBoxContainer.new()
	_form.add_child(operations)
	_button(operations, "Validate / Preview", func(): _request_preview("save_draft"))
	for operation: String in ["save_draft", "publish", "disable", "delete"]:
		_button(operations, _support.operation_name(operation), _request_preview.bind(operation))
	_apply = _button(_form, "Apply Previewed Operation", _apply_preview)
	_apply.disabled = true
	_status = _label(_form, "Select a definition or create a draft.")
	_changes = VBoxContainer.new()
	_form.add_child(_changes)
	_validation = VBoxContainer.new()
	_form.add_child(_validation)


func _label(parent: Node, value: String) -> Label:
	var label := Label.new()
	label.text = value
	parent.add_child(label)
	return label


func _button(parent: Node, caption: String, action: Callable) -> Button:
	var button := Button.new()
	button.text = caption
	button.pressed.connect(action)
	parent.add_child(button)
	return button


func _text_field(caption: String, parent: Node) -> LineEdit:
	_label(parent, caption)
	var field := LineEdit.new()
	field.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	parent.add_child(field)
	field.text_changed.connect(_invalidate)
	return field


# Ordered rows own their controls. Moving a row changes only serialized order.
func _row_tools(row: HBoxContainer, parent: VBoxContainer) -> void:
	_button(row, "↑", func(): parent.move_child(row, maxi(0, row.get_index() - 1)); _invalidate())
	_button(row, "↓", func(): parent.move_child(row, mini(parent.get_child_count() - 1, row.get_index() + 1)); _invalidate())
	_button(row, "Remove", func(): parent.remove_child(row); row.queue_free(); _invalidate())


func _add_action(value: Dictionary) -> void:
	var row := HBoxContainer.new()
	_actions.add_child(row)
	var action := LineEdit.new()
	action.placeholder_text = "action_id"
	action.text = str(value.get("action_id", ""))
	row.add_child(action)
	action.text_changed.connect(_invalidate)
	var label := LineEdit.new()
	label.placeholder_text = "Label"
	label.text = str(value.get("label", ""))
	row.add_child(label)
	label.text_changed.connect(_invalidate)
	var default_action := CheckBox.new()
	default_action.text = "Default"
	default_action.button_pressed = bool(value.get("is_default", false))
	row.add_child(default_action)
	default_action.toggled.connect(func(selected: bool):
		if selected:
			for other: Node in _actions.get_children():
				if other != row: (other.get_child(2) as CheckBox).set_pressed_no_signal(false)
		_invalidate())
	_row_tools(row, _actions)


func _add_frame(path: String) -> void:
	var row := HBoxContainer.new()
	_frames.add_child(row)
	var field := LineEdit.new()
	field.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	field.placeholder_text = "res://assets/...png"
	field.text = path
	row.add_child(field)
	field.text_changed.connect(_invalidate)
	_row_tools(row, _frames)


func _on_options(payload: Dictionary) -> void:
	_options = payload


func _on_catalog(payload: Dictionary) -> void:
	_list.clear()
	for item: Dictionary in payload.get("items", []):
		var index := _list.add_item("%s [%s]" % [item["display_name"], item["publication_state"]])
		_list.set_item_metadata(index, item["definition_id"])


func _new_definition() -> void:
	if _options.is_empty(): return
	_on_definition({"definition_id": "", "publication_state": "Draft", "draft": _options.get("defaults", {})})


func _on_definition(payload: Dictionary) -> void:
	_loading = true
	_current = payload
	_definition_id.text = str(payload.get("definition_id", ""))
	_definition_id.editable = payload.get("updated_at_utc") == null
	_state.text = str(payload.get("publication_state", "Draft"))
	var draft: Dictionary = payload.get("draft", {})
	for key: String in _fields:
		var control: Control = _fields[key]
		if control is LineEdit: control.text = str(draft.get(key, ""))
		elif control is CheckBox: control.button_pressed = bool(draft.get(key, false))
		elif control is SpinBox: control.value = float(draft.get(key, 0.0)) if draft.get(key) != null else 0.0
	_support.clear_container(_actions)
	_support.clear_container(_frames)
	for action: Dictionary in draft.get("public_interactions", []): _add_action(action)
	for path: String in draft.get("visual_animation_frames", []): _add_frame(path)
	_loading = false
	_form.visible = true
	_invalidate()
	_refresh_visual()


func _draft() -> Dictionary:
	var draft := {}
	for key: String in _fields:
		var control: Control = _fields[key]
		if control is LineEdit: draft[key] = control.text.strip_edges()
		elif control is CheckBox: draft[key] = control.button_pressed
		elif control is SpinBox: draft[key] = control.value
	for key: String in ["source_width", "source_height", "footprint_width_tiles", "footprint_height_tiles"]:
		draft[key] = int(draft[key])
	if draft["visual_animation_fps"] == 0: draft["visual_animation_fps"] = null
	var actions: Array = []
	for row: Node in _actions.get_children():
		actions.append({"action_id": (row.get_child(0) as LineEdit).text.strip_edges(), "label": (row.get_child(1) as LineEdit).text.strip_edges(), "is_default": (row.get_child(2) as CheckBox).button_pressed})
	draft["public_interactions"] = actions
	var frames: Array = []
	for row: Node in _frames.get_children(): frames.append((row.get_child(0) as LineEdit).text.strip_edges())
	draft["visual_animation_frames"] = frames
	return draft


func _request_preview(operation: String) -> void:
	_invalidate()
	_pending_definition_id = _definition_id.text.strip_edges()
	_preview_request = {"draft": _draft(), "expected_updated_at_utc": _current.get("updated_at_utc"), "target_operation": operation}
	_client.preview_world_object(_pending_definition_id, _preview_request)


func _on_preview(payload: Dictionary) -> void:
	# Ignore responses for a form edited or replaced while the request was in flight.
	if _preview_request.is_empty(): return
	_support.render_changes(_changes, payload.get("changes", []))
	_support.render_validation(_validation, payload.get("messages", []))
	var operation := str(payload.get("target_operation", ""))
	_support.accept_preview(operation, str(payload.get("preview_signature", "")), bool(payload.get("applicable", false)), _apply, "Apply: " + _support.operation_name(operation))


func _apply_preview() -> void:
	if not _support.can_apply(_support.preview_operation, _support.preview_signature): return
	var request := _preview_request.duplicate(true)
	request["preview_signature"] = _support.preview_signature
	var operation: String = _support.preview_operation
	_invalidate()
	_client.mutate_world_object(_pending_definition_id, operation, request)


func _on_mutation(payload: Dictionary) -> void:
	if payload.get("definition") is Dictionary:
		_on_definition(payload["definition"])
	else:
		_new_definition()
	_support.render_validation(_validation, payload.get("messages", []))
	_status.text = "Operation committed and reloaded. Regenerate maps and package content before restarting the game."
	_client.load_world_objects(_search.text)


func _on_failed(operation: String, message: String, errors: Array) -> void:
	if not operation.begins_with("world_object_"): return
	_invalidate()
	_status.text = message
	_support.render_validation(_validation, errors)


func _invalidate(_value: Variant = null) -> void:
	if _loading or _apply == null: return
	_preview_request = {}
	_support.clear_preview(_apply, _changes, _validation)


# Preview uses the normal game asset root and Godot image loading; no asset import.
func _refresh_visual() -> void:
	_animation.stop()
	_preview_textures.clear()
	var draft := _draft()
	var paths: Array = draft["visual_animation_frames"]
	if paths.is_empty(): paths = [draft["visual_texture_path"]]
	var root := str(_options.get("game_assets_root", ""))
	for path: String in paths:
		if root.is_empty() or not path.begins_with("res://assets/") or path.contains(".."): continue
		var file_path := root.path_join(path.trim_prefix("res://assets/"))
		if not FileAccess.file_exists(file_path): continue
		var image := Image.load_from_file(file_path)
		if image != null and not image.is_empty(): _preview_textures.append(ImageTexture.create_from_image(image))
	_frame_index = 0
	_visual.texture = null if _preview_textures.is_empty() else _preview_textures[0]
	if _preview_textures.size() > 1 and draft["visual_animation_fps"] != null:
		_animation.start(1.0 / maxf(0.1, float(draft["visual_animation_fps"])))


func _advance_frame() -> void:
	if _preview_textures.is_empty(): return
	_frame_index = (_frame_index + 1) % _preview_textures.size()
	_visual.texture = _preview_textures[_frame_index]
