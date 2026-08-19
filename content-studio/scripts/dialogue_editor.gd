extends HBoxContainer
class_name DialogueEditor

const CATALOG_PANE_TOGGLE := preload("res://scripts/catalog_pane_toggle.gd")

signal workspace_open_requested(workspace_id: String, resource_id: String)

const WORKSPACE_SUPPORT_SCRIPT := preload("res://scripts/authoring_workspace_support.gd")
const CONTENT_STUDIO_LOGGER := preload("res://scripts/content_studio_logger.gd")
const NODE_TYPE_SPEAKER_TEXT := "speaker_text"
const NODE_TYPE_PLAYER_CHOICE := "player_choice"
const NODE_TYPE_END := "end"
const FORM_LABEL_WIDTH := 132.0
const GRAPH_NODE_MIN_WIDTH := 190.0
const GRAPH_NODE_SUMMARY_WIDTH := 150.0
const CONDITION_TYPE_QUEST_STATUS := "quest_status"
const CONDITION_TYPE_QUEST_STEP := "quest_step"
const CONDITION_TYPE_HAS_ITEM := "has_item"
const EFFECT_TYPE_START_QUEST := "start_quest"
const EFFECT_TYPE_ADVANCE_QUEST := "advance_quest"
const EFFECT_TYPE_COMPLETE_QUEST := "complete_quest"
const EFFECT_TYPE_GRANT_ITEM := "grant_item"
const EFFECT_TYPE_REMOVE_ITEM := "remove_item"
const EFFECT_TYPE_GRANT_EXPERIENCE := "grant_experience"
const QUEST_STATUSES := ["not_started", "active", "completed"]

@onready var _client: AuthoringHostClient = %AuthoringHostClient

var _workspace_support
var _options: Dictionary = {}
var _dialogues: Array = []
var _current_dialogue: Dictionary = {}
var _selected_node_id := ""
var _is_loading := false
var _is_new := false
var _schema_available := false
var _form_editable := false
var _reload_dialogue_id := ""
var _visited_node_ids: Array = []
var _playthrough_node_id := ""
var _pending_graph_node_selection := ""
var _graph_selection_update_queued := false

var _search: LineEdit
var _list: VBoxContainer
var _new_button: Button
var _dialogue_id: LineEdit
var _display_name: LineEdit
var _publication: Label
var _updated: Label
var _schema_version: SpinBox
var _metadata_description: TextEdit
var _notes: TextEdit
var _entry_points: VBoxContainer
var _add_entry_button: Button
var _graph: GraphEdit
var _node_title: Label
var _node_id: LineEdit
var _node_type: OptionButton
var _speaker: LineEdit
var _text: TextEdit
var _next_node: OptionButton
var _dismissible: CheckBox
var _delete_node_button: Button
var _editor_notes: TextEdit
var _choices: VBoxContainer
var _add_choice_button: Button
var _operation: OptionButton
var _preview_button: Button
var _delete_button: Button
var _apply_button: Button
var _status: Label
var _analysis: VBoxContainer
var _reference_summary: VBoxContainer
var _playthrough: VBoxContainer
var _changes: VBoxContainer
var _validation: VBoxContainer
var _condition_status: Label
var _effect_status: Label
var _runtime_status: Label


func _ready() -> void:
	_workspace_support = WORKSPACE_SUPPORT_SCRIPT.new()
	_build_ui()
	_apply_options()
	_connect_client()
	_set_form_enabled(false)
	_clear_preview()
	_render_empty_playthrough()


func _connect_client() -> void:
	_client.dialogue_options_received.connect(_on_dialogue_options_received)
	_client.dialogue_catalog_received.connect(_on_dialogue_catalog_received)
	_client.dialogue_definition_received.connect(_on_dialogue_definition_received)
	_client.dialogue_preview_received.connect(_on_dialogue_preview_received)
	_client.dialogue_playthrough_received.connect(_on_dialogue_playthrough_received)
	_client.dialogue_mutation_completed.connect(_on_dialogue_mutation_completed)
	_client.dialogue_delete_completed.connect(_on_dialogue_delete_completed)
	_client.request_failed.connect(_on_request_failed)


func _build_ui() -> void:
	add_theme_constant_override("separation", 14)

	var catalog_panel := _panel(Vector2(300, 0))
	add_child(catalog_panel)
	var catalog_content := _vbox(catalog_panel)
	_add_heading(catalog_content, "Dialogue", 20)
	_search = LineEdit.new()
	_search.placeholder_text = "Search dialogue ID or name"
	_search.text_changed.connect(_on_search_changed)
	catalog_content.add_child(_search)
	_new_button = Button.new()
	_new_button.text = "+ New Dialogue"
	_new_button.pressed.connect(_start_new_dialogue)
	catalog_content.add_child(_new_button)
	var catalog_scroll := ScrollContainer.new()
	catalog_scroll.size_flags_vertical = Control.SIZE_EXPAND_FILL
	catalog_content.add_child(catalog_scroll)
	_list = VBoxContainer.new()
	_list.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_list.add_theme_constant_override("separation", 6)
	catalog_scroll.add_child(_list)
	CATALOG_PANE_TOGGLE.attach(self, catalog_panel)

	var graph_panel := _panel(Vector2(520, 0))
	graph_panel.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	add_child(graph_panel)
	var graph_content := _vbox(graph_panel)
	_add_heading(graph_content, "Graph", 20)
	var graph_toolbar := HBoxContainer.new()
	graph_toolbar.add_theme_constant_override("separation", 8)
	graph_content.add_child(graph_toolbar)
	_add_graph_button(graph_toolbar, "+ Speaker", NODE_TYPE_SPEAKER_TEXT)
	_add_graph_button(graph_toolbar, "+ Choice", NODE_TYPE_PLAYER_CHOICE)
	_add_graph_button(graph_toolbar, "+ End", NODE_TYPE_END)
	var restart_button := Button.new()
	restart_button.text = "Play"
	restart_button.pressed.connect(_restart_playthrough)
	graph_toolbar.add_child(restart_button)
	_add_lifecycle_section(graph_content)
	_graph = GraphEdit.new()
	_graph.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_graph.size_flags_vertical = Control.SIZE_EXPAND_FILL
	_graph.custom_minimum_size = Vector2(500, 520)
	if _graph.has_signal("connection_request"):
		_graph.connect("connection_request", Callable(self, "_on_connection_request"))
	if _graph.has_signal("disconnection_request"):
		_graph.connect("disconnection_request", Callable(self, "_on_disconnection_request"))
	if _graph.has_signal("delete_nodes_request"):
		_graph.connect("delete_nodes_request", Callable(self, "_on_delete_nodes_request"))
	if _graph.has_signal("node_selected"):
		_graph.connect("node_selected", Callable(self, "_on_graph_edit_node_selected"))
	graph_content.add_child(_graph)

	var inspector_panel := _panel(Vector2(390, 0))
	add_child(inspector_panel)
	var inspector_scroll := ScrollContainer.new()
	inspector_scroll.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	inspector_scroll.size_flags_vertical = Control.SIZE_EXPAND_FILL
	inspector_panel.add_child(inspector_scroll)
	var inspector := VBoxContainer.new()
	inspector.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	inspector.custom_minimum_size = Vector2(320, 0)
	inspector.add_theme_constant_override("separation", 12)
	inspector_scroll.add_child(inspector)
	_add_definition_section(inspector)
	_add_node_section(inspector)
	_add_capability_section(inspector)
	_add_operation_results_section(inspector)


func _add_definition_section(parent: VBoxContainer) -> void:
	_add_heading(parent, "Definition", 20)
	var grid := _grid(parent)
	_dialogue_id = _line_field(grid, "Dialogue ID", "test_npc_greeting")
	_display_name = _line_field(grid, "Display name", "Test NPC Greeting")
	_publication = _value_label(grid, "Publication", "Unknown")
	_updated = _value_label(grid, "Updated", "Unknown")
	_schema_version = _spin_field(grid, "Schema version", 1, 100, 1, 1)
	var entry_header := HBoxContainer.new()
	entry_header.add_theme_constant_override("separation", 6)
	parent.add_child(entry_header)
	_add_heading(entry_header, "Entry Points", 16)
	_add_entry_button = Button.new()
	_add_entry_button.text = "+ Entry Point"
	_add_entry_button.pressed.connect(_add_entry_point)
	entry_header.add_child(_add_entry_button)
	_entry_points = VBoxContainer.new()
	parent.add_child(_entry_points)
	_add_heading(parent, "Metadata", 16)
	_metadata_description = _text_field(parent, "Optional runtime description", 90)
	_add_heading(parent, "Notes", 16)
	_notes = _text_field(parent, "Authoring notes", 90)


func _add_node_section(parent: VBoxContainer) -> void:
	_add_heading(parent, "Selected Node", 20)
	_node_title = _wrapped_label("No node selected.")
	parent.add_child(_node_title)
	var grid := _grid(parent)
	_node_id = _line_field(grid, "Node ID", "start")
	_node_type = _option_field(grid, "Node type")
	_node_type.item_selected.connect(_on_node_type_selected.unbind(1))
	_speaker = _line_field(grid, "Speaker", "Archivist")
	_next_node = _option_field(grid, "Next node")
	_next_node.item_selected.connect(_on_next_node_selected.unbind(1))
	_dismissible = CheckBox.new()
	_dismissible.text = "Dismissible"
	_dismissible.toggled.connect(_on_selected_node_changed.unbind(1))
	parent.add_child(_dismissible)
	_delete_node_button = Button.new()
	_delete_node_button.text = "Delete Selected Node"
	_delete_node_button.disabled = true
	_delete_node_button.pressed.connect(_delete_selected_node)
	parent.add_child(_delete_node_button)
	_add_heading(parent, "Text", 16)
	_text = _text_field(parent, "Dialogue text", 120)
	_add_heading(parent, "Choices", 16)
	_choices = VBoxContainer.new()
	parent.add_child(_choices)
	_add_choice_button = Button.new()
	_add_choice_button.text = "+ Choice"
	_add_choice_button.pressed.connect(_add_choice_to_selected_node)
	parent.add_child(_add_choice_button)
	_add_heading(parent, "Editor Notes", 16)
	_editor_notes = _text_field(parent, "Node notes", 90)


func _add_capability_section(parent: VBoxContainer) -> void:
	_add_heading(parent, "Capabilities", 20)
	var grid := _grid(parent)
	_runtime_status = _value_label(grid, "Runtime export", "Unavailable")
	_condition_status = _value_label(grid, "Conditions", "Unavailable")
	_effect_status = _value_label(grid, "Effects", "Unavailable")


func _add_lifecycle_section(parent: VBoxContainer) -> void:
	_add_heading(parent, "Dialogue Lifecycle", 16)
	var row := HBoxContainer.new()
	row.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	row.add_theme_constant_override("separation", 8)
	parent.add_child(row)
	_operation = OptionButton.new()
	_operation.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_add_operation("Save as Draft", "save_draft")
	_add_operation("Publish", "publish")
	_add_operation("Disable", "disable")
	_add_operation("Delete Dialogue", "delete")
	_operation.item_selected.connect(_on_operation_changed.unbind(1))
	row.add_child(_operation)
	_preview_button = Button.new()
	_preview_button.text = "Validate and Preview Changes"
	_preview_button.pressed.connect(_preview)
	row.add_child(_preview_button)
	_apply_button = Button.new()
	_apply_button.text = "Apply Previewed Operation"
	_apply_button.disabled = true
	_apply_button.pressed.connect(_apply)
	row.add_child(_apply_button)
	_delete_button = Button.new()
	_delete_button.text = "Preview Dialogue Delete"
	_delete_button.disabled = true
	_delete_button.pressed.connect(_preview_delete)
	row.add_child(_delete_button)
	_status = _wrapped_label("Load or create a dialogue definition.")
	parent.add_child(_status)


func _add_operation_results_section(parent: VBoxContainer) -> void:
	_add_heading(parent, "Operation Results", 20)
	_add_heading(parent, "Graph Analysis", 16)
	_analysis = VBoxContainer.new()
	parent.add_child(_analysis)
	_add_heading(parent, "NPC References", 16)
	_reference_summary = VBoxContainer.new()
	parent.add_child(_reference_summary)
	_add_heading(parent, "Playthrough", 16)
	_playthrough = VBoxContainer.new()
	parent.add_child(_playthrough)
	_add_heading(parent, "Exact Logical Changes", 16)
	_changes = VBoxContainer.new()
	parent.add_child(_changes)
	_add_heading(parent, "Validation", 16)
	_validation = VBoxContainer.new()
	parent.add_child(_validation)


func _on_dialogue_options_received(payload: Dictionary) -> void:
	_schema_available = true
	_options = payload
	_apply_options()
	_set_form_enabled(not _current_dialogue.is_empty() or _is_new)
	if _current_dialogue.is_empty() and not _is_new:
		_status.text = "Dialogue schema ready. Load or create a dialogue definition."


func _on_dialogue_catalog_received(payload: Dictionary) -> void:
	_schema_available = true
	_dialogues = payload.get("items", []) as Array
	_rebuild_list()
	_set_form_enabled(not _current_dialogue.is_empty() or _is_new)
	if not _reload_dialogue_id.is_empty():
		var dialogue_id := _reload_dialogue_id
		_reload_dialogue_id = ""
		_client.load_dialogue(dialogue_id)


func _on_dialogue_definition_received(payload: Dictionary) -> void:
	_load_dialogue(payload)


func _on_dialogue_preview_received(payload: Dictionary) -> void:
	var operation := str(payload.get("target_operation", "save_draft"))
	var applicable := bool(payload.get("valid_for_publication", false)) if operation == "publish" else bool(payload.get("valid_for_draft", false))
	_workspace_support.accept_preview(
		operation,
		str(payload.get("preview_signature", "")),
		applicable,
		_apply_button,
		"Apply %s" % _dialogue_operation_name(operation)
	)
	_workspace_support.render_changes(_changes, payload.get("changes", []) as Array)
	_workspace_support.render_validation(_validation, payload.get("messages", []) as Array)
	_render_analysis(payload.get("analysis", {}) as Dictionary)
	_render_reference_summary(payload.get("reference_summary", {}) as Dictionary)
	_status.text = "Preview ready." if applicable else "Preview contains blocking validation errors."


func _on_dialogue_playthrough_received(payload: Dictionary) -> void:
	_visited_node_ids = payload.get("visited_node_ids", []) as Array
	var node: Variant = payload.get("current_node", {})
	_playthrough_node_id = str((node as Dictionary).get("node_id", "")) if node is Dictionary else ""
	_render_playthrough(payload)


func _on_dialogue_mutation_completed(payload: Dictionary) -> void:
	var operation := str(payload.get("operation", "operation"))
	var dialogue := payload.get("dialogue", {}) as Dictionary
	var dialogue_id := str(dialogue.get("dialogue_definition_id", _dialogue_id.text))
	_reload_dialogue_id = dialogue_id
	_current_dialogue = dialogue
	_is_new = false
	_clear_preview()
	_status.text = "%s completed. Reloading dialogue definition..." % _dialogue_operation_name(operation)
	_client.load_dialogues(_search.text)


func _on_dialogue_delete_completed(payload: Dictionary) -> void:
	var deleted_id := str(payload.get("deleted_id", _dialogue_id.text))
	_reload_dialogue_id = ""
	_start_new_dialogue()
	_status.text = "Deleted dialogue %s." % deleted_id
	_client.load_dialogues(_search.text)


func _on_request_failed(operation: String, message: String, errors: Array) -> void:
	if not operation.begins_with("dialogue"):
		return
	_status.text = "%s failed: %s" % [operation, message]
	_workspace_support.render_validation(_validation, errors)
	_apply_button.disabled = true
	if _has_error_code(errors, "dialogue_version_conflict"):
		_status.text = "Version conflict. Reload the dialogue definition before applying changes."
	if operation == "dialogue_options" or operation == "dialogues":
		_schema_available = false
		_set_form_enabled(false)
		if message.contains("route does not exist"):
			_status.text = "Dialogue unavailable: restart the authoring host from this branch, then relaunch Studio."
		else:
			_status.text = "Dialogue unavailable: %s" % message


func open_resource(dialogue_definition_id: String) -> void:
	if not dialogue_definition_id.strip_edges().is_empty():
		_client.load_dialogue(dialogue_definition_id.strip_edges())


func _start_new_dialogue() -> void:
	_is_loading = true
	var defaults := _options.get("defaults", {}) as Dictionary
	_current_dialogue = {}
	_is_new = true
	_selected_node_id = str(defaults.get("start_node_id", "start"))
	_dialogue_id.text = ""
	_dialogue_id.editable = _schema_available
	_display_name.text = ""
	_publication.text = "Unsaved"
	_updated.text = "Not saved"
	_schema_version.value = int(defaults.get("schema_version", 1))
	_metadata_description.text = ""
	_notes.text = ""
	_current_dialogue["entry_points"] = [{
		"entry_id": str(defaults.get("entry_id", "default")),
		"node_id": _selected_node_id,
		"priority": 0,
		"entry_order": 0,
		"conditions": [],
	}]
	_current_dialogue["nodes"] = [
		{
			"node_id": _selected_node_id,
			"node_type": NODE_TYPE_SPEAKER_TEXT,
			"speaker": "",
			"text": "",
			"next_node_id": "end",
			"dismissible": bool(defaults.get("dismissible", true)),
			"canvas_x": 0,
			"canvas_y": 0,
			"editor_notes": "",
			"choices": [],
		},
		{
			"node_id": "end",
			"node_type": NODE_TYPE_END,
			"speaker": "",
			"text": "",
			"next_node_id": null,
			"dismissible": true,
			"canvas_x": 260,
			"canvas_y": 0,
			"editor_notes": "",
			"choices": [],
		},
	]
	_select_option(_operation, "save_draft")
	_set_form_enabled(_schema_available)
	_rebuild_entry_points()
	_rebuild_graph()
	_load_selected_node()
	_clear_preview()
	_render_empty_playthrough()
	_status.text = "Creating a new dialogue definition."
	_dialogue_id.grab_focus()
	_is_loading = false


func _load_dialogue(payload: Dictionary) -> void:
	_is_loading = true
	_current_dialogue = payload.duplicate(true)
	_is_new = false
	_dialogue_id.text = str(payload.get("dialogue_definition_id", ""))
	_dialogue_id.editable = false
	_display_name.text = str(payload.get("display_name", ""))
	_publication.text = str(payload.get("publication_state", "Unknown"))
	_updated.text = str(payload.get("updated_at_utc", "Unknown"))
	_schema_version.value = int(payload.get("schema_version", 1))
	_metadata_description.text = _nullable_string(payload.get("metadata_description", ""))
	_notes.text = _nullable_string(payload.get("notes", ""))
	var nodes := payload.get("nodes", []) as Array
	_selected_node_id = str((nodes[0] as Dictionary).get("node_id", "")) if not nodes.is_empty() and nodes[0] is Dictionary else ""
	_set_form_enabled(bool(payload.get("editable_in_dialogue", true)) and _schema_available)
	_update_operation_default()
	_rebuild_entry_points()
	_rebuild_graph()
	_load_selected_node()
	_clear_preview()
	_render_empty_playthrough()
	_status.text = "Loaded %s." % _dialogue_id.text
	_is_loading = false


func _rebuild_list() -> void:
	_clear_children(_list)
	var query := _search.text.strip_edges().to_lower()
	for variant in _dialogues:
		if variant is not Dictionary:
			continue
		var dialogue := variant as Dictionary
		var haystack := "%s %s" % [
			dialogue.get("dialogue_definition_id", ""),
			dialogue.get("display_name", ""),
		]
		if not query.is_empty() and not haystack.to_lower().contains(query):
			continue
		var button := Button.new()
		button.alignment = HORIZONTAL_ALIGNMENT_LEFT
		button.text = "%s\n%s | %s | %s nodes | %s choices" % [
			str(dialogue.get("display_name", "Unnamed Dialogue")),
			str(dialogue.get("dialogue_definition_id", "")),
			str(dialogue.get("publication_state", "Unknown")),
			str(dialogue.get("node_count", 0)),
			str(dialogue.get("choice_count", 0)),
		]
		button.tooltip_text = "Updated %s" % str(dialogue.get("updated_at_utc", ""))
		button.pressed.connect(_load_dialogue_id.bind(str(dialogue.get("dialogue_definition_id", ""))))
		_list.add_child(button)


func _load_dialogue_id(dialogue_definition_id: String) -> void:
	if not dialogue_definition_id.is_empty():
		_client.load_dialogue(dialogue_definition_id)


func _dialogue_operation_name(operation: String) -> String:
	return "Delete Dialogue" if operation == "delete" else _workspace_support.operation_name(operation)


func _preview() -> void:
	var dialogue_definition_id := _dialogue_id.text.strip_edges()
	if dialogue_definition_id.is_empty():
		_status.text = "Enter a stable dialogue definition ID before previewing."
		return
	var payload := _payload()
	payload.erase("preview_signature")
	payload["target_operation"] = _selected_metadata(_operation)
	_client.preview_dialogue(dialogue_definition_id, payload)
	_status.text = "Calculating validation and exact logical changes..."


func _preview_delete() -> void:
	if _current_dialogue.is_empty():
		_status.text = "Select a saved disabled dialogue definition before deleting."
		return
	_select_option(_operation, "delete")
	_preview()


func _apply() -> void:
	var operation := _selected_metadata(_operation)
	var preview_signature: String = _workspace_support.preview_signature
	if not _workspace_support.can_apply(operation, preview_signature):
		_status.text = "The form changed. Preview the operation again before applying it."
		_apply_button.disabled = true
		return
	var dialogue_definition_id := _dialogue_id.text.strip_edges()
	var expected: Variant = _current_dialogue.get("updated_at_utc", null)
	match operation:
		"publish":
			_client.publish_dialogue(dialogue_definition_id, expected, preview_signature)
			_status.text = "Publishing saved dialogue definition..."
		"disable":
			_client.disable_dialogue(dialogue_definition_id, expected, preview_signature)
			_status.text = "Disabling saved dialogue definition..."
		"delete":
			_client.delete_dialogue(dialogue_definition_id, expected, preview_signature)
			_status.text = "Deleting dialogue definition..."
		_:
			var payload := _payload()
			payload["preview_signature"] = preview_signature
			_client.save_dialogue_draft(dialogue_definition_id, payload)
			_status.text = "Saving complete dialogue draft..."
	_apply_button.disabled = true


func _payload() -> Dictionary:
	_sync_selected_node_from_form()
	_sync_graph_connections_to_draft()
	return {
		"display_name": _display_name.text,
		"schema_version": int(_schema_version.value),
		"entry_points": _entry_points_payload(_current_dialogue.get("entry_points", []) as Array),
		"nodes": _nodes_payload(_current_dialogue.get("nodes", []) as Array),
		"metadata_description": _optional_payload(_metadata_description.text),
		"notes": _optional_payload(_notes.text),
		"expected_updated_at_utc": _current_dialogue.get("updated_at_utc", null),
		"preview_signature": null,
	}


func _entry_points_payload(entries: Array) -> Array:
	var payload := []
	for index in range(entries.size()):
		if entries[index] is not Dictionary:
			continue
		var entry := entries[index] as Dictionary
		payload.append({
			"entry_id": str(entry.get("entry_id", "")),
			"node_id": str(entry.get("node_id", "")),
			"priority": int(entry.get("priority", 0)),
			"entry_order": index,
			"conditions": _conditions_payload(entry.get("conditions", []) as Array),
		})
	return payload


func _nodes_payload(nodes: Array) -> Array:
	var payload := []
	for variant in nodes:
		if variant is not Dictionary:
			continue
		var node := variant as Dictionary
		payload.append({
			"node_id": str(node.get("node_id", "")),
			"node_type": str(node.get("node_type", NODE_TYPE_SPEAKER_TEXT)),
			"speaker": _optional_variant_payload(node.get("speaker", null)),
			"text": _optional_variant_payload(node.get("text", null)),
			"next_node_id": _optional_variant_payload(node.get("next_node_id", null)),
			"dismissible": bool(node.get("dismissible", true)),
			"canvas_x": float(node.get("canvas_x", 0)),
			"canvas_y": float(node.get("canvas_y", 0)),
			"editor_notes": _optional_variant_payload(node.get("editor_notes", null)),
			"choices": _choices_payload(node.get("choices", []) as Array),
		})
	return payload


func _choices_payload(choices: Array) -> Array:
	var payload := []
	for variant in choices:
		if variant is not Dictionary:
			continue
		var choice := variant as Dictionary
		payload.append({
			"choice_id": str(choice.get("choice_id", "")),
			"text": str(choice.get("text", "")),
			"target_node_id": str(choice.get("target_node_id", "")),
			"choice_order": int(choice.get("choice_order", 0)),
			"conditions": _conditions_payload(choice.get("conditions", []) as Array),
			"effects": _effects_payload(choice.get("effects", []) as Array),
		})
	return payload


func _conditions_payload(conditions: Array) -> Array:
	var payload := []
	for variant in conditions:
		if variant is not Dictionary:
			continue
		var condition := variant as Dictionary
		payload.append({
			"condition_type": str(condition.get("condition_type", CONDITION_TYPE_QUEST_STATUS)),
			"quest_id": _optional_variant_payload(condition.get("quest_id", null)),
			"status": _optional_variant_payload(_condition_value(condition, "quest_status", "status", null)),
			"step_id": _optional_variant_payload(_condition_value(condition, "quest_step_id", "step_id", null)),
			"item_id": _optional_variant_payload(condition.get("item_id", null)),
			"quantity": _optional_int_payload(_condition_value(condition, "item_quantity", "quantity", null)),
		})
	return payload


func _effects_payload(effects: Array) -> Array:
	var payload := []
	for variant in effects:
		if variant is not Dictionary:
			continue
		var effect := variant as Dictionary
		payload.append({
			"effect_id": str(effect.get("effect_id", "")),
			"effect_order": int(effect.get("effect_order", 0)),
			"effect_type": str(effect.get("effect_type", EFFECT_TYPE_GRANT_ITEM)),
			"quest_id": _optional_variant_payload(effect.get("quest_id", null)),
			"transition_id": _optional_variant_payload(effect.get("transition_id", null)),
			"item_id": _optional_variant_payload(effect.get("item_id", null)),
			"quantity": _optional_int_payload(effect.get("quantity", null)),
			"skill_id": _optional_variant_payload(effect.get("skill_id", null)),
			"xp_amount": _optional_int_payload(effect.get("xp_amount", null)),
		})
	return payload


func _restart_playthrough() -> void:
	var dialogue_definition_id := _dialogue_id.text.strip_edges()
	if dialogue_definition_id.is_empty():
		_status.text = "Enter a dialogue ID before previewing playthrough."
		return
	_visited_node_ids = []
	_playthrough_node_id = ""
	_client.preview_dialogue_playthrough(dialogue_definition_id, {
		"draft": _payload(),
		"entry_id": _first_entry_id(),
		"current_node_id": null,
		"selected_choice_id": null,
		"acknowledge_end": false,
		"restart": true,
		"visited_node_ids": [],
		"maximum_step_count": int((_options.get("supported_limits", {}) as Dictionary).get("max_playthrough_steps", 128)),
	})


func _continue_playthrough() -> void:
	_client.preview_dialogue_playthrough(_dialogue_id.text.strip_edges(), {
		"draft": _payload(),
		"entry_id": null,
		"current_node_id": _playthrough_node_id,
		"selected_choice_id": null,
		"acknowledge_end": false,
		"restart": false,
		"visited_node_ids": _visited_node_ids,
		"maximum_step_count": int((_options.get("supported_limits", {}) as Dictionary).get("max_playthrough_steps", 128)),
	})


func _choose_playthrough(choice_id: String) -> void:
	_client.preview_dialogue_playthrough(_dialogue_id.text.strip_edges(), {
		"draft": _payload(),
		"entry_id": null,
		"current_node_id": _playthrough_node_id,
		"selected_choice_id": choice_id,
		"acknowledge_end": false,
		"restart": false,
		"visited_node_ids": _visited_node_ids,
		"maximum_step_count": int((_options.get("supported_limits", {}) as Dictionary).get("max_playthrough_steps", 128)),
	})


func _acknowledge_end() -> void:
	_client.preview_dialogue_playthrough(_dialogue_id.text.strip_edges(), {
		"draft": _payload(),
		"entry_id": null,
		"current_node_id": _playthrough_node_id,
		"selected_choice_id": null,
		"acknowledge_end": true,
		"restart": false,
		"visited_node_ids": _visited_node_ids,
		"maximum_step_count": int((_options.get("supported_limits", {}) as Dictionary).get("max_playthrough_steps", 128)),
	})


func _add_node(node_type: String) -> void:
	if not _form_editable:
		return
	_sync_selected_node_from_form()
	var nodes := _current_dialogue.get("nodes", []) as Array
	var node_id := _unique_node_id(node_type)
	nodes.append({
		"node_id": node_id,
		"node_type": node_type,
		"speaker": "",
		"text": "",
		"next_node_id": null,
		"dismissible": true,
		"canvas_x": 80 + (nodes.size() * 80),
		"canvas_y": 80 + (nodes.size() * 35),
		"editor_notes": "",
		"choices": [] if node_type != NODE_TYPE_PLAYER_CHOICE else [{
			"choice_id": "choice_1",
			"text": "Continue",
			"target_node_id": "",
			"choice_order": 0,
			"conditions": [],
			"effects": [],
		}],
	})
	_current_dialogue["nodes"] = nodes
	_selected_node_id = node_id
	_rebuild_entry_points()
	_rebuild_graph()
	_load_selected_node()
	_on_form_changed()


func _add_entry_point() -> void:
	if not _form_editable:
		return
	_sync_selected_node_from_form()
	var entries := _current_dialogue.get("entry_points", []) as Array
	var node_id := _selected_node_id
	if node_id.is_empty():
		node_id = _first_node_id()
	var entry_id_seed := "%s_entry" % node_id if not node_id.is_empty() else "entry"
	entries.append({
		"entry_id": _unique_entry_id(entry_id_seed),
		"node_id": node_id,
		"priority": 10 if not entries.is_empty() else 0,
		"entry_order": entries.size(),
		"conditions": [],
	})
	_current_dialogue["entry_points"] = entries
	_normalize_entry_orders()
	_rebuild_entry_points()
	_on_form_changed()


func _remove_entry_point(index: int) -> void:
	if not _form_editable:
		return
	var entries := _current_dialogue.get("entry_points", []) as Array
	if entries.size() <= 1 or index < 0 or index >= entries.size():
		return
	entries.remove_at(index)
	_current_dialogue["entry_points"] = entries
	_normalize_entry_orders()
	_rebuild_entry_points()
	_on_form_changed()


func _delete_selected_node() -> void:
	if not _form_editable or _selected_node_id.is_empty():
		return
	var deleted_node_id := _selected_node_id
	var nodes := _current_dialogue.get("nodes", []) as Array
	var remaining: Array = []
	for variant in nodes:
		if variant is Dictionary and str((variant as Dictionary).get("node_id", "")) != _selected_node_id:
			remaining.append(variant)
	_current_dialogue["nodes"] = remaining
	for node_variant in remaining:
		if node_variant is not Dictionary:
			continue
		var node := node_variant as Dictionary
		if str(node.get("next_node_id", "")) == _selected_node_id:
			node["next_node_id"] = null
		for choice_variant in node.get("choices", []) as Array:
			if choice_variant is Dictionary and str((choice_variant as Dictionary).get("target_node_id", "")) == _selected_node_id:
				(choice_variant as Dictionary)["target_node_id"] = ""
	_selected_node_id = str((remaining[0] as Dictionary).get("node_id", "")) if not remaining.is_empty() and remaining[0] is Dictionary else ""
	_rebuild_entry_points()
	_rebuild_graph()
	_load_selected_node()
	_on_form_changed()
	_status.text = "Deleted node %s from the draft. Save the dialogue draft to persist this change." % deleted_node_id


func _rebuild_graph() -> void:
	for child in _graph.get_children():
		if child is not GraphNode:
			continue
		_graph.remove_child(child)
		child.queue_free()
	if _graph.has_method("clear_connections"):
		_graph.call("clear_connections")
	var nodes := _current_dialogue.get("nodes", []) as Array
	for variant in nodes:
		if variant is not Dictionary:
			continue
		var node := variant as Dictionary
		var graph_node := GraphNode.new()
		graph_node.name = str(node.get("node_id", "node"))
		graph_node.title = "%s  [%s]" % [str(node.get("node_id", "node")), _node_type_label(str(node.get("node_type", "")))]
		graph_node.position_offset = Vector2(float(node.get("canvas_x", 0)), float(node.get("canvas_y", 0)))
		graph_node.resizable = false
		graph_node.custom_minimum_size = Vector2(GRAPH_NODE_MIN_WIDTH, 0)
		var summary := Label.new()
		summary.text = _graph_summary(node)
		summary.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
		summary.custom_minimum_size = Vector2(GRAPH_NODE_SUMMARY_WIDTH, 0)
		graph_node.add_child(summary)
		graph_node.set_slot(0, true, 0, Color(0.42, 0.7, 0.9), str(node.get("node_type", "")) != NODE_TYPE_END, 0, Color(0.85, 0.67, 0.35))
		if graph_node.has_signal("position_offset_changed"):
			graph_node.connect("position_offset_changed", Callable(self, "_on_graph_node_moved").bind(graph_node.name))
		if not _graph.has_signal("node_selected") and graph_node.has_signal("selected"):
			graph_node.connect("selected", Callable(self, "_on_graph_node_selected").bind(graph_node.name))
		if graph_node.has_signal("close_request"):
			graph_node.connect("close_request", Callable(self, "_on_graph_node_delete_requested").bind(graph_node.name))
		_graph.add_child(graph_node)
	_connect_graph_nodes()


func _connect_graph_nodes() -> void:
	for node_variant in _current_dialogue.get("nodes", []) as Array:
		if node_variant is not Dictionary:
			continue
		var node := node_variant as Dictionary
		if str(node.get("node_type", "")) == NODE_TYPE_END:
			continue
		var from_node := str(node.get("node_id", ""))
		var next_node := str(node.get("next_node_id", ""))
		if not next_node.is_empty():
			_connect_graph_node(from_node, next_node)
		for choice_variant in node.get("choices", []) as Array:
			if choice_variant is Dictionary:
				var target := str((choice_variant as Dictionary).get("target_node_id", ""))
				if not target.is_empty():
					_connect_graph_node(from_node, target)


func _connect_graph_node(from_node: String, to_node: String) -> void:
	if _graph.has_method("connect_node") and _has_node_id(from_node) and _has_node_id(to_node):
		var result = _graph.call("connect_node", from_node, 0, to_node, 0)
		if result != OK and result != ERR_ALREADY_EXISTS:
			return


func _on_connection_request(from_node: StringName, from_port: int, to_node: StringName, to_port: int) -> void:
	var from_node_id := str(from_node)
	var to_node_id := str(to_node)
	var node := _find_node(from_node_id)
	if node.is_empty():
		CONTENT_STUDIO_LOGGER.debug("Dialogue graph connection requested for missing source node", {
			"from_node": from_node_id,
			"from_port": from_port,
			"to_node": to_node_id,
			"to_port": to_port,
		})
		return
	var node_type := str(node.get("node_type", ""))
	var changed_field := "next_node_id"
	var previous_target := _nullable_string(node.get("next_node_id", null))
	if node_type == NODE_TYPE_PLAYER_CHOICE:
		var choices := node.get("choices", []) as Array
		if choices.is_empty():
			changed_field = "choices[0].target_node_id"
			previous_target = ""
			choices.append({
				"choice_id": "choice_1",
				"text": "Continue",
				"target_node_id": to_node_id,
				"choice_order": 0,
				"conditions": [],
				"effects": [],
			})
		else:
			changed_field = "choices[0].target_node_id"
			previous_target = str((choices[0] as Dictionary).get("target_node_id", ""))
			(choices[0] as Dictionary)["target_node_id"] = to_node_id
		node["choices"] = choices
	else:
		node["next_node_id"] = to_node_id
	CONTENT_STUDIO_LOGGER.debug("Dialogue graph connection requested", {
		"changed_field": changed_field,
		"from_node": from_node_id,
		"from_port": from_port,
		"new_target": to_node_id,
		"node_type": node_type,
		"previous_target": previous_target,
		"to_node": to_node_id,
		"to_port": to_port,
	})
	_rebuild_graph()
	_load_selected_node()
	_on_form_changed()


func _on_disconnection_request(from_node: StringName, from_port: int, to_node: StringName, to_port: int) -> void:
	var from_node_id := str(from_node)
	var to_node_id := str(to_node)
	var node := _find_node(from_node_id)
	if node.is_empty():
		CONTENT_STUDIO_LOGGER.debug("Dialogue graph disconnection requested for missing source node", {
			"from_node": from_node_id,
			"from_port": from_port,
			"to_node": to_node_id,
			"to_port": to_port,
		})
		return
	var cleared_fields: Array[String] = []
	if str(node.get("next_node_id", "")) == to_node_id:
		node["next_node_id"] = null
		cleared_fields.append("next_node_id")
	for choice_variant in node.get("choices", []) as Array:
		if choice_variant is Dictionary and str((choice_variant as Dictionary).get("target_node_id", "")) == str(to_node):
			(choice_variant as Dictionary)["target_node_id"] = ""
			cleared_fields.append("choice.target_node_id")
	CONTENT_STUDIO_LOGGER.debug("Dialogue graph disconnection requested", {
		"cleared": not cleared_fields.is_empty(),
		"cleared_fields": ", ".join(cleared_fields),
		"from_node": from_node_id,
		"from_port": from_port,
		"node_type": str(node.get("node_type", "")),
		"to_node": to_node_id,
		"to_port": to_port,
	})
	_rebuild_graph()
	_load_selected_node()
	_on_form_changed()


func _on_delete_nodes_request(nodes: Array) -> void:
	if not nodes.is_empty():
		_selected_node_id = str(nodes[0])
		_delete_selected_node()


func _on_graph_node_selected(node_name: StringName) -> void:
	_pending_graph_node_selection = str(node_name)
	if _graph_selection_update_queued:
		return
	_graph_selection_update_queued = true
	call_deferred("_apply_graph_node_selection")


func _on_graph_edit_node_selected(node: Node) -> void:
	if node is not GraphNode:
		return
	_select_graph_node(str(node.name))


func _apply_graph_node_selection() -> void:
	_graph_selection_update_queued = false
	var selected_node_id := _selected_graph_node_id()
	_pending_graph_node_selection = ""
	_select_graph_node(selected_node_id)


func _select_graph_node(node_id: String) -> void:
	if node_id.is_empty() or node_id == _selected_node_id:
		return
	_sync_selected_node_from_form()
	_selected_node_id = node_id
	_load_selected_node()


func _selected_graph_node_id() -> String:
	var selected_ids := []
	for child in _graph.get_children():
		if child is not GraphNode:
			continue
		var graph_node := child as GraphNode
		if bool(graph_node.get("selected")):
			selected_ids.append(str(graph_node.name))
	if _pending_graph_node_selection in selected_ids:
		return _pending_graph_node_selection
	if selected_ids.size() == 1:
		return str(selected_ids[0])
	return _pending_graph_node_selection


func _on_graph_node_moved(node_name: StringName) -> void:
	var node := _find_node(str(node_name))
	var graph_node := _graph.get_node_or_null(str(node_name)) as GraphNode
	if node.is_empty() or graph_node == null:
		return
	node["canvas_x"] = graph_node.position_offset.x
	node["canvas_y"] = graph_node.position_offset.y
	_on_form_changed()


func _on_graph_node_delete_requested(node_name: StringName) -> void:
	_selected_node_id = str(node_name)
	_delete_selected_node()


func _load_selected_node() -> void:
	_is_loading = true
	var node := _find_node(_selected_node_id)
	var has_node := not node.is_empty()
	_node_title.text = "Editing %s" % _selected_node_id if has_node else "No node selected."
	_node_id.text = str(node.get("node_id", ""))
	_select_option(_node_type, str(node.get("node_type", NODE_TYPE_SPEAKER_TEXT)))
	_speaker.text = _nullable_string(node.get("speaker", ""))
	_text.text = _nullable_string(node.get("text", ""))
	_fill_node_options(_next_node, _nullable_string(node.get("next_node_id", "")), "No automatic next node")
	_select_option(_next_node, _nullable_string(node.get("next_node_id", "")))
	_dismissible.button_pressed = bool(node.get("dismissible", true))
	_editor_notes.text = _nullable_string(node.get("editor_notes", ""))
	_rebuild_choices(node)
	_set_node_controls_enabled(has_node and _form_editable)
	_is_loading = false


func _sync_selected_node_from_form() -> void:
	if _is_loading or _selected_node_id.is_empty():
		return
	var node := _find_node(_selected_node_id)
	if node.is_empty():
		return
	var next_id := _node_id.text.strip_edges()
	if not next_id.is_empty() and next_id != _selected_node_id and not _has_node_id(next_id):
		_rename_node(_selected_node_id, next_id)
		_selected_node_id = next_id
		node = _find_node(_selected_node_id)
	node["node_type"] = _selected_metadata(_node_type)
	node["speaker"] = _optional_payload(_speaker.text)
	node["text"] = _optional_payload(_text.text)
	_apply_node_type_transition_shape(node)
	node["dismissible"] = _dismissible.button_pressed
	node["editor_notes"] = _optional_payload(_editor_notes.text)


func _sync_graph_connections_to_draft() -> void:
	if _graph == null or not _graph.has_method("get_connection_list"):
		return
	var speaker_next_nodes := {}
	var existing_model_link_count := 0
	for variant in _current_dialogue.get("nodes", []) as Array:
		if variant is not Dictionary:
			continue
		var node := variant as Dictionary
		if str(node.get("node_type", "")) == NODE_TYPE_SPEAKER_TEXT:
			var next_node = _optional_variant_payload(node.get("next_node_id", null))
			speaker_next_nodes[str(node.get("node_id", ""))] = next_node
			if next_node != null:
				existing_model_link_count += 1
	var reported_connections := _graph.call("get_connection_list") as Array
	var valid_connection_count := 0
	var applied_visible_connection_count := 0
	var ignored_visible_connection_count := 0
	if reported_connections.is_empty() and existing_model_link_count > 0:
		CONTENT_STUDIO_LOGGER.debug("Dialogue graph sync preserved model links without reported graph connections", {
			"existing_model_links": existing_model_link_count,
		})
	for connection_variant in reported_connections:
		if connection_variant is not Dictionary:
			continue
		var connection := connection_variant as Dictionary
		var from_node_id := str(connection.get("from_node", ""))
		var to_node_id := str(connection.get("to_node", ""))
		if from_node_id.is_empty() or to_node_id.is_empty():
			continue
		if not _has_node_id(from_node_id) or not _has_node_id(to_node_id):
			continue
		valid_connection_count += 1
		if speaker_next_nodes.has(from_node_id):
			if speaker_next_nodes[from_node_id] == null or str(speaker_next_nodes[from_node_id]) != to_node_id:
				speaker_next_nodes[from_node_id] = to_node_id
				applied_visible_connection_count += 1
		else:
			ignored_visible_connection_count += 1
	if not reported_connections.is_empty():
		CONTENT_STUDIO_LOGGER.debug("Dialogue graph sync observed reported connections", {
			"applied_visible_connections": applied_visible_connection_count,
			"existing_model_links": existing_model_link_count,
			"ignored_visible_connections": ignored_visible_connection_count,
			"reported_connections": reported_connections.size(),
			"valid_connections": valid_connection_count,
		})
	for from_node_id in speaker_next_nodes.keys():
		var node := _find_node(str(from_node_id))
		if node.is_empty():
			continue
		node["next_node_id"] = speaker_next_nodes[from_node_id]
	if speaker_next_nodes.has(_selected_node_id):
		_select_option(_next_node, _nullable_string(speaker_next_nodes[_selected_node_id]))


func _on_selected_node_changed(_value: Variant = null) -> void:
	if _is_loading:
		return
	_sync_selected_node_from_form()
	_rebuild_entry_points()
	_rebuild_graph()
	_load_selected_node()
	_on_form_changed()


func _on_node_type_selected() -> void:
	var node := _find_node(_selected_node_id)
	if node.is_empty():
		return
	node["node_type"] = _selected_metadata(_node_type)
	_apply_node_type_transition_shape(node)
	_rebuild_graph()
	_load_selected_node()
	_on_form_changed()


func _apply_node_type_transition_shape(node: Dictionary) -> void:
	var node_type := str(node.get("node_type", NODE_TYPE_SPEAKER_TEXT))
	match node_type:
		NODE_TYPE_END:
			node["next_node_id"] = null
			node["choices"] = []
		NODE_TYPE_PLAYER_CHOICE:
			node["next_node_id"] = null
		_:
			node["next_node_id"] = _optional_payload(_selected_metadata(_next_node))
			node["choices"] = []


func _on_next_node_selected() -> void:
	_sync_selected_node_from_form()
	_rebuild_graph()
	_on_form_changed()


func _add_choice_to_selected_node() -> void:
	var node := _find_node(_selected_node_id)
	if node.is_empty():
		return
	node["node_type"] = NODE_TYPE_PLAYER_CHOICE
	var choices := node.get("choices", []) as Array
	var order := choices.size()
	choices.append({
		"choice_id": _unique_choice_id(node, "choice"),
		"text": "",
		"target_node_id": "",
		"choice_order": order,
		"conditions": [],
		"effects": [],
	})
	node["choices"] = choices
	_rebuild_graph()
	_load_selected_node()
	_on_form_changed()


func _rebuild_choices(node: Dictionary) -> void:
	_clear_children(_choices)
	if node.is_empty():
		_workspace_support.add_wrapped_label(_choices, "Select a node to edit choices.")
		return
	var choices := node.get("choices", []) as Array
	if choices.is_empty():
		_workspace_support.add_wrapped_label(_choices, "No choices on this node. Effects are attached to player choices; use + Choice to author item or quest effects.")
		return
	for index in range(choices.size()):
		if choices[index] is not Dictionary:
			continue
		var choice := choices[index] as Dictionary
		var row := VBoxContainer.new()
		row.add_theme_constant_override("separation", 4)
		_choices.add_child(row)
		var id_field := LineEdit.new()
		id_field.placeholder_text = "choice_id"
		id_field.text = str(choice.get("choice_id", ""))
		id_field.text_changed.connect(_on_choice_id_changed.bind(index))
		row.add_child(id_field)
		var text_field := LineEdit.new()
		text_field.placeholder_text = "Choice text"
		text_field.text = str(choice.get("text", ""))
		text_field.text_changed.connect(_on_choice_text_changed.bind(index))
		row.add_child(text_field)
		var target := OptionButton.new()
		_fill_node_options(target, str(choice.get("target_node_id", "")), "Choose target")
		target.item_selected.connect(_on_choice_target_selected.bind(index, target))
		row.add_child(target)
		var remove := Button.new()
		remove.text = "Remove Choice"
		remove.pressed.connect(_remove_choice.bind(index))
		row.add_child(remove)
		_add_conditions_editor(row, choice.get("conditions", []) as Array, "choice", index, index)
		_add_effects_editor(row, choice.get("effects", []) as Array, index)


func _on_choice_id_changed(value: String, index: int) -> void:
	var choice := _choice_at(index)
	if choice.is_empty():
		return
	choice["choice_id"] = value.strip_edges()
	_on_form_changed()


func _on_choice_text_changed(value: String, index: int) -> void:
	var choice := _choice_at(index)
	if choice.is_empty():
		return
	choice["text"] = value
	_on_form_changed()


func _on_choice_target_selected(_selected_index: int, index: int, target: OptionButton) -> void:
	var choice := _choice_at(index)
	if choice.is_empty():
		return
	choice["target_node_id"] = _selected_metadata(target)
	_rebuild_graph()
	_on_form_changed()


func _remove_choice(index: int) -> void:
	var node := _find_node(_selected_node_id)
	if node.is_empty():
		return
	var choices := node.get("choices", []) as Array
	if index >= 0 and index < choices.size():
		choices.remove_at(index)
	for choice_index in range(choices.size()):
		if choices[choice_index] is Dictionary:
			(choices[choice_index] as Dictionary)["choice_order"] = choice_index
	node["choices"] = choices
	_rebuild_graph()
	_load_selected_node()
	_on_form_changed()


func _add_conditions_editor(parent: VBoxContainer, conditions: Array, owner_kind: String, owner_index: int, choice_index: int) -> void:
	var header := HBoxContainer.new()
	header.add_theme_constant_override("separation", 6)
	parent.add_child(header)
	var label := _wrapped_label("Conditions (%d)" % conditions.size())
	header.add_child(label)
	var add_button := Button.new()
	add_button.text = "+ Condition"
	add_button.disabled = not _form_editable
	add_button.pressed.connect(_add_condition.bind(owner_kind, owner_index, choice_index))
	header.add_child(add_button)
	for condition_index in range(conditions.size()):
		if conditions[condition_index] is not Dictionary:
			continue
		var condition := conditions[condition_index] as Dictionary
		_add_condition_row(parent, condition, owner_kind, owner_index, choice_index, condition_index)


func _add_condition_row(parent: VBoxContainer, condition: Dictionary, owner_kind: String, owner_index: int, choice_index: int, condition_index: int) -> void:
	var row := VBoxContainer.new()
	row.add_theme_constant_override("separation", 4)
	parent.add_child(row)
	var top := HBoxContainer.new()
	top.add_theme_constant_override("separation", 6)
	row.add_child(top)
	var type_select := OptionButton.new()
	type_select.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_fill_condition_type_options(type_select, str(condition.get("condition_type", CONDITION_TYPE_QUEST_STATUS)))
	type_select.disabled = not _form_editable
	type_select.item_selected.connect(_on_condition_type_selected.bind(owner_kind, owner_index, choice_index, condition_index, type_select))
	top.add_child(type_select)
	var remove := Button.new()
	remove.text = "Remove"
	remove.disabled = not _form_editable
	remove.pressed.connect(_remove_condition.bind(owner_kind, owner_index, choice_index, condition_index))
	top.add_child(remove)
	var grid := GridContainer.new()
	grid.columns = 2
	grid.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	grid.add_theme_constant_override("h_separation", 8)
	grid.add_theme_constant_override("v_separation", 4)
	row.add_child(grid)
	var condition_type := str(condition.get("condition_type", CONDITION_TYPE_QUEST_STATUS))
	match condition_type:
		CONDITION_TYPE_QUEST_STEP:
			_add_condition_quest_field(grid, str(condition.get("quest_id", "")), owner_kind, owner_index, choice_index, condition_index)
			_add_condition_step_field(grid, str(condition.get("quest_id", "")), _condition_string_value(condition, "quest_step_id", "step_id", ""), owner_kind, owner_index, choice_index, condition_index)
		CONDITION_TYPE_HAS_ITEM:
			_add_condition_item_field(grid, str(condition.get("item_id", "")), owner_kind, owner_index, choice_index, condition_index)
			_add_condition_quantity_field(grid, int(_condition_value(condition, "item_quantity", "quantity", 1)), owner_kind, owner_index, choice_index, condition_index)
		_:
			_add_condition_quest_field(grid, str(condition.get("quest_id", "")), owner_kind, owner_index, choice_index, condition_index)
			_add_condition_status_field(grid, _condition_string_value(condition, "quest_status", "status", "active"), owner_kind, owner_index, choice_index, condition_index)


func _add_condition_text_field(grid: GridContainer, label_text: String, value: String, placeholder: String, callback: Callable) -> void:
	grid.add_child(_label(label_text))
	var field := LineEdit.new()
	field.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	field.placeholder_text = placeholder
	field.text = value
	field.editable = _form_editable
	field.text_changed.connect(callback)
	grid.add_child(field)


func _add_condition_quest_field(grid: GridContainer, selected: String, owner_kind: String, owner_index: int, choice_index: int, condition_index: int) -> void:
	grid.add_child(_label("Quest"))
	var quest := OptionButton.new()
	quest.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_fill_quest_reference_options(quest, selected)
	quest.disabled = not _form_editable
	quest.item_selected.connect(_on_condition_quest_selected.bind(owner_kind, owner_index, choice_index, condition_index, quest))
	grid.add_child(quest)


func _add_condition_step_field(grid: GridContainer, quest_id: String, selected: String, owner_kind: String, owner_index: int, choice_index: int, condition_index: int) -> void:
	grid.add_child(_label("Step"))
	var step := OptionButton.new()
	step.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_fill_quest_step_options(step, quest_id, selected)
	step.disabled = not _form_editable
	step.item_selected.connect(_on_condition_step_selected.bind(owner_kind, owner_index, choice_index, condition_index, step))
	grid.add_child(step)


func _add_condition_item_field(grid: GridContainer, selected: String, owner_kind: String, owner_index: int, choice_index: int, condition_index: int) -> void:
	grid.add_child(_label("Item"))
	var item := OptionButton.new()
	item.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_fill_item_reference_options(item, selected)
	item.disabled = not _form_editable
	item.item_selected.connect(_on_condition_item_selected.bind(owner_kind, owner_index, choice_index, condition_index, item))
	grid.add_child(item)


func _add_condition_status_field(grid: GridContainer, selected: String, owner_kind: String, owner_index: int, choice_index: int, condition_index: int) -> void:
	grid.add_child(_label("Status"))
	var status := OptionButton.new()
	status.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	for quest_status in QUEST_STATUSES:
		status.add_item(quest_status)
		status.set_item_metadata(status.item_count - 1, quest_status)
	_select_option(status, selected)
	status.disabled = not _form_editable
	status.item_selected.connect(_on_condition_status_selected.bind(owner_kind, owner_index, choice_index, condition_index, status))
	grid.add_child(status)


func _add_condition_quantity_field(grid: GridContainer, value: int, owner_kind: String, owner_index: int, choice_index: int, condition_index: int) -> void:
	grid.add_child(_label("Quantity"))
	var quantity := SpinBox.new()
	quantity.min_value = 1
	quantity.max_value = 999999
	quantity.step = 1
	quantity.value = max(1, value)
	quantity.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	quantity.editable = _form_editable
	quantity.value_changed.connect(_on_condition_quantity_changed.bind(owner_kind, owner_index, choice_index, condition_index))
	grid.add_child(quantity)


func _add_effects_editor(parent: VBoxContainer, effects: Array, choice_index: int) -> void:
	var header := HBoxContainer.new()
	header.add_theme_constant_override("separation", 6)
	parent.add_child(header)
	var label := _wrapped_label("Effects (%d)" % effects.size())
	header.add_child(label)
	var add_button := Button.new()
	add_button.text = "+ Effect"
	add_button.disabled = not _form_editable
	add_button.pressed.connect(_add_effect.bind(choice_index))
	header.add_child(add_button)
	for effect_index in range(effects.size()):
		if effects[effect_index] is not Dictionary:
			continue
		var effect := effects[effect_index] as Dictionary
		_add_effect_row(parent, effect, choice_index, effect_index)


func _add_effect_row(parent: VBoxContainer, effect: Dictionary, choice_index: int, effect_index: int) -> void:
	var row := VBoxContainer.new()
	row.add_theme_constant_override("separation", 4)
	parent.add_child(row)
	var top := HBoxContainer.new()
	top.add_theme_constant_override("separation", 6)
	row.add_child(top)
	var id_field := LineEdit.new()
	id_field.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	id_field.placeholder_text = "effect_id"
	id_field.text = str(effect.get("effect_id", ""))
	id_field.editable = _form_editable
	id_field.text_changed.connect(_on_effect_id_changed.bind(choice_index, effect_index))
	top.add_child(id_field)
	var type_select := OptionButton.new()
	type_select.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_fill_effect_type_options(type_select, str(effect.get("effect_type", EFFECT_TYPE_GRANT_ITEM)))
	type_select.disabled = not _form_editable
	type_select.item_selected.connect(_on_effect_type_selected.bind(choice_index, effect_index, type_select))
	top.add_child(type_select)
	var up := Button.new()
	up.text = "Up"
	up.disabled = not _form_editable or effect_index <= 0
	up.pressed.connect(_move_effect.bind(choice_index, effect_index, -1))
	top.add_child(up)
	var down := Button.new()
	down.text = "Down"
	down.disabled = not _form_editable or effect_index >= (_effect_list_at(choice_index).size() - 1)
	down.pressed.connect(_move_effect.bind(choice_index, effect_index, 1))
	top.add_child(down)
	var remove := Button.new()
	remove.text = "Remove"
	remove.disabled = not _form_editable
	remove.pressed.connect(_remove_effect.bind(choice_index, effect_index))
	top.add_child(remove)
	var grid := GridContainer.new()
	grid.columns = 2
	grid.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	grid.add_theme_constant_override("h_separation", 8)
	grid.add_theme_constant_override("v_separation", 4)
	row.add_child(grid)
	var effect_type := str(effect.get("effect_type", EFFECT_TYPE_GRANT_ITEM))
	match effect_type:
		EFFECT_TYPE_START_QUEST, EFFECT_TYPE_ADVANCE_QUEST, EFFECT_TYPE_COMPLETE_QUEST:
			_add_effect_quest_field(grid, str(effect.get("quest_id", "")), choice_index, effect_index)
			_add_effect_transition_field(grid, str(effect.get("quest_id", "")), effect_type, str(effect.get("transition_id", "")), choice_index, effect_index)
		EFFECT_TYPE_GRANT_EXPERIENCE:
			_add_effect_skill_field(grid, str(effect.get("skill_id", "")), choice_index, effect_index)
			_add_effect_xp_field(grid, int(effect.get("xp_amount", 1)), choice_index, effect_index)
		_:
			_add_effect_item_field(grid, str(effect.get("item_id", "")), choice_index, effect_index)
			_add_effect_quantity_field(grid, int(effect.get("quantity", 1)), choice_index, effect_index)


func _add_effect_quest_field(grid: GridContainer, selected: String, choice_index: int, effect_index: int) -> void:
	grid.add_child(_label("Quest"))
	var quest := OptionButton.new()
	quest.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_fill_quest_reference_options(quest, selected)
	quest.disabled = not _form_editable
	quest.item_selected.connect(_on_effect_quest_selected.bind(choice_index, effect_index, quest))
	grid.add_child(quest)


func _add_effect_transition_field(grid: GridContainer, quest_id: String, effect_type: String, selected: String, choice_index: int, effect_index: int) -> void:
	grid.add_child(_label("Transition"))
	var transition := OptionButton.new()
	transition.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_fill_quest_transition_options(transition, quest_id, effect_type, selected)
	transition.disabled = not _form_editable
	transition.item_selected.connect(_on_effect_transition_selected.bind(choice_index, effect_index, transition))
	grid.add_child(transition)


func _add_effect_item_field(grid: GridContainer, selected: String, choice_index: int, effect_index: int) -> void:
	grid.add_child(_label("Item"))
	var item := OptionButton.new()
	item.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_fill_item_reference_options(item, selected)
	item.disabled = not _form_editable
	item.item_selected.connect(_on_effect_item_selected.bind(choice_index, effect_index, item))
	grid.add_child(item)


func _add_effect_quantity_field(grid: GridContainer, value: int, choice_index: int, effect_index: int) -> void:
	grid.add_child(_label("Quantity"))
	var quantity := SpinBox.new()
	quantity.min_value = 1
	quantity.max_value = 999999
	quantity.step = 1
	quantity.value = max(1, value)
	quantity.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	quantity.editable = _form_editable
	quantity.value_changed.connect(_on_effect_quantity_changed.bind(choice_index, effect_index))
	grid.add_child(quantity)


func _add_effect_skill_field(grid: GridContainer, selected: String, choice_index: int, effect_index: int) -> void:
	grid.add_child(_label("Skill"))
	var skill := OptionButton.new()
	skill.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_fill_skill_reference_options(skill, selected)
	skill.disabled = not _form_editable
	skill.item_selected.connect(_on_effect_skill_selected.bind(choice_index, effect_index, skill))
	grid.add_child(skill)


func _add_effect_xp_field(grid: GridContainer, value: int, choice_index: int, effect_index: int) -> void:
	grid.add_child(_label("XP"))
	var xp := SpinBox.new()
	xp.min_value = 1
	xp.max_value = 999999999
	xp.step = 1
	xp.value = max(1, value)
	xp.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	xp.editable = _form_editable
	xp.value_changed.connect(_on_effect_xp_changed.bind(choice_index, effect_index))
	grid.add_child(xp)


func _add_condition(owner_kind: String, owner_index: int, choice_index: int) -> void:
	var conditions := _condition_list_at(owner_kind, owner_index, choice_index)
	conditions.append(_default_condition(CONDITION_TYPE_QUEST_STATUS))
	_refresh_condition_owner(owner_kind)
	_on_form_changed()


func _remove_condition(owner_kind: String, owner_index: int, choice_index: int, condition_index: int) -> void:
	var conditions := _condition_list_at(owner_kind, owner_index, choice_index)
	if condition_index >= 0 and condition_index < conditions.size():
		conditions.remove_at(condition_index)
	_refresh_condition_owner(owner_kind)
	_on_form_changed()


func _on_condition_type_selected(_selected_index: int, owner_kind: String, owner_index: int, choice_index: int, condition_index: int, control: OptionButton) -> void:
	var condition := _condition_at(owner_kind, owner_index, choice_index, condition_index)
	if condition.is_empty():
		return
	var replacement := _default_condition(_selected_metadata(control))
	condition.clear()
	for key in replacement.keys():
		condition[key] = replacement[key]
	_refresh_condition_owner(owner_kind)
	_on_form_changed()


func _on_condition_quest_id_changed(value: String, owner_kind: String, owner_index: int, choice_index: int, condition_index: int) -> void:
	var condition := _condition_at(owner_kind, owner_index, choice_index, condition_index)
	if condition.is_empty():
		return
	condition["quest_id"] = value.strip_edges()
	_on_form_changed()


func _on_condition_quest_selected(_selected_index: int, owner_kind: String, owner_index: int, choice_index: int, condition_index: int, control: OptionButton) -> void:
	var condition := _condition_at(owner_kind, owner_index, choice_index, condition_index)
	if condition.is_empty():
		return
	var quest_id := _selected_metadata(control)
	condition["quest_id"] = quest_id
	if str(condition.get("condition_type", "")) == CONDITION_TYPE_QUEST_STEP:
		condition["quest_step_id"] = _first_step_id_for_quest(quest_id)
		condition["step_id"] = condition["quest_step_id"]
		_refresh_condition_owner(owner_kind)
	_on_form_changed()


func _on_condition_step_id_changed(value: String, owner_kind: String, owner_index: int, choice_index: int, condition_index: int) -> void:
	var condition := _condition_at(owner_kind, owner_index, choice_index, condition_index)
	if condition.is_empty():
		return
	condition["quest_step_id"] = value.strip_edges()
	condition["step_id"] = condition["quest_step_id"]
	_on_form_changed()


func _on_condition_step_selected(_selected_index: int, owner_kind: String, owner_index: int, choice_index: int, condition_index: int, control: OptionButton) -> void:
	var condition := _condition_at(owner_kind, owner_index, choice_index, condition_index)
	if condition.is_empty():
		return
	condition["quest_step_id"] = _selected_metadata(control)
	condition["step_id"] = condition["quest_step_id"]
	_on_form_changed()


func _on_condition_item_id_changed(value: String, owner_kind: String, owner_index: int, choice_index: int, condition_index: int) -> void:
	var condition := _condition_at(owner_kind, owner_index, choice_index, condition_index)
	if condition.is_empty():
		return
	condition["item_id"] = value.strip_edges()
	_on_form_changed()


func _on_condition_item_selected(_selected_index: int, owner_kind: String, owner_index: int, choice_index: int, condition_index: int, control: OptionButton) -> void:
	var condition := _condition_at(owner_kind, owner_index, choice_index, condition_index)
	if condition.is_empty():
		return
	condition["item_id"] = _selected_metadata(control)
	_on_form_changed()


func _on_condition_status_selected(_selected_index: int, owner_kind: String, owner_index: int, choice_index: int, condition_index: int, control: OptionButton) -> void:
	var condition := _condition_at(owner_kind, owner_index, choice_index, condition_index)
	if condition.is_empty():
		return
	condition["quest_status"] = _selected_metadata(control)
	condition["status"] = condition["quest_status"]
	_on_form_changed()


func _on_condition_quantity_changed(value: float, owner_kind: String, owner_index: int, choice_index: int, condition_index: int) -> void:
	var condition := _condition_at(owner_kind, owner_index, choice_index, condition_index)
	if condition.is_empty():
		return
	condition["item_quantity"] = int(value)
	condition["quantity"] = condition["item_quantity"]
	_on_form_changed()


func _add_effect(choice_index: int) -> void:
	var effects := _effect_list_at(choice_index)
	effects.append(_default_effect(EFFECT_TYPE_GRANT_ITEM, _choice_at(choice_index), effects.size()))
	_refresh_effect_owner()
	_on_form_changed()


func _remove_effect(choice_index: int, effect_index: int) -> void:
	var effects := _effect_list_at(choice_index)
	if effect_index >= 0 and effect_index < effects.size():
		effects.remove_at(effect_index)
	_reorder_effects(effects)
	_refresh_effect_owner()
	_on_form_changed()


func _move_effect(choice_index: int, effect_index: int, direction: int) -> void:
	var effects := _effect_list_at(choice_index)
	var target := effect_index + direction
	if effect_index < 0 or effect_index >= effects.size() or target < 0 or target >= effects.size():
		return
	var moving = effects[effect_index]
	effects.remove_at(effect_index)
	effects.insert(target, moving)
	_reorder_effects(effects)
	_refresh_effect_owner()
	_on_form_changed()


func _on_effect_id_changed(value: String, choice_index: int, effect_index: int) -> void:
	var effect := _effect_at(choice_index, effect_index)
	if effect.is_empty():
		return
	effect["effect_id"] = value.strip_edges()
	_on_form_changed()


func _on_effect_type_selected(_selected_index: int, choice_index: int, effect_index: int, control: OptionButton) -> void:
	var effect := _effect_at(choice_index, effect_index)
	if effect.is_empty():
		return
	var replacement := _default_effect(
		_selected_metadata(control),
		_choice_at(choice_index),
		int(effect.get("effect_order", effect_index)))
	replacement["effect_id"] = str(effect.get("effect_id", replacement.get("effect_id", "")))
	effect.clear()
	for key in replacement.keys():
		effect[key] = replacement[key]
	_refresh_effect_owner()
	_on_form_changed()


func _on_effect_quest_selected(_selected_index: int, choice_index: int, effect_index: int, control: OptionButton) -> void:
	var effect := _effect_at(choice_index, effect_index)
	if effect.is_empty():
		return
	var quest_id := _selected_metadata(control)
	effect["quest_id"] = quest_id
	effect["transition_id"] = _first_transition_id_for_effect(quest_id, str(effect.get("effect_type", EFFECT_TYPE_START_QUEST)))
	_refresh_effect_owner()
	_on_form_changed()


func _on_effect_transition_selected(_selected_index: int, choice_index: int, effect_index: int, control: OptionButton) -> void:
	var effect := _effect_at(choice_index, effect_index)
	if effect.is_empty():
		return
	effect["transition_id"] = _selected_metadata(control)
	_on_form_changed()


func _on_effect_item_selected(_selected_index: int, choice_index: int, effect_index: int, control: OptionButton) -> void:
	var effect := _effect_at(choice_index, effect_index)
	if effect.is_empty():
		return
	effect["item_id"] = _selected_metadata(control)
	_on_form_changed()


func _on_effect_quantity_changed(value: float, choice_index: int, effect_index: int) -> void:
	var effect := _effect_at(choice_index, effect_index)
	if effect.is_empty():
		return
	effect["quantity"] = int(value)
	_on_form_changed()


func _on_effect_skill_selected(_selected_index: int, choice_index: int, effect_index: int, control: OptionButton) -> void:
	var effect := _effect_at(choice_index, effect_index)
	if effect.is_empty():
		return
	effect["skill_id"] = _selected_metadata(control)
	_on_form_changed()


func _on_effect_xp_changed(value: float, choice_index: int, effect_index: int) -> void:
	var effect := _effect_at(choice_index, effect_index)
	if effect.is_empty():
		return
	effect["xp_amount"] = int(value)
	_on_form_changed()


func _condition_list_at(owner_kind: String, owner_index: int, choice_index: int) -> Array:
	if owner_kind == "entry":
		var entries := _current_dialogue.get("entry_points", []) as Array
		if owner_index < 0 or owner_index >= entries.size() or entries[owner_index] is not Dictionary:
			return []
		var entry := entries[owner_index] as Dictionary
		if entry.get("conditions", null) is not Array:
			entry["conditions"] = []
		return entry.get("conditions", []) as Array
	var choice := _choice_at(choice_index)
	if choice.is_empty():
		return []
	if choice.get("conditions", null) is not Array:
		choice["conditions"] = []
	return choice.get("conditions", []) as Array


func _condition_at(owner_kind: String, owner_index: int, choice_index: int, condition_index: int) -> Dictionary:
	var conditions := _condition_list_at(owner_kind, owner_index, choice_index)
	if condition_index < 0 or condition_index >= conditions.size() or conditions[condition_index] is not Dictionary:
		return {}
	return conditions[condition_index] as Dictionary


func _effect_list_at(choice_index: int) -> Array:
	var choice := _choice_at(choice_index)
	if choice.is_empty():
		return []
	if choice.get("effects", null) is not Array:
		choice["effects"] = []
	return choice.get("effects", []) as Array


func _effect_at(choice_index: int, effect_index: int) -> Dictionary:
	var effects := _effect_list_at(choice_index)
	if effect_index < 0 or effect_index >= effects.size() or effects[effect_index] is not Dictionary:
		return {}
	return effects[effect_index] as Dictionary


func _refresh_effect_owner() -> void:
	_load_selected_node()


func _reorder_effects(effects: Array) -> void:
	for index in range(effects.size()):
		if effects[index] is Dictionary:
			(effects[index] as Dictionary)["effect_order"] = index


func _refresh_condition_owner(owner_kind: String) -> void:
	if owner_kind == "entry":
		_rebuild_entry_points()
	else:
		_load_selected_node()


func _choice_at(index: int) -> Dictionary:
	var node := _find_node(_selected_node_id)
	if node.is_empty():
		return {}
	var choices := node.get("choices", []) as Array
	if index < 0 or index >= choices.size() or choices[index] is not Dictionary:
		return {}
	return choices[index] as Dictionary


func _rebuild_entry_points() -> void:
	_clear_children(_entry_points)
	var entries := _current_dialogue.get("entry_points", []) as Array
	if entries.is_empty():
		_workspace_support.add_wrapped_label(_entry_points, "No entry points. The default entry point is required before publication.")
	for index in range(entries.size()):
		if entries[index] is not Dictionary:
			continue
		var entry := entries[index] as Dictionary
		var entry_container := VBoxContainer.new()
		entry_container.add_theme_constant_override("separation", 4)
		_entry_points.add_child(entry_container)
		var row := HBoxContainer.new()
		row.add_theme_constant_override("separation", 6)
		entry_container.add_child(row)
		var entry_id := LineEdit.new()
		entry_id.custom_minimum_size = Vector2(150, 0)
		entry_id.placeholder_text = "entry_id"
		entry_id.text = str(entry.get("entry_id", ""))
		entry_id.editable = _form_editable
		entry_id.text_changed.connect(_on_entry_id_changed.bind(index))
		row.add_child(entry_id)
		var target := OptionButton.new()
		target.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		_fill_node_options(target, str(entry.get("node_id", "")), "Choose entry node")
		target.disabled = not _form_editable
		target.item_selected.connect(_on_entry_target_selected.bind(index, target))
		row.add_child(target)
		var priority := SpinBox.new()
		priority.min_value = -999
		priority.max_value = 999
		priority.step = 1
		priority.value = int(entry.get("priority", 0))
		priority.custom_minimum_size = Vector2(76, 0)
		priority.editable = _form_editable
		priority.value_changed.connect(_on_entry_priority_changed.bind(index))
		row.add_child(priority)
		var remove := Button.new()
		remove.text = "Remove"
		remove.disabled = not _form_editable or entries.size() <= 1
		remove.pressed.connect(_remove_entry_point.bind(index))
		row.add_child(remove)
		_add_conditions_editor(entry_container, entry.get("conditions", []) as Array, "entry", index, -1)


func _on_entry_id_changed(value: String, index: int) -> void:
	var entries := _current_dialogue.get("entry_points", []) as Array
	if index < 0 or index >= entries.size() or entries[index] is not Dictionary:
		return
	(entries[index] as Dictionary)["entry_id"] = value.strip_edges()
	_on_form_changed()


func _on_entry_target_selected(_selected_index: int, index: int, target: OptionButton) -> void:
	var entries := _current_dialogue.get("entry_points", []) as Array
	if index < 0 or index >= entries.size() or entries[index] is not Dictionary:
		return
	(entries[index] as Dictionary)["node_id"] = _selected_metadata(target)
	_on_form_changed()


func _on_entry_priority_changed(value: float, index: int) -> void:
	var entries := _current_dialogue.get("entry_points", []) as Array
	if index < 0 or index >= entries.size() or entries[index] is not Dictionary:
		return
	(entries[index] as Dictionary)["priority"] = int(value)
	_on_form_changed()


func _render_analysis(analysis: Dictionary) -> void:
	_clear_children(_analysis)
	if analysis.is_empty():
		_workspace_support.add_wrapped_label(_analysis, "No graph analysis available.")
		return
	for key in [
		"reachable_node_ids",
		"unreachable_node_ids",
		"dangling_target_node_ids",
		"terminal_node_ids",
		"cycle_node_ids",
		"nodes_without_terminal_path",
		"duplicate_order_fields",
	]:
		var values := analysis.get(key, []) as Array
		_workspace_support.add_wrapped_label(_analysis, "%s: %s" % [key, ", ".join(PackedStringArray(values)) if not values.is_empty() else "none"])


func _render_reference_summary(summary: Dictionary) -> void:
	_clear_children(_reference_summary)
	if summary.is_empty():
		_workspace_support.add_wrapped_label(_reference_summary, "Reference visibility is incomplete until runtime/Tiled handoff work is finished.")
		return
	_workspace_support.add_wrapped_label(_reference_summary, "Known NPC references: %d" % int(summary.get("known_reference_count", 0)))
	var sources := summary.get("reference_sources", []) as Array
	if sources.is_empty():
		_workspace_support.add_wrapped_label(_reference_summary, "No NPC definitions currently reference this dialogue.")
	else:
		for source in sources:
			var source_text := str(source)
			if source_text.begins_with("npc:"):
				var parts := source_text.split(":")
				var button := Button.new()
				button.alignment = HORIZONTAL_ALIGNMENT_LEFT
				button.text = "Open NPC %s (%s)" % [parts[1], parts[2] if parts.size() > 2 else "Unknown"]
				button.pressed.connect(_open_npc_reference.bind(parts[1]))
				_reference_summary.add_child(button)
			else:
				_workspace_support.add_wrapped_label(_reference_summary, source_text)
	if not bool(summary.get("reference_check_complete", false)):
		_workspace_support.add_wrapped_label(_reference_summary, "Reference visibility is incomplete until runtime/Tiled handoff work is finished.")


func _render_playthrough(payload: Dictionary) -> void:
	_clear_children(_playthrough)
	var speaker := _nullable_string(payload.get("speaker", ""))
	var text := _nullable_string(payload.get("text", ""))
	_workspace_support.add_wrapped_label(_playthrough, "%s%s" % [speaker + ": " if not speaker.is_empty() else "", text if not text.is_empty() else "(No text)"])
	var effects := payload.get("would_apply_effects", []) as Array
	if not effects.is_empty():
		_workspace_support.add_wrapped_label(_playthrough, "Would apply: %s" % _effect_summary_list(effects))
	var choices := payload.get("visible_choices", []) as Array
	if not choices.is_empty():
		for choice_variant in choices:
			if choice_variant is not Dictionary:
				continue
			var choice := choice_variant as Dictionary
			var button := Button.new()
			button.alignment = HORIZONTAL_ALIGNMENT_LEFT
			button.text = str(choice.get("text", "Choice"))
			button.pressed.connect(_choose_playthrough.bind(str(choice.get("choice_id", ""))))
			_playthrough.add_child(button)
	elif bool(payload.get("can_continue", false)):
		var continue_button := Button.new()
		continue_button.text = "Continue"
		continue_button.pressed.connect(_continue_playthrough)
		_playthrough.add_child(continue_button)
	elif bool(payload.get("is_end", false)):
		var end_button := Button.new()
		end_button.text = "Close"
		end_button.pressed.connect(_acknowledge_end)
		_playthrough.add_child(end_button)
	var warnings := payload.get("warnings", []) as Array
	if not warnings.is_empty():
		_workspace_support.render_validation(_playthrough, warnings)


func _render_empty_playthrough() -> void:
	_clear_children(_playthrough)
	_workspace_support.add_wrapped_label(_playthrough, "Use Play to preview the current draft through the host playthrough service.")


func _open_npc_reference(npc_definition_id: String) -> void:
	workspace_open_requested.emit("npcs", npc_definition_id)


func _on_search_changed(value: String) -> void:
	_client.load_dialogues(value)


func _on_operation_changed() -> void:
	_clear_preview()


func _on_form_changed(_value: Variant = null) -> void:
	if _is_loading:
		return
	_clear_preview()
	_reset_playthrough_preview()


func _apply_options() -> void:
	var node_types := _options.get("node_types", []) as Array
	if node_types.is_empty():
		node_types = [
			{"id": NODE_TYPE_SPEAKER_TEXT, "display_name": "Speaker Text"},
			{"id": NODE_TYPE_PLAYER_CHOICE, "display_name": "Player Choice"},
			{"id": NODE_TYPE_END, "display_name": "End"},
		]
	_fill_authoring_options(_node_type, node_types)
	_fill_node_options(_next_node, "", "No automatic next node")
	var limits := _options.get("supported_limits", {}) as Dictionary
	_set_spin_limits(_schema_version, 1, max(1, int(limits.get("max_schema_version", 100))))
	var capabilities := _options.get("capabilities", {}) as Dictionary
	_runtime_status.text = "Available" if bool(capabilities.get("supports_runtime_dialogue_catalog", false)) else "Unavailable"
	_condition_status.text = "Typed quest/item predicates" if bool(capabilities.get("supports_conditions", false)) else "Unavailable"
	_effect_status.text = "Supported" if bool(capabilities.get("supports_effects", false)) else "Not supported in D3"


func _set_form_enabled(enabled: bool) -> void:
	var editable := enabled and _schema_available
	_form_editable = editable
	_new_button.disabled = not _schema_available
	for control in [
		_dialogue_id,
		_display_name,
		_schema_version,
		_metadata_description,
		_notes,
		_add_entry_button,
		_node_id,
		_node_type,
		_speaker,
		_text,
		_next_node,
		_dismissible,
		_delete_node_button,
		_editor_notes,
		_operation,
		_preview_button,
	]:
		if control is LineEdit:
			(control as LineEdit).editable = editable
		elif control is SpinBox:
			(control as SpinBox).editable = editable
		elif control is TextEdit:
			(control as TextEdit).editable = editable
		elif control is OptionButton:
			(control as OptionButton).disabled = not editable
		elif control is CheckBox:
			(control as CheckBox).disabled = not editable
		elif control is Button:
			(control as Button).disabled = not editable
	_dialogue_id.editable = editable and _is_new
	_delete_button.disabled = not editable or _current_dialogue.is_empty()
	_add_choice_button.disabled = not editable or _selected_node_id.is_empty()
	if not editable:
		_apply_button.disabled = true
	_set_node_controls_enabled(editable and not _selected_node_id.is_empty())


func _set_node_controls_enabled(enabled: bool) -> void:
	for control in [_node_id, _speaker]:
		control.editable = enabled
	for control in [_text, _editor_notes]:
		control.editable = enabled
	_node_type.disabled = not enabled
	_next_node.disabled = not enabled
	_dismissible.disabled = not enabled
	_delete_node_button.disabled = not enabled
	_add_choice_button.disabled = not enabled


func _clear_preview() -> void:
	_workspace_support.clear_preview(_apply_button, _changes, _validation)
	_workspace_support.clear_container(_analysis)
	_workspace_support.clear_container(_reference_summary)


func _reset_playthrough_preview() -> void:
	_visited_node_ids = []
	_playthrough_node_id = ""
	_render_empty_playthrough()


func _update_operation_default() -> void:
	var state := str(_current_dialogue.get("publication_state", "Unsaved"))
	_select_option(_operation, "disable" if state == "Published" else "save_draft")


func _first_entry_id() -> Variant:
	var entries := _current_dialogue.get("entry_points", []) as Array
	if entries.is_empty() or entries[0] is not Dictionary:
		return null
	return str((entries[0] as Dictionary).get("entry_id", "default"))


func _find_node(node_id: String) -> Dictionary:
	for variant in _current_dialogue.get("nodes", []) as Array:
		if variant is Dictionary and str((variant as Dictionary).get("node_id", "")) == node_id:
			return variant as Dictionary
	return {}


func _has_node_id(node_id: String) -> bool:
	return not _find_node(node_id).is_empty()


func _rename_node(old_id: String, new_id: String) -> void:
	var node := _find_node(old_id)
	if node.is_empty():
		return
	node["node_id"] = new_id
	for entry_variant in _current_dialogue.get("entry_points", []) as Array:
		if entry_variant is Dictionary and str((entry_variant as Dictionary).get("node_id", "")) == old_id:
			(entry_variant as Dictionary)["node_id"] = new_id
	for node_variant in _current_dialogue.get("nodes", []) as Array:
		if node_variant is not Dictionary:
			continue
		var other := node_variant as Dictionary
		if str(other.get("next_node_id", "")) == old_id:
			other["next_node_id"] = new_id
		for choice_variant in other.get("choices", []) as Array:
			if choice_variant is Dictionary and str((choice_variant as Dictionary).get("target_node_id", "")) == old_id:
				(choice_variant as Dictionary)["target_node_id"] = new_id


func _fill_node_options(control: OptionButton, selected: String, empty_label: String) -> void:
	control.clear()
	control.add_item(empty_label)
	control.set_item_metadata(0, "")
	for variant in _current_dialogue.get("nodes", []) as Array:
		if variant is Dictionary:
			var node_id := str((variant as Dictionary).get("node_id", ""))
			control.add_item(node_id)
			control.set_item_metadata(control.item_count - 1, node_id)
	_select_option(control, selected)


func _fill_authoring_options(control: OptionButton, values: Array) -> void:
	var selected := _selected_metadata(control)
	control.clear()
	for variant in values:
		if variant is Dictionary:
			var option := variant as Dictionary
			control.add_item(str(option.get("display_name", option.get("id", "Option"))))
			control.set_item_metadata(control.item_count - 1, str(option.get("id", "")))
	_select_option(control, selected)


func _fill_condition_type_options(control: OptionButton, selected: String) -> void:
	control.clear()
	var values := _options.get("condition_types", []) as Array
	if values.is_empty():
		values = [
			{"id": CONDITION_TYPE_QUEST_STATUS, "display_name": "Quest Status"},
			{"id": CONDITION_TYPE_QUEST_STEP, "display_name": "Quest Step"},
			{"id": CONDITION_TYPE_HAS_ITEM, "display_name": "Has Item"},
		]
	for variant in values:
		if variant is Dictionary:
			var option := variant as Dictionary
			control.add_item(str(option.get("display_name", option.get("id", "Condition"))))
			control.set_item_metadata(control.item_count - 1, str(option.get("id", "")))
	_select_option(control, selected)


func _fill_effect_type_options(control: OptionButton, selected: String) -> void:
	control.clear()
	var values := _options.get("effect_types", []) as Array
	if values.is_empty():
		values = [
			{"id": EFFECT_TYPE_START_QUEST, "display_name": "Start Quest"},
			{"id": EFFECT_TYPE_ADVANCE_QUEST, "display_name": "Advance Quest"},
			{"id": EFFECT_TYPE_COMPLETE_QUEST, "display_name": "Complete Quest"},
			{"id": EFFECT_TYPE_GRANT_ITEM, "display_name": "Grant Item"},
			{"id": EFFECT_TYPE_REMOVE_ITEM, "display_name": "Remove Item"},
			{"id": EFFECT_TYPE_GRANT_EXPERIENCE, "display_name": "Grant Experience"},
		]
	for variant in values:
		if variant is Dictionary:
			var option := variant as Dictionary
			control.add_item(str(option.get("display_name", option.get("id", "Effect"))))
			control.set_item_metadata(control.item_count - 1, str(option.get("id", "")))
	_select_option(control, selected)


func _fill_quest_reference_options(control: OptionButton, selected: String) -> void:
	control.clear()
	var references := _options.get("quest_references", []) as Array
	var selected_found := false
	for variant in references:
		if variant is Dictionary:
			var quest := variant as Dictionary
			var quest_id := str(quest.get("quest_id", ""))
			control.add_item(_display_option_label(str(quest.get("display_name", quest_id)), quest_id))
			control.set_item_metadata(control.item_count - 1, quest_id)
			if quest_id == selected:
				selected_found = true
	if not selected_found and not selected.is_empty():
		control.add_item("%s (unavailable)" % selected)
		control.set_item_metadata(control.item_count - 1, selected)
	if control.item_count == 0:
		control.add_item("No published quests")
		control.set_item_metadata(0, "")
	_select_option(control, selected)


func _fill_quest_step_options(control: OptionButton, quest_id: String, selected: String) -> void:
	control.clear()
	var selected_found := false
	for step in _steps_for_quest(quest_id):
		if step is Dictionary:
			var option := step as Dictionary
			var step_id := str(option.get("id", ""))
			control.add_item(_display_option_label(str(option.get("display_name", step_id)), step_id))
			control.set_item_metadata(control.item_count - 1, step_id)
			if step_id == selected:
				selected_found = true
	if not selected_found and not selected.is_empty():
		control.add_item("%s (unavailable)" % selected)
		control.set_item_metadata(control.item_count - 1, selected)
	if control.item_count == 0:
		control.add_item("No steps")
		control.set_item_metadata(0, "")
	_select_option(control, selected)


func _fill_quest_transition_options(control: OptionButton, quest_id: String, effect_type: String, selected: String) -> void:
	control.clear()
	var selected_found := false
	for transition in _transitions_for_quest(quest_id):
		if transition is Dictionary and _transition_matches_effect(effect_type, transition as Dictionary):
			var option := transition as Dictionary
			var transition_id := str(option.get("transition_id", ""))
			control.add_item(str(option.get("display_name", transition_id)))
			control.set_item_metadata(control.item_count - 1, transition_id)
			if transition_id == selected:
				selected_found = true
	if not selected_found and not selected.is_empty():
		control.add_item("%s (unavailable)" % selected)
		control.set_item_metadata(control.item_count - 1, selected)
	if control.item_count == 0:
		control.add_item("No matching transitions")
		control.set_item_metadata(0, "")
	_select_option(control, selected)


func _fill_item_reference_options(control: OptionButton, selected: String) -> void:
	control.clear()
	var references := _options.get("item_references", []) as Array
	var selected_found := false
	for variant in references:
		if variant is Dictionary:
			var item := variant as Dictionary
			var item_id := str(item.get("id", ""))
			control.add_item(_display_option_label(str(item.get("display_name", item_id)), item_id))
			control.set_item_metadata(control.item_count - 1, item_id)
			if item_id == selected:
				selected_found = true
	if not selected_found and not selected.is_empty():
		control.add_item("%s (unavailable)" % selected)
		control.set_item_metadata(control.item_count - 1, selected)
	if control.item_count == 0:
		control.add_item("No runtime items")
		control.set_item_metadata(0, "")
	_select_option(control, selected)


func _fill_skill_reference_options(control: OptionButton, selected: String) -> void:
	control.clear()
	var references := _options.get("skill_references", []) as Array
	var selected_found := false
	for variant in references:
		if variant is Dictionary:
			var skill := variant as Dictionary
			var skill_id := str(skill.get("id", ""))
			control.add_item(_display_option_label(str(skill.get("display_name", skill_id)), skill_id))
			control.set_item_metadata(control.item_count - 1, skill_id)
			if skill_id == selected:
				selected_found = true
	if not selected_found and not selected.is_empty():
		control.add_item("%s (unavailable)" % selected)
		control.set_item_metadata(control.item_count - 1, selected)
	if control.item_count == 0:
		control.add_item("No skills")
		control.set_item_metadata(0, "")
	_select_option(control, selected)


func _steps_for_quest(quest_id: String) -> Array:
	for variant in _options.get("quest_references", []) as Array:
		if variant is Dictionary and str((variant as Dictionary).get("quest_id", "")) == quest_id:
			return (variant as Dictionary).get("steps", []) as Array
	return []


func _transitions_for_quest(quest_id: String) -> Array:
	for variant in _options.get("quest_references", []) as Array:
		if variant is Dictionary and str((variant as Dictionary).get("quest_id", "")) == quest_id:
			return (variant as Dictionary).get("transitions", []) as Array
	return []


func _first_quest_id() -> String:
	var references := _options.get("quest_references", []) as Array
	for variant in references:
		if variant is Dictionary:
			return str((variant as Dictionary).get("quest_id", ""))
	return ""


func _first_step_id_for_quest(quest_id: String) -> String:
	for step in _steps_for_quest(quest_id):
		if step is Dictionary:
			return str((step as Dictionary).get("id", ""))
	return ""


func _first_transition_id_for_effect(quest_id: String, effect_type: String) -> String:
	for transition in _transitions_for_quest(quest_id):
		if transition is Dictionary and _transition_matches_effect(effect_type, transition as Dictionary):
			return str((transition as Dictionary).get("transition_id", ""))
	return ""


func _first_item_id() -> String:
	var references := _options.get("item_references", []) as Array
	for variant in references:
		if variant is Dictionary:
			return str((variant as Dictionary).get("id", ""))
	return ""


func _first_skill_id() -> String:
	var references := _options.get("skill_references", []) as Array
	for variant in references:
		if variant is Dictionary:
			return str((variant as Dictionary).get("id", ""))
	return ""


func _display_option_label(display_name: String, stable_id: String) -> String:
	if stable_id.is_empty() or display_name == stable_id:
		return display_name
	return "%s (%s)" % [display_name, stable_id]


func _default_condition(condition_type: String) -> Dictionary:
	match condition_type:
		CONDITION_TYPE_QUEST_STEP:
			var quest_id := _first_quest_id()
			return {
				"condition_type": CONDITION_TYPE_QUEST_STEP,
				"quest_id": quest_id,
				"quest_step_id": _first_step_id_for_quest(quest_id),
			}
		CONDITION_TYPE_HAS_ITEM:
			return {
				"condition_type": CONDITION_TYPE_HAS_ITEM,
				"item_id": _first_item_id(),
				"item_quantity": 1,
			}
		_:
			return {
				"condition_type": CONDITION_TYPE_QUEST_STATUS,
				"quest_id": _first_quest_id(),
				"quest_status": "active",
			}


func _default_effect(effect_type: String, choice: Dictionary, order: int) -> Dictionary:
	var effect_id := _unique_effect_id(choice, "effect")
	match effect_type:
		EFFECT_TYPE_START_QUEST, EFFECT_TYPE_ADVANCE_QUEST, EFFECT_TYPE_COMPLETE_QUEST:
			var quest_id := _first_quest_id()
			return {
				"effect_id": effect_id,
				"effect_order": order,
				"effect_type": effect_type,
				"quest_id": quest_id,
				"transition_id": _first_transition_id_for_effect(quest_id, effect_type),
			}
		EFFECT_TYPE_GRANT_EXPERIENCE:
			return {
				"effect_id": effect_id,
				"effect_order": order,
				"effect_type": EFFECT_TYPE_GRANT_EXPERIENCE,
				"skill_id": _first_skill_id(),
				"xp_amount": 1,
			}
		EFFECT_TYPE_REMOVE_ITEM:
			return {
				"effect_id": effect_id,
				"effect_order": order,
				"effect_type": EFFECT_TYPE_REMOVE_ITEM,
				"item_id": _first_item_id(),
				"quantity": 1,
			}
		_:
			return {
				"effect_id": effect_id,
				"effect_order": order,
				"effect_type": EFFECT_TYPE_GRANT_ITEM,
				"item_id": _first_item_id(),
				"quantity": 1,
			}


func _unique_node_id(node_type: String) -> String:
	var prefix := "node"
	match node_type:
		NODE_TYPE_SPEAKER_TEXT:
			prefix = "speaker"
		NODE_TYPE_PLAYER_CHOICE:
			prefix = "choice"
		NODE_TYPE_END:
			prefix = "end"
	var index := 1
	var candidate := "%s_%d" % [prefix, index]
	while _has_node_id(candidate):
		index += 1
		candidate = "%s_%d" % [prefix, index]
	return candidate


func _first_node_id() -> String:
	var nodes := _current_dialogue.get("nodes", []) as Array
	for variant in nodes:
		if variant is Dictionary:
			return str((variant as Dictionary).get("node_id", ""))
	return ""


func _unique_entry_id(prefix: String) -> String:
	var normalized := prefix.strip_edges()
	if normalized.is_empty():
		normalized = "entry"
	var existing := {}
	for entry_variant in _current_dialogue.get("entry_points", []) as Array:
		if entry_variant is Dictionary:
			existing[str((entry_variant as Dictionary).get("entry_id", ""))] = true
	var candidate := normalized
	var index := 2
	while existing.has(candidate):
		candidate = "%s_%d" % [normalized, index]
		index += 1
	return candidate


func _normalize_entry_orders() -> void:
	var entries := _current_dialogue.get("entry_points", []) as Array
	for index in range(entries.size()):
		if entries[index] is Dictionary:
			(entries[index] as Dictionary)["entry_order"] = index


func _unique_choice_id(node: Dictionary, prefix: String) -> String:
	var existing := {}
	for choice_variant in node.get("choices", []) as Array:
		if choice_variant is Dictionary:
			existing[str((choice_variant as Dictionary).get("choice_id", ""))] = true
	var index := 1
	var candidate := "%s_%d" % [prefix, index]
	while existing.has(candidate):
		index += 1
		candidate = "%s_%d" % [prefix, index]
	return candidate


func _unique_effect_id(choice: Dictionary, prefix: String) -> String:
	var existing := {}
	for effect_variant in choice.get("effects", []) as Array:
		if effect_variant is Dictionary:
			existing[str((effect_variant as Dictionary).get("effect_id", ""))] = true
	var index := 1
	var candidate := "%s_%d" % [prefix, index]
	while existing.has(candidate):
		index += 1
		candidate = "%s_%d" % [prefix, index]
	return candidate


func _transition_matches_effect(effect_type: String, transition: Dictionary) -> bool:
	var source_status := str(transition.get("source_status", ""))
	var target_status := str(transition.get("target_status", ""))
	match effect_type:
		EFFECT_TYPE_START_QUEST:
			return source_status == "not_started" and target_status == "active"
		EFFECT_TYPE_ADVANCE_QUEST:
			return source_status == "active" and target_status == "active"
		EFFECT_TYPE_COMPLETE_QUEST:
			return source_status == "active" and target_status == "completed"
		_:
			return false


func _effect_summary_list(effects: Array) -> String:
	var parts: Array[String] = []
	for effect_variant in effects:
		if effect_variant is Dictionary:
			var effect := effect_variant as Dictionary
			parts.append("%s:%s" % [str(effect.get("effect_order", 0)), str(effect.get("effect_type", ""))])
	return ", ".join(parts)


func _graph_summary(node: Dictionary) -> String:
	var text := _nullable_string(node.get("text", ""))
	if text.is_empty():
		text = "(No text)"
	var choice_count := (node.get("choices", []) as Array).size()
	if choice_count > 0:
		text += "\n%s choices" % choice_count
	return text


func _node_type_label(node_type: String) -> String:
	match node_type:
		NODE_TYPE_SPEAKER_TEXT:
			return "Speaker"
		NODE_TYPE_PLAYER_CHOICE:
			return "Choice"
		NODE_TYPE_END:
			return "End"
		_:
			return node_type


func _add_graph_button(parent: HBoxContainer, label: String, node_type: String) -> void:
	var button := Button.new()
	button.text = label
	button.pressed.connect(_add_node.bind(node_type))
	parent.add_child(button)


func _add_operation(label: String, operation_id: String) -> void:
	_operation.add_item(label)
	_operation.set_item_metadata(_operation.item_count - 1, operation_id)


func _line_field(grid: GridContainer, label_text: String, placeholder: String) -> LineEdit:
	grid.add_child(_label(label_text))
	var field := LineEdit.new()
	field.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	field.placeholder_text = placeholder
	field.text_changed.connect(_on_form_changed)
	grid.add_child(field)
	return field


func _text_field(parent: VBoxContainer, placeholder: String, height: float) -> TextEdit:
	var field := TextEdit.new()
	field.custom_minimum_size = Vector2(0, height)
	field.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	field.placeholder_text = placeholder
	field.text_changed.connect(_on_form_changed)
	parent.add_child(field)
	return field


func _spin_field(grid: GridContainer, label_text: String, minimum: float, maximum: float, value: float, step: float) -> SpinBox:
	grid.add_child(_label(label_text))
	var field := SpinBox.new()
	field.min_value = minimum
	field.max_value = maximum
	field.value = value
	field.step = step
	field.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	field.value_changed.connect(_on_form_changed.unbind(1))
	grid.add_child(field)
	return field


func _option_field(grid: GridContainer, label_text: String) -> OptionButton:
	grid.add_child(_label(label_text))
	var option := OptionButton.new()
	option.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	option.item_selected.connect(_on_form_changed.unbind(1))
	grid.add_child(option)
	return option


func _value_label(grid: GridContainer, label_text: String, value: String) -> Label:
	grid.add_child(_label(label_text))
	var label := _wrapped_label(value)
	grid.add_child(label)
	return label


func _set_spin_limits(field: SpinBox, minimum: int, maximum: int) -> void:
	field.min_value = minimum
	field.max_value = maximum


func _selected_metadata(control: OptionButton) -> String:
	var selected := control.get_selected_id()
	if selected < 0:
		return ""
	var metadata = control.get_item_metadata(selected)
	return "" if metadata == null else str(metadata)


func _select_option(control: OptionButton, metadata: String) -> void:
	for index in range(control.item_count):
		if str(control.get_item_metadata(index)) == metadata:
			control.select(index)
			return
	if control.item_count > 0:
		control.select(0)


func _optional_payload(value: String) -> Variant:
	var normalized := value.strip_edges()
	return null if normalized.is_empty() else normalized


func _optional_variant_payload(value: Variant) -> Variant:
	if value == null:
		return null
	var normalized := str(value).strip_edges()
	return null if normalized.is_empty() else normalized


func _optional_int_payload(value: Variant) -> Variant:
	if value == null:
		return null
	return int(value)


func _condition_string_value(condition: Dictionary, preferred_key: String, fallback_key: String, default_value: String) -> String:
	return str(_condition_value(condition, preferred_key, fallback_key, default_value))


func _condition_value(condition: Dictionary, preferred_key: String, fallback_key: String, default_value: Variant) -> Variant:
	if condition.has(preferred_key):
		return condition.get(preferred_key)
	return condition.get(fallback_key, default_value)


func _nullable_string(value: Variant) -> String:
	return "" if value == null else str(value)


func _has_error_code(errors: Array, code: String) -> bool:
	for variant in errors:
		if variant is Dictionary and str((variant as Dictionary).get("code", "")) == code:
			return true
	return false


func _grid(parent: VBoxContainer) -> GridContainer:
	var grid := GridContainer.new()
	grid.columns = 2
	grid.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	grid.add_theme_constant_override("h_separation", 12)
	grid.add_theme_constant_override("v_separation", 6)
	parent.add_child(grid)
	return grid


func _panel(minimum_size: Vector2) -> PanelContainer:
	var panel := PanelContainer.new()
	panel.custom_minimum_size = minimum_size
	panel.size_flags_vertical = Control.SIZE_EXPAND_FILL
	panel.add_theme_stylebox_override("panel", _panel_style())
	return panel


func _panel_style() -> StyleBoxFlat:
	var style := StyleBoxFlat.new()
	style.bg_color = Color(0.086, 0.098, 0.122, 1)
	style.border_color = Color(0.19, 0.22, 0.28, 1)
	style.set_border_width_all(1)
	style.set_corner_radius_all(8)
	style.content_margin_left = 16
	style.content_margin_top = 14
	style.content_margin_right = 16
	style.content_margin_bottom = 14
	return style


func _vbox(parent: Node) -> VBoxContainer:
	var container := VBoxContainer.new()
	container.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	container.size_flags_vertical = Control.SIZE_EXPAND_FILL
	container.add_theme_constant_override("separation", 10)
	parent.add_child(container)
	return container


func _add_heading(parent: Node, text: String, size: int) -> void:
	var label := Label.new()
	label.text = text
	label.add_theme_font_size_override("font_size", size)
	parent.add_child(label)


func _label(text: String) -> Label:
	var label := Label.new()
	label.text = text
	label.custom_minimum_size = Vector2(FORM_LABEL_WIDTH, 0)
	label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	return label


func _wrapped_label(text: String) -> Label:
	var label := Label.new()
	label.text = text
	label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	return label


func _clear_children(container: Node) -> void:
	for child in container.get_children():
		container.remove_child(child)
		child.queue_free()
