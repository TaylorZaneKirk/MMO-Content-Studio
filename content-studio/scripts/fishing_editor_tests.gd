extends SceneTree
const EDITOR = preload("res://scripts/fishing_editor.gd")
class TestClient:
	extends AuthoringHostClient
	var requested := ""
	func _ready() -> void: pass
	func load_fishing(id: String) -> void: requested = id
	func load_fishing_methods(_search: String = "") -> void: pass
	func preview_fishing(_id: String, _payload: Dictionary) -> void: pass
var failures: Array[String] = []
func check(value: bool, message: String) -> void:
	if not value: failures.append(message)
func _initialize() -> void: call_deferred("run")
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
	var draft := {"action_id":"lure", "display_name":"Lure", "tool_item_id":"inventory_332_fishing_pole", "consumable_item_id":"feather", "consume_quantity":2, "interval_ms":3600, "catches":[{"item_id":"inventory_257_trout","required_level":20,"xp":50,"success_low":32,"success_high":192},{"item_id":"sardine","required_level":5,"xp":20,"success_low":32,"success_high":192}]}
	editor.open_resource("lure")
	check(client.requested == "lure" and not editor._definition_id.editable,"pending load locks identity")
	var payload := {"action_id":"lure", "updated_at_utc":"saved", "draft":draft}
	client.fishing_definition_received.emit(payload)
	check(editor._draft() == draft,"every method field and catch order round trips")
	editor._fields.interval_ms.value = 4200
	editor.open_resource("other")
	check(client.requested == "lure","dirty navigation preserves work")
	for node in editor.get_children():
		if node is ConfirmationDialog: node.canceled.emit()
	editor._request_preview("save_draft")
	check(not editor._fields.tool_item_id.editable,"pending preview locks edits")
	client.fishing_definition_received.emit(payload)
	check(editor._fields.interval_ms.value == 4200,"late load does not overwrite edited draft")
	client.request_failed.emit("fishing_preview","fixture failure",[])
	check(editor._fields.tool_item_id.editable and editor._apply.disabled,"failure invalidates apply and unlocks edits")
	print("PASS native Fishing fields, ordering, dirty/pending/late-reply guards" if failures.is_empty() else str(failures))
	scene.queue_free()
	quit(0 if failures.is_empty() else 1)
