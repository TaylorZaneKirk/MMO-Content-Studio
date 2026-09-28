# Authors scalar ordinary combat spells through the existing host client.
# Owns the form and preview; the host validates and commits durable content.
extends HBoxContainer
class_name MagicSpellEditor

const STUDIO_THEME := preload("res://scripts/studio_theme.gd")

const WORKSPACE_SUPPORT := preload("res://scripts/authoring_workspace_support.gd")
@onready var _client: AuthoringHostClient = %AuthoringHostClient
var _support := WORKSPACE_SUPPORT.new()
var _current: Dictionary = {}
var _fields: Dictionary = {}
var _list: ItemList
var _search: LineEdit
var _definition_id: LineEdit
var _state: Label
var _changes: VBoxContainer
var _validation: VBoxContainer
var _operation: OptionButton
var _preview: Button
var _apply: Button
var _status: Label
var _form: VBoxContainer
var _loading := false
var _preview_request: Dictionary = {}
var _pending_definition_id := ""


func _ready() -> void:
	_build_ui()
	_client.spell_catalog_received.connect(_on_catalog)
	_client.spell_definition_received.connect(_on_definition)
	_client.spell_preview_received.connect(_on_preview)
	_client.spell_mutation_completed.connect(_on_mutation)
	_client.request_failed.connect(_on_failed)
	_form.visible = false


func _build_ui() -> void:
	theme = STUDIO_THEME.item_theme()
	add_theme_constant_override("separation", 14)
	var catalog := _panel(240)
	_heading(catalog, "Spell library", 20)
	_search = LineEdit.new()
	_search.placeholder_text = "Search spells…"
	catalog.add_child(_search)
	_search.text_submitted.connect(func(value: String): _client.load_spells(value))
	_button(catalog, "+ New spell", _new_definition)
	_button(catalog, "Search / reload library", func(): _client.load_spells(_search.text))
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
		_client.load_spell(str(_list.get_item_metadata(index))))

	var editor := _panel()
	editor.get_parent().size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_heading(editor, "Spell details", 22)
	_note(editor, "Author ordinary combat spells. Casting is a separate game feature.")
	_form = VBoxContainer.new()
	_form.size_flags_vertical = Control.SIZE_EXPAND_FILL
	editor.add_child(_form)
	var pages := TabContainer.new()
	pages.size_flags_vertical = Control.SIZE_EXPAND_FILL
	pages.use_hidden_tabs_for_min_size = false
	_form.add_child(pages)
	var basics := _page(pages, "Basics", "Spell identity", "Spell damage, requirements, shard cost and cast XP are authored here.")
	_definition_id = _text_field("Spell ID", basics)
	_definition_id.placeholder_text = "lowercase_spell_id"
	_state = _label(basics, "Draft")
	_state.modulate = Color("91ecd7")
	_fields["display_name"] = _text_field("Display name", basics)
	_fields["tier"] = _number_field(basics, "Tier", 1, 4, 1)
	_label(basics, "Element")
	var element := OptionButton.new()
	for value: String in ["air", "earth", "fire", "water"]:
		element.add_item(value.capitalize())
		element.set_item_metadata(element.item_count - 1, value)
	basics.add_child(element)
	element.item_selected.connect(_invalidate)
	_fields["element"] = element
	_fields["required_magic_level"] = _number_field(basics, "Required Magic level", 1, 99, 1)
	_fields["shard_cost"] = _number_field(basics, "Crystal Shard cost", 1, 2147483647, 1)
	_fields["successful_hit_min_damage"] = _number_field(basics, "Successful-hit minimum damage", 0, 2147483647, 0)
	_fields["base_max_hit"] = _number_field(basics, "Base maximum hit", 0, 2147483647, 0)
	_fields["base_cast_xp_tenths"] = _number_field(basics, "Base cast XP (tenths; 15 = 1.5 XP)", 0, 2147483647, 0)

	var review := _panel(264)
	_heading(review, "Review & apply", 20)
	_label(review, "Operation")
	_operation = OptionButton.new()
	for operation: String in ["save_draft", "save_and_publish", "publish", "disable", "delete"]:
		_operation.add_item(_support.operation_name(operation))
		_operation.set_item_metadata(_operation.item_count - 1, operation)
	_operation.item_selected.connect(_invalidate)
	review.add_child(_operation)
	_preview = _button(review, "1. Preview changes", func(): _request_preview(str(_operation.get_selected_metadata())))
	_preview.theme_type_variation = "PrimaryButton"
	_preview.disabled = true
	_apply = _button(review, "2. Apply changes", _apply_preview)
	_apply.disabled = true
	_status = _label(review, "Select a Spell or create a draft to begin.")
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
	scroll.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
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


func _number_field(parent: Node, caption: String, minimum: int, maximum: int, value: int) -> SpinBox:
	var field := VBoxContainer.new()
	field.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	field.add_theme_constant_override("separation", 6)
	parent.add_child(field)
	_label(field, caption).autowrap_mode = TextServer.AUTOWRAP_OFF
	var number := SpinBox.new()
	number.custom_minimum_size = Vector2(160, 40)
	number.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	number.min_value = minimum
	number.max_value = maximum
	number.step = 1
	number.value = value
	field.add_child(number)
	number.value_changed.connect(_invalidate)
	return number


func open_resource(spell_id: String) -> void:
	_invalidate()
	_client.load_spell(spell_id)


func _on_catalog(payload: Dictionary) -> void:
	_list.clear()
	for item: Dictionary in payload.get("items", []):
		var index := _list.add_item(str(item["display_name"]))
		_list.set_item_metadata(index, item["spell_id"])
		_list.set_item_tooltip(index, "%s\n%s" % [item["spell_id"], item["publication_state"]])
		if item["spell_id"] == _current.get("spell_id", ""): _list.select(index)


func _new_definition() -> void:
	_on_definition({"spell_id": "", "publication_state": "Draft", "draft": {"display_name": "", "tier": 1, "element": "air", "required_magic_level": 1, "shard_cost": 1, "successful_hit_min_damage": 0, "base_max_hit": 0, "base_cast_xp_tenths": 0}})


func _on_definition(payload: Dictionary) -> void:
	_loading = true
	_current = payload
	_list.deselect_all()
	for index in _list.item_count:
		if _list.get_item_metadata(index) == payload.get("spell_id", ""): _list.select(index)
	_definition_id.text = str(payload.get("spell_id", ""))
	_definition_id.editable = payload.get("updated_at_utc") == null
	_state.text = str(payload.get("publication_state", "Draft"))
	_operation.select(1 if _state.text == "Published" else 0)
	var draft: Dictionary = payload.get("draft", {})
	for key: String in _fields:
		var control: Control = _fields[key]
		if control is LineEdit or control is TextEdit: control.text = str(draft.get(key, "")) if draft.get(key) != null else ""
		elif control is OptionButton:
			for index in control.item_count:
				if control.get_item_metadata(index) == draft.get(key): control.select(index)
		elif control is SpinBox: control.value = float(draft.get(key, 0.0)) if draft.get(key) != null else 0.0
	_loading = false
	_form.visible = true
	_preview.disabled = false
	_status.text = "Editing %s." % payload.get("spell_id", "") if not str(payload.get("spell_id", "")).is_empty() else "New draft. Choose a stable definition ID."
	_invalidate()


# XP is transmitted as integer tenths, never a floating-point authority.
func _draft() -> Dictionary:
	var draft: Dictionary = {}
	for key: String in _fields:
		var control: Control = _fields[key]
		if control is LineEdit: draft[key] = control.text.strip_edges()
		elif control is OptionButton: draft[key] = str(control.get_selected_metadata())
		elif control is SpinBox: draft[key] = int(control.value)
	return draft


func _request_preview(operation: String) -> void:
	_invalidate()
	_pending_definition_id = _definition_id.text.strip_edges()
	_preview_request = {"draft": _draft(), "expected_updated_at_utc": _current.get("updated_at_utc"), "target_operation": operation}
	_client.preview_spell(_pending_definition_id, _preview_request)


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
	_client.mutate_spell(_pending_definition_id, operation, request)


func _on_mutation(payload: Dictionary) -> void:
	if payload.get("definition") is Dictionary:
		_on_definition(payload["definition"])
	else:
		_new_definition()
	_support.render_validation(_validation, payload.get("messages", []))
	_status.text = "Operation committed and reloaded. Casting is not implemented in M1."
	_client.load_spells()


func _on_failed(operation: String, message: String, errors: Array) -> void:
	if not operation.begins_with("spell_"): return
	_invalidate()
	_status.text = message
	_support.render_validation(_validation, errors)


func _invalidate(_value: Variant = null) -> void:
	if _loading or _apply == null: return
	_preview_request = {}
	_support.clear_preview(_apply, _changes, _validation, "2. Apply changes")
