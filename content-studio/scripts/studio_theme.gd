extends RefCounted

# Shared checkbox affordances, plus the first redesigned workspace's visual theme.
# Icons are drawn locally so a missing/imported texture cannot hide an empty box.
static func checkbox_theme() -> Theme:
	var result := Theme.new()
	for disabled: bool in [false, true]:
		for checked: bool in [false, true]:
			var border := "#748599" if disabled else "#a4b7ca"
			var fill := "#314451" if disabled else "#66d9c1"
			var mark := '<path d="M7 12l3 3 7-7" fill="none" stroke="#10242a" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"/>' if checked else ""
			var svg := '<svg xmlns="http://www.w3.org/2000/svg" width="24" height="24"><rect x="3" y="3" width="18" height="18" rx="4" fill="%s" stroke="%s" stroke-width="2"/>%s</svg>' % [fill if checked else "#17212e", border, mark]
			var image := Image.new()
			image.load_svg_from_string(svg)
			var icon_name := "checked" if checked else "unchecked"
			if disabled:
				icon_name += "_disabled"
			result.set_icon(icon_name, "CheckBox", ImageTexture.create_from_image(image))
	result.set_constant("h_separation", "CheckBox", 10)
	result.set_color("checkbox_checked_color", "CheckBox", Color.WHITE)
	result.set_color("checkbox_unchecked_color", "CheckBox", Color.WHITE)
	return result


static func item_theme() -> Theme:
	var result := checkbox_theme()
	result.default_font_size = 15
	for type_name: String in ["Label", "Button", "OptionButton", "CheckBox", "LineEdit", "TabContainer", "ItemList", "TextEdit"]:
		result.set_color("font_color", type_name, Color("e5edf5"))
		result.set_color("font_hover_color", type_name, Color.WHITE)
		result.set_color("font_disabled_color", type_name, Color("8392a4"))
	for type_name: String in ["Button", "OptionButton"]:
		result.set_stylebox("normal", type_name, box("202d3d", "3c4c60"))
		result.set_stylebox("hover", type_name, box("2b3d50", "7690a6"))
		result.set_stylebox("pressed", type_name, box("27483f", "66d9c1"))
		result.set_stylebox("disabled", type_name, box("192330", "344253"))
		result.set_stylebox("focus", type_name, focus_box())
	result.set_type_variation("PrimaryButton", "Button")
	result.set_stylebox("normal", "PrimaryButton", box("66d9c1", "66d9c1"))
	result.set_stylebox("hover", "PrimaryButton", box("86e6d2", "86e6d2"))
	result.set_color("font_color", "PrimaryButton", Color("10242a"))
	result.set_color("font_hover_color", "PrimaryButton", Color("10242a"))
	result.set_stylebox("normal", "LineEdit", box("101923", "46566b"))
	result.set_stylebox("read_only", "LineEdit", box("17212e", "354355"))
	result.set_stylebox("focus", "LineEdit", focus_box())
	result.set_color("font_placeholder_color", "LineEdit", Color("8898ab"))
	result.set_stylebox("focus", "CheckBox", focus_box())
	result.set_stylebox("panel", "TabContainer", box("17212e", "17212e"))
	result.set_stylebox("tab_selected", "TabContainer", box("29443f", "66d9c1"))
	result.set_stylebox("tab_unselected", "TabContainer", box("17212e", "354355"))
	result.set_stylebox("tab_hovered", "TabContainer", box("243447", "7690a6"))
	result.set_color("font_selected_color", "TabContainer", Color("91ecd7"))
	result.set_color("font_unselected_color", "TabContainer", Color("a9b8c9"))
	result.set_stylebox("normal", "TextEdit", box("101923", "46566b"))
	result.set_stylebox("focus", "TextEdit", focus_box())
	result.set_stylebox("panel", "ItemList", box("101923", "354355"))
	result.set_stylebox("selected", "ItemList", box("29443f", "66d9c1"))
	result.set_stylebox("selected_focus", "ItemList", box("29443f", "66d9c1"))
	return result


static func box(fill: String, border: String) -> StyleBoxFlat:
	var style := StyleBoxFlat.new()
	style.bg_color = Color(fill)
	style.border_color = Color(border)
	style.set_border_width_all(1)
	style.set_corner_radius_all(6)
	style.content_margin_left = 12
	style.content_margin_right = 12
	style.content_margin_top = 9
	style.content_margin_bottom = 9
	return style


static func focus_box() -> StyleBoxFlat:
	var style := box("00000000", "66d9c1")
	style.set_border_width_all(2)
	return style


# Shared layout primitives only; each workspace still owns its fields and actions.
static func pages(parent: Node) -> TabContainer:
	var tabs := TabContainer.new()
	tabs.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	tabs.size_flags_vertical = Control.SIZE_EXPAND_FILL
	tabs.use_hidden_tabs_for_min_size = false
	parent.add_child(tabs)
	return tabs


static func page(parent: TabContainer, title: String) -> VBoxContainer:
	var scroll := ScrollContainer.new()
	scroll.name = title
	parent.add_child(scroll)
	return scroll_content(scroll)


static func scroll_content(parent: Node) -> VBoxContainer:
	var scroll: ScrollContainer
	if parent is ScrollContainer:
		scroll = parent
	else:
		scroll = ScrollContainer.new()
		parent.add_child(scroll)
	scroll.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	scroll.size_flags_vertical = Control.SIZE_EXPAND_FILL
	var content := VBoxContainer.new()
	content.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	content.add_theme_constant_override("separation", 12)
	scroll.add_child(content)
	return content


static func panel_content(parent: Node, width: float = 0) -> VBoxContainer:
	var panel := PanelContainer.new()
	panel.custom_minimum_size.x = width
	if width == 0: panel.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	panel.add_theme_stylebox_override("panel", box("17212e", "354355"))
	parent.add_child(panel)
	var content := VBoxContainer.new()
	content.add_theme_constant_override("separation", 12)
	panel.add_child(content)
	return content
