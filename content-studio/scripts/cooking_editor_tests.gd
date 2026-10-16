extends SceneTree
# Focused editor regression: late replies must not discard an author's work.
const EDITOR = preload("res://scripts/cooking_editor.gd")
class TestClient:
	extends AuthoringHostClient
	var requested := ""
	func _ready() -> void: pass
	func load_cooking(id: String) -> void: requested = id
	func load_cooking_recipes(_search: String = "") -> void: pass
	func preview_cooking(_id: String, _payload: Dictionary) -> void: pass
var failures: Array[String] = []
func check(value: bool, message: String) -> void:
	if not value: failures.append(message)
func _initialize() -> void:
	call_deferred("run")
func run() -> void:
	var scene := Node.new()
	var client := TestClient.new()
	client.name = "AuthoringHostClient"
	scene.add_child(client)
	client.owner = scene
	client.unique_name_in_owner = true
	var editor = EDITOR.new()
	scene.add_child(editor)
	editor.owner = scene
	root.add_child(scene)
	editor.open_resource("apple_pie")
	check(client.requested == "apple_pie" and not editor._definition_id.editable, "pending load locks edits")
	var payload := {"recipe_id":"apple_pie", "updated_at_utc":"saved", "draft":{"recipe_id":"apple_pie", "ingredients":[{"item_id":"inventory_2_apple", "quantity":1},{"item_id":"prepared_pastry_shell", "quantity":1}], "output_item_id":"apple_pie", "output_quantity":1, "required_cooking_level":30, "xp":130, "success_low":128, "success_high":256, "burnt_output_item_id":"burnt_pie"}}
	var wrong := payload.duplicate(true); wrong.recipe_id = "wrong"
	client.cooking_definition_received.emit(wrong)
	check(editor._pending == "load", "wrong recipe reply ignored")
	client.cooking_definition_received.emit(payload)
	check(editor._inputs.get_child_count() == 2 and editor._fields.xp.value == 130, "complete recipe loads")
	editor._fields.xp.value = 140
	editor.open_resource("other")
	check(client.requested == "apple_pie" and editor._fields.xp.value == 140, "dirty navigation preserves edits before confirmation")
	for node in editor.get_children():
		if node is ConfirmationDialog: node.canceled.emit()
	editor._request_preview("save_draft")
	check(not editor._fields.output_item_id.editable, "pending preview locks edits")
	editor.open_resource("other")
	check(client.requested == "apple_pie", "pending preview blocks navigation")
	client.cooking_definition_received.emit(payload)
	check(editor._fields.xp.value == 140, "late load cannot replace previewed edits")
	client.request_failed.emit("cooking_preview", "test failure", [])
	check(editor._fields.output_item_id.editable and editor._apply.disabled, "failure unlocks form and invalidates apply")
	editor._on_definition({"recipe_id":"preparation_mix", "draft":{"recipe_id":"preparation_mix", "ingredients":[{"item_id":"pot_of_flour","quantity":1},{"item_id":"bucket_of_water","quantity":1}], "output_item_id":"pastry_dough", "output_quantity":1, "required_cooking_level":1, "xp":0, "success_low":256, "success_high":256, "operation":"mix", "duration_ms":1200, "additional_outputs":[{"item_id":"empty_pot","quantity":1},{"item_id":"empty_cooking_bucket","quantity":1}]}})
	var preparation: Dictionary = editor._draft()
	check(preparation.operation=="mix" and preparation.duration_ms==1200 and preparation.xp==0 and preparation.additional_outputs.size()==2,"preparation fields round trip without losing returned containers")
	print("PASS native Cooking editor pending/dirty reply guards" if failures.is_empty() else "FAIL " + str(failures))
	scene.queue_free()
	await process_frame
	quit(0 if failures.is_empty() else 1)
