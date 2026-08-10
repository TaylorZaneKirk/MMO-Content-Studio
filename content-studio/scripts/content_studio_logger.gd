extends RefCounted
class_name ContentStudioLogger

const MAXIMUM_VALUE_LENGTH := 160


static func debug(event: String, fields: Dictionary = {}) -> void:
	_print_entry("DEBUG", event, fields)


static func info(event: String, fields: Dictionary = {}) -> void:
	_print_entry("INFO", event, fields)


static func error(event: String, fields: Dictionary = {}) -> void:
	_print_entry("ERROR", event, fields)


static func _print_entry(level: String, event: String, fields: Dictionary) -> void:
	var keys := fields.keys()
	keys.sort()
	var parts: Array[String] = []
	for key_variant in keys:
		var key := str(key_variant)
		parts.append("%s=%s" % [key, _format_value(key, fields.get(key_variant))])
	var suffix := "" if parts.is_empty() else " %s" % " ".join(parts)
	print("[ContentStudio][%s][%s] %s%s" % [
		level,
		Time.get_datetime_string_from_system(true, true),
		event,
		suffix,
	])


static func _format_value(key: String, value: Variant) -> String:
	if key.to_lower().contains("password") or key.to_lower().contains("token") or key.to_lower().contains("secret"):
		return "[redacted]"
	var text := str(value).replace("\n", "\\n").replace("\r", "\\r")
	return text.left(MAXIMUM_VALUE_LENGTH) + "..." if text.length() > MAXIMUM_VALUE_LENGTH else text
