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
signal request_failed(operation: String, message: String, errors: Array)

const API_VERSION := "1"
const DEFAULT_BASE_URL := "http://127.0.0.1:5187"
const REQUEST_TIMEOUT_SECONDS := 8.0

enum RequestKind {
	NONE,
	HANDSHAKE,
	HEALTH,
	CATALOG,
	ITEM_ASSETS,
	ITEM_ASSET_IMPORT,
	ITEMS,
	ITEM,
	ITEM_PREVIEW,
	ITEM_SAVE_DRAFT,
	ITEM_PUBLISH,
	ITEM_DISABLE,
	CONSUMABLE_OPTIONS,
	CONSUMABLES,
	CONSUMABLE,
	CONSUMABLE_PREVIEW,
	CONSUMABLE_SAVE_DRAFT,
	CONSUMABLE_PUBLISH,
	CONSUMABLE_DISABLE,
	EQUIPMENT_OPTIONS,
	EQUIPMENT,
	EQUIPMENT_ITEM,
	EQUIPMENT_PREVIEW,
	EQUIPMENT_SAVE_DRAFT,
	EQUIPMENT_PUBLISH,
	EQUIPMENT_DISABLE,
}

@export var base_url := DEFAULT_BASE_URL

var _http_request: HTTPRequest
var _request_kind := RequestKind.NONE


func _ready() -> void:
	_http_request = HTTPRequest.new()
	_http_request.timeout = REQUEST_TIMEOUT_SECONDS
	_http_request.request_completed.connect(_on_request_completed)
	add_child(_http_request)


func connect_and_load() -> void:
	if _request_kind != RequestKind.NONE:
		return

	connection_state_changed.emit("connecting", "Connecting to the local authoring host…")
	_request(RequestKind.HANDSHAKE, "/api/v1/system/handshake")


func retry() -> void:
	_request_kind = RequestKind.NONE
	connect_and_load()


func import_item_asset(source_file_path: String, target_file_name: String = "") -> void:
	_request(
		RequestKind.ITEM_ASSET_IMPORT,
		"/api/v1/assets/items/import",
		HTTPClient.METHOD_POST,
		{
			"source_file_path": source_file_path,
			"target_file_name": target_file_name,
		}
	)


func load_items(search: String = "") -> void:
	var suffix := ""
	if not search.strip_edges().is_empty():
		suffix = "?search=%s" % search.strip_edges().uri_encode()
	_request(RequestKind.ITEMS, "/api/v1/items%s" % suffix)


func load_item(item_id: String) -> void:
	_request(RequestKind.ITEM, "/api/v1/items/%s" % item_id.uri_encode())


func preview_item(item_id: String, payload: Dictionary) -> void:
	_request(
		RequestKind.ITEM_PREVIEW,
		"/api/v1/items/%s/preview" % item_id.uri_encode(),
		HTTPClient.METHOD_POST,
		payload
	)


func save_item_draft(item_id: String, payload: Dictionary) -> void:
	_request(
		RequestKind.ITEM_SAVE_DRAFT,
		"/api/v1/items/%s/draft" % item_id.uri_encode(),
		HTTPClient.METHOD_PUT,
		payload
	)


func publish_item(item_id: String, expected_updated_at_utc: Variant) -> void:
	_request(
		RequestKind.ITEM_PUBLISH,
		"/api/v1/items/%s/publish" % item_id.uri_encode(),
		HTTPClient.METHOD_POST,
		{"expected_updated_at_utc": expected_updated_at_utc}
	)


func disable_item(item_id: String, expected_updated_at_utc: Variant) -> void:
	_request(
		RequestKind.ITEM_DISABLE,
		"/api/v1/items/%s/disable" % item_id.uri_encode(),
		HTTPClient.METHOD_POST,
		{"expected_updated_at_utc": expected_updated_at_utc}
	)




func load_consumables(search: String = "") -> void:
	var suffix := ""
	if not search.strip_edges().is_empty():
		suffix = "?search=%s" % search.strip_edges().uri_encode()
	_request(RequestKind.CONSUMABLES, "/api/v1/consumables%s" % suffix)


func load_consumable(item_id: String) -> void:
	_request(RequestKind.CONSUMABLE, "/api/v1/consumables/%s" % item_id.uri_encode())


func preview_consumable(item_id: String, payload: Dictionary) -> void:
	_request(
		RequestKind.CONSUMABLE_PREVIEW,
		"/api/v1/consumables/%s/preview" % item_id.uri_encode(),
		HTTPClient.METHOD_POST,
		payload
	)


func save_consumable_draft(item_id: String, payload: Dictionary) -> void:
	_request(
		RequestKind.CONSUMABLE_SAVE_DRAFT,
		"/api/v1/consumables/%s/draft" % item_id.uri_encode(),
		HTTPClient.METHOD_PUT,
		payload
	)


func publish_consumable(item_id: String, expected_updated_at_utc: Variant) -> void:
	_request(
		RequestKind.CONSUMABLE_PUBLISH,
		"/api/v1/consumables/%s/publish" % item_id.uri_encode(),
		HTTPClient.METHOD_POST,
		{"expected_updated_at_utc": expected_updated_at_utc}
	)


func disable_consumable(item_id: String, expected_updated_at_utc: Variant) -> void:
	_request(
		RequestKind.CONSUMABLE_DISABLE,
		"/api/v1/consumables/%s/disable" % item_id.uri_encode(),
		HTTPClient.METHOD_POST,
		{"expected_updated_at_utc": expected_updated_at_utc}
	)



func load_equipment(search: String = "") -> void:
	var suffix := ""
	if not search.strip_edges().is_empty():
		suffix = "?search=%s" % search.strip_edges().uri_encode()
	_request(RequestKind.EQUIPMENT, "/api/v1/equipment%s" % suffix)


func load_equipment_item(item_id: String) -> void:
	_request(RequestKind.EQUIPMENT_ITEM, "/api/v1/equipment/%s" % item_id.uri_encode())


func preview_equipment(item_id: String, payload: Dictionary) -> void:
	_request(
		RequestKind.EQUIPMENT_PREVIEW,
		"/api/v1/equipment/%s/preview" % item_id.uri_encode(),
		HTTPClient.METHOD_POST,
		payload
	)


func save_equipment_draft(item_id: String, payload: Dictionary) -> void:
	_request(
		RequestKind.EQUIPMENT_SAVE_DRAFT,
		"/api/v1/equipment/%s/draft" % item_id.uri_encode(),
		HTTPClient.METHOD_PUT,
		payload
	)


func publish_equipment(item_id: String, expected_updated_at_utc: Variant) -> void:
	_request(
		RequestKind.EQUIPMENT_PUBLISH,
		"/api/v1/equipment/%s/publish" % item_id.uri_encode(),
		HTTPClient.METHOD_POST,
		{"expected_updated_at_utc": expected_updated_at_utc}
	)


func disable_equipment(item_id: String, expected_updated_at_utc: Variant) -> void:
	_request(
		RequestKind.EQUIPMENT_DISABLE,
		"/api/v1/equipment/%s/disable" % item_id.uri_encode(),
		HTTPClient.METHOD_POST,
		{"expected_updated_at_utc": expected_updated_at_utc}
	)


func _request(
	kind: int,
	path: String,
	method: int = HTTPClient.METHOD_GET,
	payload: Dictionary = {}
) -> void:
	if _request_kind != RequestKind.NONE:
		request_failed.emit(_kind_name(kind), "Another host request is still in progress.", [])
		return

	_request_kind = kind
	var headers := PackedStringArray([
		"Accept: application/json",
		"Content-Type: application/json",
		"X-Content-Studio-Api-Version: %s" % API_VERSION,
		"X-Request-Id: godot-%s" % str(Time.get_ticks_msec()),
	])
	var body := "" if method == HTTPClient.METHOD_GET else JSON.stringify(payload)
	var error := _http_request.request(
		base_url.trim_suffix("/") + path,
		headers,
		method,
		body
	)
	if error != OK:
		_fail_current("Unable to start request. Godot error code: %s" % error, [])


func _on_request_completed(
	result: int,
	response_code: int,
	_headers: PackedStringArray,
	body: PackedByteArray
) -> void:
	var completed_kind := _request_kind
	_request_kind = RequestKind.NONE

	if result != HTTPRequest.RESULT_SUCCESS:
		_fail_kind(completed_kind, "The authoring host could not be reached (result %s)." % result, [])
		return

	var decoded: Variant = JSON.parse_string(body.get_string_from_utf8())
	if typeof(decoded) != TYPE_DICTIONARY:
		_fail_kind(completed_kind, "The authoring host returned invalid JSON.", [])
		return

	var envelope := decoded as Dictionary
	var errors := envelope.get("errors", []) as Array
	if response_code < 200 or response_code >= 300:
		_fail_kind(completed_kind, _extract_error_message(errors, response_code), errors)
		return

	if not envelope.get("success", false):
		_fail_kind(completed_kind, _extract_error_message(errors, response_code), errors)
		return

	if str(envelope.get("api_version", "")) != API_VERSION:
		_fail_kind(
			completed_kind,
			"API version mismatch. Studio expects %s but host returned %s." % [
				API_VERSION,
				str(envelope.get("api_version", "missing")),
			],
			[]
		)
		return

	var data: Variant = envelope.get("data", {})
	if typeof(data) != TYPE_DICTIONARY:
		_fail_kind(completed_kind, "The authoring host response did not include an object payload.", [])
		return

	_match_success(completed_kind, data as Dictionary)


func _match_success(kind: int, data: Dictionary) -> void:
	match kind:
		RequestKind.HANDSHAKE:
			if str(data.get("api_version", "")) != API_VERSION:
				_fail_kind(kind, "The host does not support Content Studio API v%s." % API_VERSION, [])
				return
			handshake_received.emit(data)
			_request(RequestKind.HEALTH, "/api/v1/system/health")
		RequestKind.HEALTH:
			health_received.emit(data)
			_request(RequestKind.CATALOG, "/api/v1/catalog")
		RequestKind.CATALOG:
			catalog_received.emit(data)
			_request(RequestKind.ITEM_ASSETS, "/api/v1/assets/items")
		RequestKind.ITEM_ASSETS:
			item_assets_received.emit(data)
			_request(RequestKind.ITEMS, "/api/v1/items")
		RequestKind.ITEM_ASSET_IMPORT:
			item_asset_imported.emit(data)
		RequestKind.ITEMS:
			items_received.emit(data)
			_request(RequestKind.CONSUMABLE_OPTIONS, "/api/v1/consumables/options")
		RequestKind.CONSUMABLE_OPTIONS:
			consumable_options_received.emit(data)
			_request(RequestKind.CONSUMABLES, "/api/v1/consumables")
		RequestKind.CONSUMABLES:
			consumables_received.emit(data)
			_request(RequestKind.EQUIPMENT_OPTIONS, "/api/v1/equipment/options")
		RequestKind.EQUIPMENT_OPTIONS:
			equipment_options_received.emit(data)
			_request(RequestKind.EQUIPMENT, "/api/v1/equipment")
		RequestKind.EQUIPMENT:
			equipment_received.emit(data)
			connection_state_changed.emit("connected", "Connected to the local authoring host.")
		RequestKind.ITEM:
			item_received.emit(data)
		RequestKind.ITEM_PREVIEW:
			item_preview_received.emit(data)
		RequestKind.ITEM_SAVE_DRAFT, RequestKind.ITEM_PUBLISH, RequestKind.ITEM_DISABLE:
			item_mutation_completed.emit(data)
		RequestKind.CONSUMABLE:
			consumable_received.emit(data)
		RequestKind.CONSUMABLE_PREVIEW:
			consumable_preview_received.emit(data)
		RequestKind.CONSUMABLE_SAVE_DRAFT, RequestKind.CONSUMABLE_PUBLISH, RequestKind.CONSUMABLE_DISABLE:
			consumable_mutation_completed.emit(data)
		RequestKind.EQUIPMENT_ITEM:
			equipment_item_received.emit(data)
		RequestKind.EQUIPMENT_PREVIEW:
			equipment_preview_received.emit(data)
		RequestKind.EQUIPMENT_SAVE_DRAFT, RequestKind.EQUIPMENT_PUBLISH, RequestKind.EQUIPMENT_DISABLE:
			equipment_mutation_completed.emit(data)
		_:
			_fail_kind(kind, "Unexpected request completion.", [])


func _fail_current(message: String, errors: Array) -> void:
	var failed_kind := _request_kind
	_request_kind = RequestKind.NONE
	_fail_kind(failed_kind, message, errors)


func _fail_kind(kind: int, message: String, errors: Array) -> void:
	var operation := _kind_name(kind)
	if kind in [RequestKind.HANDSHAKE, RequestKind.HEALTH, RequestKind.CATALOG, RequestKind.ITEM_ASSETS, RequestKind.ITEMS, RequestKind.CONSUMABLE_OPTIONS, RequestKind.CONSUMABLES, RequestKind.EQUIPMENT_OPTIONS, RequestKind.EQUIPMENT]:
		connection_state_changed.emit("disconnected", message)
	request_failed.emit(operation, message, errors)


func _kind_name(kind: int) -> String:
	match kind:
		RequestKind.HANDSHAKE:
			return "handshake"
		RequestKind.HEALTH:
			return "health"
		RequestKind.CATALOG:
			return "catalog"
		RequestKind.ITEM_ASSETS:
			return "item_assets"
		RequestKind.ITEM_ASSET_IMPORT:
			return "item_asset_import"
		RequestKind.ITEMS:
			return "items"
		RequestKind.ITEM:
			return "item"
		RequestKind.ITEM_PREVIEW:
			return "item_preview"
		RequestKind.ITEM_SAVE_DRAFT:
			return "item_save_draft"
		RequestKind.ITEM_PUBLISH:
			return "item_publish"
		RequestKind.ITEM_DISABLE:
			return "item_disable"
		RequestKind.CONSUMABLE_OPTIONS:
			return "consumable_options"
		RequestKind.CONSUMABLES:
			return "consumables"
		RequestKind.CONSUMABLE:
			return "consumable"
		RequestKind.CONSUMABLE_PREVIEW:
			return "consumable_preview"
		RequestKind.CONSUMABLE_SAVE_DRAFT:
			return "consumable_save_draft"
		RequestKind.CONSUMABLE_PUBLISH:
			return "consumable_publish"
		RequestKind.CONSUMABLE_DISABLE:
			return "consumable_disable"
		RequestKind.EQUIPMENT_OPTIONS:
			return "equipment_options"
		RequestKind.EQUIPMENT:
			return "equipment"
		RequestKind.EQUIPMENT_ITEM:
			return "equipment_item"
		RequestKind.EQUIPMENT_PREVIEW:
			return "equipment_preview"
		RequestKind.EQUIPMENT_SAVE_DRAFT:
			return "equipment_save_draft"
		RequestKind.EQUIPMENT_PUBLISH:
			return "equipment_publish"
		RequestKind.EQUIPMENT_DISABLE:
			return "equipment_disable"
		_:
			return "request"


func _extract_error_message(errors: Array, response_code: int) -> String:
	if not errors.is_empty() and errors[0] is Dictionary:
		return str(errors[0].get("message", "Host request failed."))
	return "Host request failed with HTTP %s." % response_code
