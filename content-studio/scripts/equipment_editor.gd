extends HBoxContainer
class_name EquipmentEditor

var _client: AuthoringHostClient
var _items: Array = []
var _assets: Array = []
var _asset_by_path: Dictionary = {}
var _options: Dictionary = {}

var _search: LineEdit
var _list: VBoxContainer
var _item_id: Label
var _display_name: Label
var _icon_path: Label
var _publication: Label
var _kind: Label
var _slot: Label
var _required_strength: Label
var _visual_key: Label
var _updated: Label
var _icon_preview: TextureRect
var _status: Label
var _wearable_slots: VBoxContainer
var _hand_slots: VBoxContainer
var _requirements: VBoxContainer
var _modifiers: VBoxContainer
var _combat_profile: VBoxContainer
var _combat_bonuses: VBoxContainer


func _ready() -> void:
	_client = %AuthoringHostClient
	_build_ui()
	_connect_client()
	_render_empty_state()


func _connect_client() -> void:
	_client.item_assets_received.connect(_on_assets_received)
	_client.equipment_options_received.connect(_on_options_received)
	_client.equipment_received.connect(_on_equipment_received)
	_client.equipment_item_received.connect(_on_equipment_item_received)
	_client.request_failed.connect(_on_request_failed)


func _build_ui() -> void:
	add_theme_constant_override("separation", 14)
	var catalog_panel := _panel()
	catalog_panel.custom_minimum_size = Vector2(320, 0)
	add_child(catalog_panel)
	var catalog := VBoxContainer.new()
	catalog.add_theme_constant_override("separation", 10)
	catalog_panel.add_child(catalog)
	catalog.add_child(_heading("Equipment", 20))
	_search = LineEdit.new()
	_search.placeholder_text = "Search item ID, name, or slot"
	_search.text_changed.connect(_rebuild_list.unbind(1))
	catalog.add_child(_search)
	var slot_heading := _heading("Wearable Slots", 14)
	catalog.add_child(slot_heading)
	_wearable_slots = VBoxContainer.new()
	_wearable_slots.add_theme_constant_override("separation", 3)
	catalog.add_child(_wearable_slots)
	var hand_heading := _heading("Deferred Hand Slots", 14)
	catalog.add_child(hand_heading)
	_hand_slots = VBoxContainer.new()
	_hand_slots.add_theme_constant_override("separation", 3)
	catalog.add_child(_hand_slots)
	var catalog_scroll := ScrollContainer.new()
	catalog_scroll.size_flags_vertical = Control.SIZE_EXPAND_FILL
	catalog.add_child(catalog_scroll)
	_list = VBoxContainer.new()
	_list.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_list.add_theme_constant_override("separation", 6)
	catalog_scroll.add_child(_list)

	var detail_panel := _panel()
	detail_panel.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	add_child(detail_panel)
	var scroll := ScrollContainer.new()
	scroll.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	scroll.size_flags_vertical = Control.SIZE_EXPAND_FILL
	detail_panel.add_child(scroll)
	var detail := VBoxContainer.new()
	detail.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	detail.add_theme_constant_override("separation", 12)
	scroll.add_child(detail)
	detail.add_child(_heading("Equipment Definition", 20))

	var summary_row := HBoxContainer.new()
	summary_row.add_theme_constant_override("separation", 16)
	detail.add_child(summary_row)
	_icon_preview = TextureRect.new()
	_icon_preview.custom_minimum_size = Vector2(128, 128)
	_icon_preview.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	_icon_preview.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED
	summary_row.add_child(_icon_preview)
	var summary_grid := GridContainer.new()
	summary_grid.columns = 2
	summary_grid.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	summary_row.add_child(summary_grid)
	_item_id = _add_value_field(summary_grid, "Stable item ID", "No item selected")
	_display_name = _add_value_field(summary_grid, "Display name", "No item selected")
	_icon_path = _add_value_field(summary_grid, "Inventory / ground icon", "No icon selected")
	_publication = _add_value_field(summary_grid, "Publication state", "Unknown")
	_kind = _add_value_field(summary_grid, "Authoring kind", "Unknown")
	_slot = _add_value_field(summary_grid, "Equipment slot", "None")
	_required_strength = _add_value_field(summary_grid, "Required strength", "1")
	_visual_key = _add_value_field(summary_grid, "Derived visual key", "None")
	_updated = _add_value_field(summary_grid, "Last updated", "Unknown")

	_status = Label.new()
	_status.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	_status.modulate = Color(0.7, 0.73, 0.79, 1)
	detail.add_child(_status)

	detail.add_child(_heading("Skill requirements", 16))
	_requirements = VBoxContainer.new()
	_requirements.add_theme_constant_override("separation", 4)
	detail.add_child(_requirements)
	detail.add_child(_heading("Skill modifiers", 16))
	_modifiers = VBoxContainer.new()
	_modifiers.add_theme_constant_override("separation", 4)
	detail.add_child(_modifiers)
	detail.add_child(_heading("Combat profile", 16))
	_combat_profile = VBoxContainer.new()
	_combat_profile.add_theme_constant_override("separation", 4)
	detail.add_child(_combat_profile)
	detail.add_child(_heading("Combat bonuses", 16))
	_combat_bonuses = VBoxContainer.new()
	_combat_bonuses.add_theme_constant_override("separation", 4)
	detail.add_child(_combat_bonuses)


func _panel() -> PanelContainer:
	var panel := PanelContainer.new()
	var style := StyleBoxFlat.new()
	style.bg_color = Color(0.086, 0.098, 0.122, 1)
	style.border_color = Color(0.19, 0.22, 0.28, 1)
	style.set_border_width_all(1)
	style.set_corner_radius_all(8)
	style.content_margin_left = 16
	style.content_margin_top = 14
	style.content_margin_right = 16
	style.content_margin_bottom = 14
	panel.add_theme_stylebox_override("panel", style)
	return panel


func _heading(value: String, size: int) -> Label:
	var label := Label.new()
	label.text = value
	label.add_theme_font_size_override("font_size", size)
	return label


func _field_label(value: String) -> Label:
	var label := Label.new()
	label.text = value
	return label


func _add_value_field(grid: GridContainer, label_text: String, value: String) -> Label:
	grid.add_child(_field_label(label_text))
	var label := Label.new()
	label.text = value
	label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	grid.add_child(label)
	return label


func _on_assets_received(payload: Dictionary) -> void:
	_assets = payload.get("assets", []) as Array
	_asset_by_path.clear()
	for variant in _assets:
		if variant is not Dictionary:
			continue
		var asset := variant as Dictionary
		_asset_by_path[str(asset.get("resource_path", ""))] = asset


func _on_options_received(payload: Dictionary) -> void:
	_options = payload
	_render_option_list(_wearable_slots, payload.get("wearable_slots", []) as Array)
	_render_option_list(_hand_slots, payload.get("deferred_hand_slots", []) as Array)


func _on_equipment_received(payload: Dictionary) -> void:
	_items = payload.get("items", []) as Array
	_rebuild_list()


func _on_equipment_item_received(payload: Dictionary) -> void:
	_item_id.text = str(payload.get("item_id", ""))
	_display_name.text = str(payload.get("display_name", ""))
	_icon_path.text = str(payload.get("icon_texture_path", ""))
	_publication.text = str(payload.get("publication_state", "Unknown"))
	_kind.text = str(payload.get("authoring_kind", "Unknown"))
	var slot_display := str(payload.get("equipment_slot_display_name", ""))
	var slot_id := str(payload.get("equipment_slot_id", ""))
	_slot.text = "%s (%s)" % [slot_display, slot_id] if not slot_display.is_empty() else _optional(slot_id, "None")
	_required_strength.text = str(payload.get("required_strength", 1))
	_visual_key.text = _optional(str(payload.get("visual_asset_key", "")), "None")
	_updated.text = str(payload.get("updated_at_utc", "Unknown"))
	_render_requirements(payload.get("requirements", []) as Array)
	_render_modifiers(payload.get("skill_modifiers", []) as Array)
	_render_combat_profile(payload.get("combat_profile", null))
	_render_combat_bonuses(payload.get("combat_bonuses", null))
	_update_icon_preview(str(payload.get("asset_preview_file_path", "")), str(payload.get("icon_texture_path", "")))
	_status.text = _status_for_item(payload)


func _on_request_failed(operation: String, message: String, _errors: Array) -> void:
	if not operation.begins_with("equipment"):
		return
	_status.text = "%s failed: %s" % [operation, message]


func _rebuild_list() -> void:
	_clear_rows(_list)
	var query := _search.text.strip_edges().to_lower()
	for variant in _items:
		if variant is not Dictionary:
			continue
		var item := variant as Dictionary
		var haystack := "%s %s %s" % [
			item.get("item_id", ""),
			item.get("display_name", ""),
			item.get("equipment_slot_id", ""),
		]
		if not query.is_empty() and not haystack.to_lower().contains(query):
			continue
		var button := Button.new()
		button.alignment = HORIZONTAL_ALIGNMENT_LEFT
		button.text = "%s\n%s • %s • %s" % [
			str(item.get("display_name", "Unnamed item")),
			str(item.get("publication_state", "Unknown")),
			str(item.get("authoring_kind", "Unknown")),
			_optional(str(item.get("equipment_slot_id", "")), "no slot"),
		]
		button.tooltip_text = str(item.get("item_id", ""))
		button.pressed.connect(_client.load_equipment_item.bind(str(item.get("item_id", ""))))
		_list.add_child(button)


func _render_empty_state() -> void:
	_item_id.text = "No item selected"
	_display_name.text = "No item selected"
	_icon_path.text = "No icon selected"
	_publication.text = "Unknown"
	_kind.text = "Unknown"
	_slot.text = "None"
	_required_strength.text = "1"
	_visual_key.text = "None"
	_updated.text = "Unknown"
	_status.text = "Select an equipment definition to inspect its read-only aggregate."
	_icon_preview.texture = null
	_clear_rows(_requirements)
	_clear_rows(_modifiers)
	_clear_rows(_combat_profile)
	_clear_rows(_combat_bonuses)


func _render_option_list(container: VBoxContainer, values: Array) -> void:
	_clear_rows(container)
	if values.is_empty():
		_add_wrapped(container, "No options loaded.")
		return
	for variant in values:
		if variant is Dictionary:
			var option := variant as Dictionary
			_add_wrapped(container, "%s  •  %s" % [
				str(option.get("display_name", "Option")),
				str(option.get("id", "")),
			])


func _render_requirements(values: Array) -> void:
	_clear_rows(_requirements)
	if values.is_empty():
		_add_wrapped(_requirements, "No skill requirements.")
		return
	for variant in values:
		if variant is Dictionary:
			var requirement := variant as Dictionary
			_add_wrapped(_requirements, "%s >= %s" % [
				str(requirement.get("skill_display_name", requirement.get("skill_id", "Skill"))),
				str(requirement.get("required_value", 1)),
			])


func _render_modifiers(values: Array) -> void:
	_clear_rows(_modifiers)
	if values.is_empty():
		_add_wrapped(_modifiers, "No skill modifiers.")
		return
	for variant in values:
		if variant is Dictionary:
			var modifier := variant as Dictionary
			_add_wrapped(_modifiers, "%s %+d" % [
				str(modifier.get("skill_display_name", modifier.get("skill_id", "Skill"))),
				int(modifier.get("modifier_value", 0)),
			])


func _render_combat_profile(value: Variant) -> void:
	_clear_rows(_combat_profile)
	if value == null or value is not Dictionary:
		_add_wrapped(_combat_profile, "No combat profile.")
		return
	var profile := value as Dictionary
	for pair in [
		["Profile ID", profile.get("profile_id", "")],
		["Attack type", profile.get("attack_type", "")],
		["Accuracy style", profile.get("accuracy_style", "")],
		["Range", "%s-%s tiles" % [profile.get("minimum_range_tiles", 0), profile.get("maximum_range_tiles", 0)]],
		["Attack speed", "%s units" % profile.get("attack_speed_units", 0)],
	]:
		_add_wrapped(_combat_profile, "%s: %s" % [pair[0], _optional(str(pair[1]), "None")])


func _render_combat_bonuses(value: Variant) -> void:
	_clear_rows(_combat_bonuses)
	if value == null or value is not Dictionary:
		_add_wrapped(_combat_bonuses, "No combat bonuses.")
		return
	var bonuses := value as Dictionary
	var rendered := 0
	for variant in _options.get("combat_bonus_fields", []) as Array:
		if variant is not Dictionary:
			continue
		var option := variant as Dictionary
		var id := str(option.get("id", ""))
		var amount := int(bonuses.get(id, 0))
		if amount == 0:
			continue
		_add_wrapped(_combat_bonuses, "%s %+d" % [
			str(option.get("display_name", id)),
			amount,
		])
		rendered += 1
	if rendered == 0:
		_add_wrapped(_combat_bonuses, "Combat bonuses are all zero.")


func _update_icon_preview(explicit_file_path: String, resource_path: String) -> void:
	_icon_preview.texture = null
	var file_path := explicit_file_path
	if file_path.is_empty():
		var asset := _asset_by_path.get(resource_path, {}) as Dictionary
		file_path = str(asset.get("file_path", ""))
	if file_path.is_empty() or not FileAccess.file_exists(file_path):
		return
	var image := Image.load_from_file(file_path)
	if image == null or image.is_empty():
		return
	_icon_preview.texture = ImageTexture.create_from_image(image)


func _status_for_item(payload: Dictionary) -> String:
	if bool(payload.get("editable_in_equipment", false)):
		return "Read-only T3A view. Mutation controls will land after equipment validation and preview contracts."
	if str(payload.get("authoring_kind", "")) == "WeaponOrTool":
		return "Visible for context. Hand-held weapons and tools are deferred to T3B."
	return "This definition is visible here for classification context but is not editable in the T3A wearable workspace."


func _optional(value: String, fallback: String) -> String:
	return fallback if value.strip_edges().is_empty() or value == "<null>" else value


func _clear_rows(container: Node) -> void:
	for child in container.get_children():
		container.remove_child(child)
		child.queue_free()


func _add_wrapped(container: Node, value: String) -> void:
	var label := Label.new()
	label.text = value
	label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	container.add_child(label)
