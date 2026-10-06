# Authors one reusable World Object through the existing host client. This workspace
# owns form/preview state; the host validates and commits, and Tiled owns placements.
extends HBoxContainer
class_name WorldObjectEditor

const STUDIO_THEME := preload("res://scripts/studio_theme.gd")

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
var _operation: OptionButton
var _preview: Button
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
	theme = STUDIO_THEME.item_theme()
	add_theme_constant_override("separation", 14)
	var catalog := _panel(240)
	_heading(catalog, "Object library", 20)
	_search = LineEdit.new()
	_search.placeholder_text = "Search objects…"
	catalog.add_child(_search)
	_search.text_submitted.connect(func(value: String): _client.load_world_objects(value))
	_button(catalog, "+ New object", _new_definition)
	_button(catalog, "Search / reload library", func(): _client.load_world_objects(_search.text))
	_list = ItemList.new()
	_list.size_flags_vertical = Control.SIZE_EXPAND_FILL
	_list.add_theme_color_override("font_color", Color("e5edf5"))
	_list.add_theme_stylebox_override("panel", STUDIO_THEME.box("101923", "354355"))
	_list.add_theme_stylebox_override("selected", STUDIO_THEME.box("29443f", "66d9c1"))
	_list.add_theme_stylebox_override("selected_focus", STUDIO_THEME.box("29443f", "66d9c1"))
	_list.add_theme_constant_override("v_separation", 12)
	catalog.add_child(_list)
	_list.item_selected.connect(func(index: int):
		_invalidate()
		_client.load_world_object(str(_list.get_item_metadata(index))))

	var editor := _panel()
	editor.get_parent().size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_heading(editor, "Object details", 22)
	_note(editor, "Author a reusable object here. Place it in Tiled.")
	_form = VBoxContainer.new()
	_form.size_flags_vertical = Control.SIZE_EXPAND_FILL
	editor.add_child(_form)
	var pages := TabContainer.new()
	pages.size_flags_vertical = Control.SIZE_EXPAND_FILL
	pages.use_hidden_tabs_for_min_size = false
	_form.add_child(pages)
	var basics := _page(pages, "Basics", "Identity & footprint", "Name the object and define the space it occupies in the world.")
	var appearance := _page(pages, "Appearance", "Artwork & alignment", "Choose the texture, source dimensions and placement offsets.")
	var interactions := _page(pages, "Interactions", "Player actions", "Top to bottom is menu order. Mark the action used by default.")
	var animation := _page(pages, "Animation", "Frames & playback", "Frames play from top to bottom. Leave the list empty to use the main texture.")
	_definition_id = _text_field("Definition ID", basics)
	_definition_id.placeholder_text = "copper_rock"
	_state = _label(basics, "Draft")
	_state.modulate = Color("91ecd7")
	_fields["display_name"] = _text_field("Display name", basics)
	var blocks := CheckBox.new()
	blocks.text = "Blocks movement"
	basics.add_child(blocks)
	blocks.toggled.connect(_invalidate)
	_fields["blocks_movement"] = blocks
	_fields["visual_texture_path"] = _text_field("Texture path", appearance)
	_fields["visual_texture_path"].placeholder_text = "res://assets/maps/objects/world_objects/…png"
	var grids := {}
	for page: VBoxContainer in [basics, appearance, animation]:
		var grid := GridContainer.new()
		grid.columns = 2
		grid.add_theme_constant_override("h_separation", 16)
		grid.add_theme_constant_override("v_separation", 12)
		page.add_child(grid)
		grids[page] = grid
	for spec: Array in NUMBER_FIELDS:
		var page := appearance
		if str(spec[0]).begins_with("footprint_"): page = basics
		elif spec[0] == "visual_animation_fps": page = animation
		var grid: GridContainer = grids[page]
		_label(grid, str(spec[1])).autowrap_mode = TextServer.AUTOWRAP_OFF
		var number := SpinBox.new()
		number.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		number.min_value = float(spec[2])
		number.max_value = float(spec[3])
		number.step = float(spec[4])
		number.allow_greater = true
		number.allow_lesser = str(spec[0]).begins_with("visual_anchor")
		grid.add_child(number)
		number.value_changed.connect(_invalidate)
		_fields[str(spec[0])] = number
	_actions = VBoxContainer.new()
	_actions.add_theme_constant_override("separation", 12)
	interactions.add_child(_actions)
	_button(interactions, "+ Add interaction", func(): _add_action({}); _invalidate())
	_frames = VBoxContainer.new()
	_frames.add_theme_constant_override("separation", 12)
	animation.add_child(_frames)
	_button(animation, "+ Add frame", func(): _add_frame(""); _invalidate())

	var review := _panel(264)
	_heading(review, "Review & apply", 20)
	_visual = TextureRect.new()
	_visual.custom_minimum_size = Vector2(0, 120)
	_visual.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	_visual.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED
	review.add_child(_visual)
	_button(review, "Refresh visual preview", _refresh_visual)
	_label(review, "Operation")
	_operation = OptionButton.new()
	for operation: String in ["save_draft", "publish", "disable", "delete"]:
		_operation.add_item(_support.operation_name(operation))
		_operation.set_item_metadata(_operation.item_count - 1, operation)
	_operation.item_selected.connect(_invalidate)
	review.add_child(_operation)
	_preview = _button(review, "1. Preview changes", func(): _request_preview(str(_operation.get_selected_metadata())))
	_preview.theme_type_variation = "PrimaryButton"
	_preview.disabled = true
	_apply = _button(review, "2. Apply changes", _apply_preview)
	_apply.disabled = true
	_status = _label(review, "Select an object or create a draft to begin.")
	_status.modulate = Color("a9b8c9")
	var scroll := ScrollContainer.new()
	scroll.size_flags_vertical = Control.SIZE_EXPAND_FILL
	scroll.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	review.add_child(scroll)
	var feedback := VBoxContainer.new()
	feedback.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	feedback.add_theme_constant_override("separation", 12)
	scroll.add_child(feedback)
	_heading(feedback, "Validation", 16)
	_validation = VBoxContainer.new()
	feedback.add_child(_validation)
	_heading(feedback, "Changes", 16)
	_changes = VBoxContainer.new()
	feedback.add_child(_changes)


func _panel(width: float = 0) -> VBoxContainer:
	var panel := PanelContainer.new()
	panel.custom_minimum_size.x = width
	var style := STUDIO_THEME.box("17212e", "354355")
	style.content_margin_left = 16
	style.content_margin_right = 16
	style.content_margin_top = 14
	style.content_margin_bottom = 14
	panel.add_theme_stylebox_override("panel", style)
	add_child(panel)
	var content := VBoxContainer.new()
	content.add_theme_constant_override("separation", 12)
	panel.add_child(content)
	return content


func _heading(parent: Node, value: String, size: int) -> void:
	_label(parent, value).add_theme_font_size_override("font_size", size)


func _note(parent: Node, value: String) -> void:
	_label(parent, value).modulate = Color("a9b8c9")


func _page(pages: TabContainer, title: String, heading: String, description: String) -> VBoxContainer:
	var scroll := ScrollContainer.new()
	scroll.name = title
	pages.add_child(scroll)
	var page := VBoxContainer.new()
	page.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	page.add_theme_constant_override("separation", 12)
	scroll.add_child(page)
	_heading(page, heading, 20)
	_note(page, description)
	return page


func _label(parent: Node, value: String) -> Label:
	var label := Label.new()
	label.text = value
	label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
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
	action.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	action.placeholder_text = "action_id"
	action.text = str(value.get("action_id", ""))
	row.add_child(action)
	action.text_changed.connect(_invalidate)
	var label := LineEdit.new()
	label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
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
		var index := _list.add_item(str(item["display_name"]))
		_list.set_item_metadata(index, item["definition_id"])
		_list.set_item_tooltip(index, "%s\n%s" % [item["definition_id"], item["publication_state"]])
		if item["definition_id"] == _current.get("definition_id", ""): _list.select(index)


func _new_definition() -> void:
	if _options.is_empty(): return
	_on_definition({"definition_id": "", "publication_state": "Draft", "draft": _options.get("defaults", {})})


func _on_definition(payload: Dictionary) -> void:
	_loading = true
	_current = payload
	_list.deselect_all()
	for index in _list.item_count:
		if _list.get_item_metadata(index) == payload.get("definition_id", ""): _list.select(index)
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
	_preview.disabled = false
	_status.text = "Editing %s." % payload.get("definition_id", "") if not str(payload.get("definition_id", "")).is_empty() else "New draft. Choose a stable definition ID."
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
	_support.accept_preview(operation, str(payload.get("preview_signature", "")), bool(payload.get("applicable", false)), _apply, "2. Apply " + _support.operation_name(operation))


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
	_support.clear_preview(_apply, _changes, _validation, "2. Apply changes")


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
