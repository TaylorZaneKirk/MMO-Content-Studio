extends Node
class_name AuthoringHttpTransport

const CONTENT_STUDIO_LOGGER := preload("res://scripts/content_studio_logger.gd")

signal request_succeeded(operation: String, payload: Dictionary)
signal request_failed(operation: String, message: String, errors: Array)

const API_VERSION := "1"
const DEFAULT_BASE_URL := "http://127.0.0.1:5187"
const REQUEST_TIMEOUT_SECONDS := 8.0
const MUTATION_REQUEST_TIMEOUT_SECONDS := 30.0

@export var base_url := DEFAULT_BASE_URL

var _http_request: HTTPRequest
var _operation := ""


func _ready() -> void:
	_http_request = HTTPRequest.new()
	_http_request.timeout = REQUEST_TIMEOUT_SECONDS
	_http_request.request_completed.connect(_on_request_completed)
	add_child(_http_request)


func is_busy() -> bool:
	return not _operation.is_empty()


func reset() -> void:
	_operation = ""


func request(
	operation: String,
	path: String,
	method: int = HTTPClient.METHOD_GET,
	payload: Dictionary = {}
) -> void:
	if is_busy():
		_emit_request_failed(operation, "Another host request is still in progress.", [])
		return

	_operation = operation
	_http_request.timeout = _timeout_seconds_for_operation(operation)
	var headers := PackedStringArray([
		"Accept: application/json",
		"Content-Type: application/json",
		"X-Content-Studio-Api-Version: %s" % API_VERSION,
		"X-Request-Id: godot-%s" % str(Time.get_ticks_msec()),
	])
	var body := "" if method == HTTPClient.METHOD_GET else JSON.stringify(payload)
	CONTENT_STUDIO_LOGGER.debug("Host request started", {
		"method": _method_name(method),
		"operation": operation,
		"path": path,
		"timeout_seconds": _http_request.timeout,
	})
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
	var completed_operation := _operation
	_operation = ""
	CONTENT_STUDIO_LOGGER.debug("Host request completed", {
		"http_status": response_code,
		"operation": completed_operation,
		"result": result,
	})

	if result != HTTPRequest.RESULT_SUCCESS:
		_emit_request_failed(
			completed_operation,
			_transport_failure_message(completed_operation, result),
			[]
		)
		return

	var json := JSON.new()
	if json.parse(body.get_string_from_utf8()) != OK or typeof(json.data) != TYPE_DICTIONARY:
		_emit_request_failed(
			completed_operation,
			"The authoring host returned a non-JSON response (HTTP %s)." % response_code,
			[]
		)
		return

	var envelope := json.data as Dictionary
	var errors := envelope.get("errors", []) as Array
	if response_code < 200 or response_code >= 300:
		_emit_request_failed(
			completed_operation,
			_extract_error_message(errors, response_code),
			errors
		)
		return

	if not envelope.get("success", false):
		_emit_request_failed(
			completed_operation,
			_extract_error_message(errors, response_code),
			errors
		)
		return

	if str(envelope.get("api_version", "")) != API_VERSION:
		_emit_request_failed(
			completed_operation,
			"API version mismatch. Studio expects %s but host returned %s." % [
				API_VERSION,
				str(envelope.get("api_version", "missing")),
			],
			[]
		)
		return

	var data: Variant = envelope.get("data", {})
	if typeof(data) != TYPE_DICTIONARY:
		_emit_request_failed(
			completed_operation,
			"The authoring host response did not include an object payload.",
			[]
		)
		return

	CONTENT_STUDIO_LOGGER.info("Host request succeeded", {
		"http_status": response_code,
		"operation": completed_operation,
	})
	request_succeeded.emit(completed_operation, data as Dictionary)


func _fail_current(message: String, errors: Array) -> void:
	var failed_operation := _operation
	_operation = ""
	_emit_request_failed(failed_operation, message, errors)


func _emit_request_failed(operation: String, message: String, errors: Array) -> void:
	CONTENT_STUDIO_LOGGER.error("Host request failed", {
		"error_count": errors.size(),
		"message": message,
		"operation": operation,
	})
	request_failed.emit(operation, message, errors)


func _method_name(method: int) -> String:
	match method:
		HTTPClient.METHOD_GET:
			return "GET"
		HTTPClient.METHOD_POST:
			return "POST"
		HTTPClient.METHOD_PUT:
			return "PUT"
		HTTPClient.METHOD_DELETE:
			return "DELETE"
		_:
			return str(method)


func _timeout_seconds_for_operation(operation: String) -> float:
	return MUTATION_REQUEST_TIMEOUT_SECONDS if _is_mutation_operation(operation) else REQUEST_TIMEOUT_SECONDS


func _is_mutation_operation(operation: String) -> bool:
	if operation == "actor_calibration_save":
		return true
	return operation.ends_with("_save_draft") \
		or operation.ends_with("_publish") \
		or operation.ends_with("_disable") \
		or operation.ends_with("_delete")


func _transport_failure_message(operation: String, result: int) -> String:
	if result == HTTPRequest.RESULT_TIMEOUT and _is_mutation_operation(operation):
		return "The authoring host mutation timed out after %s seconds. It may have committed already; reload before retrying." % int(_timeout_seconds_for_operation(operation))
	return "The authoring host could not be reached (result %s)." % result


func _extract_error_message(errors: Array, response_code: int) -> String:
	if not errors.is_empty() and errors[0] is Dictionary:
		return str(errors[0].get("message", "Host request failed."))
	return "Host request failed with HTTP %s." % response_code
