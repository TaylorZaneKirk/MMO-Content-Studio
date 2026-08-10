extends Control

const CONTENT_STUDIO_LOGGER := preload("res://scripts/content_studio_logger.gd")

@onready var connection_badge: Label = %ConnectionBadge
@onready var connection_message: Label = %ConnectionMessage
@onready var retry_button: Button = %RetryButton
@onready var host_value: Label = %HostValue
@onready var api_value: Label = %ApiValue
@onready var database_value: Label = %DatabaseValue
@onready var schema_value: Label = %SchemaValue
@onready var asset_roots_list: VBoxContainer = %AssetRootsList
@onready var catalog_list: VBoxContainer = %CatalogList
@onready var authoring_host_client: AuthoringHostClient = %AuthoringHostClient
@onready var tabs: TabContainer = %Tabs
@onready var npc_editor = %NPCs
@onready var dialogue_editor = %Dialogue

var _observed_controls: Dictionary = {}


func _ready() -> void:
	get_tree().node_added.connect(_on_tree_node_added)
	_observe_descendant_controls(self)
	authoring_host_client.connection_state_changed.connect(_on_connection_state_changed)
	authoring_host_client.handshake_received.connect(_on_handshake_received)
	authoring_host_client.health_received.connect(_on_health_received)
	authoring_host_client.catalog_received.connect(_on_catalog_received)
	npc_editor.workspace_open_requested.connect(_on_workspace_open_requested)
	dialogue_editor.workspace_open_requested.connect(_on_workspace_open_requested)
	retry_button.pressed.connect(authoring_host_client.retry)
	CONTENT_STUDIO_LOGGER.info("Content Studio startup requested")
	authoring_host_client.connect_and_load()


func _on_connection_state_changed(state: String, message: String) -> void:
	CONTENT_STUDIO_LOGGER.info("Connection state changed", {
		"message": message,
		"state": state,
	})
	connection_badge.text = state.to_upper()
	connection_message.text = message
	retry_button.visible = state == "disconnected"

	match state:
		"connected":
			connection_badge.modulate = Color(0.54, 0.92, 0.66)
		"connecting":
			connection_badge.modulate = Color(0.95, 0.81, 0.43)
		_:
			connection_badge.modulate = Color(1.0, 0.47, 0.47)


func _on_handshake_received(payload: Dictionary) -> void:
	CONTENT_STUDIO_LOGGER.info("Authoring host handshake loaded", {
		"host_version": payload.get("host_version", "unknown"),
		"service": payload.get("service", "unknown"),
	})
	host_value.text = "%s  •  %s" % [
		str(payload.get("service", "Unknown host")),
		str(payload.get("host_version", "unknown version")),
	]
	api_value.text = "v%s" % str(payload.get("api_version", "?"))


func _on_health_received(payload: Dictionary) -> void:
	var database := payload.get("database", {}) as Dictionary
	var database_status := str(database.get("status", "Unknown"))
	CONTENT_STUDIO_LOGGER.info("Authoring host health loaded", {
		"database_status": database_status,
		"schema_contract": payload.get("schema_contract", "unknown"),
	})
	database_value.text = "%s - %s" % [
		database_status,
		str(database.get("message", "No database status message.")),
	]
	schema_value.text = str(database.get("schema_contract", "Not verified"))

	_clear_children(asset_roots_list)
	var asset_roots: Variant = payload.get("asset_roots", [])
	if asset_roots is not Array:
		return

	for asset_root_variant in asset_roots:
		if asset_root_variant is not Dictionary:
			continue
		var asset_root := asset_root_variant as Dictionary
		var row := Label.new()
		row.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
		row.text = "* %s: %s\n  %s" % [
			str(asset_root.get("id", "asset_root")),
			str(asset_root.get("status", "Unknown")),
			str(asset_root.get("path", "Not configured")),
		]
		asset_roots_list.add_child(row)


func _on_catalog_received(payload: Dictionary) -> void:
	var sections: Variant = payload.get("sections", [])
	CONTENT_STUDIO_LOGGER.info("Content catalog loaded", {
		"section_count": sections.size() if sections is Array else 0,
	})
	_clear_children(catalog_list)
	if sections is not Array:
		return

	for section_variant in sections:
		if section_variant is not Dictionary:
			continue
		var section := section_variant as Dictionary
		var entries: Variant = section.get("entries", [])
		var count: int = entries.size() if entries is Array else 0
		var row := HBoxContainer.new()
		var name_label := Label.new()
		name_label.text = str(section.get("display_name", "Unknown"))
		name_label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		var status_label := Label.new()
		status_label.text = "%s entries  -  %s" % [
			count,
			"available" if section.get("implemented", false) else "planned",
		]
		row.add_child(name_label)
		row.add_child(status_label)
		catalog_list.add_child(row)


func _clear_children(container: Node) -> void:
	for child in container.get_children():
		container.remove_child(child)
		child.queue_free()


func _on_workspace_open_requested(workspace_id: String, resource_id: String) -> void:
	CONTENT_STUDIO_LOGGER.debug("Workspace open requested", {
		"resource_id": resource_id,
		"workspace": workspace_id,
	})
	match workspace_id:
		"dialogue":
			_open_tab(dialogue_editor)
			dialogue_editor.open_resource(resource_id)
		"npcs":
			_open_tab(npc_editor)
			npc_editor.open_resource(resource_id)


func _open_tab(control: Control) -> void:
	var index := tabs.get_tab_idx_from_control(control)
	if index >= 0:
		tabs.current_tab = index


func _on_tree_node_added(node: Node) -> void:
	if node is Control:
		call_deferred("_observe_control", node)


func _observe_descendant_controls(node: Node) -> void:
	if node is Control:
		_observe_control(node)
	for child in node.get_children():
		_observe_descendant_controls(child)


func _observe_control(control: Control) -> void:
	if not is_instance_valid(control):
		return
	var instance_id := control.get_instance_id()
	if _observed_controls.has(instance_id):
		return
	_observed_controls[instance_id] = true

	if control is CheckBox:
		(control as CheckBox).toggled.connect(_on_checkbox_toggled.bind(control))
	elif control is OptionButton:
		(control as OptionButton).item_selected.connect(_on_option_selected.bind(control))
	elif control is SpinBox:
		(control as SpinBox).value_changed.connect(_on_spin_value_changed.bind(control))
	elif control is LineEdit:
		(control as LineEdit).text_changed.connect(_on_text_value_changed.bind(control))
	elif control is TextEdit:
		(control as TextEdit).text_changed.connect(_on_text_edit_changed.bind(control))
	elif control is TabContainer:
		(control as TabContainer).tab_changed.connect(_on_dynamic_tab_changed.bind(control))
	elif control is Button:
		(control as Button).pressed.connect(_on_button_pressed.bind(control))


func _on_checkbox_toggled(pressed: bool, control: CheckBox) -> void:
	_log_control_change(control, "toggled", pressed)


func _on_option_selected(index: int, control: OptionButton) -> void:
	_log_control_change(control, "selected", control.get_item_text(index))


func _on_spin_value_changed(value: float, control: SpinBox) -> void:
	_log_control_change(control, "set", value)


func _on_text_value_changed(value: String, control: LineEdit) -> void:
	_log_control_change(control, "set", value)


func _on_text_edit_changed(control: TextEdit) -> void:
	_log_control_change(control, "set", control.text)


func _on_button_pressed(control: Button) -> void:
	CONTENT_STUDIO_LOGGER.debug("User action requested", {
		"control": _control_id(control),
		"label": control.text,
	})


func _on_dynamic_tab_changed(index: int, control: TabContainer) -> void:
	CONTENT_STUDIO_LOGGER.debug("Workspace tab changed", {
		"tab": control.get_tab_title(index),
		"tab_container": _control_id(control),
	})


func _log_control_change(control: Control, action: String, value: Variant) -> void:
	CONTENT_STUDIO_LOGGER.debug("Authoring form value changed", {
		"action": action,
		"control": _control_id(control),
		"value": value,
	})


func _control_id(control: Control) -> String:
	var button := control as BaseButton
	if button != null and not button.text.strip_edges().is_empty():
		return button.text.strip_edges()
	var label := _preceding_label(control)
	if not label.is_empty():
		return label
	if not control.name.begins_with("@"):
		return control.name
	var path := str(control.get_path())
	return path if not path.is_empty() else control.name


func _preceding_label(control: Control) -> String:
	var parent := control.get_parent()
	if parent == null:
		return ""
	var index := control.get_index() - 1
	while index >= 0:
		var sibling := parent.get_child(index)
		if sibling is Label and not (sibling as Label).text.strip_edges().is_empty():
			return (sibling as Label).text.strip_edges()
		if sibling is Control:
			return ""
		index -= 1
	return ""
