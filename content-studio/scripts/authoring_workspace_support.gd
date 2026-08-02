extends RefCounted
class_name AuthoringWorkspaceSupport

var preview_signature := ""
var preview_operation := ""
var preview_applicable := false


func clear_preview(
	apply_button: Button,
	changes_container: Node,
	validation_container: Node,
	default_button_text: String = "Apply Previewed Operation"
) -> void:
	preview_signature = ""
	preview_operation = ""
	preview_applicable = false
	apply_button.disabled = true
	apply_button.text = default_button_text
	clear_container(changes_container)
	clear_container(validation_container)


func accept_preview(
	operation: String,
	signature: String,
	applicable: bool,
	apply_button: Button,
	button_text: String
) -> void:
	preview_operation = operation
	preview_signature = signature
	preview_applicable = applicable
	apply_button.disabled = not applicable
	apply_button.text = button_text


func can_apply(operation: String, signature: String) -> bool:
	return (
		preview_applicable
		and preview_operation == operation
		and preview_signature == signature
	)


func render_changes(container: Node, values: Array) -> void:
	clear_container(container)
	if values.is_empty():
		add_wrapped_label(container, "No persisted values would change.")
		return
	for variant in values:
		if variant is not Dictionary:
			continue
		var change := variant as Dictionary
		add_wrapped_label(container, "• %s: %s → %s" % [
			str(change.get("field", "field")),
			str(change.get("before", "∅")),
			str(change.get("after", "∅")),
		])


func render_validation(container: Node, values: Array) -> void:
	clear_container(container)
	if values.is_empty():
		add_wrapped_label(container, "No validation messages.")
		return
	for variant in values:
		if variant is not Dictionary:
			continue
		var message := variant as Dictionary
		add_wrapped_label(container, "[%s] %s" % [
			str(message.get("severity", "Info")).to_upper(),
			str(message.get("message", "Validation message")),
		])


func operation_name(operation: String) -> String:
	match operation:
		"save_draft":
			return "Save Draft"
		"publish":
			return "Publish"
		"disable":
			return "Disable"
		_:
			return operation.capitalize()


func clear_container(container: Node) -> void:
	for child in container.get_children():
		container.remove_child(child)
		child.queue_free()


func add_wrapped_label(container: Node, value: String) -> void:
	var label := Label.new()
	label.text = value
	label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	container.add_child(label)
