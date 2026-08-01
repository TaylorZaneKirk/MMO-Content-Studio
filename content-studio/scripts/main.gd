extends Control

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


func _ready() -> void:
	authoring_host_client.connection_state_changed.connect(_on_connection_state_changed)
	authoring_host_client.handshake_received.connect(_on_handshake_received)
	authoring_host_client.health_received.connect(_on_health_received)
	authoring_host_client.catalog_received.connect(_on_catalog_received)
	authoring_host_client.request_failed.connect(_on_request_failed)
	retry_button.pressed.connect(authoring_host_client.retry)
	authoring_host_client.connect_and_load()


func _on_connection_state_changed(state: String, message: String) -> void:
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
	host_value.text = "%s  •  %s" % [
		str(payload.get("service", "Unknown host")),
		str(payload.get("host_version", "unknown version")),
	]
	api_value.text = "v%s" % str(payload.get("api_version", "?"))


func _on_health_received(payload: Dictionary) -> void:
	var database := payload.get("database", {}) as Dictionary
	var database_status := str(database.get("status", "Unknown"))
	database_value.text = "%s — %s" % [
		database_status,
		str(database.get("message", "No database status message.")),
	]
	schema_value.text = str(database.get("schema_contract", "Not verified"))

	_clear_children(asset_roots_list)
	var asset_roots := payload.get("asset_roots", [])
	if asset_roots is Array:
		for asset_root_variant in asset_roots:
			if asset_root_variant is not Dictionary:
				continue
			var asset_root := asset_root_variant as Dictionary
			var row := Label.new()
			row.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
			row.text = "• %s: %s\n  %s" % [
				str(asset_root.get("id", "asset_root")),
				str(asset_root.get("status", "Unknown")),
				str(asset_root.get("path", "Not configured")),
			]
			asset_roots_list.add_child(row)


func _on_catalog_received(payload: Dictionary) -> void:
	_clear_children(catalog_list)
	var sections := payload.get("sections", [])
	if sections is not Array:
		return

	for section_variant in sections:
		if section_variant is not Dictionary:
			continue
		var section := section_variant as Dictionary
		var entries := section.get("entries", [])
		var count := entries.size() if entries is Array else 0
		var row := HBoxContainer.new()
		var name_label := Label.new()
		name_label.text = str(section.get("display_name", "Unknown"))
		name_label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		var status_label := Label.new()
		status_label.text = "%s entries  •  %s" % [
			count,
			"available" if section.get("implemented", false) else "planned",
		]
		row.add_child(name_label)
		row.add_child(status_label)
		catalog_list.add_child(row)


func _on_request_failed(_operation: String, _message: String) -> void:
	pass


func _clear_children(container: Node) -> void:
	for child in container.get_children():
		child.queue_free()
