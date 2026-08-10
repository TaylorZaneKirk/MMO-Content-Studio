extends HBoxContainer
class_name DialogueEditor

const CATALOG_PANE_TOGGLE := preload("res://scripts/catalog_pane_toggle.gd")

signal workspace_open_requested(workspace_id: String, resource_id: String)

const WORKSPACE_SUPPORT_SCRIPT := preload("res://scripts/authoring_workspace_support.gd")
const NODE_TYPE_SPEAKER_TEXT := "speaker_text"
const NODE_TYPE_PLAYER_CHOICE := "player_choice"
const NODE_TYPE_END := "end"
const FORM_LABEL_WIDTH := 132.0

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
var _graph: GraphEdit
var _node_title: Label
var _node_id: LineEdit
var _node_type: OptionButton
var _speaker: LineEdit
var _text: TextEdit
var _next_node: OptionButton
var _dismissible: CheckBox
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
	_add_operation_section(inspector)


func _add_definition_section(parent: VBoxContainer) -> void:
	_add_heading(parent, "Definition", 20)
	var grid := _grid(parent)
	_dialogue_id = _line_field(grid, "Dialogue ID", "test_npc_greeting")
	_display_name = _line_field(grid, "Display name", "Test NPC Greeting")
	_publication = _value_label(grid, "Publication", "Unknown")
	_updated = _value_label(grid, "Updated", "Unknown")
	_schema_version = _spin_field(grid, "Schema version", 1, 100, 1, 1)
	_add_heading(parent, "Entry Points", 16)
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


func _add_operation_section(parent: VBoxContainer) -> void:
	_add_heading(parent, "Operation", 20)
	_operation = OptionButton.new()
	_add_operation("Save as Draft", "save_draft")
	_add_operation("Publish", "publish")
	_add_operation("Disable", "disable")
	_add_operation("Delete", "delete")
	_operation.item_selected.connect(_on_operation_changed.unbind(1))
	parent.add_child(_operation)
	_preview_button = Button.new()
	_preview_button.text = "Validate and Preview Changes"
	_preview_button.pressed.connect(_preview)
	parent.add_child(_preview_button)
	_delete_button = Button.new()
	_delete_button.text = "Delete"
	_delete_button.disabled = true
	_delete_button.pressed.connect(_preview_delete)
	parent.add_child(_delete_button)
	_apply_button = Button.new()
	_apply_button.text = "Apply Previewed Operation"
	_apply_button.disabled = true
	_apply_button.pressed.connect(_apply)
	parent.add_child(_apply_button)
	_status = _wrapped_label("Load or create a dialogue definition.")
	parent.add_child(_status)
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
		"Apply %s" % _workspace_support.operation_name(operation)
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
	_status.text = "%s completed. Reloading dialogue definition..." % _workspace_support.operation_name(operation)
	_client.load_dialogues(_search.text)


func _on_dialogue_delete_completed(payload: Dictionary) -> void:
	var deleted_id := str(payload.get("deleted_id", _dialogue_id.text))
	_reload_dialogue_id = ""
	_start_new_dialogue()
	_status.text = "Deleted %s." % deleted_id
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
	return {
		"display_name": _display_name.text,
		"schema_version": int(_schema_version.value),
		"entry_points": _current_dialogue.get("entry_points", []) as Array,
		"nodes": _current_dialogue.get("nodes", []) as Array,
		"metadata_description": _optional_payload(_metadata_description.text),
		"notes": _optional_payload(_notes.text),
		"expected_updated_at_utc": _current_dialogue.get("updated_at_utc", null),
		"preview_signature": null,
	}


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
		}],
	})
	_current_dialogue["nodes"] = nodes
	_selected_node_id = node_id
	_rebuild_entry_points()
	_rebuild_graph()
	_load_selected_node()
	_on_form_changed()


func _delete_selected_node() -> void:
	if not _form_editable or _selected_node_id.is_empty():
		return
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
		graph_node.resizable = true
		graph_node.custom_minimum_size = Vector2(190, 120)
		var summary := Label.new()
		summary.text = _graph_summary(node)
		summary.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
		graph_node.add_child(summary)
		graph_node.set_slot(0, true, 0, Color(0.42, 0.7, 0.9), str(node.get("node_type", "")) != NODE_TYPE_END, 0, Color(0.85, 0.67, 0.35))
		if graph_node.has_signal("position_offset_changed"):
			graph_node.connect("position_offset_changed", Callable(self, "_on_graph_node_moved").bind(graph_node.name))
		if graph_node.has_signal("selected"):
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


func _on_connection_request(from_node: StringName, _from_port: int, to_node: StringName, _to_port: int) -> void:
	var node := _find_node(str(from_node))
	if node.is_empty():
		return
	if str(node.get("node_type", "")) == NODE_TYPE_PLAYER_CHOICE:
		var choices := node.get("choices", []) as Array
		if choices.is_empty():
			choices.append({
				"choice_id": "choice_1",
				"text": "Continue",
				"target_node_id": str(to_node),
				"choice_order": 0,
				"conditions": [],
			})
		else:
			(choices[0] as Dictionary)["target_node_id"] = str(to_node)
		node["choices"] = choices
	else:
		node["next_node_id"] = str(to_node)
	_rebuild_graph()
	_load_selected_node()
	_on_form_changed()


func _on_disconnection_request(from_node: StringName, _from_port: int, to_node: StringName, _to_port: int) -> void:
	var node := _find_node(str(from_node))
	if node.is_empty():
		return
	if str(node.get("next_node_id", "")) == str(to_node):
		node["next_node_id"] = null
	for choice_variant in node.get("choices", []) as Array:
		if choice_variant is Dictionary and str((choice_variant as Dictionary).get("target_node_id", "")) == str(to_node):
			(choice_variant as Dictionary)["target_node_id"] = ""
	_rebuild_graph()
	_load_selected_node()
	_on_form_changed()


func _on_delete_nodes_request(nodes: Array) -> void:
	if not nodes.is_empty():
		_selected_node_id = str(nodes[0])
		_delete_selected_node()


func _on_graph_node_selected(node_name: StringName) -> void:
	_sync_selected_node_from_form()
	_selected_node_id = str(node_name)
	_load_selected_node()


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
	node["next_node_id"] = _optional_payload(_selected_metadata(_next_node))
	node["dismissible"] = _dismissible.button_pressed
	node["editor_notes"] = _optional_payload(_editor_notes.text)


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
	if str(node.get("node_type", "")) != NODE_TYPE_PLAYER_CHOICE:
		node["choices"] = []
	_rebuild_graph()
	_load_selected_node()
	_on_form_changed()


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
		_workspace_support.add_wrapped_label(_choices, "No choices on this node.")
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
		var condition_note := _wrapped_label("Conditions: none. Quest and condition authoring are deferred.")
		row.add_child(condition_note)


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
		var row := HBoxContainer.new()
		row.add_theme_constant_override("separation", 6)
		_entry_points.add_child(row)
		var label := _wrapped_label("%s ->" % str(entry.get("entry_id", "entry")))
		row.add_child(label)
		var target := OptionButton.new()
		target.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		_fill_node_options(target, str(entry.get("node_id", "")), "Choose entry node")
		target.item_selected.connect(_on_entry_target_selected.bind(index, target))
		row.add_child(target)
		var condition_label := _wrapped_label("Conditions unavailable")
		row.add_child(condition_label)


func _on_entry_target_selected(_selected_index: int, index: int, target: OptionButton) -> void:
	var entries := _current_dialogue.get("entry_points", []) as Array
	if index < 0 or index >= entries.size() or entries[index] is not Dictionary:
		return
	(entries[index] as Dictionary)["node_id"] = _selected_metadata(target)
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


func _apply_options() -> void:
	_fill_authoring_options(_node_type, _options.get("node_types", []) as Array)
	_fill_node_options(_next_node, "", "No automatic next node")
	var limits := _options.get("supported_limits", {}) as Dictionary
	_set_spin_limits(_schema_version, 1, max(1, int(limits.get("max_schema_version", 100))))
	var capabilities := _options.get("capabilities", {}) as Dictionary
	_runtime_status.text = "Available" if bool(capabilities.get("supports_runtime_dialogue_catalog", false)) else "Unavailable"
	_condition_status.text = "Supported" if bool(capabilities.get("supports_conditions", false)) else "Not supported in D3"
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
		_node_id,
		_node_type,
		_speaker,
		_text,
		_next_node,
		_dismissible,
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
	_add_choice_button.disabled = not enabled


func _clear_preview() -> void:
	_workspace_support.clear_preview(_apply_button, _changes, _validation)
	_workspace_support.clear_container(_analysis)
	_workspace_support.clear_container(_reference_summary)


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
