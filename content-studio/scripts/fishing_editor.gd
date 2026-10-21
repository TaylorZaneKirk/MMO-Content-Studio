# Owns method form/preview state; host owns validation, persistence and publication.
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
var _preview: Button
var _apply: Button
var _status: Label
var _form: VBoxContainer
var _loading := false
var _pending := ""
var _baseline := ""
var _loading_id := ""
var _preview_request: Dictionary = {}
var _pending_definition_id := ""

func _ready() -> void:
	_build_ui()
	_client.fishing_catalog_received.connect(_on_catalog)
	_client.fishing_definition_received.connect(_receive_definition)
	_client.fishing_preview_received.connect(_on_preview)
	_client.fishing_mutation_completed.connect(_on_mutation)
	_client.request_failed.connect(_on_failed)
	_show_new()

func open_resource(action_id: String) -> void:
	_navigate(action_id)

func _build_ui() -> void:
	theme = STUDIO_THEME.item_theme()
	add_theme_constant_override("separation", 14)
	var library := _panel(240)
	_heading(library, "Fishing methods", 20)
	_search = _text_field("Search", library)
	_search.text_submitted.connect(func(value: String): _client.load_fishing_methods(value))
	_button(library, "+ New method", _new_definition)
	_button(library, "Reload library", func(): _client.load_fishing_methods(_search.text))
	_list = ItemList.new()
	_list.size_flags_vertical = Control.SIZE_EXPAND_FILL
	library.add_child(_list)
	_list.item_selected.connect(func(index: int): _navigate(str(_list.get_item_metadata(index))))
	var editor := _panel()
	editor.get_parent().size_flags_horizontal = Control.SIZE_EXPAND_FILL
	var scroll := ScrollContainer.new()
	scroll.size_flags_vertical = Control.SIZE_EXPAND_FILL
	editor.add_child(scroll)
	_form = VBoxContainer.new()
	_form.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	scroll.add_child(_form)
	_heading(_form, "Method details", 22)
	_note(_form, "Catches are tried in order. Only a committed successful catch consumes bait and awards XP. The required method level is the lowest catch level.")
	_definition_id = _text_field("Action ID (matches world-object interaction)", _form)
	_state = _label(_form, "Draft")
	_fields["display_name"] = _text_field("Method display name", _form)
	_fields["tool_item_id"] = _text_field("Reusable carried tool item ID", _form)
	_fields["consumable_item_id"] = _text_field("Stackable consumable item ID (blank: none)", _form)
	_fields["consume_quantity"] = _number("Quantity per successful catch (zero without consumable)", 0, 1000, 1)
	_fields["interval_ms"] = _number("Attempt interval (milliseconds)", 600, 60000, 1)
	_label(_form, "Ordered catches: item ID / level / XP / low / high (0–256)")
	_inputs = VBoxContainer.new()
	_form.add_child(_inputs)
	_button(_form, "+ Catch", func(): _add_input({}); _invalidate())
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
	for spec: Array in [["required_level", 1, 99, 1], ["xp", 1, 100000, 1], ["success_low", 0, 256, 32], ["success_high", 0, 256, 192]]:
		var number := SpinBox.new()
		number.tooltip_text = str(spec[0])
		number.min_value = spec[1]
		number.max_value = spec[2]
		number.value = value.get(spec[0], spec[3])
		row.add_child(number)
		number.value_changed.connect(_invalidate)
	_row_tools(row, _inputs)

func _on_catalog(payload: Dictionary) -> void:
	_list.clear()
	for item: Dictionary in payload.get("items", []):
		var index := _list.add_item("%s [%s]" % [item["display_name"], item["publication_state"]])
		_list.set_item_metadata(index, item["action_id"])

func _new_definition() -> void:
	_navigate("")

func _navigate(action_id: String) -> void:
	if not _pending.is_empty(): return
	if JSON.stringify(_draft()) != _baseline:
		var confirm := ConfirmationDialog.new()
		confirm.dialog_text = "Discard unsaved Fishing method edits?"
		add_child(confirm)
		confirm.confirmed.connect(func(): confirm.queue_free(); _load_method(action_id))
		confirm.canceled.connect(confirm.queue_free)
		confirm.popup_centered()
		return
	_load_method(action_id)

func _load_method(action_id: String) -> void:
	if not _pending.is_empty(): return
	_invalidate()
	if action_id.is_empty():
		_show_new()
	else:
		_loading_id = action_id
		_set_pending("load")
		_client.load_fishing(action_id)

func _receive_definition(payload: Dictionary) -> void:
	if _pending != "load" or payload.get("action_id") != _loading_id: return
	_set_pending("")
	_on_definition(payload)

# Lock edits while the host owns a request. Late replies cannot replace a newer form.
func _set_pending(operation: String) -> void:
	_pending = operation
	for node: Node in find_children("*", "", true, false):
		if node is BaseButton: node.disabled = not operation.is_empty()
		elif node is LineEdit: node.editable = operation.is_empty()
		elif node is SpinBox: node.editable = operation.is_empty()
	_definition_id.editable = operation.is_empty() and _current.get("updated_at_utc") == null
	_list.mouse_filter = Control.MOUSE_FILTER_STOP if operation.is_empty() else Control.MOUSE_FILTER_IGNORE
	_list.focus_mode = Control.FOCUS_ALL if operation.is_empty() else Control.FOCUS_NONE

func _show_new() -> void:
	_on_definition({"action_id": "", "publication_state": "Draft", "draft": {"display_name": "", "tool_item_id": "", "consumable_item_id": null, "consume_quantity": 0, "interval_ms": 3000, "catches": []}})

func _on_definition(payload: Dictionary) -> void:
	_loading = true
	_current = payload
	_definition_id.text = str(payload.get("action_id", ""))
	_definition_id.editable = payload.get("updated_at_utc") == null
	_state.text = str(payload.get("publication_state", "Draft"))
	var draft: Dictionary = payload.get("draft", {}).duplicate(true)
	for key: String in _fields:
		var control: Control = _fields[key]
		if control is LineEdit: control.text = str(draft.get(key, "")) if draft.get(key) != null else ""
		elif control is SpinBox: control.value = float(draft.get(key, 0))
	_support.clear_container(_inputs)
	for catch_fact: Dictionary in draft.get("catches", []): _add_input(catch_fact)
	_loading = false
	_baseline = JSON.stringify(_draft())
	_invalidate()

func _draft() -> Dictionary:
	var catches: Array = []
	for row: Node in _inputs.get_children():
		catches.append({"item_id": row.get_child(0).text.strip_edges(), "required_level": int(row.get_child(1).value), "xp": int(row.get_child(2).value), "success_low": int(row.get_child(3).value), "success_high": int(row.get_child(4).value)})
	var consumable: String = _fields["consumable_item_id"].text.strip_edges()
	return {"action_id": _definition_id.text.strip_edges(), "display_name": _fields["display_name"].text.strip_edges(), "tool_item_id": _fields["tool_item_id"].text.strip_edges(), "consumable_item_id": null if consumable.is_empty() else consumable, "consume_quantity": int(_fields["consume_quantity"].value), "interval_ms": int(_fields["interval_ms"].value), "catches": catches}


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
	if not _pending.is_empty(): return
	_invalidate()
	_pending_definition_id = _definition_id.text.strip_edges()
	_preview_request = {"draft": _draft(), "expected_updated_at_utc": _current.get("updated_at_utc"), "target_operation": operation}
	_set_pending("preview")
	_client.preview_fishing(_pending_definition_id, _preview_request)


func _on_preview(payload: Dictionary) -> void:
	# Ignore responses for a form edited or replaced while the request was in flight.
	if _pending != "preview" or _preview_request.is_empty(): return
	_set_pending("")
	_support.render_changes(_changes, payload.get("changes", []))
	_support.render_validation(_validation, payload.get("messages", []))
	var operation := str(payload.get("target_operation", ""))
	_support.accept_preview(operation, str(payload.get("preview_signature", "")), bool(payload.get("applicable", false)), _apply, "2. Apply " + _support.operation_name(operation))


func _apply_preview() -> void:
	if not _pending.is_empty(): return
	if not _support.can_apply(_support.preview_operation, _support.preview_signature): return
	var request := _preview_request.duplicate(true)
	request["preview_signature"] = _support.preview_signature
	var operation: String = _support.preview_operation
	_invalidate()
	_set_pending("mutation")
	_client.mutate_fishing(_pending_definition_id, operation, request)


func _on_mutation(payload: Dictionary) -> void:
	if _pending != "mutation": return
	_set_pending("")
	if payload.get("definition") is Dictionary:
		_on_definition(payload["definition"])
	else:
		_show_new()
	_support.render_validation(_validation, payload.get("messages", []))
	_status.text = "Operation committed and reloaded. Export methods and package content before restarting the game."
	_client.load_fishing_methods(_search.text)


func _on_failed(operation: String, message: String, errors: Array) -> void:
	if not operation.begins_with("fishing_"): return
	_set_pending("")
	_invalidate()
	_status.text = message
	_support.render_validation(_validation, errors)


func _invalidate(_value: Variant = null) -> void:
	if _loading or _apply == null: return
	_preview_request = {}
	_support.clear_preview(_apply, _changes, _validation, "2. Apply changes")
