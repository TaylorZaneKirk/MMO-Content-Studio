# Authors scalar ordinary combat spells through the existing host client.
# Owns the form and preview; the host validates and commits durable content.
extends HBoxContainer
class_name MagicSpellEditor

const STUDIO_THEME := preload("res://scripts/studio_theme.gd")

const WORKSPACE_SUPPORT := preload("res://scripts/authoring_workspace_support.gd")
@onready var _client: AuthoringHostClient = %AuthoringHostClient
var _support := WORKSPACE_SUPPORT.new()
var _current: Dictionary = {}
var _fields: Dictionary = {}
var _frames: Dictionary = {}
var _visuals: Dictionary = {}
var _preview_textures: Dictionary = {}
var _preview_elapsed := 0.0
var _game_assets_root := ""
var _list: ItemList
var _search: LineEdit
var _definition_id: LineEdit
var _state: Label
var _changes: VBoxContainer
var _validation: VBoxContainer
var _operation: OptionButton
var _preview: Button
var _apply: Button
var _status: Label
var _form: VBoxContainer
var _matter_fields: VBoxContainer
var _fire_fields: VBoxContainer
var _water_fields: VBoxContainer
var _force_fields: VBoxContainer
var _matter_presentation_fields: VBoxContainer
var _fire_presentation_fields: VBoxContainer
var _water_presentation_fields: VBoxContainer
var _loading := false
var _preview_request: Dictionary = {}
var _pending_definition_id := ""


func _ready() -> void:
	_build_ui()
	_client.spell_options_received.connect(func(payload: Dictionary): _game_assets_root = str(payload.get("game_assets_root", "")))
	_client.spell_catalog_received.connect(_on_catalog)
	_client.spell_definition_received.connect(_on_definition)
	_client.spell_preview_received.connect(_on_preview)
	_client.spell_mutation_completed.connect(_on_mutation)
	_client.request_failed.connect(_on_failed)
	_form.visible = false


func _build_ui() -> void:
	theme = STUDIO_THEME.item_theme()
	add_theme_constant_override("separation", 14)
	var catalog := _panel(240)
	_heading(catalog, "Spell library", 20)
	_search = LineEdit.new()
	_search.placeholder_text = "Search spells…"
	catalog.add_child(_search)
	_search.text_submitted.connect(func(value: String): _client.load_spells(value))
	_button(catalog, "+ New spell", _new_definition)
	_button(catalog, "Search / reload library", func(): _client.load_spells(_search.text))
	_list = ItemList.new()
	_list.size_flags_vertical = Control.SIZE_EXPAND_FILL
	_list.add_theme_color_override("font_color", Color("e5edf5"))
	_list.add_theme_stylebox_override("panel", STUDIO_THEME.box("101923", "354355"))
	_list.add_theme_stylebox_override("selected", STUDIO_THEME.box("29443f", "66d9c1"))
	_list.add_theme_stylebox_override("selected_focus", STUDIO_THEME.box("29443f", "66d9c1"))
	_list.add_theme_constant_override("v_separation", 12)
	catalog.add_child(_list)
	_list.item_selected.connect(func(index: int):
		_invalidate()
		_client.load_spell(str(_list.get_item_metadata(index))))

	var editor := _panel()
	editor.get_parent().size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_heading(editor, "Spell details", 22)
	_note(editor, "Author combat spells and explicit techniques.")
	_form = VBoxContainer.new()
	_form.size_flags_vertical = Control.SIZE_EXPAND_FILL
	editor.add_child(_form)
	var pages := TabContainer.new()
	pages.size_flags_vertical = Control.SIZE_EXPAND_FILL
	pages.use_hidden_tabs_for_min_size = false
	_form.add_child(pages)
	var basics := _page(pages, "Basics", "Spell identity", "Spell damage, requirements, shard cost and cast XP are authored here.")
	_definition_id = _text_field("Spell ID", basics)
	_definition_id.placeholder_text = "lowercase_spell_id"
	_state = _label(basics, "Draft")
	_state.modulate = Color("91ecd7")
	_label(basics, "Cast mode")
	var cast_mode := OptionButton.new()
	for value: String in ["selected_combat", "explicit_technique"]:
		cast_mode.add_item(value.replace("_", " ").capitalize())
		cast_mode.set_item_metadata(cast_mode.item_count - 1, value)
	basics.add_child(cast_mode)
	cast_mode.item_selected.connect(_invalidate)
	_fields["cast_mode"] = cast_mode
	_label(basics, "Target mode")
	var target_mode := OptionButton.new()
	for value: String in ["mob", "tile", "physical"]:
		target_mode.add_item(value.capitalize())
		target_mode.set_item_metadata(target_mode.item_count - 1, value)
	basics.add_child(target_mode)
	target_mode.item_selected.connect(_invalidate)
	_fields["target_mode"] = target_mode
	_fields["display_name"] = _text_field("Display name", basics)
	_fields["tier"] = _number_field(basics, "Tier", 1, 4, 1)
	_label(basics, "Element")
	var element := OptionButton.new()
	for value: String in ["air", "earth", "fire", "water"]:
		element.add_item(value.capitalize())
		element.set_item_metadata(element.item_count - 1, value)
	basics.add_child(element)
	element.item_selected.connect(_invalidate)
	_fields["element"] = element
	_fields["required_magic_level"] = _number_field(basics, "Required Magic level", 1, 99, 1)
	_fields["shard_cost"] = _number_field(basics, "Crystal Shard cost", 1, 2147483647, 1)
	_fields["successful_hit_min_damage"] = _number_field(basics, "Successful-hit minimum damage", 0, 2147483647, 0)
	_fields["base_max_hit"] = _number_field(basics, "Base maximum hit", 0, 2147483647, 0)
	_fields["base_cast_xp_tenths"] = _number_field(basics, "Base cast XP (tenths; 15 = 1.5 XP)", 0, 2147483647, 0)

	_label(basics, "Impact effect")
	var effect := OptionButton.new()
	effect.add_item("None")
	effect.set_item_metadata(0, null)
	effect.add_item("Air displacement")
	effect.set_item_metadata(1, "air_displacement")
	effect.add_item("Earth matter")
	effect.set_item_metadata(2, "earth_matter")
	effect.add_item("Burning terrain")
	effect.set_item_metadata(3, "burning_terrain")
	effect.add_item("Slippery terrain")
	effect.set_item_metadata(4, "slippery_terrain")
	basics.add_child(effect)
	_fields["impact_effect"] = effect
	_force_fields = VBoxContainer.new()
	basics.add_child(_force_fields)
	_fields["force"] = _number_field(_force_fields, "Base force", 1, 2147483647, 1)
	_fields["force_falloff_per_tile"] = _number_field(_force_fields, "Force falloff / tile after first", 0, 2147483647, 0)
	_fields["max_displacement_tiles"] = _number_field(_force_fields, "Base maximum displacement", 1, 2147483647, 1)
	var mastery_heading := Label.new()
	mastery_heading.text = "Gust/Air force mastery (all 0 = fixed force; otherwise fill all six)"
	_force_fields.add_child(mastery_heading)
	_fields["force_mastery_magic_levels_per_step"] = _number_field(_force_fields, "Effective Magic levels / force step", 0, 2147483647, 0)
	_fields["force_mastery_force_per_step"] = _number_field(_force_fields, "Force gained / step", 0, 2147483647, 0)
	_fields["force_mastery_max_force"] = _number_field(_force_fields, "Maximum mastered force", 0, 2147483647, 0)
	_fields["displacement_mastery_magic_levels_per_step"] = _number_field(_force_fields, "Base Magic levels / displacement step", 0, 2147483647, 0)
	_fields["displacement_mastery_tiles_per_step"] = _number_field(_force_fields, "Displacement tiles gained / step", 0, 2147483647, 0)
	_fields["displacement_mastery_max_tiles"] = _number_field(_force_fields, "Maximum mastered displacement", 0, 2147483647, 0)
	_matter_fields = VBoxContainer.new()
	basics.add_child(_matter_fields)
	_fields["manifestation_base_success_percent"] = _number_field(_matter_fields, "Base manifestation success %", 0, 100, 55)
	_fields["manifestation_magic_levels_per_step"] = _number_field(_matter_fields, "Magic levels / manifestation step", 1, 2147483647, 5)
	_fields["manifestation_success_percent_per_step"] = _number_field(_matter_fields, "Success % / step", 0, 100, 7)
	_fields["matter_lifetime_milliseconds"] = _number_field(_matter_fields, "Matter lifetime (milliseconds)", 1, 2147483647, 20000)
	_fields["matter_capacity_magic_levels_per_step"] = _number_field(_matter_fields, "Base Magic levels / capacity step", 1, 2147483647, 15)
	_fields["matter_physical_weight"] = _number_field(_matter_fields, "Physical weight (0 = unset)", 0, 2147483647, 0)
	_fields["matter_max_active"] = _number_field(_matter_fields, "Maximum active matter", 1, 2147483647, 3)
	_fire_fields = VBoxContainer.new()
	basics.add_child(_fire_fields)
	_fields["ignition_base_success_percent"] = _number_field(_fire_fields, "Base ignition success %", 0, 100, 55)
	_fields["ignition_magic_levels_per_step"] = _number_field(_fire_fields, "Effective Magic levels / success step", 1, 2147483647, 5)
	_fields["ignition_success_percent_per_step"] = _number_field(_fire_fields, "Success % / step", 0, 100, 7)
	_fields["burning_lifetime_milliseconds"] = _number_field(_fire_fields, "Burning lifetime (ms)", 1, 2147483647, 10000)
	_fields["burning_capacity_magic_levels_per_step"] = _number_field(_fire_fields, "Base Magic levels / capacity step", 1, 2147483647, 20)
	_fields["burning_max_active"] = _number_field(_fire_fields, "Maximum active fire", 1, 2147483647, 3)
	_fields["burning_min_damage"] = _number_field(_fire_fields, "Minimum environmental Fire damage", 1, 2147483647, 1)
	_fields["burning_max_damage"] = _number_field(_fire_fields, "Maximum environmental Fire damage", 1, 2147483647, 2)
	_fields["burning_hazard_cooldown_milliseconds"] = _number_field(_fire_fields, "Per-Mob Fire hazard cooldown (ms)", 1, 2147483647, 2400)
	_water_fields = VBoxContainer.new()
	basics.add_child(_water_fields)
	_fields["slick_base_success_percent"] = _number_field(_water_fields, "Base slick manifestation success %", 0, 100, 55)
	_fields["slick_magic_levels_per_step"] = _number_field(_water_fields, "Effective Magic levels / success step", 1, 2147483647, 5)
	_fields["slick_success_percent_per_step"] = _number_field(_water_fields, "Success % / step", 0, 100, 7)
	_fields["slick_lifetime_milliseconds"] = _number_field(_water_fields, "Slick lifetime (ms)", 1, 2147483647, 15000)
	_fields["slick_capacity_magic_levels_per_step"] = _number_field(_water_fields, "Base Magic levels / capacity step", 1, 2147483647, 20)
	_fields["slick_max_active"] = _number_field(_water_fields, "Maximum active slicks", 1, 2147483647, 3)

	var presentation := _page(pages, "Presentation", "Spell presentation", "Optional game assets. Choose the direction the source projectile art faces when rotation is enabled. Zero FPS/scale means unset.")
	_fields["icon_texture_path"] = _text_field("Spellbook icon (res://assets/...png)", presentation)
	_add_visual(presentation, "icon")
	for phase: String in ["projectile", "impact", "splash"]:
		_heading(presentation, phase.capitalize(), 18)
		var rows := VBoxContainer.new()
		presentation.add_child(rows)
		_frames[phase] = rows
		_button(presentation, "+ Add frame", func(): _add_frame(phase, ""); _invalidate())
		var fps := _number_field(presentation, "Animation FPS", 0, 1000, 0)
		fps.step = 0.01
		_fields[phase + "_animation_fps"] = fps
		var render_scale := _number_field(presentation, "Render scale", 0, 1000, 0)
		render_scale.step = 0.01
		_fields[phase + "_render_scale"] = render_scale
		if phase == "projectile":
			var rotates := CheckBox.new()
			rotates.text = "Rotate toward target"
			rotates.button_pressed = true
			presentation.add_child(rotates)
			rotates.toggled.connect(_invalidate)
			_fields["projectile_rotates_to_travel"] = rotates
			_label(presentation, "Source art faces")
			var facing := OptionButton.new()
			for direction: String in ["right", "down", "left", "up"]:
				facing.add_item(direction.capitalize())
				facing.set_item_metadata(facing.item_count - 1, direction)
			presentation.add_child(facing)
			facing.item_selected.connect(_invalidate)
			_fields["projectile_source_facing"] = facing
			var homing := CheckBox.new()
			homing.text = "Visually home toward moving target"
			presentation.add_child(homing)
			_fields["projectile_homing_enabled"] = homing
			var strength := _number_field(presentation, "Homing strength (1.0 tracks throughout flight; lower bends later)", 0, 1, 1)
			strength.min_value = 0.01
			strength.step = 0.01
			strength.editable = false
			_fields["projectile_homing_strength"] = strength
			homing.toggled.connect(func(enabled: bool): strength.editable = enabled; _invalidate())
		_fields["cast_sound_path" if phase == "projectile" else phase + "_sound_path"] = _text_field("Cast sound" if phase == "projectile" else phase.capitalize() + " sound", presentation)
		_add_visual(presentation, phase)
	_matter_presentation_fields = VBoxContainer.new()
	presentation.add_child(_matter_presentation_fields)
	_heading(_matter_presentation_fields, "Persistent Earth matter", 18)
	_fields["matter_visual_texture_path"] = _text_field("Persistent matter PNG (res://assets/...png)", _matter_presentation_fields)
	var matter_scale := _number_field(_matter_presentation_fields, "Persistent matter render scale (0 = unset)", 0, 1000, 0)
	matter_scale.step = 0.01
	_fields["matter_visual_render_scale"] = matter_scale
	_button(_matter_presentation_fields, "Refresh matter preview", _refresh_visuals)
	_add_visual(_matter_presentation_fields, "matter")
	_fire_presentation_fields = VBoxContainer.new()
	presentation.add_child(_fire_presentation_fields)
	_heading(_fire_presentation_fields, "Persistent Fire animation", 18)
	var fire_frames := VBoxContainer.new()
	_fire_presentation_fields.add_child(fire_frames)
	_frames["burning_visual"] = fire_frames
	_button(_fire_presentation_fields, "+ Add Fire frame", func(): _add_frame("burning_visual", ""); _invalidate())
	var fire_fps := _number_field(_fire_presentation_fields, "Persistent Fire animation FPS (0 = unset)", 0, 1000, 0)
	fire_fps.step = 0.01
	_fields["burning_visual_animation_fps"] = fire_fps
	var fire_scale := _number_field(_fire_presentation_fields, "Persistent Fire render scale (0 = unset)", 0, 1000, 0)
	fire_scale.step = 0.01
	_fields["burning_visual_render_scale"] = fire_scale
	_button(_fire_presentation_fields, "Refresh Fire preview", _refresh_visuals)
	_add_visual(_fire_presentation_fields, "burning_visual")
	_water_presentation_fields = VBoxContainer.new()
	presentation.add_child(_water_presentation_fields)
	_heading(_water_presentation_fields, "Persistent slick animation", 18)
	var water_frames := VBoxContainer.new()
	_water_presentation_fields.add_child(water_frames)
	_frames["slick_visual"] = water_frames
	_button(_water_presentation_fields, "+ Add slick frame", func(): _add_frame("slick_visual", ""); _invalidate())
	var water_fps := _number_field(_water_presentation_fields, "Persistent slick animation FPS (0 = unset)", 0, 1000, 0)
	water_fps.step = 0.01
	_fields["slick_visual_animation_fps"] = water_fps
	var water_scale := _number_field(_water_presentation_fields, "Persistent slick render scale (0 = unset)", 0, 1000, 0)
	water_scale.step = 0.01
	_fields["slick_visual_render_scale"] = water_scale
	_button(_water_presentation_fields, "Refresh slick preview", _refresh_visuals)
	_add_visual(_water_presentation_fields, "slick_visual")
	_button(presentation, "Refresh visual previews", _refresh_visuals)
	effect.item_selected.connect(func(_index: int):
		_show_impact_sections(effect.selected)
		_invalidate())
	_show_impact_sections(effect.selected)

	var review := _panel(264)
	_heading(review, "Review & apply", 20)
	_label(review, "Operation")
	_operation = OptionButton.new()
	for operation: String in ["save_draft", "save_and_publish", "publish", "disable", "delete"]:
		_operation.add_item(_support.operation_name(operation))
		_operation.set_item_metadata(_operation.item_count - 1, operation)
	_operation.item_selected.connect(_invalidate)
	review.add_child(_operation)
	_preview = _button(review, "1. Preview changes", func(): _request_preview(str(_operation.get_selected_metadata())))
	_preview.theme_type_variation = "PrimaryButton"
	_preview.disabled = true
	_apply = _button(review, "2. Apply changes", _apply_preview)
	_apply.disabled = true
	_status = _label(review, "Select a Spell or create a draft to begin.")
	_status.modulate = Color("a9b8c9")
	var scroll := ScrollContainer.new()
	scroll.size_flags_vertical = Control.SIZE_EXPAND_FILL
	scroll.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	review.add_child(scroll)
	var feedback := VBoxContainer.new()
	feedback.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	feedback.add_theme_constant_override("separation", 12)
	scroll.add_child(feedback)
	_heading(feedback, "Validation", 16)
	_validation = VBoxContainer.new()
	feedback.add_child(_validation)
	_heading(feedback, "Changes", 16)
	_changes = VBoxContainer.new()
	feedback.add_child(_changes)


func _panel(width: float = 0) -> VBoxContainer:
	var panel := PanelContainer.new()
	panel.custom_minimum_size.x = width
	var style := STUDIO_THEME.box("17212e", "354355")
	style.content_margin_left = 16
	style.content_margin_right = 16
	style.content_margin_top = 14
	style.content_margin_bottom = 14
	panel.add_theme_stylebox_override("panel", style)
	add_child(panel)
	var content := VBoxContainer.new()
	content.add_theme_constant_override("separation", 12)
	panel.add_child(content)
	return content


func _heading(parent: Node, value: String, size: int) -> void:
	_label(parent, value).add_theme_font_size_override("font_size", size)


func _note(parent: Node, value: String) -> void:
	_label(parent, value).modulate = Color("a9b8c9")


func _page(pages: TabContainer, title: String, heading: String, description: String) -> VBoxContainer:
	var scroll := ScrollContainer.new()
	scroll.name = title
	scroll.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	pages.add_child(scroll)
	var page := VBoxContainer.new()
	page.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	page.add_theme_constant_override("separation", 12)
	scroll.add_child(page)
	_heading(page, heading, 20)
	_note(page, description)
	return page


func _label(parent: Node, value: String) -> Label:
	var label := Label.new()
	label.text = value
	label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	parent.add_child(label)
	return label


func _button(parent: Node, caption: String, action: Callable) -> Button:
	var button := Button.new()
	button.text = caption
	button.pressed.connect(action)
	parent.add_child(button)
	return button


func _text_field(caption: String, parent: Node) -> LineEdit:
	_label(parent, caption)
	var field := LineEdit.new()
	field.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	parent.add_child(field)
	field.text_changed.connect(_invalidate)
	return field


func _number_field(parent: Node, caption: String, minimum: int, maximum: int, value: int) -> SpinBox:
	var field := VBoxContainer.new()
	field.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	field.add_theme_constant_override("separation", 6)
	parent.add_child(field)
	_label(field, caption).autowrap_mode = TextServer.AUTOWRAP_OFF
	var number := SpinBox.new()
	number.custom_minimum_size = Vector2(160, 40)
	number.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	number.min_value = minimum
	number.max_value = maximum
	number.step = 1
	number.value = value
	field.add_child(number)
	number.value_changed.connect(_invalidate)
	return number


func open_resource(spell_id: String) -> void:
	_invalidate()
	_client.load_spell(spell_id)


func _on_catalog(payload: Dictionary) -> void:
	_list.clear()
	for item: Dictionary in payload.get("items", []):
		var index := _list.add_item(str(item["display_name"]))
		_list.set_item_metadata(index, item["spell_id"])
		_list.set_item_tooltip(index, "%s\n%s" % [item["spell_id"], item["publication_state"]])
		if item["spell_id"] == _current.get("spell_id", ""): _list.select(index)


func _new_definition() -> void:
	_on_definition({"spell_id": "", "publication_state": "Draft", "draft": {"display_name": "", "tier": 1, "element": "air", "required_magic_level": 1, "shard_cost": 1, "successful_hit_min_damage": 0, "base_max_hit": 0, "base_cast_xp_tenths": 0}})


func _on_definition(payload: Dictionary) -> void:
	_loading = true
	_current = payload
	_list.deselect_all()
	for index in _list.item_count:
		if _list.get_item_metadata(index) == payload.get("spell_id", ""): _list.select(index)
	_definition_id.text = str(payload.get("spell_id", ""))
	_definition_id.editable = payload.get("updated_at_utc") == null
	_state.text = str(payload.get("publication_state", "Draft"))
	_operation.select(1 if _state.text == "Published" else 0)
	var draft: Dictionary = payload.get("draft", {})
	for key: String in _fields:
		var control: Control = _fields[key]
		if control is LineEdit or control is TextEdit: control.text = str(draft.get(key, "")) if draft.get(key) != null else ""
		elif control is OptionButton:
			if key in ["projectile_source_facing", "cast_mode", "impact_effect", "target_mode"]: control.select(0)
			for index in control.item_count:
				if control.get_item_metadata(index) == draft.get(key, "right" if key == "projectile_source_facing" else null): control.select(index)
		elif control is CheckBox: control.button_pressed = bool(draft.get(key, key != "projectile_homing_enabled"))
		elif control is SpinBox: control.value = float(draft.get(key, 1.0 if key == "projectile_homing_strength" else 0.0)) if draft.get(key) != null else 0.0
	(_fields["projectile_homing_strength"] as SpinBox).editable = (_fields["projectile_homing_enabled"] as CheckBox).button_pressed
	for phase: String in _frames:
		_support.clear_container(_frames[phase])
		var paths: Array = draft.get(phase + "_frames", []) if draft.get(phase + "_frames") != null else []
		for path: String in paths: _add_frame(phase, path)
	_show_impact_sections((_fields["impact_effect"] as OptionButton).selected)
	_loading = false
	_refresh_visuals()
	_form.visible = true
	_preview.disabled = false
	_status.text = "Editing %s." % payload.get("spell_id", "") if not str(payload.get("spell_id", "")).is_empty() else "New draft. Choose a stable definition ID."
	_invalidate()


func _show_impact_sections(effect_index: int) -> void:
	_force_fields.visible = effect_index == 1
	_matter_fields.visible = effect_index == 2
	_fire_fields.visible = effect_index == 3
	_water_fields.visible = effect_index == 4
	_matter_presentation_fields.visible = effect_index == 2
	_fire_presentation_fields.visible = effect_index == 3
	_water_presentation_fields.visible = effect_index == 4


# XP is transmitted as integer tenths, never a floating-point authority.
func _draft() -> Dictionary:
	var draft: Dictionary = {}
	for key: String in _fields:
		var control: Control = _fields[key]
		if control is LineEdit:
			draft[key] = control.text.strip_edges()
			if key.ends_with("_path") and control.text.strip_edges().is_empty(): draft[key] = null
		elif control is OptionButton: draft[key] = control.get_selected_metadata()
		elif control is CheckBox: draft[key] = control.button_pressed
		elif control is SpinBox:
			if key == "matter_physical_weight" or key.begins_with("force_mastery_") or key.begins_with("displacement_mastery_"):
				draft[key] = int(control.value) if control.value > 0 else null
			elif key == "projectile_homing_strength": draft[key] = control.value
			elif key.ends_with("_fps") or key.ends_with("_scale"):
				draft[key] = control.value if control.value > 0 else null
			else: draft[key] = int(control.value)
	for phase: String in _frames:
		var paths: Array = []
		for row: Node in _frames[phase].get_children(): paths.append((row.get_child(0) as LineEdit).text.strip_edges())
		draft[phase + "_frames"] = paths
	if draft["impact_effect"] != "air_displacement":
		for key: String in ["force", "force_falloff_per_tile", "max_displacement_tiles", "force_mastery_magic_levels_per_step", "force_mastery_force_per_step", "force_mastery_max_force", "displacement_mastery_magic_levels_per_step", "displacement_mastery_tiles_per_step", "displacement_mastery_max_tiles"]: draft[key] = null
	if draft["impact_effect"] != "earth_matter":
		for key: String in _fields:
			if key.begins_with("manifestation_") or key.begins_with("matter_"): draft[key] = null
	if draft["impact_effect"] != "burning_terrain":
		for key: String in _fields:
			if key.begins_with("ignition_") or key.begins_with("burning_"): draft[key] = null
		draft["burning_visual_frames"] = null
	if draft["impact_effect"] != "slippery_terrain":
		for key: String in draft:
			if key.begins_with("slick_"): draft[key] = null
		draft["slick_visual_frames"] = null
	return draft


func _request_preview(operation: String) -> void:
	_invalidate()
	_pending_definition_id = _definition_id.text.strip_edges()
	_preview_request = {"draft": _draft(), "expected_updated_at_utc": _current.get("updated_at_utc"), "target_operation": operation}
	_client.preview_spell(_pending_definition_id, _preview_request)


func _on_preview(payload: Dictionary) -> void:
	# Ignore responses for a form edited or replaced while the request was in flight.
	if _preview_request.is_empty(): return
	_support.render_changes(_changes, payload.get("changes", []))
	_support.render_validation(_validation, payload.get("messages", []))
	var operation := str(payload.get("target_operation", ""))
	_support.accept_preview(operation, str(payload.get("preview_signature", "")), bool(payload.get("applicable", false)), _apply, "2. Apply " + _support.operation_name(operation))


func _apply_preview() -> void:
	if not _support.can_apply(_support.preview_operation, _support.preview_signature): return
	var request := _preview_request.duplicate(true)
	request["preview_signature"] = _support.preview_signature
	var operation: String = _support.preview_operation
	_invalidate()
	_client.mutate_spell(_pending_definition_id, operation, request)


func _on_mutation(payload: Dictionary) -> void:
	if payload.get("definition") is Dictionary:
		_on_definition(payload["definition"])
	else:
		_new_definition()
	_support.render_validation(_validation, payload.get("messages", []))
	_status.text = "Operation committed and reloaded. Restart the game server to load published presentation."
	_client.load_spells()


func _on_failed(operation: String, message: String, errors: Array) -> void:
	if not operation.begins_with("spell_"): return
	_invalidate()
	_status.text = message
	_support.render_validation(_validation, errors)


func _invalidate(_value: Variant = null) -> void:
	if _loading or _apply == null: return
	_preview_request = {}
	_support.clear_preview(_apply, _changes, _validation, "2. Apply changes")


# Same ordered-row interaction as World Objects; frame order is explicit content.
func _add_frame(phase: String, path: String) -> void:
	var parent: VBoxContainer = _frames[phase]
	var row := HBoxContainer.new()
	parent.add_child(row)
	var field := LineEdit.new()
	field.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	field.placeholder_text = "res://assets/...png"
	field.text = path
	row.add_child(field)
	field.text_changed.connect(_invalidate)
	_button(row, "↑", func(): parent.move_child(row, maxi(0, row.get_index() - 1)); _invalidate())
	_button(row, "↓", func(): parent.move_child(row, mini(parent.get_child_count() - 1, row.get_index() + 1)); _invalidate())
	_button(row, "Remove", func(): parent.remove_child(row); row.queue_free(); _invalidate())


func _add_visual(parent: Node, phase: String) -> void:
	var visual := TextureRect.new()
	visual.custom_minimum_size = Vector2(128, 96)
	visual.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	visual.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED
	parent.add_child(visual)
	_visuals[phase] = visual


# Read images from the configured game asset root without importing/copying them.
func _refresh_visuals() -> void:
	_preview_textures.clear()
	_preview_elapsed = 0.0
	var draft := _draft()
	for phase: String in _visuals:
		var paths: Array = []
		if phase == "icon":
			paths = [draft.get("icon_texture_path")]
		elif phase == "matter":
			paths = [draft.get("matter_visual_texture_path")]
		elif draft.get(phase + "_frames") is Array:
			paths = draft[phase + "_frames"]
		var textures: Array[Texture2D] = []
		for value: Variant in paths:
			if not (value is String): continue
			var path := str(value)
			if _game_assets_root.is_empty() or not path.begins_with("res://assets/") or path.contains("..") or path.contains("\\"): continue
			var file_path := _game_assets_root.path_join(path.trim_prefix("res://assets/"))
			if not FileAccess.file_exists(file_path): continue
			var image := Image.load_from_file(file_path)
			if image != null and not image.is_empty(): textures.append(ImageTexture.create_from_image(image))
		_preview_textures[phase] = textures
		(_visuals[phase] as TextureRect).texture = null if textures.is_empty() else textures[0]


func _process(delta: float) -> void:
	_preview_elapsed += delta
	for phase: String in _preview_textures:
		var textures: Array = _preview_textures[phase]
		if phase in ["icon", "matter"] or textures.size() < 2: continue
		var fps := (_fields[phase + "_animation_fps"] as SpinBox).value
		if fps <= 0: continue
		var frame := int(_preview_elapsed * fps)
		frame = frame % textures.size() if phase in ["projectile", "burning_visual", "slick_visual"] else mini(frame, textures.size() - 1)
		(_visuals[phase] as TextureRect).texture = textures[frame]
