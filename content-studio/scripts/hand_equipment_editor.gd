extends HBoxContainer
class_name HandEquipmentEditor

const WORKSPACE_SUPPORT_SCRIPT := preload("res://scripts/authoring_workspace_support.gd")
const PAPER_DOLL_PREVIEW_SCRIPT := preload("res://scripts/paper_doll_preview.gd")

const HAND_VISIBLE_SLOTS := ["right_hand", "left_hand"]
const DEFAULT_PREVIEW_DIRECTION := "N"
const DEFAULT_PREVIEW_FRAME := 3
const COMBAT_UNIT_MILLISECONDS := 600

var _client: AuthoringHostClient
var _items: Array = []
var _assets: Array = []
var _asset_by_path: Dictionary = {}
var _options: Dictionary = {}
var _current_item: Dictionary = {}
var _bonus_controls: Dictionary = {}
var _workspace_support
var _paper_doll_preview
var _reload_item_id := ""
var _game_client_assets_root := ""

var _search: LineEdit
var _list: VBoxContainer
var _item_id: Label
var _display_name: LineEdit
var _icon: OptionButton
var _icon_preview: TextureRect
var _publication: Label
var _kind: Label
var _classification: Label
var _updated: Label
var _equippable: CheckBox
var _slot: OptionButton
var _required_strength: SpinBox
var _visual_key: Label
var _visual_model: Label
var _requirements: VBoxContainer
var _modifiers: VBoxContainer
var _bonus_grid: GridContainer
var _weapon_enabled: CheckBox
var _weapon_profile_id: LineEdit
var _weapon_attack_family: OptionButton
var _weapon_attack_style: OptionButton
var _weapon_min_range: SpinBox
var _weapon_max_range: SpinBox
var _weapon_speed_units: SpinBox
var _weapon_timing: Label
var _weapon_note: Label
var _tool_rows: VBoxContainer
var _operation: OptionButton
var _preview_button: Button
var _delete_button: Button
var _apply_button: Button
var _status: Label
var _changes: VBoxContainer
var _validation: VBoxContainer
var _file_dialog: FileDialog
var _import_button: Button
var _add_requirement_button: Button
var _add_modifier_button: Button
var _add_tool_button: Button
var _paper_doll_stage: Control
var _paper_doll_status: Label
var _preview_direction: OptionButton
var _preview_frame: SpinBox


func _ready() -> void:
	_workspace_support = WORKSPACE_SUPPORT_SCRIPT.new()
	_paper_doll_preview = PAPER_DOLL_PREVIEW_SCRIPT.new()
	_client = %AuthoringHostClient
	_build_ui()
	_connect_client()
	_set_form_enabled(false)


func _connect_client() -> void:
	_client.health_received.connect(_on_health_received)
	_client.item_assets_received.connect(_on_assets_received)
	_client.item_asset_imported.connect(_on_asset_imported)
	_client.hand_equipment_options_received.connect(_on_options_received)
	_client.hand_equipment_received.connect(_on_hand_equipment_received)
	_client.hand_equipment_item_received.connect(_on_hand_equipment_item_received)
	_client.hand_equipment_preview_received.connect(_on_preview_received)
	_client.hand_equipment_mutation_completed.connect(_on_mutation_completed)
	_client.request_failed.connect(_on_request_failed)


func _build_ui() -> void:
	add_theme_constant_override("separation", 14)
	var catalog_panel := _panel()
	catalog_panel.custom_minimum_size = Vector2(350, 0)
	add_child(catalog_panel)
	var catalog := VBoxContainer.new()
	catalog.add_theme_constant_override("separation", 10)
	catalog_panel.add_child(catalog)
	catalog.add_child(_heading("Weapons & Tools", 20))
	_search = LineEdit.new()
	_search.placeholder_text = "Search item ID, name, slot, weapon, or tool"
	_search.text_changed.connect(_rebuild_list.unbind(1))
	catalog.add_child(_search)
	var refresh := Button.new()
	refresh.text = "Refresh"
	refresh.pressed.connect(_client.load_hand_equipment.bind(""))
	catalog.add_child(refresh)
	var catalog_scroll := ScrollContainer.new()
	catalog_scroll.size_flags_vertical = Control.SIZE_EXPAND_FILL
	catalog.add_child(catalog_scroll)
	_list = VBoxContainer.new()
	_list.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_list.add_theme_constant_override("separation", 6)
	catalog_scroll.add_child(_list)

	var editor_panel := _panel()
	editor_panel.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	add_child(editor_panel)
	var scroll := ScrollContainer.new()
	scroll.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	scroll.size_flags_vertical = Control.SIZE_EXPAND_FILL
	editor_panel.add_child(scroll)
	var editor := VBoxContainer.new()
	editor.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	editor.add_theme_constant_override("separation", 12)
	scroll.add_child(editor)
	editor.add_child(_heading("Hand Equipment Definition", 20))

	var grid := GridContainer.new()
	grid.columns = 2
	grid.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	editor.add_child(grid)
	_item_id = _add_value_field(grid, "Stable item ID", "No item selected")
	_display_name = _add_line_field(grid, "Display name", "Select an item")
	grid.add_child(_field_label("Inventory / ground icon"))
	var icon_row := HBoxContainer.new()
	icon_row.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	grid.add_child(icon_row)
	_icon = OptionButton.new()
	_icon.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_icon.item_selected.connect(_on_form_changed.unbind(1))
	icon_row.add_child(_icon)
	_import_button = Button.new()
	_import_button.text = "Import PNG..."
	_import_button.pressed.connect(_open_import)
	icon_row.add_child(_import_button)
	_publication = _add_value_field(grid, "Publication state", "Unknown")
	_kind = _add_value_field(grid, "Authoring kind", "Unknown")
	_classification = _add_value_field(grid, "Classification", "Unknown")
	_updated = _add_value_field(grid, "Last updated", "Unknown")
	grid.add_child(_field_label("Equippable"))
	_equippable = CheckBox.new()
	_equippable.text = "Item can be equipped"
	_equippable.toggled.connect(_on_equippable_toggled)
	grid.add_child(_equippable)
	_slot = _add_option_field(grid, "Equipment slot")
	_slot.item_selected.connect(_on_slot_changed.unbind(1))
	_required_strength = _add_spin_field(grid, "Required strength", 1, 1000000, 1)
	_visual_key = _add_value_field(grid, "Derived visual key", "None")
	_visual_model = _add_value_field(grid, "Visual model", "Unknown")

	var preview_row := HBoxContainer.new()
	preview_row.add_theme_constant_override("separation", 16)
	editor.add_child(preview_row)
	_icon_preview = TextureRect.new()
	_icon_preview.custom_minimum_size = Vector2(128, 128)
	_icon_preview.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	_icon_preview.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED
	preview_row.add_child(_icon_preview)
	var visual_note := Label.new()
	visual_note.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	visual_note.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	visual_note.text = "Current runtime active weapons use right_hand. left_hand tool capabilities may be drafted; left_hand weapon publication is blocked by server validation."
	preview_row.add_child(visual_note)

	editor.add_child(_heading("Directional paper-doll preview", 16))
	var doll_row := HBoxContainer.new()
	doll_row.add_theme_constant_override("separation", 16)
	editor.add_child(doll_row)
	var doll_panel := PanelContainer.new()
	var doll_style := StyleBoxFlat.new()
	doll_style.bg_color = Color(0.045, 0.052, 0.066, 1)
	doll_style.border_color = Color(0.19, 0.22, 0.28, 1)
	doll_style.set_border_width_all(1)
	doll_style.set_corner_radius_all(6)
	doll_panel.add_theme_stylebox_override("panel", doll_style)
	doll_panel.custom_minimum_size = PAPER_DOLL_PREVIEW_SCRIPT.STAGE_SIZE
	doll_row.add_child(doll_panel)
	_paper_doll_stage = Control.new()
	_paper_doll_stage.clip_contents = true
	_paper_doll_stage.custom_minimum_size = PAPER_DOLL_PREVIEW_SCRIPT.STAGE_SIZE
	doll_panel.add_child(_paper_doll_stage)
	var doll_controls := VBoxContainer.new()
	doll_controls.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	doll_controls.add_theme_constant_override("separation", 8)
	doll_row.add_child(doll_controls)
	var direction_row := HBoxContainer.new()
	direction_row.add_child(_field_label("Direction"))
	_preview_direction = OptionButton.new()
	for direction in ["N", "S", "E", "W"]:
		_preview_direction.add_item(direction)
		_preview_direction.set_item_metadata(_preview_direction.item_count - 1, direction)
	_preview_direction.select(0)
	_preview_direction.item_selected.connect(_on_visual_preview_changed.unbind(1))
	direction_row.add_child(_preview_direction)
	doll_controls.add_child(direction_row)
	var frame_row := HBoxContainer.new()
	frame_row.add_child(_field_label("Frame"))
	_preview_frame = SpinBox.new()
	_preview_frame.min_value = 1
	_preview_frame.max_value = 4
	_preview_frame.step = 1
	_preview_frame.value = DEFAULT_PREVIEW_FRAME
	_preview_frame.value_changed.connect(_on_visual_preview_changed.unbind(1))
	frame_row.add_child(_preview_frame)
	doll_controls.add_child(frame_row)
	_paper_doll_status = Label.new()
	_paper_doll_status.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	_paper_doll_status.modulate = Color(0.7, 0.73, 0.79, 1)
	_paper_doll_status.text = "Configure game_client_assets to load the directional player preview."
	doll_controls.add_child(_paper_doll_status)
	_paper_doll_preview.bind(_paper_doll_stage, _paper_doll_status)

	var requirement_header := HBoxContainer.new()
	editor.add_child(requirement_header)
	var requirement_heading := _heading("Additional skill requirements", 16)
	requirement_heading.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	requirement_header.add_child(requirement_heading)
	_add_requirement_button = Button.new()
	_add_requirement_button.text = "+ Requirement"
	_add_requirement_button.pressed.connect(_add_requirement_row)
	requirement_header.add_child(_add_requirement_button)
	_requirements = VBoxContainer.new()
	_requirements.add_theme_constant_override("separation", 6)
	editor.add_child(_requirements)

	var modifier_header := HBoxContainer.new()
	editor.add_child(modifier_header)
	var modifier_heading := _heading("Skill modifiers while equipped", 16)
	modifier_heading.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	modifier_header.add_child(modifier_heading)
	_add_modifier_button = Button.new()
	_add_modifier_button.text = "+ Modifier"
	_add_modifier_button.pressed.connect(_add_modifier_row)
	modifier_header.add_child(_add_modifier_button)
	_modifiers = VBoxContainer.new()
	_modifiers.add_theme_constant_override("separation", 6)
	editor.add_child(_modifiers)

	editor.add_child(_heading("Combat bonuses", 16))
	_bonus_grid = GridContainer.new()
	_bonus_grid.columns = 4
	_bonus_grid.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	editor.add_child(_bonus_grid)

	editor.add_child(_heading("Weapon profile", 16))
	_weapon_enabled = CheckBox.new()
	_weapon_enabled.text = "Weapon Profile Enabled"
	_weapon_enabled.toggled.connect(_on_weapon_enabled_toggled)
	editor.add_child(_weapon_enabled)
	var weapon_grid := GridContainer.new()
	weapon_grid.columns = 2
	weapon_grid.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	editor.add_child(weapon_grid)
	_weapon_profile_id = _add_line_field(weapon_grid, "Profile ID", "gold_sword_melee")
	_weapon_attack_family = _add_option_field(weapon_grid, "Attack family")
	_weapon_attack_style = _add_option_field(weapon_grid, "Attack style")
	_weapon_min_range = _add_spin_field(weapon_grid, "Minimum range tiles", 0, 32, 1)
	_weapon_max_range = _add_spin_field(weapon_grid, "Maximum range tiles", 0, 32, 1)
	_weapon_speed_units = _add_spin_field(weapon_grid, "Attack speed units", 1, 60, 1)
	_weapon_timing = _add_value_field(weapon_grid, "Attack interval", "1 units x 600 ms = 600 ms")
	_weapon_note = Label.new()
	_weapon_note.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	_weapon_note.modulate = Color(0.7, 0.73, 0.79, 1)
	editor.add_child(_weapon_note)
	_weapon_profile_id.text_changed.connect(_on_form_changed.unbind(1))
	_weapon_min_range.value_changed.connect(_on_form_changed.unbind(1))
	_weapon_max_range.value_changed.connect(_on_form_changed.unbind(1))
	_weapon_speed_units.value_changed.connect(_on_weapon_speed_changed.unbind(1))

	var tool_header := HBoxContainer.new()
	editor.add_child(tool_header)
	var tool_heading := _heading("Tool capabilities", 16)
	tool_heading.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	tool_header.add_child(tool_heading)
	_add_tool_button = Button.new()
	_add_tool_button.text = "+ Capability"
	_add_tool_button.pressed.connect(_add_tool_row)
	tool_header.add_child(_add_tool_button)
	_tool_rows = VBoxContainer.new()
	_tool_rows.add_theme_constant_override("separation", 6)
	editor.add_child(_tool_rows)

	var operation_row := HBoxContainer.new()
	operation_row.add_theme_constant_override("separation", 8)
	editor.add_child(operation_row)
	_operation = OptionButton.new()
	for option in [["Save as Draft", "save_draft"], ["Publish", "publish"], ["Disable", "disable"], ["Delete", "delete"]]:
		_operation.add_item(option[0])
		_operation.set_item_metadata(_operation.item_count - 1, option[1])
	_operation.item_selected.connect(_on_form_changed.unbind(1))
	operation_row.add_child(_operation)
	_preview_button = Button.new()
	_preview_button.text = "Validate and Preview Changes"
	_preview_button.pressed.connect(_preview)
	operation_row.add_child(_preview_button)
	_delete_button = Button.new()
	_delete_button.text = "Delete"
	_delete_button.disabled = true
	_delete_button.pressed.connect(_preview_delete)
	operation_row.add_child(_delete_button)
	_apply_button = Button.new()
	_apply_button.text = "Apply Previewed Operation"
	_apply_button.disabled = true
	_apply_button.pressed.connect(_apply)
	operation_row.add_child(_apply_button)

	_status = Label.new()
	_status.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	_status.text = "Select an item."
	editor.add_child(_status)
	editor.add_child(_heading("Exact logical changes", 16))
	_changes = VBoxContainer.new()
	editor.add_child(_changes)
	editor.add_child(_heading("Validation", 16))
	_validation = VBoxContainer.new()
	editor.add_child(_validation)

	_file_dialog = FileDialog.new()
	_file_dialog.file_mode = FileDialog.FILE_MODE_OPEN_FILE
	_file_dialog.access = FileDialog.ACCESS_FILESYSTEM
	_file_dialog.filters = PackedStringArray(["*.png ; PNG images"])
	_file_dialog.file_selected.connect(_import_selected)
	add_child(_file_dialog)

	_display_name.text_changed.connect(_on_form_changed.unbind(1))
	_required_strength.value_changed.connect(_on_form_changed.unbind(1))
	_update_weapon_timing()


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


func _add_line_field(grid: GridContainer, label_text: String, placeholder: String) -> LineEdit:
	grid.add_child(_field_label(label_text))
	var edit := LineEdit.new()
	edit.placeholder_text = placeholder
	edit.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	grid.add_child(edit)
	return edit


func _add_value_field(grid: GridContainer, label_text: String, value: String) -> Label:
	grid.add_child(_field_label(label_text))
	var label := Label.new()
	label.text = value
	label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	grid.add_child(label)
	return label


func _add_option_field(grid: GridContainer, label_text: String) -> OptionButton:
	grid.add_child(_field_label(label_text))
	var option := OptionButton.new()
	option.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	option.item_selected.connect(_on_form_changed.unbind(1))
	grid.add_child(option)
	return option


func _add_spin_field(grid: GridContainer, label_text: String, minimum: float, maximum: float, step: float) -> SpinBox:
	grid.add_child(_field_label(label_text))
	var spin := SpinBox.new()
	spin.min_value = minimum
	spin.max_value = maximum
	spin.step = step
	spin.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	grid.add_child(spin)
	return spin


func _on_health_received(payload: Dictionary) -> void:
	_game_client_assets_root = ""
	for variant in payload.get("asset_roots", []) as Array:
		if variant is not Dictionary:
			continue
		var asset_root := variant as Dictionary
		if str(asset_root.get("id", "")) == "game_client_assets":
			_game_client_assets_root = str(asset_root.get("path", ""))
			break
	_paper_doll_preview.game_client_assets_root = _game_client_assets_root
	_paper_doll_preview.clear_cache()
	_update_paper_doll_preview()


func _on_assets_received(payload: Dictionary) -> void:
	_assets = payload.get("assets", []) as Array
	_asset_by_path.clear()
	for variant in _assets:
		if variant is Dictionary:
			var asset := variant as Dictionary
			_asset_by_path[str(asset.get("resource_path", ""))] = asset
	_rebuild_icon_options(_selected_metadata(_icon))


func _on_asset_imported(payload: Dictionary) -> void:
	var asset := payload.get("asset", {}) as Dictionary
	var resource_path := str(asset.get("resource_path", ""))
	_client.connect_and_load()
	_status.text = "Imported %s. Reloading catalogs..." % resource_path


func _on_options_received(payload: Dictionary) -> void:
	_options = payload
	var slots: Array = []
	slots.append_array(payload.get("hand_slots", []) as Array)
	slots.append_array(payload.get("wearable_slots", []) as Array)
	_fill_option(_slot, slots)
	_fill_option(_weapon_attack_family, payload.get("attack_families", []) as Array)
	_fill_option(_weapon_attack_style, payload.get("attack_styles", []) as Array)
	_rebuild_bonus_grid()


func _on_hand_equipment_received(payload: Dictionary) -> void:
	_items = payload.get("items", []) as Array
	_rebuild_list()
	if not _reload_item_id.is_empty():
		var item_id := _reload_item_id
		_reload_item_id = ""
		_client.load_hand_equipment_item(item_id)


func _on_hand_equipment_item_received(payload: Dictionary) -> void:
	_current_item = payload.duplicate(true)
	_item_id.text = str(payload.get("item_id", ""))
	_display_name.text = str(payload.get("display_name", ""))
	_publication.text = str(payload.get("publication_state", "Unknown"))
	_kind.text = str(payload.get("authoring_kind", "Unknown"))
	_classification.text = str(payload.get("classification_label", "Unknown"))
	_updated.text = str(payload.get("updated_at_utc", "Unknown"))
	_equippable.button_pressed = bool(payload.get("equippable", false))
	_select_option(_slot, str(payload.get("equipment_slot_id", "")))
	_required_strength.value = float(payload.get("required_strength", 1))
	_visual_key.text = _optional(str(payload.get("visual_asset_key", "")), "None")
	_visual_model.text = str(payload.get("visual_asset_model", "Current runtime derives visual keys."))
	_rebuild_icon_options(str(payload.get("icon_texture_path", "")))
	_clear_rows(_requirements)
	for variant in payload.get("requirements", []) as Array:
		if variant is Dictionary:
			_add_requirement_row(variant as Dictionary)
	_clear_rows(_modifiers)
	for variant in payload.get("skill_modifiers", []) as Array:
		if variant is Dictionary:
			_add_modifier_row(variant as Dictionary)
	var bonuses: Dictionary = {}
	var bonuses_variant: Variant = payload.get("combat_bonuses", null)
	if bonuses_variant is Dictionary:
		bonuses = bonuses_variant
	_apply_bonus_values(bonuses)
	_apply_weapon_profile(payload.get("weapon_profile", null))
	_clear_rows(_tool_rows)
	for variant in payload.get("tool_capabilities", []) as Array:
		if variant is Dictionary:
			_add_tool_row(variant as Dictionary)
	_update_icon_preview(str(payload.get("asset_preview_file_path", "")))
	_update_paper_doll_preview()
	_update_operation_default()
	_update_editability()
	_clear_preview()
	_status.text = _status_for_item(payload)


func _on_preview_received(payload: Dictionary) -> void:
	var operation := str(payload.get("target_operation", ""))
	var valid := bool(payload.get("valid_for_publication", false)) if operation == "publish" else bool(payload.get("valid_for_draft", false))
	_workspace_support.accept_preview(
		operation,
		str(payload.get("preview_signature", "")),
		valid,
		_apply_button,
		"Apply %s" % _workspace_support.operation_name(operation)
	)
	_workspace_support.render_changes(_changes, payload.get("changes", []) as Array)
	_workspace_support.render_validation(_validation, payload.get("messages", []) as Array)
	_update_icon_preview(str(payload.get("asset_preview_file_path", "")))
	_status.text = "Preview ready." if valid else "Preview contains blocking validation errors."


func _on_mutation_completed(payload: Dictionary) -> void:
	var operation := str(payload.get("operation", "operation"))
	if operation == "delete":
		var deleted_id := str(payload.get("deleted_id", _item_id.text))
		_reload_item_id = ""
		_clear_selection_after_delete(deleted_id)
		_status.text = "Deleted %s." % deleted_id
		_client.load_hand_equipment(_search.text)
		return
	var item := payload.get("item", {}) as Dictionary
	_reload_item_id = str(item.get("item_id", _item_id.text))
	_clear_preview()
	_status.text = "%s completed. Reloading item..." % _workspace_support.operation_name(operation)
	_client.load_hand_equipment(_search.text)


func _on_request_failed(operation: String, message: String, errors: Array) -> void:
	if not operation.begins_with("hand_equipment") and operation != "item_asset_import":
		return
	_status.text = "%s failed: %s" % [operation, message]
	_workspace_support.render_validation(_validation, errors)
	_apply_button.disabled = true


func _rebuild_list() -> void:
	_clear_rows(_list)
	var query := _search.text.strip_edges().to_lower()
	for variant in _items:
		if variant is not Dictionary:
			continue
		var item := variant as Dictionary
		var haystack := "%s %s %s %s" % [
			item.get("item_id", ""),
			item.get("display_name", ""),
			item.get("equipment_slot_id", ""),
			item.get("classification_label", ""),
		]
		if not query.is_empty() and not haystack.to_lower().contains(query):
			continue
		var button := Button.new()
		button.alignment = HORIZONTAL_ALIGNMENT_LEFT
		button.text = "%s\n%s | %s | %s | %s%s%s" % [
			str(item.get("display_name", "Unnamed item")),
			str(item.get("publication_state", "Unknown")),
			str(item.get("classification_label", item.get("authoring_kind", "Unknown"))),
			_optional(str(item.get("equipment_slot_id", "")), "not equippable"),
			str(item.get("item_id", "")),
			" | weapon" if bool(item.get("has_weapon_profile", false)) else "",
			" | tool" if bool(item.get("has_tool_capabilities", false)) else "",
		]
		if not bool(item.get("editable_in_hand_equipment", false)):
			button.text += "\nRead-only here: open the owning workspace."
		button.tooltip_text = str(item.get("item_id", ""))
		button.pressed.connect(_client.load_hand_equipment_item.bind(str(item.get("item_id", ""))))
		_list.add_child(button)


func _add_requirement_row(initial: Dictionary = {}) -> void:
	var row := HBoxContainer.new()
	row.add_theme_constant_override("separation", 6)
	var skill := OptionButton.new()
	skill.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_fill_option(skill, _options.get("skills", []) as Array)
	_select_option(skill, str(initial.get("skill_id", "")))
	row.add_child(skill)
	var value := SpinBox.new()
	value.min_value = 1
	value.max_value = 1000000
	value.value = float(initial.get("required_value", 1))
	value.custom_minimum_size = Vector2(130, 0)
	row.add_child(value)
	var remove := Button.new()
	remove.text = "Remove"
	remove.pressed.connect(_remove_row.bind(row))
	row.add_child(remove)
	row.set_meta("skill", skill)
	row.set_meta("value", value)
	skill.item_selected.connect(_on_form_changed.unbind(1))
	value.value_changed.connect(_on_form_changed.unbind(1))
	_requirements.add_child(row)
	_clear_preview()


func _add_modifier_row(initial: Dictionary = {}) -> void:
	var row := HBoxContainer.new()
	row.add_theme_constant_override("separation", 6)
	var skill := OptionButton.new()
	skill.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_fill_option(skill, _options.get("skills", []) as Array)
	_select_option(skill, str(initial.get("skill_id", "")))
	row.add_child(skill)
	var value := SpinBox.new()
	value.min_value = -1000000
	value.max_value = 1000000
	value.value = float(initial.get("modifier_value", 0))
	value.custom_minimum_size = Vector2(130, 0)
	row.add_child(value)
	var remove := Button.new()
	remove.text = "Remove"
	remove.pressed.connect(_remove_row.bind(row))
	row.add_child(remove)
	row.set_meta("skill", skill)
	row.set_meta("value", value)
	skill.item_selected.connect(_on_form_changed.unbind(1))
	value.value_changed.connect(_on_form_changed.unbind(1))
	_modifiers.add_child(row)
	_clear_preview()


func _add_tool_row(initial: Dictionary = {}) -> void:
	var row := HBoxContainer.new()
	row.add_theme_constant_override("separation", 6)
	var capability := OptionButton.new()
	capability.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_fill_option(capability, _options.get("tool_capabilities", []) as Array)
	_select_option(capability, str(initial.get("capability_id", "")))
	row.add_child(capability)
	var power := SpinBox.new()
	power.min_value = 1
	power.max_value = 1000
	power.value = float(initial.get("power_tier", 1))
	power.custom_minimum_size = Vector2(90, 0)
	row.add_child(power)
	var action := LineEdit.new()
	action.placeholder_text = "action animation ID"
	action.text = str(initial.get("action_animation_id", ""))
	action.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	row.add_child(action)
	var effect := LineEdit.new()
	effect.placeholder_text = "effect resource ID"
	effect.text = str(initial.get("effect_resource_id", ""))
	effect.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	row.add_child(effect)
	var up := Button.new()
	up.text = "Up"
	up.pressed.connect(_move_tool_row.bind(row, -1))
	row.add_child(up)
	var down := Button.new()
	down.text = "Down"
	down.pressed.connect(_move_tool_row.bind(row, 1))
	row.add_child(down)
	var remove := Button.new()
	remove.text = "Remove"
	remove.pressed.connect(_remove_row.bind(row))
	row.add_child(remove)
	row.set_meta("capability", capability)
	row.set_meta("power", power)
	row.set_meta("action", action)
	row.set_meta("effect", effect)
	capability.item_selected.connect(_on_form_changed.unbind(1))
	power.value_changed.connect(_on_form_changed.unbind(1))
	action.text_changed.connect(_on_form_changed.unbind(1))
	effect.text_changed.connect(_on_form_changed.unbind(1))
	_tool_rows.add_child(row)
	_clear_preview()


func _move_tool_row(row: Control, delta: int) -> void:
	var index := row.get_index()
	var target := clampi(index + delta, 0, _tool_rows.get_child_count() - 1)
	if target == index:
		return
	_tool_rows.move_child(row, target)
	_clear_preview()


func _remove_row(row: Control) -> void:
	var parent := row.get_parent()
	if parent != null:
		parent.remove_child(row)
	row.queue_free()
	_clear_preview()


func _rebuild_bonus_grid() -> void:
	_clear_rows(_bonus_grid)
	_bonus_controls.clear()
	for variant in _options.get("combat_bonus_fields", []) as Array:
		if variant is not Dictionary:
			continue
		var option := variant as Dictionary
		var id := str(option.get("id", ""))
		_bonus_grid.add_child(_field_label(str(option.get("display_name", id))))
		var spin := SpinBox.new()
		spin.min_value = -1000000
		spin.max_value = 1000000
		spin.value_changed.connect(_on_form_changed.unbind(1))
		_bonus_grid.add_child(spin)
		_bonus_controls[id] = spin


func _apply_bonus_values(values: Dictionary) -> void:
	for id in _bonus_controls:
		(_bonus_controls[id] as SpinBox).value = float(values.get(id, 0))


func _apply_weapon_profile(profile_variant: Variant) -> void:
	var has_profile := profile_variant is Dictionary
	_weapon_enabled.button_pressed = has_profile
	var profile := profile_variant as Dictionary if has_profile else {}
	_weapon_profile_id.text = str(profile.get("profile_id", ""))
	_select_option(_weapon_attack_family, str(profile.get("attack_type", "melee")))
	_select_option(_weapon_attack_style, str(profile.get("accuracy_style", "slash")))
	_weapon_min_range.value = float(profile.get("minimum_range_tiles", 1))
	_weapon_max_range.value = float(profile.get("maximum_range_tiles", 1))
	_weapon_speed_units.value = float(profile.get("attack_speed_units", 4))
	_update_weapon_timing()


func _collect_requirements() -> Array:
	var values: Array = []
	for row in _requirements.get_children():
		if row.has_meta("skill"):
			values.append({"skill_id": _selected_metadata(row.get_meta("skill") as OptionButton), "required_value": int((row.get_meta("value") as SpinBox).value)})
	return values


func _collect_modifiers() -> Array:
	var values: Array = []
	for row in _modifiers.get_children():
		if row.has_meta("skill"):
			values.append({"skill_id": _selected_metadata(row.get_meta("skill") as OptionButton), "modifier_value": int((row.get_meta("value") as SpinBox).value)})
	return values


func _collect_bonuses() -> Dictionary:
	var values: Dictionary = {}
	for id in _bonus_controls:
		values[id] = int((_bonus_controls[id] as SpinBox).value)
	return values


func _collect_tool_capabilities() -> Array:
	var values: Array = []
	for row in _tool_rows.get_children():
		if row.has_meta("capability"):
			values.append({
				"capability_id": _selected_metadata(row.get_meta("capability") as OptionButton),
				"power_tier": int((row.get_meta("power") as SpinBox).value),
				"action_animation_id": _optional_payload((row.get_meta("action") as LineEdit).text),
				"effect_resource_id": _optional_payload((row.get_meta("effect") as LineEdit).text),
			})
	return values


func _weapon_profile_payload() -> Variant:
	if not _weapon_enabled.button_pressed or not _is_hand_slot(_selected_metadata(_slot)):
		return null
	return {
		"profile_id": _weapon_profile_id.text.strip_edges(),
		"attack_type": _selected_metadata(_weapon_attack_family),
		"accuracy_style": _selected_metadata(_weapon_attack_style),
		"minimum_range_tiles": int(_weapon_min_range.value),
		"maximum_range_tiles": int(_weapon_max_range.value),
		"attack_speed_units": int(_weapon_speed_units.value),
	}


func _payload() -> Dictionary:
	var equippable := _equippable.button_pressed
	var slot_id := _selected_metadata(_slot) if equippable else ""
	var hand_slot := _is_hand_slot(slot_id)
	return {
		"display_name": _display_name.text,
		"icon_texture_path": _selected_metadata(_icon),
		"equippable": equippable,
		"equipment_slot_id": slot_id if equippable else null,
		"required_strength": int(_required_strength.value) if equippable else 1,
		"requirements": _collect_requirements() if equippable else [],
		"skill_modifiers": _collect_modifiers() if equippable else [],
		"weapon_profile": _weapon_profile_payload() if equippable and hand_slot else null,
		"combat_bonuses": _collect_bonuses() if equippable else _zero_bonuses(),
		"tool_capabilities": _collect_tool_capabilities() if equippable and hand_slot else [],
		"expected_updated_at_utc": _current_item.get("updated_at_utc", null),
	}


func _preview() -> void:
	if _item_id.text.is_empty():
		_status.text = "Select an item before previewing."
		return
	var payload := _payload()
	payload["target_operation"] = _selected_metadata(_operation)
	_client.preview_hand_equipment(_item_id.text, payload)
	_status.text = "Calculating validation and exact database changes..."


func _preview_delete() -> void:
	if _current_item.is_empty():
		_status.text = "Select a saved hand-equipment item before deleting."
		return
	_select_option(_operation, "delete")
	_preview()


func _apply() -> void:
	var operation := _selected_metadata(_operation)
	var preview_signature: String = _workspace_support.preview_signature
	if not _workspace_support.can_apply(operation, preview_signature):
		_status.text = "The form changed. Preview the operation again before applying it."
		_apply_button.disabled = true
		return
	var expected: Variant = _current_item.get("updated_at_utc", null)
	match operation:
		"publish":
			_client.publish_hand_equipment(_item_id.text, expected, preview_signature)
		"disable":
			_client.disable_hand_equipment(_item_id.text, expected, preview_signature)
		"delete":
			_client.delete_hand_equipment(_item_id.text, expected, preview_signature)
		_:
			var payload := _payload()
			payload["preview_signature"] = preview_signature
			_client.save_hand_equipment_draft(_item_id.text, payload)
	_apply_button.disabled = true
	_status.text = "Applying transactional hand-equipment operation..."


func _on_equippable_toggled(_value: bool) -> void:
	if not _value:
		_select_option(_operation, "save_draft")
	_update_editability()
	_on_form_changed()


func _on_slot_changed() -> void:
	_update_editability()
	_on_form_changed()


func _on_weapon_enabled_toggled(_value: bool) -> void:
	_update_editability()
	_on_form_changed()


func _on_weapon_speed_changed() -> void:
	_update_weapon_timing()
	_on_form_changed()


func _update_editability() -> void:
	var enabled := bool(_current_item.get("editable_in_hand_equipment", false))
	_set_form_enabled(enabled)
	var metadata_enabled := enabled and _equippable.button_pressed
	var slot_id := _selected_metadata(_slot)
	var hand_slot := metadata_enabled and _is_hand_slot(slot_id)
	var weapon_enabled := hand_slot and _weapon_enabled.button_pressed
	_required_strength.editable = metadata_enabled
	_add_requirement_button.disabled = not metadata_enabled
	_add_modifier_button.disabled = not metadata_enabled
	for child in _requirements.get_children():
		_set_dynamic_row_enabled(child, metadata_enabled)
	for child in _modifiers.get_children():
		_set_dynamic_row_enabled(child, metadata_enabled)
	for id in _bonus_controls:
		(_bonus_controls[id] as SpinBox).editable = metadata_enabled
	_weapon_enabled.disabled = not hand_slot
	_weapon_profile_id.editable = weapon_enabled
	_weapon_attack_family.disabled = not weapon_enabled
	_weapon_attack_style.disabled = not weapon_enabled
	_weapon_min_range.editable = weapon_enabled
	_weapon_max_range.editable = weapon_enabled
	_weapon_speed_units.editable = weapon_enabled
	_add_tool_button.disabled = not hand_slot
	for child in _tool_rows.get_children():
		_set_tool_row_enabled(child, hand_slot)
	_update_guidance()


func _set_form_enabled(enabled: bool) -> void:
	_display_name.editable = enabled
	_icon.disabled = not enabled
	_import_button.disabled = not enabled
	_equippable.disabled = not enabled
	_slot.disabled = not enabled
	_operation.disabled = not enabled
	_preview_button.disabled = not enabled
	_delete_button.disabled = not enabled or _current_item.is_empty()
	if not enabled:
		_apply_button.disabled = true


func _set_dynamic_row_enabled(row: Node, enabled: bool) -> void:
	if row.has_meta("skill"):
		(row.get_meta("skill") as OptionButton).disabled = not enabled
	if row.has_meta("value"):
		(row.get_meta("value") as SpinBox).editable = enabled
	for child in row.get_children():
		if child is Button:
			(child as Button).disabled = not enabled


func _set_tool_row_enabled(row: Node, enabled: bool) -> void:
	if row.has_meta("capability"):
		(row.get_meta("capability") as OptionButton).disabled = not enabled
	if row.has_meta("power"):
		(row.get_meta("power") as SpinBox).editable = enabled
	if row.has_meta("action"):
		(row.get_meta("action") as LineEdit).editable = enabled
	if row.has_meta("effect"):
		(row.get_meta("effect") as LineEdit).editable = enabled
	for child in row.get_children():
		if child is Button:
			(child as Button).disabled = not enabled


func _status_for_item(payload: Dictionary) -> String:
	if bool(payload.get("editable_in_hand_equipment", false)):
		return "Edit hand equipment, weapon profile, and tool capabilities. Server validation remains authoritative."
	if str(payload.get("authoring_kind", "")) == "Consumable":
		return "Consumables must be edited in the Consumables workspace."
	return "This item is read-only here."


func _update_guidance() -> void:
	if not _equippable.button_pressed:
		_weapon_note.text = "Not equippable: preview will clear slot, requirements, modifiers, weapon profile, combat bonuses, and tool capabilities."
		return
	var slot_id := _selected_metadata(_slot)
	if not _is_hand_slot(slot_id):
		_weapon_note.text = "Wearable slot selected: preview will remove hand-only weapon profile and tool capability rows."
	elif slot_id == "left_hand":
		_weapon_note.text = "left_hand tools can be drafted. Current runtime blocks left_hand weapon profile publication."
	else:
		_weapon_note.text = "right_hand supports current active weapon publication when a valid weapon profile is present."


func _update_operation_default() -> void:
	var state := str(_current_item.get("publication_state", "Draft"))
	_operation.select(2 if state == "Published" else 0)


func _clear_selection_after_delete(_deleted_id: String) -> void:
	_current_item = {}
	_item_id.text = ""
	_display_name.text = ""
	_publication.text = "No item selected"
	_kind.text = "Unknown"
	_classification.text = "Unknown"
	_updated.text = "Unknown"
	_equippable.button_pressed = false
	_select_option(_slot, "")
	_required_strength.value = 1
	_visual_key.text = "None"
	_visual_model.text = "Current runtime derives visual keys."
	_clear_rows(_requirements)
	_clear_rows(_modifiers)
	_apply_bonus_values({})
	_apply_weapon_profile(null)
	_clear_rows(_tool_rows)
	_update_icon_preview("")
	_update_paper_doll_preview()
	_set_form_enabled(false)
	_clear_preview()


func _open_import() -> void:
	_file_dialog.popup_centered_ratio(0.75)


func _import_selected(path: String) -> void:
	_client.import_item_asset(path, path.get_file())
	_status.text = "Importing PNG into the canonical item asset directory..."


func _rebuild_icon_options(selected_path: String) -> void:
	_icon.clear()
	for variant in _assets:
		if variant is Dictionary:
			var asset := variant as Dictionary
			_icon.add_item(str(asset.get("display_name", asset.get("resource_path", "Icon"))))
			_icon.set_item_metadata(_icon.item_count - 1, str(asset.get("resource_path", "")))
	_select_option(_icon, selected_path)
	_update_icon_preview()


func _update_icon_preview(explicit_file_path: String = "") -> void:
	_icon_preview.texture = null
	var file_path := explicit_file_path
	if file_path.is_empty():
		var asset := _asset_by_path.get(_selected_metadata(_icon), {}) as Dictionary
		file_path = str(asset.get("file_path", ""))
	if file_path.is_empty() or not FileAccess.file_exists(file_path):
		return
	var image := Image.load_from_file(file_path)
	if image == null or image.is_empty():
		return
	_icon_preview.texture = ImageTexture.create_from_image(image)


func _on_form_changed() -> void:
	_clear_preview()
	_update_icon_preview()
	_visual_key.text = _derive_visual_key()
	_update_paper_doll_preview()


func _derive_visual_key() -> String:
	if not _equippable.button_pressed:
		return "None"
	return _paper_doll_preview.normalize_visual_key(_display_name.text.strip_edges())


func _on_visual_preview_changed() -> void:
	_update_paper_doll_preview()


func _update_paper_doll_preview() -> void:
	var direction := _selected_metadata(_preview_direction)
	if direction.is_empty():
		direction = DEFAULT_PREVIEW_DIRECTION
	_paper_doll_preview.update(
		_equippable.button_pressed,
		_selected_metadata(_slot) if _equippable.button_pressed else "",
		_derive_visual_key(),
		direction,
		int(_preview_frame.value),
		HAND_VISIBLE_SLOTS
	)


func _update_weapon_timing() -> void:
	var units := int(_weapon_speed_units.value) if _weapon_speed_units != null else 1
	_weapon_timing.text = "%d units x %d ms = %d ms" % [
		units,
		COMBAT_UNIT_MILLISECONDS,
		units * COMBAT_UNIT_MILLISECONDS,
	]


func _clear_preview() -> void:
	_workspace_support.clear_preview(_apply_button, _changes, _validation)


func _fill_option(control: OptionButton, values: Array) -> void:
	var selected := _selected_metadata(control)
	control.clear()
	for variant in values:
		if variant is Dictionary:
			var option := variant as Dictionary
			control.add_item(str(option.get("display_name", option.get("id", "Option"))))
			control.set_item_metadata(control.item_count - 1, str(option.get("id", "")))
	if control.item_count > 0:
		_select_option(control, selected)


func _select_option(control: OptionButton, id: String) -> void:
	for index in range(control.item_count):
		if str(control.get_item_metadata(index)) == id:
			control.select(index)
			return
	if control.item_count > 0:
		control.select(0)


func _selected_metadata(control: OptionButton) -> String:
	return "" if control.selected < 0 else str(control.get_item_metadata(control.selected))


func _zero_bonuses() -> Dictionary:
	var values: Dictionary = {}
	for variant in _options.get("combat_bonus_fields", []) as Array:
		if variant is Dictionary:
			values[str((variant as Dictionary).get("id", ""))] = 0
	return values


func _optional(value: String, fallback: String) -> String:
	return fallback if value.strip_edges().is_empty() else value


func _optional_payload(value: String) -> Variant:
	var trimmed := value.strip_edges()
	return null if trimmed.is_empty() else trimmed


func _is_hand_slot(value: String) -> bool:
	return value == "right_hand" or value == "left_hand"


func _clear_rows(container: Node) -> void:
	for child in container.get_children():
		container.remove_child(child)
		child.queue_free()
