extends HBoxContainer
class_name MobEditor

const WORKSPACE_SUPPORT_SCRIPT := preload("res://scripts/authoring_workspace_support.gd")
const DEFAULT_BONUS_FIELDS := [
	"attack_thrust",
	"attack_slash",
	"attack_crush",
	"attack_ranged",
	"attack_magic",
	"strength_melee",
	"strength_ranged",
	"strength_magic",
	"defence_thrust",
	"defence_slash",
	"defence_crush",
	"defence_ranged",
	"defence_magic",
]
const GAME_ASSET_PREFIX := "res://assets/"

class MobVisualPreview:
	extends Control

	var texture: Texture2D
	var source_width := 32
	var source_height := 32
	var anchor_offset := Vector2.ZERO
	var render_scale := 1.0
	var footprint_tiles := Vector2i.ONE
	var status_text := "No mob visual selected."

	func _ready() -> void:
		custom_minimum_size = Vector2(240, 220)

	func set_payload(
		next_texture: Texture2D,
		width: int,
		height: int,
		anchor_x: float,
		anchor_y: float,
		scale: float,
		footprint_width: int,
		footprint_height: int,
		next_status: String
	) -> void:
		texture = next_texture
		source_width = max(1, width)
		source_height = max(1, height)
		anchor_offset = Vector2(anchor_x, anchor_y)
		render_scale = max(0.01, scale)
		footprint_tiles = Vector2i(max(1, footprint_width), max(1, footprint_height))
		status_text = next_status
		queue_redraw()

	func _draw() -> void:
		draw_rect(Rect2(Vector2.ZERO, size), Color(0.045, 0.052, 0.065), true)
		draw_rect(Rect2(Vector2.ZERO, size), Color(0.19, 0.22, 0.28), false, 1.0)
		var tile_pixels := 32.0
		var footprint_size := Vector2(float(footprint_tiles.x), float(footprint_tiles.y)) * tile_pixels
		var origin := size * 0.5
		var footprint := Rect2(origin - (footprint_size * 0.5), footprint_size)
		draw_rect(footprint, Color(0.11, 0.16, 0.2, 0.72), true)
		draw_rect(footprint, Color(0.36, 0.56, 0.72, 0.95), false, 1.0)
		for x in range(1, footprint_tiles.x):
			var px := footprint.position.x + (x * tile_pixels)
			draw_line(Vector2(px, footprint.position.y), Vector2(px, footprint.end.y), Color(0.36, 0.56, 0.72, 0.45), 1.0)
		for y in range(1, footprint_tiles.y):
			var py := footprint.position.y + (y * tile_pixels)
			draw_line(Vector2(footprint.position.x, py), Vector2(footprint.end.x, py), Color(0.36, 0.56, 0.72, 0.45), 1.0)
		draw_line(origin + Vector2(-7, 0), origin + Vector2(7, 0), Color(0.95, 0.82, 0.36), 2.0)
		draw_line(origin + Vector2(0, -7), origin + Vector2(0, 7), Color(0.95, 0.82, 0.36), 2.0)
		var anchor := origin + anchor_offset
		draw_circle(anchor, 4.0, Color(0.96, 0.45, 0.43))
		if texture != null:
			var scaled_size := texture.get_size() * render_scale
			var destination := Rect2(anchor - (scaled_size * 0.5), scaled_size)
			draw_texture_rect(texture, destination, false)

@onready var _client: AuthoringHostClient = %AuthoringHostClient

var _workspace_support
var _options: Dictionary = {}
var _mobs: Array = []
var _current_mob: Dictionary = {}
var _is_loading := false
var _is_new := false
var _schema_available := false
var _form_editable := false
var _reload_mob_id := ""
var _asset_preview_file_path := ""
var _game_client_assets_root := ""
var _drop_rows: Array = []
var _bonus_fields: Array = []
var _bonus_controls: Dictionary = {}
var _form_controls: Array = []
var _attack_speed_unit_milliseconds := 600

var _search: LineEdit
var _list: VBoxContainer
var _new_button: Button
var _mob_id: LineEdit
var _display_name: LineEdit
var _publication: Label
var _updated: Label
var _visual_path: LineEdit
var _source_width: SpinBox
var _source_height: SpinBox
var _anchor_x: SpinBox
var _anchor_y: SpinBox
var _render_scale: SpinBox
var _footprint_width: SpinBox
var _footprint_height: SpinBox
var _max_health: SpinBox
var _movement_speed: SpinBox
var _movement_behavior: OptionButton
var _wander_radius: SpinBox
var _aggression_mode: OptionButton
var _aggression_radius: SpinBox
var _leash_radius: SpinBox
var _return_home_behavior: OptionButton
var _behavior_note: Label
var _faction: OptionButton
var _proactive: CheckBox
var _detection_radius: SpinBox
var _scan_interval: SpinBox
var _candidate_limit: SpinBox
var _attack_enabled: CheckBox
var _attack_type: OptionButton
var _accuracy_style: OptionButton
var _minimum_range: SpinBox
var _maximum_range: SpinBox
var _attack_speed_units: SpinBox
var _attack_interval: Label
var _attack_level: SpinBox
var _strength_level: SpinBox
var _defence_level: SpinBox
var _drops: VBoxContainer
var _add_drop_button: Button
var _operation: OptionButton
var _preview_button: Button
var _delete_button: Button
var _apply_button: Button
var _status: Label
var _changes: VBoxContainer
var _validation: VBoxContainer
var _visual_preview: MobVisualPreview
var _visual_status: Label


func _ready() -> void:
	_workspace_support = WORKSPACE_SUPPORT_SCRIPT.new()
	_bonus_fields = DEFAULT_BONUS_FIELDS.duplicate()
	_build_ui()
	_connect_client()
	_set_form_enabled(false)
	_update_attack_controls()
	_update_targeting_controls()
	_clear_preview()


func _connect_client() -> void:
	_client.health_received.connect(_on_health_received)
	_client.mob_options_received.connect(_on_mob_options_received)
	_client.mob_catalog_received.connect(_on_mob_catalog_received)
	_client.mob_item_received.connect(_on_mob_item_received)
	_client.mob_preview_received.connect(_on_mob_preview_received)
	_client.mob_mutation_completed.connect(_on_mob_mutation_completed)
	_client.request_failed.connect(_on_request_failed)


func _build_ui() -> void:
	add_theme_constant_override("separation", 14)

	var catalog_panel := _panel(Vector2(310, 0))
	add_child(catalog_panel)
	var catalog_content := _vbox(catalog_panel)
	_add_heading(catalog_content, "Mobs", 20)
	_search = LineEdit.new()
	_search.placeholder_text = "Search mob ID or name"
	_search.text_changed.connect(_on_search_changed)
	catalog_content.add_child(_search)
	_new_button = Button.new()
	_new_button.text = "+ New Mob"
	_new_button.pressed.connect(_start_new_mob)
	catalog_content.add_child(_new_button)
	var catalog_scroll := ScrollContainer.new()
	catalog_scroll.size_flags_vertical = Control.SIZE_EXPAND_FILL
	catalog_content.add_child(catalog_scroll)
	_list = VBoxContainer.new()
	_list.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_list.add_theme_constant_override("separation", 6)
	catalog_scroll.add_child(_list)

	var form_panel := _panel(Vector2(520, 0))
	form_panel.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	add_child(form_panel)
	var form_scroll := ScrollContainer.new()
	form_scroll.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	form_scroll.size_flags_vertical = Control.SIZE_EXPAND_FILL
	form_panel.add_child(form_scroll)
	var form := VBoxContainer.new()
	form.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	form.add_theme_constant_override("separation", 12)
	form_scroll.add_child(form)
	_add_identity_section(form)
	_add_visual_section(form)
	_add_stats_section(form)
	_add_behavior_section(form)
	_add_faction_section(form)
	_add_attack_section(form)
	_add_bonuses_section(form)
	_add_drops_section(form)

	var preview_panel := _panel(Vector2(330, 0))
	add_child(preview_panel)
	var preview_scroll := ScrollContainer.new()
	preview_scroll.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	preview_scroll.size_flags_vertical = Control.SIZE_EXPAND_FILL
	preview_panel.add_child(preview_scroll)
	var preview_content := VBoxContainer.new()
	preview_content.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	preview_content.add_theme_constant_override("separation", 10)
	preview_scroll.add_child(preview_content)
	_add_heading(preview_content, "Preview", 20)
	_visual_preview = MobVisualPreview.new()
	preview_content.add_child(_visual_preview)
	_visual_status = _wrapped_label("No mob visual selected.")
	preview_content.add_child(_visual_status)
	_add_heading(preview_content, "Operation", 16)
	_operation = OptionButton.new()
	_add_operation("Save as Draft", "save_draft")
	_add_operation("Publish", "publish")
	_add_operation("Disable", "disable")
	_add_operation("Delete", "delete")
	_operation.item_selected.connect(_on_option_changed.unbind(1))
	preview_content.add_child(_operation)
	_preview_button = Button.new()
	_preview_button.text = "Validate and Preview Changes"
	_preview_button.pressed.connect(_preview)
	preview_content.add_child(_preview_button)
	_delete_button = Button.new()
	_delete_button.text = "Delete"
	_delete_button.disabled = true
	_delete_button.pressed.connect(_preview_delete)
	preview_content.add_child(_delete_button)
	_apply_button = Button.new()
	_apply_button.text = "Apply Previewed Operation"
	_apply_button.disabled = true
	_apply_button.pressed.connect(_apply)
	preview_content.add_child(_apply_button)
	_status = _wrapped_label("Load or create a mob definition.")
	preview_content.add_child(_status)

	var feedback_content := VBoxContainer.new()
	feedback_content.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	feedback_content.add_theme_constant_override("separation", 10)
	preview_content.add_child(feedback_content)
	_add_heading(feedback_content, "Exact Logical Changes", 16)
	_changes = VBoxContainer.new()
	feedback_content.add_child(_changes)
	_add_heading(feedback_content, "Validation", 16)
	_validation = VBoxContainer.new()
	feedback_content.add_child(_validation)


func _add_identity_section(parent: VBoxContainer) -> void:
	_add_heading(parent, "Identity", 18)
	var grid := _grid(parent)
	_mob_id = _line_field(grid, "Stable mob definition ID", "slime")
	_display_name = _line_field(grid, "Display name", "")
	_publication = _value_label(grid, "Publication state", "No mob selected")
	_updated = _value_label(grid, "Last updated", "Unknown")


func _add_visual_section(parent: VBoxContainer) -> void:
	_add_heading(parent, "Visuals and Footprint", 18)
	var grid := _grid(parent)
	_visual_path = _line_field(grid, "Texture path", "res://assets/maps/objects/mobs/slime.png")
	_visual_path.text_changed.connect(_on_visual_path_changed)
	_source_width = _spin_field(grid, "Source width", 1, 2048, 32, 1)
	_source_height = _spin_field(grid, "Source height", 1, 2048, 32, 1)
	_anchor_x = _spin_field(grid, "Anchor offset X", -512, 512, 0, 0.25)
	_anchor_y = _spin_field(grid, "Anchor offset Y", -512, 512, 0, 0.25)
	_render_scale = _spin_field(grid, "Render scale", 0.01, 8, 0.25, 0.01)
	_footprint_width = _spin_field(grid, "Footprint width tiles", 1, 16, 1, 1)
	_footprint_height = _spin_field(grid, "Footprint height tiles", 1, 16, 1, 1)


func _add_stats_section(parent: VBoxContainer) -> void:
	_add_heading(parent, "Stats", 18)
	var grid := _grid(parent)
	_max_health = _spin_field(grid, "Maximum health", 1, 10000, 10, 1)
	_movement_speed = _spin_field(grid, "Movement speed tiles/s", 0, 64, 2, 0.1)


func _add_behavior_section(parent: VBoxContainer) -> void:
	_add_heading(parent, "Movement and Aggression", 18)
	var grid := _grid(parent)
	_movement_behavior = _option_field(grid, "Movement behavior")
	_movement_behavior.item_selected.connect(_on_behavior_changed.unbind(1))
	_wander_radius = _spin_field(grid, "Wander radius tiles", 0, 128, 0, 1)
	_aggression_mode = _option_field(grid, "Aggression mode")
	_aggression_mode.item_selected.connect(_on_behavior_changed.unbind(1))
	_aggression_radius = _spin_field(grid, "Aggression radius tiles", 0, 128, 0, 1)
	_leash_radius = _spin_field(grid, "Leash radius tiles", 0, 128, 6, 1)
	_return_home_behavior = _option_field(grid, "Return-home behavior")
	_behavior_note = _wrapped_label("Home comes from the Tiled EnemySpawn coordinate. Wander, aggression, and leash radii are measured from that home point; leash must be at least the larger of wander and aggression radius.")
	parent.add_child(_behavior_note)


func _add_faction_section(parent: VBoxContainer) -> void:
	_add_heading(parent, "Faction and Hostile-Mob Targeting", 18)
	var grid := _grid(parent)
	_faction = _option_field(grid, "Faction")
	_proactive = CheckBox.new()
	_proactive.text = "Proactively target hostile mobs"
	_register_control(_proactive)
	_proactive.toggled.connect(_on_proactive_toggled)
	grid.add_child(Label.new())
	grid.add_child(_proactive)
	_detection_radius = _spin_field(grid, "Detection radius tiles", 0, 128, 0, 1)
	_scan_interval = _spin_field(grid, "Scan interval ms", 0, 60000, 0, 50)
	_candidate_limit = _spin_field(grid, "Scan candidate limit", 0, 512, 0, 1)


func _add_attack_section(parent: VBoxContainer) -> void:
	_add_heading(parent, "Primary Attack", 18)
	var grid := _grid(parent)
	_attack_enabled = CheckBox.new()
	_attack_enabled.text = "Primary combat profile"
	_register_control(_attack_enabled)
	_attack_enabled.toggled.connect(_on_attack_toggled)
	grid.add_child(Label.new())
	grid.add_child(_attack_enabled)
	_attack_type = _option_field(grid, "Attack type")
	_accuracy_style = _option_field(grid, "Accuracy style")
	_minimum_range = _spin_field(grid, "Minimum range tiles", 0, 128, 1, 1)
	_maximum_range = _spin_field(grid, "Maximum range tiles", 0, 128, 1, 1)
	_attack_speed_units = _spin_field(grid, "Attack speed units", 1, 60, 4, 1)
	_attack_speed_units.value_changed.connect(_on_attack_speed_changed.unbind(1))
	_attack_interval = _value_label(grid, "Attack interval", "")
	_attack_level = _spin_field(grid, "Attack level", 1, 1000, 1, 1)
	_strength_level = _spin_field(grid, "Strength level", 1, 1000, 1, 1)
	_defence_level = _spin_field(grid, "Defence level", 1, 1000, 1, 1)


func _add_bonuses_section(parent: VBoxContainer) -> void:
	_add_heading(parent, "Combat Bonuses", 18)
	var grid := _grid(parent)
	for field_name in _bonus_fields:
		_bonus_controls[field_name] = _spin_field(grid, field_name.capitalize(), -10000, 10000, 0, 1)


func _add_drops_section(parent: VBoxContainer) -> void:
	_add_heading(parent, "Guaranteed Drops", 18)
	_drops = VBoxContainer.new()
	_drops.add_theme_constant_override("separation", 6)
	parent.add_child(_drops)
	_add_drop_button = Button.new()
	_add_drop_button.text = "+ Add Drop"
	_add_drop_button.pressed.connect(_add_empty_drop_row)
	parent.add_child(_add_drop_button)
	_register_control(_add_drop_button)


func _on_mob_options_received(payload: Dictionary) -> void:
	_schema_available = true
	_options = payload
	_apply_options()
	_set_form_enabled(not _current_mob.is_empty() or _is_new)
	if _current_mob.is_empty() and not _is_new:
		_status.text = "Mob schema ready. Load or create a mob definition."


func _on_health_received(payload: Dictionary) -> void:
	_game_client_assets_root = ""
	for variant in payload.get("asset_roots", []) as Array:
		if variant is not Dictionary:
			continue
		var asset_root := variant as Dictionary
		if str(asset_root.get("id", "")) == "game_client_assets":
			_game_client_assets_root = str(asset_root.get("path", ""))
			break
	_update_visual_preview()


func _on_mob_catalog_received(payload: Dictionary) -> void:
	_mobs = payload.get("items", []) as Array
	_rebuild_list()
	if not _reload_mob_id.is_empty():
		var mob_id := _reload_mob_id
		_reload_mob_id = ""
		_client.load_mob(mob_id)


func _on_mob_item_received(payload: Dictionary) -> void:
	_load_mob(payload)


func _on_mob_preview_received(payload: Dictionary) -> void:
	var operation := str(payload.get("target_operation", "save_draft"))
	var applicable := bool(payload.get("valid_for_publication", false)) if operation == "publish" else bool(payload.get("valid_for_draft", false))
	_workspace_support.accept_preview(
		operation,
		str(payload.get("preview_signature", "")),
		applicable,
		_apply_button,
		"Apply %s" % _workspace_support.operation_name(operation)
	)
	_workspace_support.render_changes(_changes, payload.get("changes", []) as Array)
	_workspace_support.render_validation(_validation, payload.get("messages", []) as Array)
	_asset_preview_file_path = str(payload.get("asset_preview_file_path", ""))
	_update_visual_preview()
	_status.text = "Preview ready." if applicable else "Preview contains blocking validation errors."


func _on_mob_mutation_completed(payload: Dictionary) -> void:
	var operation := str(payload.get("operation", "operation"))
	if operation == "delete":
		var deleted_id := str(payload.get("deleted_id", _mob_id.text))
		_reload_mob_id = ""
		_start_new_mob()
		_status.text = "Deleted %s." % deleted_id
		_client.load_mobs(_search.text)
		return
	var mob := payload.get("mob", {}) as Dictionary
	_reload_mob_id = str(mob.get("mob_definition_id", _mob_id.text))
	_is_new = false
	_current_mob = mob
	_clear_preview()
	_status.text = "%s completed. Reloading mob..." % _workspace_support.operation_name(operation)
	_client.load_mobs(_search.text)


func _on_request_failed(operation: String, message: String, errors: Array) -> void:
	if not operation.begins_with("mob"):
		return
	_status.text = "%s failed: %s" % [operation, message]
	_workspace_support.render_validation(_validation, errors)
	_apply_button.disabled = true
	if operation == "mob_options" or operation == "mobs":
		_schema_available = false
		_set_form_enabled(false)
		if message.contains("route does not exist"):
			_status.text = "Mobs unavailable: restart the authoring host from this branch, then relaunch Studio."
		else:
			_status.text = "Mobs unavailable: %s" % message


func _apply_options() -> void:
	var defaults := _options.get("defaults", {}) as Dictionary
	var limits := _options.get("supported_limits", {}) as Dictionary
	_attack_speed_unit_milliseconds = int(defaults.get("attack_speed_unit_milliseconds", _options.get("attack_speed_unit_milliseconds", _attack_speed_unit_milliseconds)))
	_set_spin_limits(_minimum_range, 0, int(limits.get("max_range_tiles", 128)))
	_set_spin_limits(_maximum_range, 0, int(limits.get("max_range_tiles", 128)))
	_set_spin_limits(_wander_radius, 0, int(limits.get("max_wander_radius_tiles", 128)))
	_set_spin_limits(_aggression_radius, 0, int(limits.get("max_aggression_radius_tiles", 128)))
	_set_spin_limits(_leash_radius, 0, int(limits.get("max_leash_radius_tiles", 128)))
	_set_spin_limits(_attack_speed_units, int(limits.get("min_attack_speed_units", 1)), int(limits.get("max_attack_speed_units", 60)))
	for field_name in _bonus_controls:
		_set_spin_limits(_bonus_controls[field_name] as SpinBox, -int(limits.get("max_combat_bonus_magnitude", 10000)), int(limits.get("max_combat_bonus_magnitude", 10000)))
	_fill_authoring_options(_attack_type, _options.get("attack_types", []) as Array)
	_fill_authoring_options(_accuracy_style, _options.get("accuracy_styles", []) as Array)
	_fill_authoring_options(_movement_behavior, _options.get("movement_behaviors", []) as Array)
	_fill_authoring_options(_aggression_mode, _options.get("aggression_modes", []) as Array)
	_fill_authoring_options(_return_home_behavior, _options.get("return_home_behaviors", []) as Array)
	_fill_factions("")
	_update_behavior_controls()
	_update_attack_interval()


func _rebuild_list() -> void:
	_clear_children(_list)
	var query := _search.text.strip_edges().to_lower()
	for variant in _mobs:
		if variant is not Dictionary:
			continue
		var mob := variant as Dictionary
		var haystack := "%s %s" % [mob.get("mob_definition_id", ""), mob.get("display_name", "")]
		if not query.is_empty() and not haystack.to_lower().contains(query):
			continue
		var button := Button.new()
		button.alignment = HORIZONTAL_ALIGNMENT_LEFT
		button.text = "%s\n%s  |  %s  |  HP %d  |  %s  |  %s  |  drops %d" % [
			str(mob.get("display_name", "Unnamed mob")),
			str(mob.get("mob_definition_id", "")),
			str(mob.get("publication_state", "Unknown")),
			int(mob.get("max_health", 0)),
			str(mob.get("combat_faction_display_name", mob.get("combat_faction_id", "No faction"))),
			"combat" if bool(mob.get("has_combat_profile", false)) else "no combat",
			int(mob.get("guaranteed_drop_count", 0)),
		]
		button.tooltip_text = str(mob.get("visual_texture_path", ""))
		button.pressed.connect(_load_mob_id.bind(str(mob.get("mob_definition_id", ""))))
		_list.add_child(button)


func _load_mob_id(mob_definition_id: String) -> void:
	if not mob_definition_id.is_empty():
		_client.load_mob(mob_definition_id)


func _load_mob(payload: Dictionary) -> void:
	_is_loading = true
	_current_mob = payload
	_is_new = false
	_mob_id.text = str(payload.get("mob_definition_id", ""))
	_mob_id.editable = false
	_display_name.text = str(payload.get("display_name", ""))
	_publication.text = str(payload.get("publication_state", "Unknown"))
	_updated.text = str(payload.get("updated_at_utc", "Unknown"))
	_visual_path.text = str(payload.get("visual_texture_path", ""))
	_source_width.value = int(payload.get("source_width", 32))
	_source_height.value = int(payload.get("source_height", 32))
	_anchor_x.value = float(payload.get("visual_anchor_offset_x", 0.0))
	_anchor_y.value = float(payload.get("visual_anchor_offset_y", 0.0))
	_render_scale.value = float(payload.get("visual_render_scale", 0.25))
	_footprint_width.value = int(payload.get("footprint_width_tiles", 1))
	_footprint_height.value = int(payload.get("footprint_height_tiles", 1))
	_max_health.value = int(payload.get("max_health", 10))
	_movement_speed.value = float(payload.get("movement_speed_tiles_per_second", 2.0))
	_select_option(_movement_behavior, str(payload.get("movement_behavior", "static")))
	_wander_radius.value = int(payload.get("wander_radius_tiles", 0))
	_select_option(_aggression_mode, str(payload.get("aggression_mode", "retaliatory")))
	_aggression_radius.value = int(payload.get("aggression_radius_tiles", 0))
	_leash_radius.value = int(payload.get("leash_radius_tiles", 6))
	_select_option(_return_home_behavior, str(payload.get("return_home_behavior", "return_to_spawn")))
	_fill_factions(str(payload.get("combat_faction_id", "")))
	_proactive.button_pressed = bool(payload.get("can_proactively_target_hostile_mobs", false))
	_detection_radius.value = int(payload.get("mob_detection_radius_tiles", 0))
	_scan_interval.value = int(payload.get("mob_target_scan_interval_ms", 0))
	_candidate_limit.value = int(payload.get("mob_target_scan_candidate_limit", 0))
	_load_combat_profile(payload.get("primary_combat_profile", null))
	_load_bonuses(payload.get("combat_bonuses", {}))
	_load_drops(payload.get("guaranteed_drops", []) as Array)
	_asset_preview_file_path = str(payload.get("asset_preview_file_path", ""))
	_set_form_enabled(bool(payload.get("editable_in_mobs", true)) and _schema_available)
	_update_operation_default()
	_update_behavior_controls()
	_update_targeting_controls()
	_update_attack_controls()
	_update_visual_preview()
	_clear_preview()
	_status.text = "Loaded %s." % _mob_id.text
	_is_loading = false


func _start_new_mob() -> void:
	_is_loading = true
	var defaults := _options.get("defaults", {}) as Dictionary
	_current_mob = {}
	_is_new = true
	_mob_id.text = ""
	_mob_id.editable = _schema_available
	_display_name.text = ""
	_publication.text = "Unsaved"
	_updated.text = "Not saved"
	_visual_path.text = ""
	_source_width.value = 32
	_source_height.value = 32
	_anchor_x.value = 0
	_anchor_y.value = 0
	_render_scale.value = float(defaults.get("visual_render_scale", 0.25))
	_footprint_width.value = int(defaults.get("footprint_width_tiles", 1))
	_footprint_height.value = int(defaults.get("footprint_height_tiles", 1))
	_max_health.value = 10
	_movement_speed.value = float(defaults.get("movement_speed_tiles_per_second", 2.0))
	_select_option(_movement_behavior, str(defaults.get("movement_behavior", "static")))
	_wander_radius.value = int(defaults.get("wander_radius_tiles", 0))
	_select_option(_aggression_mode, str(defaults.get("aggression_mode", "retaliatory")))
	_aggression_radius.value = int(defaults.get("aggression_radius_tiles", 0))
	_leash_radius.value = int(defaults.get("leash_radius_tiles", 6))
	_select_option(_return_home_behavior, str(defaults.get("return_home_behavior", "return_to_spawn")))
	_fill_factions("")
	_proactive.button_pressed = bool(defaults.get("can_proactively_target_hostile_mobs", false))
	_detection_radius.value = int(defaults.get("mob_detection_radius_tiles", 0))
	_scan_interval.value = int(defaults.get("mob_target_scan_interval_ms", 0))
	_candidate_limit.value = int(defaults.get("mob_target_scan_candidate_limit", 0))
	_attack_enabled.button_pressed = true
	_select_option(_attack_type, str(defaults.get("attack_type", "melee")))
	_select_option(_accuracy_style, str(defaults.get("accuracy_style", "slash")))
	_minimum_range.value = int(defaults.get("minimum_range_tiles", 1))
	_maximum_range.value = int(defaults.get("maximum_range_tiles", 1))
	_attack_speed_units.value = int(defaults.get("attack_speed_units", 4))
	_attack_level.value = 1
	_strength_level.value = 1
	_defence_level.value = 1
	_zero_bonuses()
	_load_drops([])
	_asset_preview_file_path = ""
	_operation.select(0)
	_set_form_enabled(_schema_available)
	_update_behavior_controls()
	_update_targeting_controls()
	_update_attack_controls()
	_update_visual_preview()
	_clear_preview()
	_status.text = "Creating a new reusable mob definition."
	_mob_id.grab_focus()
	_is_loading = false


func _load_combat_profile(profile_variant: Variant) -> void:
	if profile_variant is not Dictionary:
		_attack_enabled.button_pressed = false
		return
	var profile := profile_variant as Dictionary
	_attack_enabled.button_pressed = true
	_select_option(_attack_type, str(profile.get("attack_type", "")))
	_select_option(_accuracy_style, str(profile.get("accuracy_style", "")))
	_minimum_range.value = int(profile.get("minimum_range_tiles", 1))
	_maximum_range.value = int(profile.get("maximum_range_tiles", 1))
	_attack_speed_units.value = int(profile.get("attack_speed_units", 4))
	_attack_level.value = int(profile.get("attack_level", 1))
	_strength_level.value = int(profile.get("strength_level", 1))
	_defence_level.value = int(profile.get("defence_level", 1))


func _load_bonuses(bonuses_variant: Variant) -> void:
	if bonuses_variant is not Dictionary:
		_zero_bonuses()
		return
	var bonuses := bonuses_variant as Dictionary
	for field_name in _bonus_controls:
		(_bonus_controls[field_name] as SpinBox).value = int(bonuses.get(field_name, 0))


func _zero_bonuses() -> void:
	for field_name in _bonus_controls:
		(_bonus_controls[field_name] as SpinBox).value = 0


func _load_drops(drop_entries: Array) -> void:
	_drop_rows.clear()
	_clear_children(_drops)
	for variant in drop_entries:
		if variant is Dictionary:
			var entry := variant as Dictionary
			_add_drop_row(str(entry.get("item_id", "")), int(entry.get("stack_count", 1)))


func _preview() -> void:
	var mob_definition_id := _mob_id.text.strip_edges()
	if mob_definition_id.is_empty():
		_status.text = "Enter a stable mob definition ID before previewing."
		return
	var payload := _payload()
	payload["target_operation"] = _selected_metadata(_operation)
	_client.preview_mob(mob_definition_id, payload)
	_status.text = "Calculating validation and exact database changes..."


func _preview_delete() -> void:
	if _current_mob.is_empty():
		_status.text = "Select a saved mob definition before deleting."
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
	var mob_definition_id := _mob_id.text.strip_edges()
	var expected: Variant = _current_mob.get("updated_at_utc", null)
	match operation:
		"publish":
			_client.publish_mob(mob_definition_id, expected, preview_signature)
			_status.text = "Publishing mob definition..."
		"disable":
			_client.disable_mob(mob_definition_id, expected, preview_signature)
			_status.text = "Disabling mob definition. Generated-spawn reference guards are not integrated yet."
		"delete":
			_client.delete_mob(mob_definition_id, expected, preview_signature)
			_status.text = "Deleting mob definition..."
		_:
			var payload := _payload()
			payload["preview_signature"] = preview_signature
			_client.save_mob_draft(mob_definition_id, payload)
			_status.text = "Saving complete mob draft..."
	_apply_button.disabled = true


func _payload() -> Dictionary:
	var proactive := _proactive.button_pressed
	return {
		"display_name": _display_name.text,
		"visual_texture_path": _visual_path.text,
		"source_width": int(_source_width.value),
		"source_height": int(_source_height.value),
		"visual_anchor_offset_x": float(_anchor_x.value),
		"visual_anchor_offset_y": float(_anchor_y.value),
		"visual_render_scale": float(_render_scale.value),
		"footprint_width_tiles": int(_footprint_width.value),
		"footprint_height_tiles": int(_footprint_height.value),
		"max_health": int(_max_health.value),
		"movement_speed_tiles_per_second": float(_movement_speed.value),
		"movement_behavior": _selected_metadata(_movement_behavior),
		"wander_radius_tiles": int(_wander_radius.value),
		"aggression_mode": _selected_metadata(_aggression_mode),
		"aggression_radius_tiles": int(_aggression_radius.value),
		"leash_radius_tiles": int(_leash_radius.value),
		"return_home_behavior": _selected_metadata(_return_home_behavior),
		"combat_faction_id": _selected_metadata(_faction),
		"can_proactively_target_hostile_mobs": proactive,
		"mob_detection_radius_tiles": int(_detection_radius.value) if proactive else 0,
		"mob_target_scan_interval_ms": int(_scan_interval.value) if proactive else 0,
		"mob_target_scan_candidate_limit": int(_candidate_limit.value) if proactive else 0,
		"primary_combat_profile": _combat_profile_payload() if _attack_enabled.button_pressed else null,
		"combat_bonuses": _bonus_payload(),
		"guaranteed_drops": _drop_payload(),
		"expected_updated_at_utc": _current_mob.get("updated_at_utc", null),
	}


func _combat_profile_payload() -> Dictionary:
	return {
		"attack_type": _selected_metadata(_attack_type),
		"accuracy_style": _selected_metadata(_accuracy_style),
		"minimum_range_tiles": int(_minimum_range.value),
		"maximum_range_tiles": int(_maximum_range.value),
		"attack_speed_units": int(_attack_speed_units.value),
		"attack_level": int(_attack_level.value),
		"strength_level": int(_strength_level.value),
		"defence_level": int(_defence_level.value),
	}


func _bonus_payload() -> Dictionary:
	var values := {}
	for field_name in _bonus_controls:
		values[field_name] = int((_bonus_controls[field_name] as SpinBox).value)
	return values


func _drop_payload() -> Array:
	var values := []
	var order := 0
	for row_variant in _drop_rows:
		var row := row_variant as HBoxContainer
		if row == null:
			continue
		var item := row.get_meta("item") as OptionButton
		var stack := row.get_meta("stack") as SpinBox
		var item_id := _selected_metadata(item)
		if item_id.is_empty():
			continue
		values.append({
			"drop_order": order,
			"item_id": item_id,
			"stack_count": int(stack.value),
		})
		order += 1
	return values


func _add_empty_drop_row() -> void:
	_add_drop_row("", 1)
	_on_form_changed()


func _add_drop_row(item_id: String, stack_count: int) -> void:
	var row := HBoxContainer.new()
	row.add_theme_constant_override("separation", 6)
	var item := OptionButton.new()
	item.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_fill_drop_items(item, item_id)
	item.item_selected.connect(_on_drop_item_changed.unbind(1))
	row.add_child(item)
	var stack := SpinBox.new()
	stack.min_value = 1
	stack.max_value = _max_stack_count()
	stack.step = 1
	stack.value = max(1, stack_count)
	stack.custom_minimum_size = Vector2(76, 0)
	stack.value_changed.connect(_on_spin_changed.unbind(1))
	row.add_child(stack)
	var up := Button.new()
	up.text = "Up"
	up.pressed.connect(_move_drop.bind(row, -1))
	row.add_child(up)
	var down := Button.new()
	down.text = "Down"
	down.pressed.connect(_move_drop.bind(row, 1))
	row.add_child(down)
	var remove := Button.new()
	remove.text = "Remove"
	remove.pressed.connect(_remove_drop.bind(row))
	row.add_child(remove)
	row.set_meta("item", item)
	row.set_meta("stack", stack)
	_drop_rows.append(row)
	_drops.add_child(row)
	_update_drop_controls()


func _move_drop(row: HBoxContainer, offset: int) -> void:
	var index := _drop_rows.find(row)
	var next_index := clampi(index + offset, 0, _drop_rows.size() - 1)
	if index < 0 or index == next_index:
		return
	_drop_rows.remove_at(index)
	_drop_rows.insert(next_index, row)
	_drops.move_child(row, next_index)
	_update_drop_controls()
	_on_form_changed()


func _remove_drop(row: HBoxContainer) -> void:
	_drop_rows.erase(row)
	_drops.remove_child(row)
	row.queue_free()
	_update_drop_controls()
	_on_form_changed()


func _update_drop_controls() -> void:
	for index in range(_drop_rows.size()):
		var row := _drop_rows[index] as HBoxContainer
		var item := row.get_meta("item") as OptionButton
		var stack := row.get_meta("stack") as SpinBox
		item.disabled = not _form_editable
		stack.editable = _form_editable
		for child in row.get_children():
			if child is Button:
				var button := child as Button
				if button.text == "Up":
					button.disabled = index == 0 or not _form_editable
				elif button.text == "Down":
					button.disabled = index == _drop_rows.size() - 1 or not _form_editable
				else:
					button.disabled = not _form_editable
	var duplicate_count := _drop_items_have_duplicates()
	if duplicate_count > 0:
		_status.text = "Guaranteed drops contain duplicate item selections."


func _drop_items_have_duplicates() -> int:
	var seen := {}
	var duplicates := 0
	for row_variant in _drop_rows:
		var row := row_variant as HBoxContainer
		var item_id := _selected_metadata(row.get_meta("item") as OptionButton)
		if item_id.is_empty():
			continue
		if seen.has(item_id):
			duplicates += 1
		seen[item_id] = true
	return duplicates


func _on_search_changed(value: String) -> void:
	_client.load_mobs(value)


func _on_form_changed(_value: Variant = null) -> void:
	if _is_loading:
		return
	_clear_preview()
	_update_visual_preview()


func _on_visual_path_changed(_value: String) -> void:
	if _is_loading:
		return
	_asset_preview_file_path = ""
	_clear_preview()
	_update_visual_preview()


func _on_option_changed() -> void:
	_on_form_changed()


func _on_spin_changed() -> void:
	_on_form_changed()


func _on_proactive_toggled(_value: bool) -> void:
	_update_targeting_controls()
	_on_form_changed()


func _on_behavior_changed() -> void:
	_update_behavior_controls()
	_on_form_changed()


func _on_attack_toggled(_value: bool) -> void:
	_update_attack_controls()
	_on_form_changed()


func _on_attack_speed_changed() -> void:
	_update_attack_interval()
	_on_form_changed()


func _on_drop_item_changed() -> void:
	_update_drop_controls()
	_on_form_changed()


func _update_targeting_controls() -> void:
	var enabled := _form_editable and _proactive.button_pressed
	_detection_radius.editable = enabled
	_scan_interval.editable = enabled
	_candidate_limit.editable = enabled
	if not _proactive.button_pressed:
		_detection_radius.value = 0
		_scan_interval.value = 0
		_candidate_limit.value = 0


func _update_behavior_controls() -> void:
	if _movement_behavior == null or _aggression_mode == null:
		return
	var movement := _selected_metadata(_movement_behavior)
	var aggression := _selected_metadata(_aggression_mode)
	var can_edit_wander := _form_editable and movement == "random_wander"
	var can_edit_aggression_radius := _form_editable and aggression == "proactive"
	_wander_radius.editable = can_edit_wander
	_aggression_radius.editable = can_edit_aggression_radius
	if movement == "static":
		_wander_radius.value = 0
	if aggression != "proactive":
		_aggression_radius.value = 0


func _update_attack_controls() -> void:
	var enabled := _form_editable and _attack_enabled.button_pressed
	for control in [_attack_type, _accuracy_style]:
		(control as OptionButton).disabled = not enabled
	for control in [_minimum_range, _maximum_range, _attack_speed_units, _attack_level, _strength_level, _defence_level]:
		(control as SpinBox).editable = enabled
	_update_attack_interval()


func _update_attack_interval() -> void:
	var units := int(_attack_speed_units.value) if _attack_speed_units != null else 1
	_attack_interval.text = "%d units x %d ms = %d ms" % [
		units,
		_attack_speed_unit_milliseconds,
		units * _attack_speed_unit_milliseconds,
	]


func _update_visual_preview() -> void:
	if _visual_preview == null:
		return
	var resolved := _resolve_visual_preview_file_path()
	var file_path := str(resolved.get("file_path", ""))
	var status := str(resolved.get("status", "No mob visual selected."))
	var texture: Texture2D = null
	if not file_path.is_empty():
		var image := Image.load_from_file(file_path)
		if image != null and not image.is_empty():
			texture = ImageTexture.create_from_image(image)
		else:
			status = "The resolved mob PNG could not be loaded."
	_visual_preview.set_payload(
		texture,
		int(_source_width.value),
		int(_source_height.value),
		float(_anchor_x.value),
		float(_anchor_y.value),
		float(_render_scale.value),
		int(_footprint_width.value),
		int(_footprint_height.value),
		status
	)
	_visual_status.text = status


func _resolve_visual_preview_file_path() -> Dictionary:
	var resource_path := _visual_path.text.strip_edges()
	if not _asset_preview_file_path.is_empty() and FileAccess.file_exists(_asset_preview_file_path):
		return {
			"file_path": _asset_preview_file_path,
			"status": "Preview uses the host-resolved asset path.",
		}
	if resource_path.is_empty():
		return {
			"file_path": "",
			"status": "No mob visual selected.",
		}
	if resource_path.begins_with(GAME_ASSET_PREFIX):
		if _game_client_assets_root.is_empty() or not DirAccess.dir_exists_absolute(_game_client_assets_root):
			return {
				"file_path": "",
				"status": "Configure game_client_assets to preview mob visuals.",
			}
		var relative := resource_path.substr(GAME_ASSET_PREFIX.length())
		var candidate := _game_client_assets_root.path_join(relative)
		if FileAccess.file_exists(candidate):
			return {
				"file_path": candidate,
				"status": "Preview uses the configured game_client_assets root.",
			}
		return {
			"file_path": "",
			"status": "No PNG found for %s under the configured game_client_assets root." % resource_path,
		}
	if FileAccess.file_exists(resource_path):
		return {
			"file_path": resource_path,
			"status": "Preview uses the configured file path.",
		}
	return {
		"file_path": "",
		"status": "No PNG found for %s." % resource_path,
	}


func _set_form_enabled(enabled: bool) -> void:
	var editable := enabled and _schema_available
	_form_editable = editable
	_new_button.disabled = not _schema_available
	for control_variant in _form_controls:
		var control := control_variant as Control
		if control is LineEdit:
			(control as LineEdit).editable = editable
		elif control is SpinBox:
			(control as SpinBox).editable = editable
		elif control is OptionButton:
			(control as OptionButton).disabled = not editable
		elif control is Button:
			(control as Button).disabled = not editable
		elif control is CheckBox:
			(control as CheckBox).disabled = not editable
	_mob_id.editable = editable and _is_new
	_operation.disabled = not editable
	_preview_button.disabled = not editable
	_delete_button.disabled = not editable or _current_mob.is_empty()
	if not editable:
		_apply_button.disabled = true
	_update_targeting_controls()
	_update_behavior_controls()
	_update_attack_controls()
	_update_drop_controls()


func _clear_preview() -> void:
	_workspace_support.clear_preview(_apply_button, _changes, _validation)


func _update_operation_default() -> void:
	var state := str(_current_mob.get("publication_state", "Draft"))
	_operation.select(2 if state == "Published" else 0)


func _fill_authoring_options(control: OptionButton, values: Array) -> void:
	var selected := _selected_metadata(control)
	control.clear()
	for variant in values:
		if variant is Dictionary:
			var option := variant as Dictionary
			control.add_item(str(option.get("display_name", option.get("id", "Option"))))
			control.set_item_metadata(control.item_count - 1, str(option.get("id", "")))
	_select_option(control, selected)


func _fill_factions(selected_id: String) -> void:
	_faction.clear()
	_faction.add_item("No faction")
	_faction.set_item_metadata(0, "")
	for variant in _options.get("factions", []) as Array:
		if variant is Dictionary:
			var faction := variant as Dictionary
			_faction.add_item(str(faction.get("display_name", faction.get("faction_id", "Faction"))))
			_faction.set_item_metadata(_faction.item_count - 1, str(faction.get("faction_id", "")))
	_select_option(_faction, selected_id)


func _fill_drop_items(control: OptionButton, selected_id: String) -> void:
	control.clear()
	control.add_item("Select published item")
	control.set_item_metadata(0, "")
	for variant in _options.get("published_drop_items", []) as Array:
		if variant is Dictionary:
			var item := variant as Dictionary
			control.add_item(str(item.get("display_name", item.get("item_id", "Item"))))
			control.set_item_metadata(control.item_count - 1, str(item.get("item_id", "")))
	_select_option(control, selected_id)


func _add_operation(label: String, operation_id: String) -> void:
	_operation.add_item(label)
	_operation.set_item_metadata(_operation.item_count - 1, operation_id)


func _line_field(grid: GridContainer, label_text: String, placeholder: String) -> LineEdit:
	grid.add_child(_label(label_text))
	var field := LineEdit.new()
	field.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	field.placeholder_text = placeholder
	field.text_changed.connect(_on_form_changed)
	grid.add_child(field)
	_register_control(field)
	return field


func _spin_field(grid: GridContainer, label_text: String, minimum: float, maximum: float, value: float, step: float) -> SpinBox:
	grid.add_child(_label(label_text))
	var field := SpinBox.new()
	field.min_value = minimum
	field.max_value = maximum
	field.value = value
	field.step = step
	field.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	field.value_changed.connect(_on_spin_changed.unbind(1))
	grid.add_child(field)
	_register_control(field)
	return field


func _option_field(grid: GridContainer, label_text: String) -> OptionButton:
	grid.add_child(_label(label_text))
	var option := OptionButton.new()
	option.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	option.item_selected.connect(_on_option_changed.unbind(1))
	grid.add_child(option)
	_register_control(option)
	return option


func _value_label(grid: GridContainer, label_text: String, value: String) -> Label:
	grid.add_child(_label(label_text))
	var label := _wrapped_label(value)
	grid.add_child(label)
	return label


func _register_control(control: Control) -> void:
	_form_controls.append(control)


func _grid(parent: VBoxContainer) -> GridContainer:
	var grid := GridContainer.new()
	grid.columns = 2
	grid.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	grid.add_theme_constant_override("h_separation", 12)
	grid.add_theme_constant_override("v_separation", 6)
	parent.add_child(grid)
	return grid


func _panel(minimum_size: Vector2) -> PanelContainer:
	var panel := PanelContainer.new()
	panel.custom_minimum_size = minimum_size
	panel.size_flags_vertical = Control.SIZE_EXPAND_FILL
	panel.add_theme_stylebox_override("panel", _panel_style())
	return panel


func _panel_style() -> StyleBoxFlat:
	var style := StyleBoxFlat.new()
	style.bg_color = Color(0.086, 0.098, 0.122, 1)
	style.border_color = Color(0.19, 0.22, 0.28, 1)
	style.set_border_width_all(1)
	style.set_corner_radius_all(8)
	style.content_margin_left = 16
	style.content_margin_top = 14
	style.content_margin_right = 16
	style.content_margin_bottom = 14
	return style


func _vbox(parent: Node) -> VBoxContainer:
	var container := VBoxContainer.new()
	container.add_theme_constant_override("separation", 10)
	container.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	container.size_flags_vertical = Control.SIZE_EXPAND_FILL
	parent.add_child(container)
	return container


func _add_heading(parent: VBoxContainer, text: String, size: int) -> void:
	var heading := Label.new()
	heading.text = text
	heading.add_theme_font_size_override("font_size", size)
	parent.add_child(heading)


func _label(text: String) -> Label:
	var label := Label.new()
	label.text = text
	label.custom_minimum_size = Vector2(170, 0)
	label.size_flags_horizontal = Control.SIZE_SHRINK_BEGIN
	label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	return label


func _wrapped_label(text: String) -> Label:
	var label := Label.new()
	label.text = text
	label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	label.modulate = Color(0.72, 0.75, 0.82, 1)
	return label


func _set_spin_limits(control: SpinBox, minimum: int, maximum: int) -> void:
	control.min_value = minimum
	control.max_value = maximum


func _max_stack_count() -> int:
	var limits := _options.get("supported_limits", {}) as Dictionary
	return int(limits.get("max_stack_count", 2147483647))


func _select_option(control: OptionButton, id: String) -> void:
	for index in range(control.item_count):
		if str(control.get_item_metadata(index)) == id:
			control.select(index)
			return
	if control.item_count > 0:
		control.select(0)


func _selected_metadata(control: OptionButton) -> String:
	if control == null or control.selected < 0:
		return ""
	return str(control.get_item_metadata(control.selected))


func _clear_children(container: Node) -> void:
	for child in container.get_children():
		child.queue_free()
