extends HBoxContainer
class_name QuestEditor

const CATALOG_PANE_TOGGLE := preload("res://scripts/catalog_pane_toggle.gd")

@onready var _client: AuthoringHostClient = %AuthoringHostClient

var _quests: Array = []
var _current_quest: Dictionary = {}
var _preview_signature := ""

var _search: LineEdit
var _list: VBoxContainer
var _quest_id: LineEdit
var _display_name: LineEdit
var _publication: Label
var _updated: Label
var _steps: TextEdit
var _transitions: TextEdit
var _operation: OptionButton
var _preview_button: Button
var _apply_button: Button
var _delete_button: Button
var _status: Label
var _diagnostics: RichTextLabel


func _ready() -> void:
	_build_ui()
	_connect_client()
	_set_editable(false)


func _connect_client() -> void:
	_client.quest_catalog_received.connect(_on_quest_catalog_received)
	_client.quest_definition_received.connect(_on_quest_definition_received)
	_client.quest_preview_received.connect(_on_quest_preview_received)
	_client.quest_mutation_completed.connect(_on_quest_mutation_completed)
	_client.quest_delete_completed.connect(_on_quest_delete_completed)
	_client.request_failed.connect(_on_request_failed)


func _build_ui() -> void:
	add_theme_constant_override("separation", 14)

	var catalog_panel := _panel(Vector2(300, 0))
	add_child(catalog_panel)
	var catalog := _vbox(catalog_panel)
	_heading(catalog, "Quests", 20)
	_search = LineEdit.new()
	_search.placeholder_text = "Search quest ID or name"
	_search.text_changed.connect(_on_search_changed)
	catalog.add_child(_search)
	var new_button := Button.new()
	new_button.text = "+ New Quest"
	new_button.pressed.connect(_start_new)
	catalog.add_child(new_button)
	var scroll := ScrollContainer.new()
	scroll.size_flags_vertical = Control.SIZE_EXPAND_FILL
	catalog.add_child(scroll)
	_list = VBoxContainer.new()
	scroll.add_child(_list)
	CATALOG_PANE_TOGGLE.attach(self, catalog_panel)

	var editor_panel := _panel(Vector2(520, 0))
	editor_panel.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	add_child(editor_panel)
	var editor := _vbox(editor_panel)
	_heading(editor, "Definition", 20)
	var grid := GridContainer.new()
	grid.columns = 2
	grid.add_theme_constant_override("h_separation", 10)
	grid.add_theme_constant_override("v_separation", 8)
	editor.add_child(grid)
	_quest_id = _line(grid, "Quest ID", "test_quest")
	_display_name = _line(grid, "Display name", "Test Quest")
	_publication = _value(grid, "Publication", "Unsaved")
	_updated = _value(grid, "Updated", "Never")
	_heading(editor, "Steps", 16)
	_steps = _text(editor, "step_id|display name|order", 130)
	_heading(editor, "Transitions", 16)
	_transitions = _text(editor, "transition_id|source_status|source_step_id|target_status|target_step_id|order", 180)

	var action_panel := _panel(Vector2(360, 0))
	add_child(action_panel)
	var actions := _vbox(action_panel)
	_heading(actions, "Operation", 20)
	_operation = OptionButton.new()
	for option in [["Save Draft", "save_draft"], ["Publish", "publish"], ["Disable", "disable"], ["Delete", "delete"]]:
		_operation.add_item(option[0])
		_operation.set_item_metadata(_operation.item_count - 1, option[1])
	actions.add_child(_operation)
	_preview_button = Button.new()
	_preview_button.text = "Validate"
	_preview_button.pressed.connect(_preview)
	actions.add_child(_preview_button)
	_apply_button = Button.new()
	_apply_button.text = "Apply"
	_apply_button.disabled = true
	_apply_button.pressed.connect(_apply)
	actions.add_child(_apply_button)
	_delete_button = Button.new()
	_delete_button.text = "Delete Disabled Quest"
	_delete_button.pressed.connect(_delete)
	actions.add_child(_delete_button)
	_status = Label.new()
	_status.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	_status.text = "Select or create a quest."
	actions.add_child(_status)
	_diagnostics = RichTextLabel.new()
	_diagnostics.custom_minimum_size = Vector2(300, 320)
	_diagnostics.fit_content = false
	actions.add_child(_diagnostics)


func _on_quest_catalog_received(payload: Dictionary) -> void:
	_quests = payload.get("items", []) as Array
	_render_list()


func _on_quest_definition_received(payload: Dictionary) -> void:
	_current_quest = payload.duplicate(true)
	_preview_signature = ""
	_quest_id.text = str(payload.get("quest_id", ""))
	_quest_id.editable = false
	_display_name.text = str(payload.get("display_name", ""))
	_publication.text = str(payload.get("publication_state", "Draft"))
	_updated.text = str(payload.get("updated_at_utc", ""))
	_steps.text = _format_steps(payload.get("steps", []) as Array)
	_transitions.text = _format_transitions(payload.get("transitions", []) as Array)
	_set_editable(true)
	_status.text = "Loaded %s." % _quest_id.text


func _on_quest_preview_received(payload: Dictionary) -> void:
	_preview_signature = str(payload.get("preview_signature", ""))
	var messages := payload.get("messages", []) as Array
	_apply_button.disabled = not bool(payload.get("valid_for_draft", false)) and _selected_operation() == "save_draft"
	if _selected_operation() == "publish":
		_apply_button.disabled = not bool(payload.get("valid_for_publication", false))
	elif _selected_operation() in ["disable", "delete"]:
		_apply_button.disabled = _has_errors(messages)
	_render_diagnostics(payload)
	_status.text = "Preview ready."


func _on_quest_mutation_completed(payload: Dictionary) -> void:
	var quest := payload.get("quest", {}) as Dictionary
	_on_quest_definition_received(quest)
	_client.load_quests(_search.text)
	_status.text = "Applied %s." % str(payload.get("operation", "operation"))


func _on_quest_delete_completed(payload: Dictionary) -> void:
	_current_quest = {}
	_preview_signature = ""
	_set_editable(false)
	_client.load_quests(_search.text)
	_status.text = "Deleted %s." % str(payload.get("deleted_id", "quest"))


func _on_request_failed(operation: String, message: String, errors: Array) -> void:
	if not operation.begins_with("quest"):
		return
	_status.text = message
	_diagnostics.text = "\n".join(errors.map(func(error: Variant) -> String:
		var item := error as Dictionary
		return "%s: %s" % [item.get("code", "error"), item.get("message", "")]
	))


func _start_new() -> void:
	_current_quest = {}
	_preview_signature = ""
	_quest_id.text = ""
	_quest_id.editable = true
	_display_name.text = ""
	_publication.text = "Unsaved"
	_updated.text = "Never"
	_steps.text = "first|First|0"
	_transitions.text = "accept|not_started||active|first|0\nfinish|active|first|completed||1"
	_set_editable(true)
	_status.text = "Creating a new quest draft."


func _preview() -> void:
	var quest_id := _quest_id.text.strip_edges()
	if quest_id.is_empty():
		_status.text = "Enter a stable quest ID."
		return
	_apply_button.disabled = true
	_client.preview_quest(quest_id, _build_payload(_selected_operation(), false))
	_status.text = "Validating..."


func _apply() -> void:
	var quest_id := _quest_id.text.strip_edges()
	var expected: Variant = _current_quest.get("updated_at_utc", null)
	match _selected_operation():
		"publish":
			_client.publish_quest(quest_id, expected, _preview_signature)
		"disable":
			_client.disable_quest(quest_id, expected, _preview_signature)
		"delete":
			_client.delete_quest(quest_id, expected, _preview_signature)
		_:
			_client.save_quest_draft(quest_id, _build_payload("save_draft", true))
	_status.text = "Applying..."


func _delete() -> void:
	_operation.select(3)
	_preview()


func _build_payload(target_operation: String, include_signature: bool) -> Dictionary:
	var payload := {
		"display_name": _display_name.text.strip_edges(),
		"schema_version": 1,
		"steps": _parse_steps(),
		"transitions": _parse_transitions(),
		"expected_updated_at_utc": _current_quest.get("updated_at_utc", null),
	}
	if include_signature:
		payload["preview_signature"] = _preview_signature
	else:
		payload["target_operation"] = target_operation
	return payload


func _parse_steps() -> Array:
	var items := []
	for line in _steps.text.split("\n", false):
		var parts := line.split("|", true)
		if parts.size() < 3:
			continue
		items.append({"step_id": parts[0].strip_edges(), "display_name": parts[1].strip_edges(), "step_order": int(parts[2])})
	return items


func _parse_transitions() -> Array:
	var items := []
	for line in _transitions.text.split("\n", false):
		var parts := line.split("|", true)
		if parts.size() < 6:
			continue
		items.append({
			"transition_id": parts[0].strip_edges(),
			"source_status": parts[1].strip_edges(),
			"source_step_id": _optional(parts[2]),
			"target_status": parts[3].strip_edges(),
			"target_step_id": _optional(parts[4]),
			"transition_order": int(parts[5]),
		})
	return items


func _render_list() -> void:
	for child in _list.get_children():
		_list.remove_child(child)
		child.queue_free()
	for quest_variant: Variant in _quests:
		var quest := quest_variant as Dictionary
		var button := Button.new()
		button.text = "%s\n%s  %s" % [quest.get("display_name", "Unnamed Quest"), quest.get("quest_id", ""), quest.get("publication_state", "Draft")]
		button.alignment = HORIZONTAL_ALIGNMENT_LEFT
		button.pressed.connect(_client.load_quest.bind(str(quest.get("quest_id", ""))))
		_list.add_child(button)


func _render_diagnostics(payload: Dictionary) -> void:
	var lines := []
	for message_variant: Variant in payload.get("messages", []) as Array:
		var message := message_variant as Dictionary
		lines.append("%s: %s" % [message.get("code", "message"), message.get("message", "")])
	var analysis := payload.get("analysis", {}) as Dictionary
	lines.append("reachable_steps: %s" % JSON.stringify(analysis.get("reachable_step_ids", [])))
	lines.append("unreachable_steps: %s" % JSON.stringify(analysis.get("unreachable_step_ids", [])))
	lines.append("unreachable_transitions: %s" % JSON.stringify(analysis.get("unreachable_transition_ids", [])))
	_diagnostics.text = "\n".join(lines)


func _format_steps(items: Array) -> String:
	return "\n".join(items.map(func(item_variant: Variant) -> String:
		var item := item_variant as Dictionary
		return "%s|%s|%d" % [item.get("step_id", ""), item.get("display_name", ""), int(item.get("step_order", 0))]
	))


func _format_transitions(items: Array) -> String:
	return "\n".join(items.map(func(item_variant: Variant) -> String:
		var item := item_variant as Dictionary
		return "%s|%s|%s|%s|%s|%d" % [
			item.get("transition_id", ""),
			item.get("source_status", ""),
			item.get("source_step_id", ""),
			item.get("target_status", ""),
			item.get("target_step_id", ""),
			int(item.get("transition_order", 0))
		]
	))


func _selected_operation() -> String:
	return str(_operation.get_item_metadata(_operation.selected))


func _optional(value: String) -> Variant:
	var text := value.strip_edges()
	return null if text.is_empty() else text


func _has_errors(messages: Array) -> bool:
	for message_variant: Variant in messages:
		var message := message_variant as Dictionary
		if str(message.get("severity", "")).to_lower() == "error":
			return true
	return false


func _on_search_changed(_text: String) -> void:
	_client.load_quests(_search.text)


func _set_editable(enabled: bool) -> void:
	for control in [_display_name, _steps, _transitions]:
		if control != null:
			control.editable = enabled
	_preview_button.disabled = not enabled
	_delete_button.disabled = not enabled
	_apply_button.disabled = true


func _panel(minimum_size: Vector2) -> PanelContainer:
	var panel := PanelContainer.new()
	panel.custom_minimum_size = minimum_size
	panel.size_flags_vertical = Control.SIZE_EXPAND_FILL
	return panel


func _vbox(parent: Control) -> VBoxContainer:
	var box := VBoxContainer.new()
	box.add_theme_constant_override("separation", 10)
	box.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	box.size_flags_vertical = Control.SIZE_EXPAND_FILL
	parent.add_child(box)
	return box


func _heading(parent: Control, text: String, size: int) -> void:
	var label := Label.new()
	label.text = text
	label.add_theme_font_size_override("font_size", size)
	parent.add_child(label)


func _line(parent: GridContainer, label_text: String, placeholder: String) -> LineEdit:
	var label := Label.new()
	label.text = label_text
	parent.add_child(label)
	var edit := LineEdit.new()
	edit.placeholder_text = placeholder
	parent.add_child(edit)
	return edit


func _value(parent: GridContainer, label_text: String, text: String) -> Label:
	var label := Label.new()
	label.text = label_text
	parent.add_child(label)
	var value := Label.new()
	value.text = text
	parent.add_child(value)
	return value


func _text(parent: Control, placeholder: String, height: int) -> TextEdit:
	var edit := TextEdit.new()
	edit.placeholder_text = placeholder
	edit.custom_minimum_size = Vector2(0, height)
	parent.add_child(edit)
	return edit
