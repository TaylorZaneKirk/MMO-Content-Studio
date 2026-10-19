extends SceneTree
# One native form roundtrip: an unrelated edit must retain the authored pose.
func _initialize() -> void:
	var form = load("res://scripts/npc_editor.gd").new()
	form._workspace_support = load("res://scripts/authoring_workspace_support.gd").new()
	form._build_ui()
	form._load_npc({"npc_definition_id": "garrick_hale", "display_name": "Garrick Hale", "ambient_animation": "woodcutting"})
	form._display_name.text = "Garrick Hale (edited)"
	if form._payload().ambient_animation != "woodcutting":
		push_error("Unrelated native edit lost ambient animation")
		quit(1)
		return
	form._load_npc({"npc_definition_id": "ordinary_npc"})
	if form._payload().ambient_animation != "none":
		push_error("Legacy NPC inherited ambient animation")
		quit(1)
		return
	form.free()
	print("PASS: native NPC editor retains animation through unrelated edits; legacy NPC defaults none.")
	quit()
