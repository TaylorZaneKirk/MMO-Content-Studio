extends HBoxContainer
class_name ConsumableEditor

const WORKSPACE_SUPPORT_SCRIPT := preload("res://scripts/authoring_workspace_support.gd")

var _client: AuthoringHostClient
var _items: Array = []
var _assets: Array = []
var _asset_by_path: Dictionary = {}
var _options: Dictionary = {}
var _current_item: Dictionary = {}
var _workspace_support

var _search: LineEdit
var _list: VBoxContainer
var _new_button: Button
var _item_id: LineEdit
var _display_name: LineEdit
var _icon: OptionButton
var _icon_preview: TextureRect
var _publication: Label
var _kind: Label
var _updated: Label
var _use_action: OptionButton
var _consume_quantity: SpinBox
var _result_item_id: LineEdit
var _success_message: LineEdit
var _usable_in_combat: CheckBox
var _cooldown_ms: SpinBox
var _animation_id: LineEdit
var _sound_path: LineEdit
var _requirements: VBoxContainer
var _effects: VBoxContainer
var _requirement_note: Label
var _operation: OptionButton
var _preview_button: Button
var _apply_button: Button
var _status: Label
var _changes: VBoxContainer
var _validation: VBoxContainer
var _file_dialog: FileDialog


func _ready() -> void:
	_workspace_support = WORKSPACE_SUPPORT_SCRIPT.new()
	_client = %AuthoringHostClient
	_build_ui()
	_connect_client()
	_set_form_enabled(false)


func _connect_client() -> void:
	_client.item_assets_received.connect(_on_assets_received)
	_client.item_asset_imported.connect(_on_asset_imported)
	_client.consumable_options_received.connect(_on_options_received)
	_client.consumables_received.connect(_on_consumables_received)
	_client.consumable_received.connect(_on_consumable_received)
	_client.consumable_preview_received.connect(_on_preview_received)
	_client.consumable_mutation_completed.connect(_on_mutation_completed)
	_client.request_failed.connect(_on_request_failed)


func _build_ui() -> void:
	add_theme_constant_override("separation", 14)
	var catalog_panel := _panel()
	catalog_panel.custom_minimum_size = Vector2(320, 0)
	add_child(catalog_panel)
	var catalog := VBoxContainer.new()
	catalog.add_theme_constant_override("separation", 10)
	catalog_panel.add_child(catalog)
	catalog.add_child(_heading("Consumables", 20))
	_search = LineEdit.new()
	_search.placeholder_text = "Search item ID or name"
	_search.text_changed.connect(_rebuild_list.unbind(1))
	catalog.add_child(_search)
	_new_button = Button.new()
	_new_button.text = "+ New Consumable"
	_new_button.disabled = true
	_new_button.pressed.connect(_start_new)
	catalog.add_child(_new_button)
	var catalog_scroll := ScrollContainer.new()
	catalog_scroll.size_flags_vertical = Control.SIZE_EXPAND_FILL
	catalog.add_child(catalog_scroll)
	_list = VBoxContainer.new()
	_list.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_list.add_theme_constant_override("separation", 6)
	catalog_scroll.add_child(_list)

	var editor_panel := _panel()
	editor_panel.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	add_child(editor_panel)
	var scroll := ScrollContainer.new()
	scroll.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	scroll.size_flags_vertical = Control.SIZE_EXPAND_FILL
	editor_panel.add_child(scroll)
	var editor := VBoxContainer.new()
	editor.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	editor.add_theme_constant_override("separation", 12)
	scroll.add_child(editor)
	editor.add_child(_heading("Consumable Definition", 20))

	var grid := GridContainer.new()
	grid.columns = 2
	grid.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	editor.add_child(grid)
	_item_id = _add_line_field(grid, "Stable item ID", "minor_health_potion")
	_display_name = _add_line_field(grid, "Display name", "Minor health potion")
	grid.add_child(_field_label("Inventory / ground icon"))
	var icon_row := HBoxContainer.new()
	icon_row.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	grid.add_child(icon_row)
	_icon = OptionButton.new()
	_icon.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_icon.item_selected.connect(_on_form_changed.unbind(1))
	icon_row.add_child(_icon)
	var import_button := Button.new()
	import_button.text = "Import PNG…"
	import_button.pressed.connect(_open_import)
	icon_row.add_child(import_button)
	_publication = _add_value_field(grid, "Publication state", "No item selected")
	_kind = _add_value_field(grid, "Authoring kind", "Unknown")
	_updated = _add_value_field(grid, "Last updated", "Unknown")
	_use_action = _add_option_field(grid, "Use action")
	_consume_quantity = _add_spin_field(grid, "Quantity consumed", 1, 999, 1)
	_result_item_id = _add_line_field(grid, "Result item ID", "Optional portion / empty container")
	_success_message = _add_line_field(grid, "Success message", "Optional player-facing message")
	grid.add_child(_field_label("Usable in combat"))
	_usable_in_combat = CheckBox.new()
	_usable_in_combat.button_pressed = true
	_usable_in_combat.toggled.connect(_on_form_changed.unbind(1))
	grid.add_child(_usable_in_combat)
	_cooldown_ms = _add_spin_field(grid, "Cooldown (ms)", 0, 86400000, 100)
	_animation_id = _add_line_field(grid, "Use animation ID", "Optional semantic ID")
	_sound_path = _add_line_field(grid, "Sound resource path", "Optional res://assets/... path")

	var preview_row := HBoxContainer.new()
	preview_row.add_theme_constant_override("separation", 16)
	editor.add_child(preview_row)
	_icon_preview = TextureRect.new()
	_icon_preview.custom_minimum_size = Vector2(128, 128)
	_icon_preview.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	_icon_preview.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED
	preview_row.add_child(_icon_preview)
	var note := Label.new()
	note.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	note.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	note.text = "T2 uses declarative effects. Portions and empty containers use Result Item ID. Per-instance charge counters remain deferred."
	preview_row.add_child(note)

	var requirement_header := HBoxContainer.new()
	editor.add_child(requirement_header)
	var requirement_heading := _heading("Requirements", 16)
	requirement_heading.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	requirement_header.add_child(requirement_heading)
	var add_requirement := Button.new()
	add_requirement.text = "+ Skill Requirement"
	add_requirement.pressed.connect(_add_requirement_row)
	requirement_header.add_child(add_requirement)
	_requirement_note = Label.new()
	_requirement_note.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	_requirement_note.modulate = Color(0.7, 0.73, 0.79)
	editor.add_child(_requirement_note)
	_requirements = VBoxContainer.new()
	_requirements.add_theme_constant_override("separation", 6)
	editor.add_child(_requirements)

	var effect_header := HBoxContainer.new()
	editor.add_child(effect_header)
	var effect_heading := _heading("Effects", 16)
	effect_heading.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	effect_header.add_child(effect_heading)
	var add_effect := Button.new()
	add_effect.text = "+ Restore Resource"
	add_effect.pressed.connect(_add_effect_row)
	effect_header.add_child(add_effect)
	_effects = VBoxContainer.new()
	_effects.add_theme_constant_override("separation", 6)
	editor.add_child(_effects)

	var operation_row := HBoxContainer.new()
	operation_row.add_theme_constant_override("separation", 8)
	editor.add_child(operation_row)
	_operation = OptionButton.new()
	for option in [["Save as Draft", "save_draft"], ["Publish", "publish"], ["Disable", "disable"], ["Delete", "delete"]]:
		_operation.add_item(option[0])
		_operation.set_item_metadata(_operation.item_count - 1, option[1])
	_operation.item_selected.connect(_on_form_changed.unbind(1))
	operation_row.add_child(_operation)
	_preview_button = Button.new()
	_preview_button.text = "Validate and Preview Changes"
	_preview_button.pressed.connect(_preview)
	operation_row.add_child(_preview_button)
	_apply_button = Button.new()
	_apply_button.text = "Apply Previewed Operation"
	_apply_button.disabled = true
	_apply_button.pressed.connect(_apply)
	operation_row.add_child(_apply_button)
	_status = Label.new()
	_status.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	_status.text = "Select an item or create a consumable."
	editor.add_child(_status)
	editor.add_child(_heading("Exact logical changes", 16))
	_changes = VBoxContainer.new()
	editor.add_child(_changes)
	editor.add_child(_heading("Validation", 16))
	_validation = VBoxContainer.new()
	editor.add_child(_validation)

	_file_dialog = FileDialog.new()
	_file_dialog.file_mode = FileDialog.FILE_MODE_OPEN_FILE
	_file_dialog.access = FileDialog.ACCESS_FILESYSTEM
	_file_dialog.filters = PackedStringArray(["*.png ; PNG images"])
	_file_dialog.file_selected.connect(_import_selected)
	add_child(_file_dialog)

	for edit in [_item_id, _display_name, _result_item_id, _success_message, _animation_id, _sound_path]:
		edit.text_changed.connect(_on_form_changed.unbind(1))
	_consume_quantity.value_changed.connect(_on_form_changed.unbind(1))
	_cooldown_ms.value_changed.connect(_on_form_changed.unbind(1))


func _panel() -> PanelContainer:
	var panel := PanelContainer.new()
	var style := StyleBoxFlat.new()
	style.bg_color = Color(0.086, 0.098, 0.122, 1)
	style.border_color = Color(0.19, 0.22, 0.28, 1)
	style.set_border_width_all(1)
	style.set_corner_radius_all(8)
	style.content_margin_left = 16
	style.content_margin_top = 14
	style.content_margin_right = 16
	style.content_margin_bottom = 14
	panel.add_theme_stylebox_override("panel", style)
	return panel


func _heading(value: String, size: int) -> Label:
	var label := Label.new()
	label.text = value
	label.add_theme_font_size_override("font_size", size)
	return label


func _field_label(value: String) -> Label:
	var label := Label.new()
	label.text = value
	return label


func _add_line_field(grid: GridContainer, label_text: String, placeholder: String) -> LineEdit:
	grid.add_child(_field_label(label_text))
	var edit := LineEdit.new()
	edit.placeholder_text = placeholder
	edit.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	grid.add_child(edit)
	return edit


func _add_value_field(grid: GridContainer, label_text: String, value: String) -> Label:
	grid.add_child(_field_label(label_text))
	var label := Label.new()
	label.text = value
	label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	grid.add_child(label)
	return label


func _add_option_field(grid: GridContainer, label_text: String) -> OptionButton:
	grid.add_child(_field_label(label_text))
	var option := OptionButton.new()
	option.item_selected.connect(_on_form_changed.unbind(1))
	grid.add_child(option)
	return option


func _add_spin_field(grid: GridContainer, label_text: String, minimum: float, maximum: float, step: float) -> SpinBox:
	grid.add_child(_field_label(label_text))
	var spin := SpinBox.new()
	spin.min_value = minimum
	spin.max_value = maximum
	spin.step = step
	spin.allow_greater = false
	spin.allow_lesser = false
	grid.add_child(spin)
	return spin


func _on_assets_received(payload: Dictionary) -> void:
	_assets = payload.get("assets", []) as Array
	_rebuild_asset_options()


func _on_asset_imported(payload: Dictionary) -> void:
	var asset := payload.get("asset", {}) as Dictionary
	if asset.is_empty():
		return
	var path := str(asset.get("resource_path", ""))
	if not _asset_by_path.has(path):
		_assets.append(asset)
	_rebuild_asset_options(path)
	_status.text = str(payload.get("message", "Item asset imported."))
	_clear_preview()


func _rebuild_asset_options(select_path: String = "") -> void:
	var previous := select_path if not select_path.is_empty() else _selected_metadata(_icon)
	_asset_by_path.clear()
	_icon.clear()
	_icon.add_item("Select an item icon…")
	_icon.set_item_metadata(0, "")
	for variant in _assets:
		if variant is not Dictionary:
			continue
		var asset := variant as Dictionary
		var path := str(asset.get("resource_path", ""))
		_asset_by_path[path] = asset
		_icon.add_item(str(asset.get("display_name", path)))
		_icon.set_item_metadata(_icon.item_count - 1, path)
	_select_option(_icon, previous)
	_update_icon_preview()


func _on_options_received(payload: Dictionary) -> void:
	_options = payload
	_fill_option(_use_action, payload.get("use_actions", []) as Array)
	_requirement_note.text = str(payload.get("charge_model_message", ""))
	_new_button.disabled = false


func _on_consumables_received(payload: Dictionary) -> void:
	_items = payload.get("items", []) as Array
	_rebuild_list()


func _on_consumable_received(payload: Dictionary) -> void:
	_current_item = payload
	_item_id.text = str(payload.get("item_id", ""))
	_item_id.editable = false
	_display_name.text = str(payload.get("display_name", ""))
	_select_option(_icon, str(payload.get("icon_texture_path", "")))
	_publication.text = str(payload.get("publication_state", "Unknown"))
	_kind.text = str(payload.get("authoring_kind", "Unknown"))
	_updated.text = str(payload.get("updated_at_utc", "Unknown"))
	_select_option(_use_action, str(payload.get("use_action", "use")))
	_consume_quantity.value = float(payload.get("consume_quantity", 1))
	_result_item_id.text = str(payload.get("result_item_id", "") if payload.get("result_item_id") != null else "")
	_success_message.text = str(payload.get("success_message", "") if payload.get("success_message") != null else "")
	_usable_in_combat.button_pressed = bool(payload.get("usable_in_combat", true))
	_cooldown_ms.value = float(payload.get("cooldown_ms", 0))
	_animation_id.text = str(payload.get("use_animation_id", "") if payload.get("use_animation_id") != null else "")
	_sound_path.text = str(payload.get("use_sound_resource_path", "") if payload.get("use_sound_resource_path") != null else "")
	_clear_rows(_requirements)
	for requirement in payload.get("requirements", []) as Array:
		if requirement is Dictionary:
			_add_requirement_row(requirement)
	_clear_rows(_effects)
	for effect in payload.get("effects", []) as Array:
		if effect is Dictionary:
			_add_effect_row(effect)
	var editable := bool(payload.get("editable_in_consumables", false))
	_set_form_enabled(editable)
	_item_id.editable = false
	_update_icon_preview(str(payload.get("asset_preview_file_path", "")))
	_update_operation_default()
	_clear_preview()
	_status.text = "Loaded %s." % _item_id.text


func _on_preview_received(payload: Dictionary) -> void:
	var operation := str(payload.get("target_operation", "save_draft"))
	var applicable := bool(payload.get("valid_for_publication", false)) if operation == "publish" else bool(payload.get("valid_for_draft", false))
	_workspace_support.accept_preview(
		operation,
		_signature(operation),
		applicable,
		_apply_button,
		"Apply: %s" % _workspace_support.operation_name(operation)
	)
	_workspace_support.render_changes(_changes, payload.get("changes", []) as Array)
	_workspace_support.render_validation(_validation, payload.get("messages", []) as Array)
	_update_icon_preview(str(payload.get("asset_preview_file_path", "")))
	_status.text = "Preview ready. Review the changes before applying."


func _on_mutation_completed(payload: Dictionary) -> void:
	var operation := str(payload.get("operation", "mutation"))
	if operation == "delete":
		var deleted_id := str(payload.get("deleted_id", _item_id.text))
		_start_new()
		_status.text = "Deleted %s." % deleted_id
		_client.load_consumables(_search.text)
		return
	var item := payload.get("item", {}) as Dictionary
	_status.text = "%s completed successfully." % _workspace_support.operation_name(operation)
	_on_consumable_received(item)
	_client.load_consumables(_search.text)


func _on_request_failed(operation: String, message: String, errors: Array) -> void:
	if not operation.begins_with("consumable") and operation != "item_asset_import":
		return
	_status.text = "%s failed: %s" % [operation, message]
	_workspace_support.render_validation(_validation, errors)
	_apply_button.disabled = true


func _rebuild_list() -> void:
	_clear_rows(_list)
	var query := _search.text.strip_edges().to_lower()
	for variant in _items:
		if variant is not Dictionary:
			continue
		var item := variant as Dictionary
		var haystack := "%s %s" % [item.get("item_id", ""), item.get("display_name", "")]
		if not query.is_empty() and not haystack.to_lower().contains(query):
			continue
		var button := Button.new()
		button.alignment = HORIZONTAL_ALIGNMENT_LEFT
		button.text = "%s\n%s • %s" % [
			str(item.get("display_name", "Unnamed item")),
			str(item.get("publication_state", "Unknown")),
			str(item.get("authoring_kind", "Unknown")),
		]
		button.tooltip_text = str(item.get("item_id", ""))
		button.pressed.connect(_client.load_consumable.bind(str(item.get("item_id", ""))))
		_list.add_child(button)


func _start_new() -> void:
	_current_item = {}
	_item_id.text = ""
	_item_id.editable = true
	_display_name.text = ""
	_icon.select(0)
	_publication.text = "Unsaved"
	_kind.text = "Consumable"
	_updated.text = "Not saved"
	_select_option(_use_action, "use")
	_consume_quantity.value = 1
	_result_item_id.text = ""
	_success_message.text = ""
	_usable_in_combat.button_pressed = true
	_cooldown_ms.value = 0
	_animation_id.text = ""
	_sound_path.text = ""
	_clear_rows(_requirements)
	_clear_rows(_effects)
	_add_effect_row({"effect_type": "restore_resource", "target_id": "health", "minimum_amount": 1, "maximum_amount": 1})
	_set_form_enabled(true)
	_item_id.grab_focus()
	_update_operation_default()
	_update_icon_preview()
	_clear_preview()
	_status.text = "Creating a new consumable."


func _add_requirement_row(initial: Dictionary = {}) -> void:
	var row := HBoxContainer.new()
	row.add_theme_constant_override("separation", 6)
	var type := OptionButton.new()
	_fill_option(type, _options.get("requirement_types", []) as Array)
	_select_option(type, str(initial.get("requirement_type", "skill_minimum")))
	row.add_child(type)
	var target := OptionButton.new()
	target.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_fill_option(target, _options.get("skills", []) as Array)
	_select_option(target, str(initial.get("target_id", "")))
	row.add_child(target)
	var minimum := SpinBox.new()
	minimum.min_value = 1
	minimum.max_value = 1000000
	minimum.value = float(initial.get("minimum_value", 1))
	minimum.custom_minimum_size = Vector2(120, 0)
	row.add_child(minimum)
	var remove := Button.new()
	remove.text = "Remove"
	remove.pressed.connect(_remove_row.bind(row))
	row.add_child(remove)
	row.set_meta("type", type)
	row.set_meta("target", target)
	row.set_meta("value", minimum)
	type.item_selected.connect(_on_form_changed.unbind(1))
	target.item_selected.connect(_on_form_changed.unbind(1))
	minimum.value_changed.connect(_on_form_changed.unbind(1))
	_requirements.add_child(row)
	_clear_preview()


func _add_effect_row(initial: Dictionary = {}) -> void:
	var row := HBoxContainer.new()
	row.add_theme_constant_override("separation", 6)
	var type := OptionButton.new()
	_fill_option(type, _options.get("effect_types", []) as Array)
	_select_option(type, str(initial.get("effect_type", "restore_resource")))
	row.add_child(type)
	var target := OptionButton.new()
	target.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_fill_option(target, _options.get("resource_targets", []) as Array)
	_select_option(target, str(initial.get("target_id", "health")))
	row.add_child(target)
	var minimum := SpinBox.new()
	minimum.min_value = 1
	minimum.max_value = 1000000
	minimum.value = float(initial.get("minimum_amount", 1))
	minimum.custom_minimum_size = Vector2(110, 0)
	minimum.tooltip_text = "Minimum restore"
	row.add_child(minimum)
	var maximum := SpinBox.new()
	maximum.min_value = 1
	maximum.max_value = 1000000
	maximum.value = float(initial.get("maximum_amount", initial.get("minimum_amount", 1)))
	maximum.custom_minimum_size = Vector2(110, 0)
	maximum.tooltip_text = "Maximum restore (inclusive)"
	row.add_child(maximum)
	var remove := Button.new()
	remove.text = "Remove"
	remove.pressed.connect(_remove_row.bind(row))
	row.add_child(remove)
	row.set_meta("type", type)
	row.set_meta("target", target)
	row.set_meta("minimum", minimum)
	row.set_meta("maximum", maximum)
	type.item_selected.connect(_on_form_changed.unbind(1))
	target.item_selected.connect(_on_form_changed.unbind(1))
	minimum.value_changed.connect(_on_form_changed.unbind(1))
	maximum.value_changed.connect(_on_form_changed.unbind(1))
	_effects.add_child(row)
	_clear_preview()


func _remove_row(row: Control) -> void:
	var parent := row.get_parent()
	if parent != null:
		parent.remove_child(row)
	row.queue_free()
	_clear_preview()


func _collect_requirements() -> Array:
	var values: Array = []
	for row in _requirements.get_children():
		if not row.has_meta("type"):
			continue
		values.append({
			"requirement_index": values.size(),
			"requirement_type": _selected_metadata(row.get_meta("type") as OptionButton),
			"target_id": _selected_metadata(row.get_meta("target") as OptionButton),
			"minimum_value": int((row.get_meta("value") as SpinBox).value),
		})
	return values


func _collect_effects() -> Array:
	var values: Array = []
	for row in _effects.get_children():
		if not row.has_meta("type"):
			continue
		values.append({
			"effect_index": values.size(),
			"effect_type": _selected_metadata(row.get_meta("type") as OptionButton),
			"target_id": _selected_metadata(row.get_meta("target") as OptionButton),
			"minimum_amount": int((row.get_meta("minimum") as SpinBox).value),
			"maximum_amount": int((row.get_meta("maximum") as SpinBox).value),
		})
	return values


func _payload() -> Dictionary:
	return {
		"display_name": _display_name.text,
		"icon_texture_path": _selected_metadata(_icon),
		"use_action": _selected_metadata(_use_action),
		"consume_quantity": int(_consume_quantity.value),
		"result_item_id": _result_item_id.text,
		"success_message": _success_message.text,
		"usable_in_combat": _usable_in_combat.button_pressed,
		"cooldown_ms": int(_cooldown_ms.value),
		"use_animation_id": _animation_id.text,
		"use_sound_resource_path": _sound_path.text,
		"requirements": _collect_requirements(),
		"effects": _collect_effects(),
		"expected_updated_at_utc": _current_item.get("updated_at_utc", null),
	}


func _preview() -> void:
	var item_id := _item_id.text.strip_edges()
	if item_id.is_empty():
		_status.text = "Enter a stable item ID before previewing."
		return
	var payload := _payload()
	payload["target_operation"] = _selected_metadata(_operation)
	_client.preview_consumable(item_id, payload)
	_status.text = "Calculating validation and exact database changes…"


func _apply() -> void:
	var operation := _selected_metadata(_operation)
	if not _workspace_support.can_apply(operation, _signature(operation)):
		_status.text = "The form changed. Preview the operation again before applying it."
		_apply_button.disabled = true
		return
	var item_id := _item_id.text.strip_edges()
	var expected: Variant = _current_item.get("updated_at_utc", null)
	match operation:
		"publish":
			_client.publish_consumable(item_id, expected)
		"disable":
			_client.disable_consumable(item_id, expected)
		"delete":
			_client.delete_consumable(item_id, expected)
		_:
			_client.save_consumable_draft(item_id, _payload())
	_apply_button.disabled = true
	_status.text = "Applying transactional consumable operation…"


func _open_import() -> void:
	_file_dialog.popup_centered_ratio(0.75)


func _import_selected(path: String) -> void:
	_client.import_item_asset(path, path.get_file())
	_status.text = "Importing PNG into the canonical item asset directory…"


func _update_operation_default() -> void:
	var state := str(_current_item.get("publication_state", "Unsaved"))
	_operation.select(1 if state == "Draft" else 2 if state == "Published" else 0)


func _set_form_enabled(enabled: bool) -> void:
	_display_name.editable = enabled
	_icon.disabled = not enabled
	_use_action.disabled = not enabled
	_consume_quantity.editable = enabled
	_result_item_id.editable = enabled
	_success_message.editable = enabled
	_usable_in_combat.disabled = not enabled
	_cooldown_ms.editable = enabled
	_animation_id.editable = enabled
	_sound_path.editable = enabled
	_operation.disabled = not enabled
	_preview_button.disabled = not enabled
	if not enabled:
		_apply_button.disabled = true


func _on_form_changed() -> void:
	_clear_preview()
	_update_icon_preview()


func _clear_preview() -> void:
	_workspace_support.clear_preview(_apply_button, _changes, _validation)


func _signature(operation: String) -> String:
	return JSON.stringify([_item_id.text.strip_edges(), _payload(), operation])



func _update_icon_preview(explicit_file_path: String = "") -> void:
	_icon_preview.texture = null
	var file_path := explicit_file_path
	if file_path.is_empty():
		var asset := _asset_by_path.get(_selected_metadata(_icon), {}) as Dictionary
		file_path = str(asset.get("file_path", ""))
	if file_path.is_empty() or not FileAccess.file_exists(file_path):
		return
	var image := Image.load_from_file(file_path)
	if image == null or image.is_empty():
		return
	_icon_preview.texture = ImageTexture.create_from_image(image)


func _fill_option(control: OptionButton, values: Array) -> void:
	var selected := _selected_metadata(control)
	control.clear()
	for variant in values:
		if variant is not Dictionary:
			continue
		var option := variant as Dictionary
		control.add_item(str(option.get("display_name", option.get("id", "Option"))))
		control.set_item_metadata(control.item_count - 1, str(option.get("id", "")))
	if control.item_count > 0:
		_select_option(control, selected)


func _select_option(control: OptionButton, id: String) -> void:
	for index in range(control.item_count):
		if str(control.get_item_metadata(index)) == id:
			control.select(index)
			return
	if control.item_count > 0:
		control.select(0)


func _selected_metadata(control: OptionButton) -> String:
	return "" if control.selected < 0 else str(control.get_item_metadata(control.selected))



func _clear_rows(container: Node) -> void:
	for child in container.get_children():
		container.remove_child(child)
		child.queue_free()
