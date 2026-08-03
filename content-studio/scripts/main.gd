extends Control

const WORKSPACE_SUPPORT_SCRIPT := preload("res://scripts/authoring_workspace_support.gd")

@onready var connection_badge: Label = %ConnectionBadge
@onready var connection_message: Label = %ConnectionMessage
@onready var retry_button: Button = %RetryButton
@onready var host_value: Label = %HostValue
@onready var api_value: Label = %ApiValue
@onready var database_value: Label = %DatabaseValue
@onready var schema_value: Label = %SchemaValue
@onready var asset_roots_list: VBoxContainer = %AssetRootsList
@onready var catalog_list: VBoxContainer = %CatalogList
@onready var item_search: LineEdit = %ItemSearch
@onready var item_list: VBoxContainer = %ItemList
@onready var new_item_button: Button = %NewItemButton
@onready var item_id_edit: LineEdit = %ItemIdEdit
@onready var display_name_edit: LineEdit = %DisplayNameEdit
@onready var icon_option: OptionButton = %IconOption
@onready var import_asset_button: Button = %ImportAssetButton
@onready var asset_file_dialog: FileDialog = %AssetFileDialog
@onready var icon_preview: TextureRect = %IconPreview
@onready var publication_value: Label = %PublicationValue
@onready var authoring_kind_value: Label = %AuthoringKindValue
@onready var updated_value: Label = %UpdatedValue
@onready var target_operation: OptionButton = %TargetOperation
@onready var preview_button: Button = %PreviewButton
@onready var apply_button: Button = %ApplyButton
@onready var changes_list: VBoxContainer = %ChangesList
@onready var validation_list: VBoxContainer = %ValidationList
@onready var operation_status: Label = %OperationStatus
@onready var authoring_host_client: AuthoringHostClient = %AuthoringHostClient

var _items: Array = []
var _asset_entries: Array = []
var _asset_by_resource_path: Dictionary = {}
var _current_item: Dictionary = {}
var _workspace_support


func _ready() -> void:
	_workspace_support = WORKSPACE_SUPPORT_SCRIPT.new()
	authoring_host_client.connection_state_changed.connect(_on_connection_state_changed)
	authoring_host_client.handshake_received.connect(_on_handshake_received)
	authoring_host_client.health_received.connect(_on_health_received)
	authoring_host_client.catalog_received.connect(_on_catalog_received)
	authoring_host_client.item_assets_received.connect(_on_item_assets_received)
	authoring_host_client.item_asset_imported.connect(_on_item_asset_imported)
	authoring_host_client.items_received.connect(_on_items_received)
	authoring_host_client.item_received.connect(_on_item_received)
	authoring_host_client.item_preview_received.connect(_on_item_preview_received)
	authoring_host_client.item_mutation_completed.connect(_on_item_mutation_completed)
	authoring_host_client.request_failed.connect(_on_request_failed)
	retry_button.pressed.connect(authoring_host_client.retry)
	new_item_button.pressed.connect(_start_new_item)
	item_search.text_changed.connect(_on_item_search_changed)
	item_id_edit.text_changed.connect(_on_form_changed)
	display_name_edit.text_changed.connect(_on_form_changed)
	icon_option.item_selected.connect(_on_icon_selected)
	import_asset_button.pressed.connect(_open_asset_import)
	asset_file_dialog.file_selected.connect(_on_asset_file_selected)
	target_operation.item_selected.connect(_on_target_operation_changed)
	preview_button.pressed.connect(_preview_changes)
	apply_button.pressed.connect(_apply_previewed_operation)
	_configure_target_operations()
	_set_form_enabled(false)
	authoring_host_client.connect_and_load()


func _configure_target_operations() -> void:
	target_operation.clear()
	target_operation.add_item("Save as Draft")
	target_operation.set_item_metadata(0, "save_draft")
	target_operation.add_item("Publish")
	target_operation.set_item_metadata(1, "publish")
	target_operation.add_item("Disable")
	target_operation.set_item_metadata(2, "disable")
	target_operation.add_item("Delete")
	target_operation.set_item_metadata(3, "delete")


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
	var asset_roots: Variant = payload.get("asset_roots", [])
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
	var sections: Variant = payload.get("sections", [])
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
		status_label.text = "%s entries  •  %s" % [
			count,
			"available" if section.get("implemented", false) else "planned",
		]
		row.add_child(name_label)
		row.add_child(status_label)
		catalog_list.add_child(row)


func _on_item_assets_received(payload: Dictionary) -> void:
	_asset_entries = payload.get("assets", []) as Array
	_asset_by_resource_path.clear()
	icon_option.clear()
	icon_option.add_item("Select an item icon…")
	icon_option.set_item_metadata(0, "")
	for asset_variant in _asset_entries:
		if asset_variant is not Dictionary:
			continue
		var asset := asset_variant as Dictionary
		var resource_path := str(asset.get("resource_path", ""))
		_asset_by_resource_path[resource_path] = asset
		icon_option.add_item(str(asset.get("display_name", resource_path)))
		icon_option.set_item_metadata(icon_option.item_count - 1, resource_path)




func _on_item_asset_imported(payload: Dictionary) -> void:
	var asset := payload.get("asset", {}) as Dictionary
	if asset.is_empty():
		return
	var resource_path := str(asset.get("resource_path", ""))
	if not _asset_by_resource_path.has(resource_path):
		_asset_entries.append(asset)
		_asset_by_resource_path[resource_path] = asset
		icon_option.add_item(str(asset.get("display_name", resource_path)))
		icon_option.set_item_metadata(icon_option.item_count - 1, resource_path)
	_select_icon_path(resource_path)
	_update_icon_preview(str(asset.get("file_path", "")))
	operation_status.text = str(payload.get("message", "Item asset imported."))
	_clear_preview()


func _open_asset_import() -> void:
	asset_file_dialog.popup_centered_ratio(0.75)


func _on_asset_file_selected(path: String) -> void:
	var suggested_name := path.get_file()
	authoring_host_client.import_item_asset(path, suggested_name)
	operation_status.text = "Importing PNG into the canonical item asset directory…"


func _on_items_received(payload: Dictionary) -> void:
	_items = payload.get("items", []) as Array
	_rebuild_item_list()


func _on_item_received(payload: Dictionary) -> void:
	_current_item = payload
	item_id_edit.text = str(payload.get("item_id", ""))
	item_id_edit.editable = false
	display_name_edit.text = str(payload.get("display_name", ""))
	_select_icon_path(str(payload.get("icon_texture_path", "")))
	publication_value.text = str(payload.get("publication_state", "Unknown"))
	authoring_kind_value.text = str(payload.get("authoring_kind", "Unknown"))
	updated_value.text = str(payload.get("updated_at_utc", "Unknown"))
	var editable := bool(payload.get("editable_in_basic_items", false))
	_set_form_enabled(editable)
	item_id_edit.editable = false
	operation_status.text = "Loaded %s." % item_id_edit.text
	_clear_preview()
	_update_target_defaults()
	_update_icon_preview(str(payload.get("asset_preview_file_path", "")))


func _on_item_preview_received(payload: Dictionary) -> void:
	var operation := str(payload.get("target_operation", "save_draft"))
	var valid_for_draft := bool(payload.get("valid_for_draft", false))
	var valid_for_publication := bool(payload.get("valid_for_publication", false))
	var applicable := valid_for_publication if operation == "publish" else valid_for_draft
	if operation == "disable":
		applicable = valid_for_draft
	_workspace_support.accept_preview(
		operation,
		_form_signature(operation),
		applicable,
		apply_button,
		"Apply: %s" % _workspace_support.operation_name(operation)
	)
	_workspace_support.render_changes(changes_list, payload.get("changes", []) as Array)
	_workspace_support.render_validation(validation_list, payload.get("messages", []) as Array)
	_update_icon_preview(str(payload.get("asset_preview_file_path", "")))
	operation_status.text = "Preview ready. Review the exact changes before applying."


func _on_item_mutation_completed(payload: Dictionary) -> void:
	var operation := str(payload.get("operation", "mutation"))
	if operation == "delete":
		var deleted_id := str(payload.get("deleted_id", item_id_edit.text))
		_start_new_item()
		operation_status.text = "Deleted %s." % deleted_id
		authoring_host_client.load_items(item_search.text)
		return
	var item := payload.get("item", {}) as Dictionary
	operation_status.text = "%s completed successfully." % _workspace_support.operation_name(operation)
	_current_item = item
	_on_item_received(item)
	authoring_host_client.load_items(item_search.text)


func _on_request_failed(operation: String, message: String, errors: Array) -> void:
	operation_status.text = "%s failed: %s" % [operation, message]
	_workspace_support.render_validation(validation_list, errors)
	apply_button.disabled = true


func _on_item_search_changed(_value: String) -> void:
	_rebuild_item_list()


func _rebuild_item_list() -> void:
	_clear_children(item_list)
	var search := item_search.text.strip_edges().to_lower()
	for item_variant in _items:
		if item_variant is not Dictionary:
			continue
		var item := item_variant as Dictionary
		var haystack := "%s %s" % [item.get("item_id", ""), item.get("display_name", "")]
		if not search.is_empty() and not haystack.to_lower().contains(search):
			continue
		var button := Button.new()
		button.text = "%s\n%s  •  %s" % [
			str(item.get("display_name", "Unnamed item")),
			str(item.get("publication_state", "Unknown")),
			str(item.get("authoring_kind", "Unknown")),
		]
		button.alignment = HORIZONTAL_ALIGNMENT_LEFT
		button.tooltip_text = str(item.get("item_id", ""))
		button.pressed.connect(_load_item.bind(str(item.get("item_id", ""))))
		item_list.add_child(button)


func _load_item(item_id: String) -> void:
	if not item_id.is_empty():
		authoring_host_client.load_item(item_id)


func _start_new_item() -> void:
	_current_item = {}
	item_id_edit.text = ""
	item_id_edit.editable = true
	display_name_edit.text = ""
	icon_option.select(0)
	publication_value.text = "Unsaved"
	authoring_kind_value.text = "Basic"
	updated_value.text = "Not saved"
	_set_form_enabled(true)
	item_id_edit.grab_focus()
	operation_status.text = "Creating a new basic item."
	_update_icon_preview("")
	_clear_preview()
	_update_target_defaults()


func _on_form_changed(_value: String) -> void:
	_clear_preview()


func _on_icon_selected(_index: int) -> void:
	_clear_preview()
	var resource_path := _selected_icon_path()
	var asset := _asset_by_resource_path.get(resource_path, {}) as Dictionary
	_update_icon_preview(str(asset.get("file_path", "")))


func _on_target_operation_changed(_index: int) -> void:
	_clear_preview()


func _preview_changes() -> void:
	var item_id := item_id_edit.text.strip_edges()
	if item_id.is_empty():
		operation_status.text = "Enter a stable item ID before previewing."
		return
	var operation := _selected_operation()
	authoring_host_client.preview_item(item_id, {
		"display_name": display_name_edit.text,
		"icon_texture_path": _selected_icon_path(),
		"expected_updated_at_utc": _current_item.get("updated_at_utc", null),
		"target_operation": operation,
	})
	operation_status.text = "Calculating validation and change summary…"


func _apply_previewed_operation() -> void:
	var operation := _selected_operation()
	if not _workspace_support.can_apply(operation, _form_signature(operation)):
		operation_status.text = "The form changed. Preview the operation again before applying it."
		apply_button.disabled = true
		return
	var item_id := item_id_edit.text.strip_edges()
	var expected: Variant = _current_item.get("updated_at_utc", null)
	match operation:
		"publish":
			authoring_host_client.publish_item(item_id, expected)
		"disable":
			authoring_host_client.disable_item(item_id, expected)
		"delete":
			authoring_host_client.delete_item(item_id, expected)
		_:
			authoring_host_client.save_item_draft(item_id, {
				"display_name": display_name_edit.text,
				"icon_texture_path": _selected_icon_path(),
				"expected_updated_at_utc": expected,
			})
	apply_button.disabled = true
	operation_status.text = "Applying transactional authoring operation…"


func _update_target_defaults() -> void:
	var state := str(_current_item.get("publication_state", "Unsaved"))
	if state == "Draft":
		target_operation.select(1)
	elif state == "Published":
		target_operation.select(2)
	else:
		target_operation.select(0)


func _selected_operation() -> String:
	return str(target_operation.get_item_metadata(target_operation.selected))


func _selected_icon_path() -> String:
	if icon_option.selected < 0:
		return ""
	return str(icon_option.get_item_metadata(icon_option.selected))


func _select_icon_path(resource_path: String) -> void:
	for index in range(icon_option.item_count):
		if str(icon_option.get_item_metadata(index)) == resource_path:
			icon_option.select(index)
			return
	icon_option.select(0)


func _set_form_enabled(enabled: bool) -> void:
	display_name_edit.editable = enabled
	icon_option.disabled = not enabled
	import_asset_button.disabled = not enabled
	target_operation.disabled = not enabled
	preview_button.disabled = not enabled
	if not enabled:
		apply_button.disabled = true


func _clear_preview() -> void:
	_workspace_support.clear_preview(apply_button, changes_list, validation_list)


func _form_signature(operation: String) -> String:
	return JSON.stringify([
		item_id_edit.text.strip_edges(),
		display_name_edit.text.strip_edges(),
		_selected_icon_path(),
		_current_item.get("updated_at_utc", null),
		operation,
	])



func _update_icon_preview(file_path: String) -> void:
	icon_preview.texture = null
	if file_path.is_empty() or not FileAccess.file_exists(file_path):
		return
	var image := Image.load_from_file(file_path)
	if image == null or image.is_empty():
		return
	icon_preview.texture = ImageTexture.create_from_image(image)



func _clear_children(container: Node) -> void:
	for child in container.get_children():
		child.queue_free()
