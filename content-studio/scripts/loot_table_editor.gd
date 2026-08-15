extends HBoxContainer

const SUPPORT_SCRIPT := preload("res://scripts/authoring_workspace_support.gd")

var _support: AuthoringWorkspaceSupport = SUPPORT_SCRIPT.new()
var _client: AuthoringHostClient
var _current_definition: Dictionary = {}
var _catalog_rows: Dictionary = {}

var _search_edit: LineEdit
var _catalog_list: ItemList
var _id_edit: LineEdit
var _display_name_edit: LineEdit
var _description_edit: TextEdit
var _groups_edit: TextEdit
var _state_label: Label
var _updated_label: Label
var _ev_label: Label
var _preview_button: Button
var _apply_button: Button
var _publish_button: Button
var _disable_button: Button
var _delete_button: Button
var _changes_container: VBoxContainer
var _validation_container: VBoxContainer


func _ready() -> void:
	_client = get_node("%AuthoringHostClient") as AuthoringHostClient
	_build_ui()
	_connect_client()


func open_resource(resource_id: String) -> void:
	_id_edit.text = resource_id
	if not resource_id.strip_edges().is_empty():
		_client.load_loot_table(resource_id)


func _build_ui() -> void:
	size_flags_horizontal = Control.SIZE_EXPAND_FILL
	size_flags_vertical = Control.SIZE_EXPAND_FILL
	add_theme_constant_override("separation", 14)

	var left := VBoxContainer.new()
	left.custom_minimum_size = Vector2(280, 0)
	left.size_flags_vertical = Control.SIZE_EXPAND_FILL
	add_child(left)

	var catalog_heading := Label.new()
	catalog_heading.text = "Loot Tables"
	catalog_heading.add_theme_font_size_override("font_size", 20)
	left.add_child(catalog_heading)

	_search_edit = LineEdit.new()
	_search_edit.placeholder_text = "Search loot tables"
	left.add_child(_search_edit)

	var search_button := Button.new()
	search_button.text = "Search"
	left.add_child(search_button)

	_catalog_list = ItemList.new()
	_catalog_list.size_flags_vertical = Control.SIZE_EXPAND_FILL
	left.add_child(_catalog_list)

	var new_button := Button.new()
	new_button.text = "New Loot Table"
	left.add_child(new_button)

	var right := VBoxContainer.new()
	right.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	right.size_flags_vertical = Control.SIZE_EXPAND_FILL
	right.add_theme_constant_override("separation", 10)
	add_child(right)

	_id_edit = LineEdit.new()
	_id_edit.placeholder_text = "loot_table_id"
	right.add_child(_labeled_control("Stable ID", _id_edit))

	_display_name_edit = LineEdit.new()
	_display_name_edit.placeholder_text = "Display name"
	right.add_child(_labeled_control("Display Name", _display_name_edit))

	_description_edit = TextEdit.new()
	_description_edit.custom_minimum_size = Vector2(0, 64)
	_description_edit.wrap_mode = TextEdit.LINE_WRAPPING_BOUNDARY
	right.add_child(_labeled_control("Description", _description_edit))

	_groups_edit = TextEdit.new()
	_groups_edit.custom_minimum_size = Vector2(0, 280)
	_groups_edit.size_flags_vertical = Control.SIZE_EXPAND_FILL
	_groups_edit.wrap_mode = TextEdit.LINE_WRAPPING_NONE
	right.add_child(_labeled_control("Groups JSON", _groups_edit))

	var meta_row := HBoxContainer.new()
	_state_label = Label.new()
	_state_label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_updated_label = Label.new()
	_updated_label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	meta_row.add_child(_state_label)
	meta_row.add_child(_updated_label)
	right.add_child(meta_row)

	_ev_label = Label.new()
	_ev_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	right.add_child(_ev_label)

	var buttons := HBoxContainer.new()
	_preview_button = Button.new()
	_preview_button.text = "Preview Draft"
	_apply_button = Button.new()
	_apply_button.text = "Apply Previewed Operation"
	_apply_button.disabled = true
	_publish_button = Button.new()
	_publish_button.text = "Preview Publish"
	_disable_button = Button.new()
	_disable_button.text = "Preview Disable"
	_delete_button = Button.new()
	_delete_button.text = "Preview Delete"
	for button in [_preview_button, _apply_button, _publish_button, _disable_button, _delete_button]:
		buttons.add_child(button)
	right.add_child(buttons)

	var lower := HSplitContainer.new()
	lower.size_flags_vertical = Control.SIZE_EXPAND_FILL
	_changes_container = VBoxContainer.new()
	_validation_container = VBoxContainer.new()
	lower.add_child(_scroll_box("Changes", _changes_container))
	lower.add_child(_scroll_box("Validation", _validation_container))
	right.add_child(lower)

	search_button.pressed.connect(func() -> void: _client.load_loot_tables(_search_edit.text))
	new_button.pressed.connect(_stage_new)
	_catalog_list.item_selected.connect(_on_catalog_selected)
	_preview_button.pressed.connect(func() -> void: _preview("save_draft"))
	_publish_button.pressed.connect(func() -> void: _preview("publish"))
	_disable_button.pressed.connect(func() -> void: _preview("disable"))
	_delete_button.pressed.connect(func() -> void: _preview("delete"))
	_apply_button.pressed.connect(_apply_preview)


func _connect_client() -> void:
	_client.loot_table_catalog_received.connect(_on_catalog_received)
	_client.loot_table_definition_received.connect(_on_definition_received)
	_client.loot_table_preview_received.connect(_on_preview_received)
	_client.loot_table_mutation_completed.connect(_on_mutation_completed)
	_client.loot_table_delete_completed.connect(_on_deleted)
	_client.request_failed.connect(_on_request_failed)


func _on_catalog_received(payload: Dictionary) -> void:
	_catalog_rows.clear()
	_catalog_list.clear()
	var items: Variant = payload.get("items", [])
	if items is not Array:
		return
	for item_variant in items:
		if item_variant is not Dictionary:
			continue
		var item := item_variant as Dictionary
		var id := str(item.get("loot_table_id", ""))
		_catalog_rows[id] = item
		_catalog_list.add_item("%s  [%s]" % [
			str(item.get("display_name", id)),
			str(item.get("publication_state", "Draft")),
		])
		_catalog_list.set_item_metadata(_catalog_list.item_count - 1, id)


func _on_definition_received(payload: Dictionary) -> void:
	_current_definition = payload
	_id_edit.text = str(payload.get("loot_table_id", ""))
	_display_name_edit.text = str(payload.get("display_name", ""))
	_description_edit.text = str(payload.get("description", ""))
	_groups_edit.text = JSON.stringify(payload.get("groups", []), "\t")
	_state_label.text = "State: %s" % str(payload.get("publication_state", "Draft"))
	_updated_label.text = "Updated: %s" % str(payload.get("updated_at_utc", "not saved"))
	_render_ev(payload.get("expected_value", {}))
	_support.clear_preview(_apply_button, _changes_container, _validation_container)


func _on_preview_received(payload: Dictionary) -> void:
	var operation := str(payload.get("target_operation", "save_draft"))
	_support.render_changes(_changes_container, payload.get("changes", []))
	_support.render_validation(_validation_container, payload.get("messages", []))
	_render_ev(payload.get("expected_value", {}))
	_support.accept_preview(
		operation,
		str(payload.get("preview_signature", "")),
		bool(payload.get("valid_for_draft", false)) if operation == "save_draft" else not _has_error(payload.get("messages", [])),
		_apply_button,
		"Apply %s" % _support.operation_name(operation)
	)


func _on_mutation_completed(payload: Dictionary) -> void:
	var table: Dictionary = payload.get("loot_table", {}) as Dictionary
	_on_definition_received(table)
	_client.load_loot_tables(_search_edit.text)


func _on_deleted(payload: Dictionary) -> void:
	_stage_new()
	_client.load_loot_tables(_search_edit.text)


func _on_request_failed(operation: String, message: String, errors: Array) -> void:
	if not operation.begins_with("loot_table"):
		return
	_support.clear_container(_validation_container)
	_support.add_wrapped_label(_validation_container, message)
	_support.render_validation(_validation_container, errors)


func _on_catalog_selected(index: int) -> void:
	var id := str(_catalog_list.get_item_metadata(index))
	if not id.is_empty():
		_client.load_loot_table(id)


func _stage_new() -> void:
	_current_definition = {}
	_id_edit.text = ""
	_display_name_edit.text = ""
	_description_edit.text = ""
	_groups_edit.text = JSON.stringify([], "\t")
	_state_label.text = "State: Draft"
	_updated_label.text = "Updated: not saved"
	_ev_label.text = "Expected value: not previewed"
	_support.clear_preview(_apply_button, _changes_container, _validation_container)


func _preview(operation: String) -> void:
	var id := _id_edit.text.strip_edges()
	if id.is_empty():
		_support.clear_container(_validation_container)
		_support.add_wrapped_label(_validation_container, "Loot table id is required.")
		return
	var parsed: Variant = JSON.parse_string(_groups_edit.text)
	if parsed is not Array:
		_support.clear_container(_validation_container)
		_support.add_wrapped_label(_validation_container, "Groups JSON must be an array.")
		return
	_client.preview_loot_table(id, {
		"display_name": _display_name_edit.text,
		"description": _description_edit.text,
		"groups": parsed,
		"expected_updated_at_utc": _current_definition.get("updated_at_utc", null),
		"target_operation": operation,
	})


func _apply_preview() -> void:
	var id := _id_edit.text.strip_edges()
	var operation := _support.preview_operation
	var signature := _support.preview_signature
	if not _support.can_apply(operation, signature):
		return
	match operation:
		"save_draft":
			var parsed: Variant = JSON.parse_string(_groups_edit.text)
			_client.save_loot_table_draft(id, {
				"display_name": _display_name_edit.text,
				"description": _description_edit.text,
				"groups": parsed,
				"expected_updated_at_utc": _current_definition.get("updated_at_utc", null),
				"preview_signature": signature,
			})
		"publish":
			_client.publish_loot_table(id, _current_definition.get("updated_at_utc", null), signature)
		"disable":
			_client.disable_loot_table(id, _current_definition.get("updated_at_utc", null), signature)
		"delete":
			_client.delete_loot_table(id, _current_definition.get("updated_at_utc", null), signature)


func _render_ev(value: Variant) -> void:
	if value is not Dictionary:
		_ev_label.text = "Expected value: unavailable"
		return
	var ev := value as Dictionary
	var total := ev.get("total_reference_value", {}) as Dictionary
	_ev_label.text = "Expected value: %s reference units across %d item totals" % [
		str(total.get("display", "0")),
		(ev.get("item_totals", []) as Array).size() if ev.get("item_totals", []) is Array else 0,
	]


func _has_error(messages: Variant) -> bool:
	if messages is not Array:
		return false
	for message_variant in messages:
		if message_variant is Dictionary and str((message_variant as Dictionary).get("severity", "")) == "Error":
			return true
	return false


func _labeled_control(label_text: String, control: Control) -> Control:
	var box := VBoxContainer.new()
	var label := Label.new()
	label.text = label_text
	box.add_child(label)
	box.add_child(control)
	return box


func _scroll_box(title: String, content: VBoxContainer) -> Control:
	var box := VBoxContainer.new()
	box.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	box.size_flags_vertical = Control.SIZE_EXPAND_FILL
	var label := Label.new()
	label.text = title
	box.add_child(label)
	var scroll := ScrollContainer.new()
	scroll.size_flags_vertical = Control.SIZE_EXPAND_FILL
	scroll.add_child(content)
	box.add_child(scroll)
	return box
