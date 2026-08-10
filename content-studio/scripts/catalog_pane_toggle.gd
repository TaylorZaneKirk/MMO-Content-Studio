extends RefCounted
class_name CatalogPaneToggle

static func attach(workspace: HBoxContainer, catalog_panel: Control) -> Button:
	var toggle := Button.new()
	toggle.name = "CatalogToggle"
	toggle.custom_minimum_size = Vector2(32, 0)
	toggle.tooltip_text = "Collapse catalog"
	toggle.text = "<"
	toggle.pressed.connect(func() -> void:
		catalog_panel.visible = not catalog_panel.visible
		_set_remaining_panels_equal(workspace, catalog_panel, not catalog_panel.visible)
		toggle.text = ">" if not catalog_panel.visible else "<"
		toggle.tooltip_text = "Expand catalog" if not catalog_panel.visible else "Collapse catalog"
	)
	workspace.add_child(toggle)
	workspace.move_child(toggle, workspace.get_children().find(catalog_panel))
	return toggle


static func _set_remaining_panels_equal(workspace: HBoxContainer, catalog_panel: Control, collapsed: bool) -> void:
	var catalog_index := workspace.get_children().find(catalog_panel)
	for index in range(catalog_index + 1, workspace.get_child_count()):
		var panel := workspace.get_child(index) as Control
		if panel == null:
			continue
		if collapsed:
			panel.set_meta("catalog_toggle_minimum_width", panel.custom_minimum_size.x)
			panel.set_meta("catalog_toggle_size_flags", panel.size_flags_horizontal)
			panel.set_meta("catalog_toggle_stretch_ratio", panel.size_flags_stretch_ratio)
			panel.custom_minimum_size.x = 0.0
			panel.size_flags_horizontal = Control.SIZE_EXPAND_FILL
			panel.size_flags_stretch_ratio = 1.0
		else:
			panel.custom_minimum_size.x = float(panel.get_meta("catalog_toggle_minimum_width", panel.custom_minimum_size.x))
			panel.size_flags_horizontal = int(panel.get_meta("catalog_toggle_size_flags", panel.size_flags_horizontal))
			panel.size_flags_stretch_ratio = float(panel.get_meta("catalog_toggle_stretch_ratio", panel.size_flags_stretch_ratio))
