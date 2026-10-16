extends SceneTree
func _initialize() -> void:
	var form = load("res://scripts/blacksmithing_editor.gd").new()
	form._build_ui()
	form._new_definition()
	if form._draft().success_percent != 100:
		push_error("New recipes must default to guaranteed")
		quit(1)
		return
	form._on_definition({"recipe_id":"smelt_iron_bar","publication_state":"Published","draft":{"required_level":15,"output_quantity":1,"duration_ms":3000,"success_percent":50,"xp_tenths":125,"inputs":[]}})
	if form._draft().success_percent != 50 or form._draft().xp_tenths != 125:
		push_error("Native editor lost exact chance or XP")
		quit(1)
		return
	form.free()
	print("PASS native blacksmithing chance default and round-trip")
	quit(0)
