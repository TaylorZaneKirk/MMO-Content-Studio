extends Node
class_name AuthoringHostClient

signal connection_state_changed(state: String, message: String)
signal handshake_received(payload: Dictionary)
signal health_received(payload: Dictionary)
signal catalog_received(payload: Dictionary)
signal item_assets_received(payload: Dictionary)
signal item_asset_imported(payload: Dictionary)
signal items_received(payload: Dictionary)
signal item_received(payload: Dictionary)
signal item_preview_received(payload: Dictionary)
signal item_mutation_completed(payload: Dictionary)
signal consumable_options_received(payload: Dictionary)
signal consumables_received(payload: Dictionary)
signal consumable_received(payload: Dictionary)
signal consumable_preview_received(payload: Dictionary)
signal consumable_mutation_completed(payload: Dictionary)
signal equipment_options_received(payload: Dictionary)
signal equipment_received(payload: Dictionary)
signal equipment_item_received(payload: Dictionary)
signal equipment_preview_received(payload: Dictionary)
signal equipment_mutation_completed(payload: Dictionary)
signal hand_equipment_options_received(payload: Dictionary)
signal hand_equipment_received(payload: Dictionary)
signal hand_equipment_item_received(payload: Dictionary)
signal hand_equipment_preview_received(payload: Dictionary)
signal hand_equipment_mutation_completed(payload: Dictionary)
signal request_failed(operation: String, message: String, errors: Array)

const TRANSPORT_SCRIPT := preload("res://scripts/http_json_client.gd")
const DEFAULT_BASE_URL := "http://127.0.0.1:5187"

const OP_HANDSHAKE := "handshake"
const OP_HEALTH := "health"
const OP_CATALOG := "catalog"
const OP_ITEM_ASSETS := "item_assets"
const OP_ITEM_ASSET_IMPORT := "item_asset_import"
const OP_ITEMS := "items"
const OP_ITEM := "item"
const OP_ITEM_PREVIEW := "item_preview"
const OP_ITEM_SAVE_DRAFT := "item_save_draft"
const OP_ITEM_PUBLISH := "item_publish"
const OP_ITEM_DISABLE := "item_disable"
const OP_CONSUMABLE_OPTIONS := "consumable_options"
const OP_CONSUMABLES := "consumables"
const OP_CONSUMABLE := "consumable"
const OP_CONSUMABLE_PREVIEW := "consumable_preview"
const OP_CONSUMABLE_SAVE_DRAFT := "consumable_save_draft"
const OP_CONSUMABLE_PUBLISH := "consumable_publish"
const OP_CONSUMABLE_DISABLE := "consumable_disable"
const OP_EQUIPMENT_OPTIONS := "equipment_options"
const OP_EQUIPMENT := "equipment"
const OP_EQUIPMENT_ITEM := "equipment_item"
const OP_EQUIPMENT_PREVIEW := "equipment_preview"
const OP_EQUIPMENT_SAVE_DRAFT := "equipment_save_draft"
const OP_EQUIPMENT_PUBLISH := "equipment_publish"
const OP_EQUIPMENT_DISABLE := "equipment_disable"
const OP_HAND_EQUIPMENT_OPTIONS := "hand_equipment_options"
const OP_HAND_EQUIPMENT := "hand_equipment"
const OP_HAND_EQUIPMENT_ITEM := "hand_equipment_item"
const OP_HAND_EQUIPMENT_PREVIEW := "hand_equipment_preview"
const OP_HAND_EQUIPMENT_SAVE_DRAFT := "hand_equipment_save_draft"
const OP_HAND_EQUIPMENT_PUBLISH := "hand_equipment_publish"
const OP_HAND_EQUIPMENT_DISABLE := "hand_equipment_disable"

const CONNECTION_OPERATIONS := [
	OP_HANDSHAKE,
	OP_HEALTH,
	OP_CATALOG,
	OP_ITEM_ASSETS,
	OP_ITEMS,
	OP_CONSUMABLE_OPTIONS,
	OP_CONSUMABLES,
	OP_EQUIPMENT_OPTIONS,
	OP_EQUIPMENT,
	OP_HAND_EQUIPMENT_OPTIONS,
	OP_HAND_EQUIPMENT,
]

@export var base_url := DEFAULT_BASE_URL

var _transport: AuthoringHttpTransport


func _ready() -> void:
	_transport = TRANSPORT_SCRIPT.new() as AuthoringHttpTransport
	_transport.base_url = base_url
	_transport.request_succeeded.connect(_on_request_succeeded)
	_transport.request_failed.connect(_on_request_failed)
	add_child(_transport)


func connect_and_load() -> void:
	if _transport.is_busy():
		return

	connection_state_changed.emit("connecting", "Connecting to the local authoring host…")
	_request(OP_HANDSHAKE, "/api/v1/system/handshake")


func retry() -> void:
	_transport.reset()
	connect_and_load()


func import_item_asset(source_file_path: String, target_file_name: String = "") -> void:
	_request(OP_ITEM_ASSET_IMPORT, "/api/v1/assets/items/import", HTTPClient.METHOD_POST, {
		"source_file_path": source_file_path,
		"target_file_name": target_file_name,
	})


func load_items(search: String = "") -> void:
	var suffix := ""
	if not search.strip_edges().is_empty():
		suffix = "?search=%s" % search.strip_edges().uri_encode()
	_request(OP_ITEMS, "/api/v1/items%s" % suffix)


func load_item(item_id: String) -> void:
	_request(OP_ITEM, "/api/v1/items/%s" % item_id.uri_encode())


func preview_item(item_id: String, payload: Dictionary) -> void:
	_request(OP_ITEM_PREVIEW, "/api/v1/items/%s/preview" % item_id.uri_encode(), HTTPClient.METHOD_POST, payload)


func save_item_draft(item_id: String, payload: Dictionary) -> void:
	_request(OP_ITEM_SAVE_DRAFT, "/api/v1/items/%s/draft" % item_id.uri_encode(), HTTPClient.METHOD_PUT, payload)


func publish_item(item_id: String, expected_updated_at_utc: Variant) -> void:
	_request(OP_ITEM_PUBLISH, "/api/v1/items/%s/publish" % item_id.uri_encode(), HTTPClient.METHOD_POST, {"expected_updated_at_utc": expected_updated_at_utc})


func disable_item(item_id: String, expected_updated_at_utc: Variant) -> void:
	_request(OP_ITEM_DISABLE, "/api/v1/items/%s/disable" % item_id.uri_encode(), HTTPClient.METHOD_POST, {"expected_updated_at_utc": expected_updated_at_utc})


func load_consumables(search: String = "") -> void:
	var suffix := ""
	if not search.strip_edges().is_empty():
		suffix = "?search=%s" % search.strip_edges().uri_encode()
	_request(OP_CONSUMABLES, "/api/v1/consumables%s" % suffix)


func load_consumable(item_id: String) -> void:
	_request(OP_CONSUMABLE, "/api/v1/consumables/%s" % item_id.uri_encode())


func preview_consumable(item_id: String, payload: Dictionary) -> void:
	_request(OP_CONSUMABLE_PREVIEW, "/api/v1/consumables/%s/preview" % item_id.uri_encode(), HTTPClient.METHOD_POST, payload)


func save_consumable_draft(item_id: String, payload: Dictionary) -> void:
	_request(OP_CONSUMABLE_SAVE_DRAFT, "/api/v1/consumables/%s/draft" % item_id.uri_encode(), HTTPClient.METHOD_PUT, payload)


func publish_consumable(item_id: String, expected_updated_at_utc: Variant) -> void:
	_request(OP_CONSUMABLE_PUBLISH, "/api/v1/consumables/%s/publish" % item_id.uri_encode(), HTTPClient.METHOD_POST, {"expected_updated_at_utc": expected_updated_at_utc})


func disable_consumable(item_id: String, expected_updated_at_utc: Variant) -> void:
	_request(OP_CONSUMABLE_DISABLE, "/api/v1/consumables/%s/disable" % item_id.uri_encode(), HTTPClient.METHOD_POST, {"expected_updated_at_utc": expected_updated_at_utc})


func load_equipment(search: String = "") -> void:
	var suffix := ""
	if not search.strip_edges().is_empty():
		suffix = "?search=%s" % search.strip_edges().uri_encode()
	_request(OP_EQUIPMENT, "/api/v1/equipment%s" % suffix)


func load_equipment_item(item_id: String) -> void:
	_request(OP_EQUIPMENT_ITEM, "/api/v1/equipment/%s" % item_id.uri_encode())


func preview_equipment(item_id: String, payload: Dictionary) -> void:
	_request(OP_EQUIPMENT_PREVIEW, "/api/v1/equipment/%s/preview" % item_id.uri_encode(), HTTPClient.METHOD_POST, payload)


func save_equipment_draft(item_id: String, payload: Dictionary) -> void:
	_request(OP_EQUIPMENT_SAVE_DRAFT, "/api/v1/equipment/%s/draft" % item_id.uri_encode(), HTTPClient.METHOD_PUT, payload)


func publish_equipment(item_id: String, expected_updated_at_utc: Variant) -> void:
	_request(OP_EQUIPMENT_PUBLISH, "/api/v1/equipment/%s/publish" % item_id.uri_encode(), HTTPClient.METHOD_POST, {"expected_updated_at_utc": expected_updated_at_utc})


func disable_equipment(item_id: String, expected_updated_at_utc: Variant) -> void:
	_request(OP_EQUIPMENT_DISABLE, "/api/v1/equipment/%s/disable" % item_id.uri_encode(), HTTPClient.METHOD_POST, {"expected_updated_at_utc": expected_updated_at_utc})


func load_hand_equipment_options() -> void:
	_request(OP_HAND_EQUIPMENT_OPTIONS, "/api/v1/hand-equipment/options")


func load_hand_equipment(search: String = "") -> void:
	var suffix := ""
	if not search.strip_edges().is_empty():
		suffix = "?search=%s" % search.strip_edges().uri_encode()
	_request(OP_HAND_EQUIPMENT, "/api/v1/hand-equipment%s" % suffix)


func load_hand_equipment_item(item_id: String) -> void:
	_request(OP_HAND_EQUIPMENT_ITEM, "/api/v1/hand-equipment/%s" % item_id.uri_encode())


func preview_hand_equipment(item_id: String, payload: Dictionary) -> void:
	_request(OP_HAND_EQUIPMENT_PREVIEW, "/api/v1/hand-equipment/%s/preview" % item_id.uri_encode(), HTTPClient.METHOD_POST, payload)


func save_hand_equipment_draft(item_id: String, payload: Dictionary) -> void:
	_request(OP_HAND_EQUIPMENT_SAVE_DRAFT, "/api/v1/hand-equipment/%s/draft" % item_id.uri_encode(), HTTPClient.METHOD_PUT, payload)


func publish_hand_equipment(item_id: String, expected_updated_at_utc: Variant, preview_signature: String) -> void:
	_request(OP_HAND_EQUIPMENT_PUBLISH, "/api/v1/hand-equipment/%s/publish" % item_id.uri_encode(), HTTPClient.METHOD_POST, {
		"expected_updated_at_utc": expected_updated_at_utc,
		"preview_signature": preview_signature,
	})


func disable_hand_equipment(item_id: String, expected_updated_at_utc: Variant, preview_signature: String) -> void:
	_request(OP_HAND_EQUIPMENT_DISABLE, "/api/v1/hand-equipment/%s/disable" % item_id.uri_encode(), HTTPClient.METHOD_POST, {
		"expected_updated_at_utc": expected_updated_at_utc,
		"preview_signature": preview_signature,
	})


func _request(
	operation: String,
	path: String,
	method: int = HTTPClient.METHOD_GET,
	payload: Dictionary = {}
) -> void:
	_transport.request(operation, path, method, payload)


func _on_request_succeeded(operation: String, data: Dictionary) -> void:
	match operation:
		OP_HANDSHAKE:
			if str(data.get("api_version", "")) != AuthoringHttpTransport.API_VERSION:
				_on_request_failed(
					operation,
					"The host does not support Content Studio API v%s." % AuthoringHttpTransport.API_VERSION,
					[]
				)
				return
			handshake_received.emit(data)
			_request(OP_HEALTH, "/api/v1/system/health")
		OP_HEALTH:
			health_received.emit(data)
			_request(OP_CATALOG, "/api/v1/catalog")
		OP_CATALOG:
			catalog_received.emit(data)
			_request(OP_ITEM_ASSETS, "/api/v1/assets/items")
		OP_ITEM_ASSETS:
			item_assets_received.emit(data)
			_request(OP_ITEMS, "/api/v1/items")
		OP_ITEM_ASSET_IMPORT:
			item_asset_imported.emit(data)
		OP_ITEMS:
			items_received.emit(data)
			_request(OP_CONSUMABLE_OPTIONS, "/api/v1/consumables/options")
		OP_CONSUMABLE_OPTIONS:
			consumable_options_received.emit(data)
			_request(OP_CONSUMABLES, "/api/v1/consumables")
		OP_CONSUMABLES:
			consumables_received.emit(data)
			_request(OP_EQUIPMENT_OPTIONS, "/api/v1/equipment/options")
		OP_EQUIPMENT_OPTIONS:
			equipment_options_received.emit(data)
			_request(OP_EQUIPMENT, "/api/v1/equipment")
		OP_EQUIPMENT:
			equipment_received.emit(data)
			_request(OP_HAND_EQUIPMENT_OPTIONS, "/api/v1/hand-equipment/options")
		OP_HAND_EQUIPMENT_OPTIONS:
			hand_equipment_options_received.emit(data)
			_request(OP_HAND_EQUIPMENT, "/api/v1/hand-equipment")
		OP_HAND_EQUIPMENT:
			hand_equipment_received.emit(data)
			connection_state_changed.emit("connected", "Connected to the local authoring host.")
		OP_ITEM:
			item_received.emit(data)
		OP_ITEM_PREVIEW:
			item_preview_received.emit(data)
		OP_ITEM_SAVE_DRAFT, OP_ITEM_PUBLISH, OP_ITEM_DISABLE:
			item_mutation_completed.emit(data)
		OP_CONSUMABLE:
			consumable_received.emit(data)
		OP_CONSUMABLE_PREVIEW:
			consumable_preview_received.emit(data)
		OP_CONSUMABLE_SAVE_DRAFT, OP_CONSUMABLE_PUBLISH, OP_CONSUMABLE_DISABLE:
			consumable_mutation_completed.emit(data)
		OP_EQUIPMENT_ITEM:
			equipment_item_received.emit(data)
		OP_EQUIPMENT_PREVIEW:
			equipment_preview_received.emit(data)
		OP_EQUIPMENT_SAVE_DRAFT, OP_EQUIPMENT_PUBLISH, OP_EQUIPMENT_DISABLE:
			equipment_mutation_completed.emit(data)
		OP_HAND_EQUIPMENT_ITEM:
			hand_equipment_item_received.emit(data)
		OP_HAND_EQUIPMENT_PREVIEW:
			hand_equipment_preview_received.emit(data)
		OP_HAND_EQUIPMENT_SAVE_DRAFT, OP_HAND_EQUIPMENT_PUBLISH, OP_HAND_EQUIPMENT_DISABLE:
			hand_equipment_mutation_completed.emit(data)
		_:
			_on_request_failed(operation, "Unexpected request completion.", [])


func _on_request_failed(operation: String, message: String, errors: Array) -> void:
	if operation in CONNECTION_OPERATIONS:
		connection_state_changed.emit("disconnected", message)
	request_failed.emit(operation, message, errors)
