extends HBoxContainer
class_name NpcEditor

signal workspace_open_requested(workspace_id: String, resource_id: String)

const WORKSPACE_SUPPORT_SCRIPT := preload("res://scripts/authoring_workspace_support.gd")
const GAME_ASSET_PREFIX := "res://assets/"

class NpcVisualPreview:
	extends Control

	var texture: Texture2D = null
	var source_width := 128
	var source_height := 128
	var anchor_offset := Vector2.ZERO
	var render_scale := 0.25
	var footprint_tiles := Vector2i.ONE
	var facing := "south"
	var status_text := "No NPC visual selected."
	var rigged_sprite_preview: Dictionary = {}
	var rigged_cosmetics: Array = []

	func _ready() -> void:
		custom_minimum_size = Vector2(280, 220)

	func set_payload(
		next_texture: Texture2D,
		width: int,
		height: int,
		anchor_x: float,
		anchor_y: float,
		scale: float,
		footprint_width: int,
		footprint_height: int,
		next_facing: String,
		next_status: String
	) -> void:
		texture = next_texture
		source_width = max(1, width)
		source_height = max(1, height)
		anchor_offset = Vector2(anchor_x, anchor_y)
		render_scale = max(0.01, scale)
		footprint_tiles = Vector2i(max(1, footprint_width), max(1, footprint_height))
		facing = next_facing
		status_text = next_status
		queue_redraw()

	func set_rigged_sprite_preview(next_preview: Dictionary) -> void:
		rigged_sprite_preview = next_preview
		rigged_cosmetics.clear()
		for cosmetic_variant in next_preview.get("cosmetics", []) as Array:
			var cosmetic := cosmetic_variant as Dictionary
			var image := Image.load_from_file(str(cosmetic.get("file_path", "")))
			if image != null and not image.is_empty():
				var resolved := cosmetic.duplicate()
				resolved["texture"] = ImageTexture.create_from_image(image)
				rigged_cosmetics.append(resolved)
		queue_redraw()

	func _draw() -> void:
		draw_rect(Rect2(Vector2.ZERO, size), Color(0.045, 0.052, 0.065), true)
		draw_rect(Rect2(Vector2.ZERO, size), Color(0.19, 0.22, 0.28), false, 1.0)
		var tile_pixels := 64.0
		var footprint_size := Vector2(float(footprint_tiles.x), float(footprint_tiles.y)) * tile_pixels
		var origin := size * 0.5
		var footprint := Rect2(origin - (footprint_size * 0.5), footprint_size)
		draw_rect(footprint, Color(0.11, 0.16, 0.2, 0.72), true)
		draw_rect(footprint, Color(0.36, 0.56, 0.72, 0.95), false, 1.0)
		draw_line(origin + Vector2(-8, 0), origin + Vector2(8, 0), Color(0.95, 0.82, 0.36), 2.0)
		draw_line(origin + Vector2(0, -8), origin + Vector2(0, 8), Color(0.95, 0.82, 0.36), 2.0)
		var anchor := origin + anchor_offset
		draw_circle(anchor, 4.0, Color(0.96, 0.45, 0.43))
		if not rigged_sprite_preview.is_empty() and texture != null:
			var scaled_size := texture.get_size() * render_scale
			var base_destination := Rect2(anchor - (scaled_size * 0.5), scaled_size)
			draw_texture_rect(texture, base_destination, false)
			for cosmetic_variant in rigged_cosmetics:
				var cosmetic := cosmetic_variant as Dictionary
				var cosmetic_texture := cosmetic.get("texture") as Texture2D
				if cosmetic_texture == null:
					continue
				var destination := Rect2(base_destination.position + Vector2(float(cosmetic.get("x", 0)), float(cosmetic.get("y", 0))) * render_scale, cosmetic_texture.get_size() * render_scale)
				draw_set_transform(destination.position + destination.size * 0.5, 0.0, Vector2(-1, 1) if bool(cosmetic.get("flip_x", false)) else Vector2.ONE)
				draw_texture_rect(cosmetic_texture, Rect2(-destination.size * 0.5, destination.size), false)
				draw_set_transform(Vector2.ZERO)
		elif texture != null:
			var scaled_size := texture.get_size() * render_scale
			var destination := Rect2(anchor - (scaled_size * 0.5), scaled_size)
			draw_texture_rect(texture, destination, false)

@onready var _client: AuthoringHostClient = %AuthoringHostClient

var _workspace_support
var _options: Dictionary = {}
var _npcs: Array = []
var _current_npc: Dictionary = {}
var _is_loading := false
var _is_new := false
var _schema_available := false
var _form_editable := false
var _reload_npc_id := ""
var _asset_preview_file_path := ""
var _game_client_assets_root := ""
var _form_controls: Array = []

var _search: LineEdit
var _list: VBoxContainer
var _new_button: Button
var _npc_id: LineEdit
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
var _movement_behavior: OptionButton
var _wander_radius: SpinBox
var _tick_interval: SpinBox
var _idle_chance: SpinBox
var _movement_guidance: Label
var _interaction_enabled: CheckBox
var _interaction_range: SpinBox
var _default_interaction: OptionButton
var _dialogue_id: LineEdit
var _dialogue_options: OptionButton
var _open_dialogue_button: Button
var _dialogue_capability: Label
var _dialogue_guidance: Label
var _notes: TextEdit
var _runtime_catalog_status: Label
var _quest_status: Label
var _multiple_interactions_status: Label
var _placement_guidance: Label
var _operation: OptionButton
var _preview_button: Button
var _delete_button: Button
var _apply_button: Button
var _status: Label
var _reference_summary: VBoxContainer
var _changes: VBoxContainer
var _validation: VBoxContainer
var _visual_preview: NpcVisualPreview
var _visual_status: Label
var _preview_facing: OptionButton
var _preview_frame: OptionButton
var _visual_mode: OptionButton
var _rig_id: OptionButton
var _calibration_id: OptionButton
var _pose_policy: OptionButton
var _fixed_direction: OptionButton
var _fixed_frame: OptionButton
var _rigged_controls: VBoxContainer
var _cosmetic_controls: Dictionary = {}
var _composite_visual: Dictionary = {}


func _ready() -> void:
	_workspace_support = WORKSPACE_SUPPORT_SCRIPT.new()
	_build_ui()
	_connect_client()
	_set_form_enabled(false)
	_clear_preview()


func _connect_client() -> void:
	_client.health_received.connect(_on_health_received)
	_client.npc_options_received.connect(_on_npc_options_received)
	_client.npc_catalog_received.connect(_on_npc_catalog_received)
	_client.npc_definition_received.connect(_on_npc_definition_received)
	_client.npc_preview_received.connect(_on_npc_preview_received)
	_client.npc_mutation_completed.connect(_on_npc_mutation_completed)
	_client.npc_delete_completed.connect(_on_npc_delete_completed)
	_client.request_failed.connect(_on_request_failed)


func _build_ui() -> void:
	add_theme_constant_override("separation", 14)

	var catalog_panel := _panel(Vector2(310, 0))
	add_child(catalog_panel)
	var catalog_content := _vbox(catalog_panel)
	_add_heading(catalog_content, "NPCs", 20)
	_search = LineEdit.new()
	_search.placeholder_text = "Search NPC ID or name"
	_search.text_changed.connect(_on_search_changed)
	catalog_content.add_child(_search)
	var refresh := Button.new()
	refresh.text = "Refresh"
	refresh.pressed.connect(_refresh_catalog)
	catalog_content.add_child(refresh)
	_new_button = Button.new()
	_new_button.text = "+ New NPC"
	_new_button.pressed.connect(_start_new_npc)
	catalog_content.add_child(_new_button)
	var catalog_scroll := ScrollContainer.new()
	catalog_scroll.size_flags_vertical = Control.SIZE_EXPAND_FILL
	catalog_content.add_child(catalog_scroll)
	_list = VBoxContainer.new()
	_list.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_list.add_theme_constant_override("separation", 6)
	catalog_scroll.add_child(_list)

	var form_panel := _panel(Vector2(540, 0))
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
	_add_movement_section(form)
	_add_interaction_section(form)
	_add_dialogue_section(form)
	_add_notes_section(form)
	_add_runtime_guidance_section(form)

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
	_add_preview_section(preview_content)


func _add_identity_section(parent: VBoxContainer) -> void:
	_add_heading(parent, "Identity", 18)
	var grid := _grid(parent)
	_npc_id = _line_field(grid, "Stable NPC definition ID", "test_npc")
	_display_name = _line_field(grid, "Display name", "")
	_publication = _value_label(grid, "Publication state", "No NPC selected")
	_updated = _value_label(grid, "Last updated", "Unknown")


func _add_visual_section(parent: VBoxContainer) -> void:
	_add_heading(parent, "Visuals", 18)
	var grid := _grid(parent)
	_visual_mode = _option_field(grid, "Visual type")
	_visual_mode.item_selected.connect(_on_visual_mode_changed.unbind(1))
	_visual_path = _line_field(grid, "Texture path", "res://assets/actors/npcs/test_npc.png")
	_visual_path.text_changed.connect(_on_visual_path_changed)
	_source_width = _spin_field(grid, "Full texture width", 1, 4096, 128, 1)
	_source_height = _spin_field(grid, "Full texture height", 1, 4096, 128, 1)
	_anchor_x = _spin_field(grid, "Anchor offset X", -1024, 1024, 0, 0.25)
	_anchor_y = _spin_field(grid, "Anchor offset Y", -1024, 1024, 0, 0.25)
	_render_scale = _spin_field(grid, "Render scale", 0.01, 8, 0.25, 0.01)
	_footprint_width = _spin_field(grid, "Footprint width tiles", 1, 1, 1, 1)
	_footprint_height = _spin_field(grid, "Footprint height tiles", 1, 1, 1, 1)
	_rigged_controls = VBoxContainer.new()
	parent.add_child(_rigged_controls)
	var rig_grid := _grid(_rigged_controls)
	_rig_id = _option_field(rig_grid, "Rig")
	_rig_id.item_selected.connect(_on_rig_changed.unbind(1))
	_calibration_id = _option_field(rig_grid, "Calibration")
	_calibration_id.item_selected.connect(_on_rigged_form_changed.unbind(1))
	_pose_policy = _option_field(rig_grid, "Pose policy")
	_pose_policy.item_selected.connect(_on_pose_policy_changed.unbind(1))
	_fixed_direction = _option_field(rig_grid, "Fixed direction")
	_fixed_frame = _option_field(rig_grid, "Fixed frame")
	_fixed_direction.item_selected.connect(_on_rigged_form_changed.unbind(1))
	_fixed_frame.item_selected.connect(_on_rigged_form_changed.unbind(1))
	parent.add_child(_wrapped_label("Initial T5 NPC runtime support uses a 1x1 logical footprint. Facing is placement-owned in Tiled."))


func _add_movement_section(parent: VBoxContainer) -> void:
	_add_heading(parent, "Movement", 18)
	var grid := _grid(parent)
	_movement_behavior = _option_field(grid, "Movement behavior")
	_movement_behavior.item_selected.connect(_on_movement_changed.unbind(1))
	_wander_radius = _spin_field(grid, "Wander radius tiles", 0, 128, 0, 1)
	_tick_interval = _spin_field(grid, "Tick interval ms", 600, 60000, 1000, 50)
	_idle_chance = _spin_field(grid, "Idle chance (0.0 to 1.0)", 0, 1, 0.5, 0.01)
	_movement_guidance = _wrapped_label("Movement behavior is reusable. Tiled placement supplies the NPC's home coordinate and initial facing.")
	parent.add_child(_movement_guidance)


func _add_interaction_section(parent: VBoxContainer) -> void:
	_add_heading(parent, "Interaction", 18)
	var grid := _grid(parent)
	_interaction_enabled = CheckBox.new()
	_interaction_enabled.text = "Enable interaction"
	_interaction_enabled.toggled.connect(_on_interaction_toggled)
	_register_control(_interaction_enabled)
	grid.add_child(Label.new())
	grid.add_child(_interaction_enabled)
	_interaction_range = _spin_field(grid, "Interaction range tiles", 1, 64, 1, 1)
	_default_interaction = _option_field(grid, "Default interaction")
	parent.add_child(_wrapped_label("T5 currently supports one server-authoritative Talk interaction."))


func _add_dialogue_section(parent: VBoxContainer) -> void:
	_add_heading(parent, "Dialogue Reference", 18)
	var grid := _grid(parent)
	_dialogue_id = _line_field(grid, "Default dialogue ID", "test_npc_greeting")
	_dialogue_options = _option_field(grid, "Known dialogue IDs")
	_dialogue_options.item_selected.connect(_on_dialogue_option_selected.unbind(1))
	grid.add_child(_label("Workspace"))
	_open_dialogue_button = Button.new()
	_open_dialogue_button.text = "Open Dialogue"
	_open_dialogue_button.disabled = true
	_open_dialogue_button.pressed.connect(_on_open_dialogue_pressed)
	grid.add_child(_open_dialogue_button)
	_dialogue_capability = _value_label(grid, "Complete reference validation", "Unknown")
	_dialogue_guidance = _wrapped_label("This field links to the current MMO Project dialogue definition. Dialogue and quest authoring remain separate future work.")
	parent.add_child(_dialogue_guidance)


func _add_notes_section(parent: VBoxContainer) -> void:
	_add_heading(parent, "Authoring Notes", 18)
	parent.add_child(_wrapped_label("Authoring notes are not exported to the runtime NPC catalog."))
	_notes = TextEdit.new()
	_notes.custom_minimum_size = Vector2(0, 120)
	_notes.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_notes.text_changed.connect(_on_form_changed)
	parent.add_child(_notes)
	_register_control(_notes)


func _add_runtime_guidance_section(parent: VBoxContainer) -> void:
	_add_heading(parent, "Runtime and Placement Guidance", 18)
	var status_grid := GridContainer.new()
	status_grid.columns = 2
	status_grid.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	status_grid.add_theme_constant_override("h_separation", 12)
	status_grid.add_theme_constant_override("v_separation", 8)
	parent.add_child(status_grid)
	_runtime_catalog_status = _guidance_status_tile(status_grid, "Runtime catalog", "Pending")
	_quest_status = _guidance_status_tile(status_grid, "Quest authoring", "Deferred")
	_multiple_interactions_status = _guidance_status_tile(status_grid, "Interactions", "Deferred")
	_placement_guidance = _wrapped_label("Tiled owns placement: npc_definition_id, spawn coordinates, home coordinates, map IDs, and facing.")
	parent.add_child(_placement_guidance)


func _add_preview_section(parent: VBoxContainer) -> void:
	_add_heading(parent, "Preview", 20)
	_visual_preview = NpcVisualPreview.new()
	parent.add_child(_visual_preview)
	_visual_status = _wrapped_label("No NPC visual selected.")
	parent.add_child(_visual_status)
	var facing_row := HBoxContainer.new()
	facing_row.add_theme_constant_override("separation", 8)
	facing_row.add_child(_label("Preview facing"))
	_preview_facing = OptionButton.new()
	for option in [
		{"id": "south", "display_name": "South"},
		{"id": "west", "display_name": "West"},
		{"id": "east", "display_name": "East"},
		{"id": "north", "display_name": "North"},
	]:
		_preview_facing.add_item(str(option.get("display_name", "")))
		_preview_facing.set_item_metadata(_preview_facing.item_count - 1, str(option.get("id", "")))
	_preview_facing.item_selected.connect(_on_preview_facing_changed.unbind(1))
	facing_row.add_child(_preview_facing)
	_preview_frame = OptionButton.new()
	for frame in [1, 2, 3, 4]:
		_preview_frame.add_item("F%d" % frame)
		_preview_frame.set_item_metadata(_preview_frame.item_count - 1, frame)
	_preview_frame.item_selected.connect(_on_preview_facing_changed.unbind(1))
	facing_row.add_child(_preview_frame)
	parent.add_child(facing_row)
	_add_heading(parent, "Operation", 16)
	_operation = OptionButton.new()
	_add_operation("Save as Draft", "save_draft")
	_add_operation("Publish", "publish")
	_add_operation("Disable", "disable")
	_add_operation("Delete", "delete")
	_operation.item_selected.connect(_on_operation_changed.unbind(1))
	parent.add_child(_operation)
	_preview_button = Button.new()
	_preview_button.text = "Validate and Preview Changes"
	_preview_button.pressed.connect(_preview)
	parent.add_child(_preview_button)
	_delete_button = Button.new()
	_delete_button.text = "Delete"
	_delete_button.disabled = true
	_delete_button.pressed.connect(_preview_delete)
	parent.add_child(_delete_button)
	_apply_button = Button.new()
	_apply_button.text = "Apply Previewed Operation"
	_apply_button.disabled = true
	_apply_button.pressed.connect(_apply)
	parent.add_child(_apply_button)
	_status = _wrapped_label("Load or create an NPC definition.")
	parent.add_child(_status)
	_add_heading(parent, "Reference Diagnostics", 16)
	_reference_summary = VBoxContainer.new()
	_reference_summary.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	parent.add_child(_reference_summary)
	_add_heading(parent, "Exact Logical Changes", 16)
	_changes = VBoxContainer.new()
	parent.add_child(_changes)
	_add_heading(parent, "Validation", 16)
	_validation = VBoxContainer.new()
	parent.add_child(_validation)


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


func _on_npc_options_received(payload: Dictionary) -> void:
	_schema_available = true
	_options = payload
	var visual_assets := _options.get("visual_assets", {}) as Dictionary
	var options_root := str(visual_assets.get("game_assets_root", ""))
	if not options_root.is_empty():
		_game_client_assets_root = options_root
	_apply_options()
	_set_form_enabled(not _current_npc.is_empty() or _is_new)
	if _current_npc.is_empty() and not _is_new:
		_status.text = "NPC schema ready. Load or create an NPC definition."


func _on_npc_catalog_received(payload: Dictionary) -> void:
	_schema_available = true
	_npcs = payload.get("items", []) as Array
	_rebuild_list()
	_set_form_enabled(not _current_npc.is_empty() or _is_new)
	if not _reload_npc_id.is_empty():
		var npc_id := _reload_npc_id
		_reload_npc_id = ""
		_client.load_npc(npc_id)


func _on_npc_definition_received(payload: Dictionary) -> void:
	_load_npc(payload)


func _on_npc_preview_received(payload: Dictionary) -> void:
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
	_render_reference_summary(payload.get("reference_summary", {}) as Dictionary)
	_asset_preview_file_path = str(payload.get("asset_preview_file_path", ""))
	_visual_preview.set_rigged_sprite_preview(payload.get("rigged_sprite_preview", {}) as Dictionary)
	_update_visual_preview()
	_status.text = "Preview ready." if applicable else "Preview contains blocking validation errors."


func _on_npc_mutation_completed(payload: Dictionary) -> void:
	var operation := str(payload.get("operation", "operation"))
	var npc := payload.get("npc", {}) as Dictionary
	var npc_id := str(npc.get("npc_definition_id", _npc_id.text))
	_reload_npc_id = npc_id
	_current_npc = npc
	_is_new = false
	_clear_preview()
	_status.text = "%s completed. Reloading NPC definition..." % _workspace_support.operation_name(operation)
	_client.load_npcs(_search.text)


func _on_npc_delete_completed(payload: Dictionary) -> void:
	var deleted_id := str(payload.get("deleted_id", _npc_id.text))
	_reload_npc_id = ""
	_start_new_npc()
	_status.text = "Deleted %s." % deleted_id
	_client.load_npcs(_search.text)


func _on_request_failed(operation: String, message: String, errors: Array) -> void:
	if not operation.begins_with("npc"):
		return
	_status.text = "%s failed: %s" % [operation, message]
	_workspace_support.render_validation(_validation, errors)
	_apply_button.disabled = true
	if _has_error_code(errors, "npc_version_conflict"):
		_status.text = "Version conflict. Reload the NPC definition before applying changes."
	if operation == "npc_options" or operation == "npcs":
		_schema_available = false
		_set_form_enabled(false)
		if message.contains("route does not exist"):
			_status.text = "NPCs unavailable: restart the authoring host from this branch, then relaunch Studio."
		else:
			_status.text = "NPCs unavailable: %s" % message


func _apply_options() -> void:
	var defaults := _options.get("defaults", {}) as Dictionary
	var limits := _options.get("supported_limits", {}) as Dictionary
	_set_spin_limits(_wander_radius, 0, int(limits.get("max_wander_radius_tiles", 128)))
	_set_spin_limits(_tick_interval, int(limits.get("minimum_tick_interval_ms", 600)), 60000)
	_set_spin_limits(_interaction_range, int(limits.get("minimum_interaction_range_tiles", 1)), 64)
	_footprint_width.min_value = int(limits.get("initial_footprint_width_tiles", 1))
	_footprint_width.max_value = int(limits.get("initial_footprint_width_tiles", 1))
	_footprint_height.min_value = int(limits.get("initial_footprint_height_tiles", 1))
	_footprint_height.max_value = int(limits.get("initial_footprint_height_tiles", 1))
	_fill_authoring_options(_movement_behavior, _options.get("movement_behaviors", []) as Array)
	_fill_authoring_options(_default_interaction, _options.get("interaction_types", []) as Array)
	_fill_dialogue_options()
	_render_capabilities()
	_apply_actor_appearance_options()
	if _current_npc.is_empty() and not _is_new:
		_select_option(_movement_behavior, str(defaults.get("movement_behavior", "static")))
		_select_option(_default_interaction, str(defaults.get("default_interaction", "talk")))
	_update_movement_controls()
	_update_interaction_controls()


func _rebuild_list() -> void:
	_clear_children(_list)
	var query := _search.text.strip_edges().to_lower()
	for variant in _npcs:
		if variant is not Dictionary:
			continue
		var npc := variant as Dictionary
		var dialogue := _nullable_string(npc.get("default_dialogue_id", ""))
		var haystack := "%s %s %s" % [
			npc.get("npc_definition_id", ""),
			npc.get("display_name", ""),
			dialogue,
		]
		if not query.is_empty() and not haystack.to_lower().contains(query):
			continue
		var button := Button.new()
		button.alignment = HORIZONTAL_ALIGNMENT_LEFT
		button.text = "%s\n%s | %s | %s | %s | %s" % [
			str(npc.get("display_name", "Unnamed NPC")),
			str(npc.get("npc_definition_id", "")),
			str(npc.get("publication_state", "Unknown")),
			str(npc.get("movement_behavior", "static")),
			"talk" if bool(npc.get("interaction_enabled", false)) else "no interaction",
			dialogue if not dialogue.is_empty() else "no dialogue",
		]
		button.tooltip_text = "%s\nUpdated %s" % [
			str(npc.get("visual_texture_path", "")),
			str(npc.get("updated_at_utc", "")),
		]
		button.pressed.connect(_load_npc_id.bind(str(npc.get("npc_definition_id", ""))))
		_list.add_child(button)


func _load_npc_id(npc_definition_id: String) -> void:
	if not npc_definition_id.is_empty():
		_client.load_npc(npc_definition_id)


func open_resource(npc_definition_id: String) -> void:
	_load_npc_id(npc_definition_id)


func _load_npc(payload: Dictionary) -> void:
	_is_loading = true
	_current_npc = payload
	_is_new = false
	_npc_id.text = str(payload.get("npc_definition_id", ""))
	_npc_id.editable = false
	_display_name.text = str(payload.get("display_name", ""))
	_publication.text = str(payload.get("publication_state", "Unknown"))
	_updated.text = str(payload.get("updated_at_utc", "Unknown"))
	_visual_path.text = str(payload.get("visual_texture_path", ""))
	_source_width.value = int(payload.get("source_width", 128))
	_source_height.value = int(payload.get("source_height", 128))
	_anchor_x.value = float(payload.get("visual_anchor_offset_x", 0.0))
	_anchor_y.value = float(payload.get("visual_anchor_offset_y", 0.0))
	_render_scale.value = float(payload.get("visual_render_scale", 0.25))
	_footprint_width.value = int(payload.get("footprint_width_tiles", 1))
	_footprint_height.value = int(payload.get("footprint_height_tiles", 1))
	_select_option(_movement_behavior, str(payload.get("movement_behavior", "static")))
	_wander_radius.value = int(payload.get("wander_radius_tiles", 0))
	_tick_interval.value = int(payload.get("tick_interval_ms", 1000))
	_idle_chance.value = float(payload.get("idle_chance", 0.5))
	_interaction_enabled.button_pressed = bool(payload.get("interaction_enabled", true))
	_interaction_range.value = int(payload.get("interaction_range_tiles", 1))
	_select_option(_default_interaction, str(payload.get("default_interaction", "talk")))
	_dialogue_id.text = _nullable_string(payload.get("default_dialogue_id", ""))
	_select_option(_dialogue_options, _dialogue_id.text.strip_edges())
	_notes.text = _nullable_string(payload.get("notes", ""))
	_asset_preview_file_path = str(payload.get("asset_preview_file_path", ""))
	_load_composite_visual(payload)
	_set_form_enabled(bool(payload.get("editable_in_npcs", true)) and _schema_available)
	_update_operation_default()
	_update_movement_controls()
	_update_interaction_controls()
	_update_visual_preview()
	_render_reference_summary({})
	_clear_preview()
	_status.text = "Loaded %s." % _npc_id.text
	_is_loading = false


func _start_new_npc() -> void:
	_is_loading = true
	var defaults := _options.get("defaults", {}) as Dictionary
	_current_npc = {}
	_is_new = true
	_npc_id.text = ""
	_npc_id.editable = _schema_available
	_display_name.text = ""
	_publication.text = "Unsaved"
	_updated.text = "Not saved"
	_visual_path.text = ""
	_source_width.value = 128
	_source_height.value = 128
	_anchor_x.value = 0
	_anchor_y.value = 0
	_render_scale.value = float(defaults.get("visual_render_scale", 0.25))
	_footprint_width.value = int(defaults.get("footprint_width_tiles", 1))
	_footprint_height.value = int(defaults.get("footprint_height_tiles", 1))
	_select_option(_movement_behavior, str(defaults.get("movement_behavior", "static")))
	_wander_radius.value = int(defaults.get("wander_radius_tiles", 0))
	_tick_interval.value = int(defaults.get("tick_interval_ms", 1000))
	_idle_chance.value = float(defaults.get("idle_chance", 0.5))
	_interaction_enabled.button_pressed = bool(defaults.get("interaction_enabled", true))
	_interaction_range.value = int(defaults.get("interaction_range_tiles", 1))
	_select_option(_default_interaction, str(defaults.get("default_interaction", "talk")))
	_dialogue_id.text = ""
	_select_option(_dialogue_options, "")
	_notes.text = ""
	_asset_preview_file_path = ""
	_composite_visual = {}
	_select_option(_visual_mode, "flat_sprite")
	_select_option(_operation, "save_draft")
	_set_form_enabled(_schema_available)
	_update_movement_controls()
	_update_interaction_controls()
	_update_visual_preview()
	_render_reference_summary({})
	_clear_preview()
	_status.text = "Creating a new reusable NPC definition."
	_npc_id.grab_focus()
	_is_loading = false


func _preview() -> void:
	var npc_definition_id := _npc_id.text.strip_edges()
	if npc_definition_id.is_empty():
		_status.text = "Enter a stable NPC definition ID before previewing."
		return
	var payload := _payload()
	payload.erase("preview_signature")
	payload["target_operation"] = _selected_metadata(_operation)
	payload["preview_direction"] = _selected_metadata(_preview_facing).to_upper().left(1)
	payload["preview_frame"] = int(_selected_metadata(_preview_frame))
	_client.preview_npc(npc_definition_id, payload)
	_status.text = "Calculating validation and exact logical changes..."


func _preview_delete() -> void:
	if _current_npc.is_empty():
		_status.text = "Select a saved disabled NPC definition before deleting."
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
	var npc_definition_id := _npc_id.text.strip_edges()
	var expected: Variant = _current_npc.get("updated_at_utc", null)
	match operation:
		"publish":
			_client.publish_npc(npc_definition_id, expected, preview_signature)
			_status.text = "Publishing saved NPC definition..."
		"disable":
			_client.disable_npc(npc_definition_id, expected, preview_signature)
			_status.text = "Disabling saved NPC definition..."
		"delete":
			_client.delete_npc(npc_definition_id, expected, preview_signature)
			_status.text = "Deleting NPC definition..."
		_:
			var payload := _payload()
			payload["preview_signature"] = preview_signature
			_client.save_npc_draft(npc_definition_id, payload)
			_status.text = "Saving complete NPC draft..."
	_apply_button.disabled = true


func _payload() -> Dictionary:
	var interaction_enabled := _interaction_enabled.button_pressed
	var dialogue_value := _dialogue_id.text.strip_edges() if interaction_enabled else ""
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
		"movement_behavior": _selected_metadata(_movement_behavior),
		"wander_radius_tiles": int(_wander_radius.value),
		"tick_interval_ms": int(_tick_interval.value),
		"idle_chance": float(_idle_chance.value),
		"interaction_enabled": interaction_enabled,
		"interaction_range_tiles": int(_interaction_range.value),
		"default_interaction": _selected_metadata(_default_interaction),
		"default_dialogue_id": _optional_payload(dialogue_value),
		"notes": _optional_payload(_notes.text),
		"expected_updated_at_utc": _current_npc.get("updated_at_utc", null),
		"preview_signature": null,
		"visual_mode": _selected_metadata(_visual_mode),
		"composite_visual": _build_composite_visual(),
	}


func _on_search_changed(value: String) -> void:
	_client.load_npcs(value)


func _refresh_catalog() -> void:
	_client.load_npcs(_search.text)


func _on_form_changed(_value: Variant = null) -> void:
	if _is_loading:
		return
	_clear_preview()
	_update_visual_preview()
	_update_dialogue_workspace_button()


func _on_visual_path_changed(_value: String) -> void:
	if _is_loading:
		return
	_asset_preview_file_path = ""
	_clear_preview()
	_update_visual_preview()


func _on_operation_changed() -> void:
	_clear_preview()


func _on_preview_facing_changed() -> void:
	_update_visual_preview()


func _on_visual_mode_changed() -> void:
	if _selected_metadata(_visual_mode) == "composite_rig" and _composite_visual.is_empty():
		_composite_visual = {
			"schema_version": 1,
			"rig_id": _first_rig_id(),
			"calibration_id": null,
			"pose_policy": "actor_pose",
			"fixed_direction": null,
			"fixed_frame": null,
			"cosmetic_item_ids": {},
		}
	_load_composite_visual(_composite_visual)
	_on_form_changed()


func _on_rig_changed() -> void:
	if _composite_visual.is_empty():
		return
	_composite_visual["rig_id"] = _selected_metadata(_rig_id)
	_composite_visual["calibration_id"] = null
	_composite_visual["cosmetic_item_ids"] = {}
	_load_composite_visual(_composite_visual)
	_status.text = "Rig changed. Incompatible calibration and cosmetic selections were cleared."
	_on_form_changed()


func _on_pose_policy_changed() -> void:
	if _composite_visual.is_empty():
		return
	_composite_visual["pose_policy"] = _selected_metadata(_pose_policy)
	if _composite_visual.get("pose_policy") == "actor_pose":
		_composite_visual["fixed_direction"] = null
		_composite_visual["fixed_frame"] = null
	_load_composite_visual(_composite_visual)
	_on_form_changed()


func _on_rigged_form_changed() -> void:
	if _composite_visual.is_empty():
		return
	_composite_visual["calibration_id"] = _optional_payload(_selected_metadata(_calibration_id))
	_composite_visual["pose_policy"] = _selected_metadata(_pose_policy)
	if _composite_visual.get("pose_policy") == "fixed":
		_composite_visual["fixed_direction"] = _selected_metadata(_fixed_direction)
		_composite_visual["fixed_frame"] = int(_selected_metadata(_fixed_frame))
	_on_form_changed()


func _apply_actor_appearance_options() -> void:
	if _visual_mode == null:
		return
	var appearance := _options.get("actor_appearance", {}) as Dictionary
	_fill_authoring_options(_visual_mode, appearance.get("visual_modes", []) as Array)
	if _visual_mode.item_count == 0:
		_visual_mode.add_item("Flat Sprite")
		_visual_mode.set_item_metadata(0, "flat_sprite")
	_select_option(_visual_mode, "flat_sprite")
	_rebuild_rigged_controls()


func _first_rig_id() -> String:
	var appearance := _options.get("actor_appearance", {}) as Dictionary
	var rigs := appearance.get("rigs", []) as Array
	return str((rigs[0] as Dictionary).get("rig_id", "")) if not rigs.is_empty() else ""


func _load_composite_visual(payload: Dictionary) -> void:
	if str(payload.get("visual_mode", "flat_sprite")) != "composite_rig":
		_composite_visual = {}
	else:
		var descriptor_variant: Variant = payload.get("composite_visual", payload)
		_composite_visual = (descriptor_variant as Dictionary).duplicate(true) if descriptor_variant is Dictionary else {}
	_select_option(_visual_mode, "composite_rig" if not _composite_visual.is_empty() else "flat_sprite")
	_rebuild_rigged_controls()


func _rebuild_rigged_controls() -> void:
	var rigged := _selected_metadata(_visual_mode) == "composite_rig"
	_rigged_controls.visible = rigged
	if not rigged:
		return
	var appearance := _options.get("actor_appearance", {}) as Dictionary
	_fill_rig_options(_rig_id, appearance.get("rigs", []) as Array)
	_select_option(_rig_id, str(_composite_visual.get("rig_id", _first_rig_id())))
	_fill_calibration_options()
	_fill_fixed_options()
	_select_option(_pose_policy, str(_composite_visual.get("pose_policy", "actor_pose")))
	var fixed := _selected_metadata(_pose_policy) == "fixed"
	_fixed_direction.visible = fixed
	_fixed_frame.visible = fixed
	_rebuild_cosmetic_controls()


func _fill_rig_options(control: OptionButton, rigs: Array) -> void:
	control.clear()
	for rig_variant in rigs:
		var rig := rig_variant as Dictionary
		var id := str(rig.get("rig_id", ""))
		control.add_item(id)
		control.set_item_metadata(control.item_count - 1, id)


func _fill_calibration_options() -> void:
	_calibration_id.clear()
	_calibration_id.add_item("Default")
	_calibration_id.set_item_metadata(0, "")
	for calibration_variant in (_options.get("actor_appearance", {}) as Dictionary).get("calibrations", []) as Array:
		var calibration := calibration_variant as Dictionary
		if str(calibration.get("rig_id", "")) == _selected_metadata(_rig_id):
			var id := str(calibration.get("calibration_id", ""))
			_calibration_id.add_item(id)
			_calibration_id.set_item_metadata(_calibration_id.item_count - 1, id)
	_select_option(_calibration_id, str(_composite_visual.get("calibration_id", "")))


func _fill_fixed_options() -> void:
	_fill_authoring_options(_pose_policy, [{"id":"actor_pose", "display_name":"Actor Pose"}, {"id":"fixed", "display_name":"Fixed Pose"}])
	_fill_authoring_options(_fixed_direction, [{"id":"N", "display_name":"N"}, {"id":"E", "display_name":"E"}, {"id":"S", "display_name":"S"}, {"id":"W", "display_name":"W"}])
	_fill_authoring_options(_fixed_frame, [{"id":"1", "display_name":"F1"}, {"id":"2", "display_name":"F2"}, {"id":"3", "display_name":"F3"}, {"id":"4", "display_name":"F4"}])
	_select_option(_fixed_direction, str(_composite_visual.get("fixed_direction", "S")))
	_select_option(_fixed_frame, str(_composite_visual.get("fixed_frame", 1)))


func _rebuild_cosmetic_controls() -> void:
	for control in _cosmetic_controls.values():
		control.queue_free()
	_cosmetic_controls.clear()
	var rig_id := _selected_metadata(_rig_id)
	var cosmetics := _composite_visual.get("cosmetic_item_ids", {}) as Dictionary
	var visuals := (_options.get("actor_appearance", {}) as Dictionary).get("equipped_visuals", []) as Array
	for layer_variant in _selected_rig_layers():
		var layer := layer_variant as Dictionary
		var layer_id := str(layer.get("layer_id", ""))
		var compatible: Array = []
		for visual_variant in visuals:
			var visual := visual_variant as Dictionary
			if str(visual.get("rig_id", "")) == rig_id and str(visual.get("render_layer_id", "")) == layer_id and str(visual.get("binding_type", "")) == "socket":
				compatible.append(visual)
		if compatible.is_empty():
			continue
		var row := HBoxContainer.new()
		row.add_child(_label(layer_id.replace("_", " ").capitalize()))
		var selector := OptionButton.new()
		selector.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		selector.add_item("None")
		selector.set_item_metadata(0, "")
		for visual_variant in compatible:
			var visual := visual_variant as Dictionary
			var item_id := str(visual.get("item_id", ""))
			selector.add_item(item_id)
			selector.set_item_metadata(selector.item_count - 1, item_id)
		_select_option(selector, str(cosmetics.get(layer_id, "")))
		selector.item_selected.connect(_on_cosmetic_changed.bind(layer_id, selector).unbind(1))
		_rigged_controls.add_child(row)
		row.add_child(selector)
		_cosmetic_controls[layer_id] = row


func _selected_rig_layers() -> Array:
	for rig_variant in (_options.get("actor_appearance", {}) as Dictionary).get("rigs", []) as Array:
		var rig := rig_variant as Dictionary
		if str(rig.get("rig_id", "")) == _selected_metadata(_rig_id):
			return rig.get("layers", []) as Array
	return []


func _on_cosmetic_changed(layer_id: String, selector: OptionButton) -> void:
	var cosmetics := _composite_visual.get("cosmetic_item_ids", {}) as Dictionary
	var item_id := _selected_metadata(selector)
	if item_id.is_empty():
		cosmetics.erase(layer_id)
	else:
		cosmetics[layer_id] = item_id
	_composite_visual["cosmetic_item_ids"] = cosmetics
	_on_form_changed()


func _build_composite_visual() -> Variant:
	if _selected_metadata(_visual_mode) != "composite_rig":
		return null
	_on_rigged_form_changed()
	return _composite_visual


func _on_movement_changed() -> void:
	_update_movement_controls()
	_on_form_changed()


func _on_interaction_toggled(_value: bool) -> void:
	_update_interaction_controls()
	_on_form_changed()


func _on_dialogue_option_selected() -> void:
	var selected := _selected_metadata(_dialogue_options)
	if not selected.is_empty():
		_dialogue_id.text = selected
	_on_form_changed()


func _on_open_dialogue_pressed() -> void:
	var dialogue_id := _dialogue_id.text.strip_edges()
	if not dialogue_id.is_empty():
		workspace_open_requested.emit("dialogue", dialogue_id)


func _update_movement_controls() -> void:
	var movement := _selected_metadata(_movement_behavior)
	var can_wander := _form_editable and movement == "random_wander"
	_wander_radius.editable = can_wander
	if movement == "static":
		_wander_radius.value = 0


func _update_interaction_controls() -> void:
	var enabled := _form_editable and _interaction_enabled.button_pressed
	_interaction_range.editable = enabled
	_default_interaction.disabled = not enabled
	_dialogue_id.editable = enabled
	_dialogue_options.disabled = not enabled or _dialogue_options.item_count <= 1
	if not _interaction_enabled.button_pressed:
		_dialogue_id.text = ""
		_select_option(_dialogue_options, "")
	_update_dialogue_workspace_button()


func _update_dialogue_workspace_button() -> void:
	if _open_dialogue_button == null:
		return
	_open_dialogue_button.disabled = _dialogue_id.text.strip_edges().is_empty()


func _update_visual_preview() -> void:
	if _visual_preview == null:
		return
	var resolved := _resolve_visual_preview_file_path()
	var file_path := str(resolved.get("file_path", ""))
	var status := str(resolved.get("status", "No NPC visual selected."))
	var texture: Texture2D = null
	if not file_path.is_empty():
		var image := Image.load_from_file(file_path)
		if image != null and not image.is_empty():
			texture = ImageTexture.create_from_image(image)
		else:
			status = "The resolved NPC PNG could not be loaded."
	_visual_preview.set_payload(
		texture,
		int(_source_width.value),
		int(_source_height.value),
		float(_anchor_x.value),
		float(_anchor_y.value),
		float(_render_scale.value),
		int(_footprint_width.value),
		int(_footprint_height.value),
		_selected_metadata(_preview_facing),
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
			"status": "No NPC visual selected.",
		}
	if resource_path.begins_with(GAME_ASSET_PREFIX):
		if _game_client_assets_root.is_empty() or not DirAccess.dir_exists_absolute(_game_client_assets_root):
			return {
				"file_path": "",
				"status": "Configure game_client_assets to preview NPC visuals.",
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


func _render_capabilities() -> void:
	var capabilities := _options.get("capabilities", {}) as Dictionary
	_runtime_catalog_status.text = "Available" if bool(capabilities.get("supports_runtime_npc_catalog", false)) else "Not yet implemented"
	_quest_status.text = "Supported" if bool(capabilities.get("supports_quest_authoring", false)) else "Not supported"
	_multiple_interactions_status.text = "Supported" if bool(capabilities.get("supports_multiple_interactions", false)) else "Not supported"
	var complete_dialogue := bool(capabilities.get("supports_complete_dialogue_reference_validation", _options.get("can_validate_dialogue_references", false)))
	_dialogue_capability.text = "Complete" if complete_dialogue else "Syntax-only"
	_dialogue_guidance.text = "This field links to the current MMO Project dialogue definition. Dialogue and quest authoring remain separate future work."
	if not complete_dialogue:
		_dialogue_guidance.text += "\nRuntime dialogue catalog visibility is incomplete, so the host validates the dialogue ID syntax only."


func _render_reference_summary(summary: Dictionary) -> void:
	_clear_children(_reference_summary)
	if summary.is_empty():
		_workspace_support.add_wrapped_label(_reference_summary, "Reference visibility is incomplete until runtime/Tiled handoff work is finished.")
		return
	_workspace_support.add_wrapped_label(_reference_summary, "Known references: %d" % int(summary.get("known_reference_count", 0)))
	var sources := summary.get("reference_sources", []) as Array
	if sources.is_empty():
		_workspace_support.add_wrapped_label(_reference_summary, "Reference sources: none reported.")
	else:
		for source in sources:
			_workspace_support.add_wrapped_label(_reference_summary, "• %s" % str(source))
	if not bool(summary.get("reference_check_complete", false)):
		_workspace_support.add_wrapped_label(_reference_summary, "Reference visibility is incomplete until runtime/Tiled handoff work is finished.")


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
		elif control is CheckBox:
			(control as CheckBox).disabled = not editable
		elif control is TextEdit:
			(control as TextEdit).editable = editable
	_npc_id.editable = editable and _is_new
	_operation.disabled = not editable
	_preview_button.disabled = not editable
	_delete_button.disabled = not editable or _current_npc.is_empty()
	if not editable:
		_apply_button.disabled = true
	_update_movement_controls()
	_update_interaction_controls()


func _clear_preview() -> void:
	_workspace_support.clear_preview(_apply_button, _changes, _validation)
	_render_reference_summary({})


func _update_operation_default() -> void:
	var state := str(_current_npc.get("publication_state", "Unsaved"))
	_select_option(_operation, "disable" if state == "Published" else "save_draft")


func _fill_authoring_options(control: OptionButton, values: Array) -> void:
	var selected := _selected_metadata(control)
	control.clear()
	for variant in values:
		if variant is Dictionary:
			var option := variant as Dictionary
			control.add_item(str(option.get("display_name", option.get("id", "Option"))))
			control.set_item_metadata(control.item_count - 1, str(option.get("id", "")))
	_select_option(control, selected)


func _fill_dialogue_options() -> void:
	var selected := _dialogue_id.text.strip_edges()
	_dialogue_options.clear()
	_dialogue_options.add_item("Type a dialogue ID")
	_dialogue_options.set_item_metadata(0, "")
	for variant in _options.get("dialogue_references", []) as Array:
		if variant is Dictionary:
			var option := variant as Dictionary
			_dialogue_options.add_item(str(option.get("display_name", option.get("id", "Dialogue"))))
			_dialogue_options.set_item_metadata(_dialogue_options.item_count - 1, str(option.get("id", "")))
	_select_option(_dialogue_options, selected)


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
	field.value_changed.connect(_on_form_changed.unbind(1))
	grid.add_child(field)
	_register_control(field)
	return field


func _option_field(grid: GridContainer, label_text: String) -> OptionButton:
	grid.add_child(_label(label_text))
	var option := OptionButton.new()
	option.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	option.item_selected.connect(_on_form_changed.unbind(1))
	grid.add_child(option)
	_register_control(option)
	return option


func _value_label(grid: GridContainer, label_text: String, value: String) -> Label:
	grid.add_child(_label(label_text))
	var label := _wrapped_label(value)
	grid.add_child(label)
	return label


func _guidance_status_tile(parent: GridContainer, label_text: String, value: String) -> Label:
	var tile := VBoxContainer.new()
	tile.custom_minimum_size = Vector2(160, 0)
	tile.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	tile.add_theme_constant_override("separation", 2)
	var label := Label.new()
	label.text = label_text
	label.add_theme_font_size_override("font_size", 13)
	label.modulate = Color(0.72, 0.75, 0.82, 1)
	label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	tile.add_child(label)
	var status := Label.new()
	status.text = value
	status.autowrap_mode = TextServer.AUTOWRAP_OFF
	status.clip_text = true
	status.text_overrun_behavior = TextServer.OVERRUN_TRIM_ELLIPSIS
	status.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	tile.add_child(status)
	parent.add_child(tile)
	return status


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
	label.custom_minimum_size = Vector2(185, 0)
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


func _optional_payload(value: String) -> Variant:
	var trimmed := value.strip_edges()
	return null if trimmed.is_empty() else trimmed


func _nullable_string(value: Variant) -> String:
	return "" if value == null else str(value)


func _has_error_code(errors: Array, code: String) -> bool:
	for variant in errors:
		if variant is Dictionary and str((variant as Dictionary).get("code", "")) == code:
			return true
	return false


func _clear_children(container: Node) -> void:
	for child in container.get_children():
		child.queue_free()
