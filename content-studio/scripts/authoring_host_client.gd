extends Node
class_name AuthoringHostClient

signal connection_state_changed(state: String, message: String)
signal handshake_received(payload: Dictionary)
signal health_received(payload: Dictionary)
signal catalog_received(payload: Dictionary)
signal request_failed(operation: String, message: String)

const API_VERSION := "1"
const DEFAULT_BASE_URL := "http://127.0.0.1:5187"
const REQUEST_TIMEOUT_SECONDS := 8.0

enum RequestKind {
	NONE,
	HANDSHAKE,
	HEALTH,
	CATALOG,
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


func _request(kind: int, path: String) -> void:
	_request_kind = kind
	var headers := PackedStringArray([
		"Accept: application/json",
		"X-Content-Studio-Api-Version: %s" % API_VERSION,
		"X-Request-Id: godot-%s" % str(Time.get_ticks_msec()),
	])
	var error := _http_request.request(base_url.trim_suffix("/") + path, headers)
	if error != OK:
		_fail_current("Unable to start request. Godot error code: %s" % error)


func _on_request_completed(
	result: int,
	response_code: int,
	_headers: PackedStringArray,
	body: PackedByteArray
) -> void:
	var completed_kind := _request_kind
	_request_kind = RequestKind.NONE

	if result != HTTPRequest.RESULT_SUCCESS:
		_fail_kind(completed_kind, "The authoring host could not be reached (result %s)." % result)
		return

	var decoded := JSON.parse_string(body.get_string_from_utf8())
	if typeof(decoded) != TYPE_DICTIONARY:
		_fail_kind(completed_kind, "The authoring host returned invalid JSON.")
		return

	var envelope := decoded as Dictionary
	if response_code < 200 or response_code >= 300:
		_fail_kind(completed_kind, _extract_error_message(envelope, response_code))
		return

	if not envelope.get("success", false):
		_fail_kind(completed_kind, _extract_error_message(envelope, response_code))
		return

	if str(envelope.get("api_version", "")) != API_VERSION:
		_fail_kind(
			completed_kind,
			"API version mismatch. Studio expects %s but host returned %s." % [
				API_VERSION,
				str(envelope.get("api_version", "missing")),
			]
		)
		return

	var data := envelope.get("data", {})
	if typeof(data) != TYPE_DICTIONARY:
		_fail_kind(completed_kind, "The authoring host response did not include an object payload.")
		return

	_match_success(completed_kind, data as Dictionary)


func _match_success(kind: int, data: Dictionary) -> void:
	match kind:
		RequestKind.HANDSHAKE:
			if str(data.get("api_version", "")) != API_VERSION:
				_fail_kind(kind, "The host does not support Content Studio API v%s." % API_VERSION)
				return
			handshake_received.emit(data)
			_request(RequestKind.HEALTH, "/api/v1/system/health")
		RequestKind.HEALTH:
			health_received.emit(data)
			_request(RequestKind.CATALOG, "/api/v1/catalog")
		RequestKind.CATALOG:
			catalog_received.emit(data)
			connection_state_changed.emit("connected", "Connected to the local authoring host.")
		_:
			_fail_kind(kind, "Unexpected request completion.")


func _fail_current(message: String) -> void:
	var failed_kind := _request_kind
	_request_kind = RequestKind.NONE
	_fail_kind(failed_kind, message)


func _fail_kind(kind: int, message: String) -> void:
	var operation := _kind_name(kind)
	connection_state_changed.emit("disconnected", message)
	request_failed.emit(operation, message)


func _kind_name(kind: int) -> String:
	match kind:
		RequestKind.HANDSHAKE:
			return "handshake"
		RequestKind.HEALTH:
			return "health"
		RequestKind.CATALOG:
			return "catalog"
		_:
			return "request"


func _extract_error_message(envelope: Dictionary, response_code: int) -> String:
	var errors := envelope.get("errors", [])
	if errors is Array and not errors.is_empty() and errors[0] is Dictionary:
		return str(errors[0].get("message", "Host request failed."))
	return "Host request failed with HTTP %s." % response_code
