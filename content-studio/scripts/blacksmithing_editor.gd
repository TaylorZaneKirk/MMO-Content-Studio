# Owns recipe form/preview state; host owns validation, persistence and publication.
extends HBoxContainer
const STUDIO_THEME := preload("res://scripts/studio_theme.gd")
const WORKSPACE_SUPPORT := preload("res://scripts/authoring_workspace_support.gd")
@onready var _client: AuthoringHostClient = %AuthoringHostClient
var _support := WORKSPACE_SUPPORT.new()
var _current: Dictionary = {}
var _fields: Dictionary = {}
var _inputs: VBoxContainer
var _list: ItemList
var _search: LineEdit
var _definition_id: LineEdit
var _state: Label
var _changes: VBoxContainer
var _validation: VBoxContainer
var _operation: OptionButton
var _recipe_operation: OptionButton
var _preview: Button
var _apply: Button
var _status: Label
var _form: VBoxContainer
var _loading := false
var _preview_request: Dictionary = {}
var _pending_definition_id := ""

func _ready() -> void:
	_build_ui()
	_client.blacksmithing_catalog_received.connect(_on_catalog)
	_client.blacksmithing_definition_received.connect(_on_definition)
	_client.blacksmithing_preview_received.connect(_on_preview)
	_client.blacksmithing_mutation_completed.connect(_on_mutation)
	_client.request_failed.connect(_on_failed)
	_new_definition()

func open_resource(recipe_id: String) -> void:
	_client.load_blacksmithing(recipe_id)

func _build_ui() -> void:
	theme = STUDIO_THEME.item_theme()
	add_theme_constant_override("separation", 14)
	var library := _panel(240)
	_heading(library, "Blacksmithing recipes", 20)
	_search = _text_field("Search", library)
	_search.text_submitted.connect(func(value: String): _client.load_blacksmithing_recipes(value))
	_button(library, "+ New recipe", _new_definition)
	_button(library, "Reload library", func(): _client.load_blacksmithing_recipes(_search.text))
	_list = ItemList.new()
	_list.size_flags_vertical = Control.SIZE_EXPAND_FILL
	library.add_child(_list)
	_list.item_selected.connect(func(index: int): _invalidate(); _client.load_blacksmithing(str(_list.get_item_metadata(index))))
	var editor := _panel()
	editor.get_parent().size_flags_horizontal = Control.SIZE_EXPAND_FILL
	var scroll := ScrollContainer.new()
	scroll.size_flags_vertical = Control.SIZE_EXPAND_FILL
	editor.add_child(scroll)
	_form = VBoxContainer.new()
	_form.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	scroll.add_child(_form)
	_heading(_form, "Recipe details", 22)
	_note(_form, "Guaranteed success. Each unit uses these ordered ingredients. Publication requires published items and a station.")
	_definition_id = _text_field("Recipe ID", _form)
	_state = _label(_form, "Draft")
	_fields["display_name"] = _text_field("Display name", _form)
	_label(_form, "Operation and required station")
	_recipe_operation = OptionButton.new()
	_recipe_operation.add_item("Smelt — Bronze smelter — no tool")
	_recipe_operation.add_item("Forge — Anvil — Blacksmithing hammer in inventory")
	_form.add_child(_recipe_operation)
	_recipe_operation.item_selected.connect(_invalidate)
	_label(_form, "Ordered ingredients: item ID / quantity")
	_inputs = VBoxContainer.new()
	_form.add_child(_inputs)
	_button(_form, "+ Ingredient", func(): _add_input({}); _invalidate())
	_fields["output_item_id"] = _text_field("Output item ID", _form)
	_fields["output_quantity"] = _number("Output quantity", 1, 2147483647, 1)
	_fields["required_level"] = _number("Required Blacksmithing level", 1, 99, 1)
	_fields["xp"] = _number("Blacksmithing XP per unit", 0, 214748364.7, 0.1)
	_fields["duration_ms"] = _number("Duration per unit (milliseconds)", 1, 2147483647, 1)
	_operation = OptionButton.new()
	for operation: String in ["save_draft", "publish", "disable", "delete"]:
		_operation.add_item(_support.operation_name(operation))
		_operation.set_item_metadata(_operation.item_count - 1, operation)
	editor.add_child(_operation)
	_operation.item_selected.connect(_invalidate)
	_preview = _button(editor, "1. Validate / Preview", func(): _request_preview(str(_operation.get_item_metadata(_operation.selected))))
	_apply = _button(editor, "2. Apply changes", _apply_preview)
	_changes = VBoxContainer.new()
	_form.add_child(_changes)
	_validation = VBoxContainer.new()
	_form.add_child(_validation)
	_status = _label(editor, "")

func _number(caption: String, minimum: float, maximum: float, step: float) -> SpinBox:
	_label(_form, caption)
	var field := SpinBox.new()
	field.min_value = minimum
	field.max_value = maximum
	field.step = step
	_form.add_child(field)
	field.value_changed.connect(_invalidate)
	return field

func _add_input(value: Dictionary) -> void:
	var row := HBoxContainer.new()
	_inputs.add_child(row)
	var item := LineEdit.new()
	item.placeholder_text = "item_id"
	item.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	item.text = str(value.get("item_id", ""))
	row.add_child(item)
	item.text_changed.connect(_invalidate)
	var quantity := SpinBox.new()
	quantity.min_value = 1
	quantity.max_value = 2147483647
	quantity.value = int(value.get("quantity", 1))
	row.add_child(quantity)
	quantity.value_changed.connect(_invalidate)
	_row_tools(row, _inputs)

func _on_catalog(payload: Dictionary) -> void:
	_list.clear()
	for item: Dictionary in payload.get("items", []):
		var index := _list.add_item("%s [%s]" % [item["display_name"], item["publication_state"]])
		_list.set_item_metadata(index, item["recipe_id"])

func _new_definition() -> void:
	_on_definition({"recipe_id": "", "publication_state": "Draft", "draft": {"required_level": 1, "output_quantity": 1, "duration_ms": 3000, "inputs": []}})

func _on_definition(payload: Dictionary) -> void:
	_loading = true
	_current = payload
	_definition_id.text = str(payload.get("recipe_id", ""))
	_definition_id.editable = payload.get("updated_at_utc") == null
	_state.text = str(payload.get("publication_state", "Draft"))
	var draft: Dictionary = payload.get("draft", {})
	for key: String in _fields:
		var control: Control = _fields[key]
		if control is LineEdit: control.text = str(draft.get(key, ""))
		elif control is SpinBox: control.value = float(draft.get(key, 0))
	_fields["xp"].value = float(draft.get("xp_tenths", 0)) / 10.0
	_recipe_operation.select(1 if draft.get("operation") == "forge" else 0)
	_support.clear_container(_inputs)
	for ingredient: Dictionary in draft.get("inputs", []): _add_input(ingredient)
	_loading = false
	_invalidate()

func _draft() -> Dictionary:
	var inputs: Array = []
	for row: Node in _inputs.get_children():
		inputs.append({"item_id": row.get_child(0).text.strip_edges(), "quantity": int(row.get_child(1).value)})
	var forge := _recipe_operation.selected == 1
	return {"recipe_id": _definition_id.text.strip_edges(), "display_name": _fields["display_name"].text.strip_edges(),
		"operation": "forge" if forge else "smelt", "station_definition_id": "blacksmithing_anvil" if forge else "bronze_smelter",
		"required_inventory_tool_id": "blacksmithing_hammer" if forge else null, "inputs": inputs,
		"output_item_id": _fields["output_item_id"].text.strip_edges(), "output_quantity": int(_fields["output_quantity"].value),
		"required_level": int(_fields["required_level"].value), "xp_tenths": int(round(_fields["xp"].value * 10.0)),
		"duration_ms": int(_fields["duration_ms"].value)}

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


func _request_preview(operation: String) -> void:
	_invalidate()
	_pending_definition_id = _definition_id.text.strip_edges()
	_preview_request = {"draft": _draft(), "expected_updated_at_utc": _current.get("updated_at_utc"), "target_operation": operation}
	_client.preview_blacksmithing(_pending_definition_id, _preview_request)


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
	_client.mutate_blacksmithing(_pending_definition_id, operation, request)


func _on_mutation(payload: Dictionary) -> void:
	if payload.get("definition") is Dictionary:
		_on_definition(payload["definition"])
	else:
		_new_definition()
	_support.render_validation(_validation, payload.get("messages", []))
	_status.text = "Operation committed and reloaded. Export recipes and package content before restarting the game."
	_client.load_blacksmithing_recipes(_search.text)


func _on_failed(operation: String, message: String, errors: Array) -> void:
	if not operation.begins_with("blacksmithing_"): return
	_invalidate()
	_status.text = message
	_support.render_validation(_validation, errors)


func _invalidate(_value: Variant = null) -> void:
	if _loading or _apply == null: return
	_preview_request = {}
	_support.clear_preview(_apply, _changes, _validation, "2. Apply changes")
