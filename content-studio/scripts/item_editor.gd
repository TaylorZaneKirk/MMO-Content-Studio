extends HBoxContainer
class_name UnifiedItemEditor

const CATALOG_PANE_TOGGLE := preload("res://scripts/catalog_pane_toggle.gd")

const WORKSPACE_SUPPORT_SCRIPT := preload("res://scripts/authoring_workspace_support.gd")
const PAPER_DOLL_PREVIEW_SCRIPT := preload("res://scripts/paper_doll_preview.gd")

const COMBAT_UNIT_MILLISECONDS := 600
const ATTACHMENT_ANCHOR_LIMIT := 4096
const DEFAULT_BONUS_FIELDS := [
	{"id": "attack_thrust", "display_name": "Attack Thrust"},
	{"id": "attack_slash", "display_name": "Attack Slash"},
	{"id": "attack_crush", "display_name": "Attack Crush"},
	{"id": "attack_ranged", "display_name": "Attack Ranged"},
	{"id": "attack_magic", "display_name": "Attack Magic"},
	{"id": "strength_melee", "display_name": "Strength Melee"},
	{"id": "strength_ranged", "display_name": "Strength Ranged"},
	{"id": "strength_magic", "display_name": "Strength Magic"},
	{"id": "defence_thrust", "display_name": "Defence Thrust"},
	{"id": "defence_slash", "display_name": "Defence Slash"},
	{"id": "defence_crush", "display_name": "Defence Crush"},
	{"id": "defence_ranged", "display_name": "Defence Ranged"},
	{"id": "defence_magic", "display_name": "Defence Magic"},
]
const DEFAULT_EQUIPMENT_SLOTS := [
	{"id": "head", "display_name": "Head"},
	{"id": "body", "display_name": "Body"},
	{"id": "legs", "display_name": "Legs"},
	{"id": "boots", "display_name": "Boots"},
	{"id": "gloves", "display_name": "Gloves"},
	{"id": "cape", "display_name": "Cape"},
	{"id": "right_hand", "display_name": "Right Hand"},
	{"id": "left_hand", "display_name": "Left Hand"},
]
const DEFAULT_WEAPON_CAPABLE_SLOTS := [{"id": "right_hand", "display_name": "Right Hand"}]

var _client: AuthoringHostClient
var _workspace_support
var _paper_doll_preview
var _items: Array = []
var _assets: Array = []
var _asset_by_path: Dictionary = {}
var _options: Dictionary = {}
var _current_item: Dictionary = {}
var _is_loading := false
var _reload_item_id := ""
var _mutation_reload_pending := false
var _game_client_assets_root := ""
var _has_persisted_equipped_visual := false
var _appearance_defaults_initialized := false

var _search: LineEdit
var _list: VBoxContainer
var _item_id: LineEdit
var _display_name: LineEdit
var _icon: OptionButton
var _icon_preview: TextureRect
var _publication: Label
var _classification: Label
var _kind: Label
var _updated: Label
var _consumable_enabled: CheckBox
var _use_action: OptionButton
var _consume_quantity: SpinBox
var _result_item_id: LineEdit
var _success_message: LineEdit
var _usable_in_combat: CheckBox
var _cooldown_ms: SpinBox
var _animation_id: LineEdit
var _sound_path: LineEdit
var _reference_value: LineEdit
var _trade_policy: OptionButton
var _death_behavior: OptionButton
var _death_transform_item_id: LineEdit
var _shop_policy: OptionButton
var _npc_buy_price: LineEdit
var _npc_sell_price: LineEdit
var _reclaim_policy: OptionButton
var _reclaim_value: LineEdit
var _condition_policy_id: LineEdit
var _repair_policy_id: LineEdit
var _consumable_requirements: VBoxContainer
var _consumable_effects: VBoxContainer
var _equipable: CheckBox
var _equipment_slot: OptionButton
var _required_strength: SpinBox
var _equip_note: Label
var _appearance_section: VBoxContainer
var _requirements_section: VBoxContainer
var _combat_bonus_section: VBoxContainer
var _weapon_section: VBoxContainer
var _tool_section: VBoxContainer
var _requirements: VBoxContainer
var _modifiers: VBoxContainer
var _bonus_controls: Dictionary = {}
var _weapon_enabled: CheckBox
var _weapon_profile_id: LineEdit
var _weapon_attack_type: OptionButton
var _weapon_accuracy_style: OptionButton
var _weapon_min_range: SpinBox
var _weapon_max_range: SpinBox
var _weapon_speed_units: SpinBox
var _weapon_timing: Label
var _tool_rows: VBoxContainer
var _operation: OptionButton
var _preview_button: Button
var _delete_button: Button
var _apply_button: Button
var _status: Label
var _changes: VBoxContainer
var _validation: VBoxContainer
var _file_dialog: FileDialog
var _paper_doll_stage: Control
var _paper_doll_status: Label
var _preview_direction: OptionButton
var _preview_frame: SpinBox
var _appearance_enabled: CheckBox
var _appearance_rig: OptionButton
var _appearance_binding: OptionButton
var _appearance_render_layer: OptionButton
var _appearance_socket: OptionButton
var _appearance_asset_key: LineEdit
var _appearance_asset_path: Label
var _appearance_rig_status: Label
var _appearance_nudge_x: SpinBox
var _appearance_nudge_y: SpinBox
var _appearance_actual_scale: CheckBox
var _appearance_zoom_out: Button
var _appearance_zoom_label: Label
var _appearance_zoom_in: Button
var _appearance_fit: Button
var _appearance_grip_x: SpinBox
var _appearance_grip_y: SpinBox
var _appearance_grip_row: HBoxContainer
var _appearance_grip_actions: HBoxContainer
var _appearance_grip_marker_legend: Label
var _appearance_visible_in_pose: CheckBox
var _appearance_item_over_grip: CheckBox
var _appearance_flip_x: CheckBox
var _appearance_clear_pose: Button
var _appearance_copy_previous: Button
var _appearance_copy_next: Button
var _equipped_visual_grip_anchors: Dictionary = {}
var _equipped_visual_flip_x: Dictionary = {}
var _equipped_visual_hidden_poses: Dictionary = {}
var _equipped_visual_item_over_grip: Dictionary = {}
var _appearance_updating := false
var _grip_pose_art_available := false
var _pending_grip_anchor_handoff: Dictionary = {}


func _ready() -> void:
	_workspace_support = WORKSPACE_SUPPORT_SCRIPT.new()
	_paper_doll_preview = PAPER_DOLL_PREVIEW_SCRIPT.new()
	_paper_doll_preview.grip_anchor_changed.connect(_on_paper_doll_grip_anchor_changed)
	_client = %AuthoringHostClient
	_build_ui()
	_refresh_preview_zoom_controls()
	_connect_client()
	_set_form_enabled(false)


func _connect_client() -> void:
	_client.health_received.connect(_on_health_received)
	_client.item_assets_received.connect(_on_assets_received)
	_client.item_asset_imported.connect(_on_asset_imported)
	_client.item_options_received.connect(_on_options_received)
	_client.item_catalog_received.connect(_on_catalog_received)
	_client.item_definition_received.connect(_on_definition_received)
	_client.item_preview_received.connect(_on_preview_received)
	_client.item_mutation_completed.connect(_on_mutation_completed)
	_client.item_delete_completed.connect(_on_delete_completed)
	_client.request_failed.connect(_on_request_failed)


func open_resource(item_id: String) -> void:
	_client.load_item_definition(item_id)


func stage_grip_anchor_handoff(item_id: String, grip_anchors: Dictionary) -> void:
	_pending_grip_anchor_handoff = {
		"item_id": item_id,
		"grip_anchors": grip_anchors.duplicate(true),
	}
	_client.load_item_definition(item_id)


func _build_ui() -> void:
	add_theme_constant_override("separation", 14)

	var catalog_panel := _panel()
	catalog_panel.custom_minimum_size = Vector2(310, 0)
	add_child(catalog_panel)
	var catalog := VBoxContainer.new()
	catalog.add_theme_constant_override("separation", 10)
	catalog_panel.add_child(catalog)
	catalog.add_child(_heading("Items", 20))
	_search = LineEdit.new()
	_search.placeholder_text = "Search item ID, name, or classification"
	_search.text_changed.connect(_on_search_changed.unbind(1))
	catalog.add_child(_search)
	var new_button := Button.new()
	new_button.text = "+ New Item"
	new_button.pressed.connect(_start_new)
	catalog.add_child(new_button)
	var refresh := Button.new()
	refresh.text = "Refresh"
	refresh.pressed.connect(_refresh_catalog)
	catalog.add_child(refresh)
	var catalog_scroll := ScrollContainer.new()
	catalog_scroll.size_flags_vertical = Control.SIZE_EXPAND_FILL
	catalog.add_child(catalog_scroll)
	_list = VBoxContainer.new()
	_list.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_list.add_theme_constant_override("separation", 6)
	catalog_scroll.add_child(_list)
	CATALOG_PANE_TOGGLE.attach(self, catalog_panel)

	var editor_panel := _panel()
	editor_panel.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	add_child(editor_panel)
	var editor_scroll := ScrollContainer.new()
	editor_scroll.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	editor_scroll.size_flags_vertical = Control.SIZE_EXPAND_FILL
	editor_panel.add_child(editor_scroll)
	var editor := VBoxContainer.new()
	editor.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	editor.add_theme_constant_override("separation", 12)
	editor_scroll.add_child(editor)
	editor.add_child(_heading("Complete Item Definition", 20))

	var identity_grid := _section_grid(editor, "Identity and Inventory")
	_item_id = _add_line_field(identity_grid, "Stable item ID", "iron_ore")
	_display_name = _add_line_field(identity_grid, "Display name", "Iron Ore")
	identity_grid.add_child(_field_label("Inventory / ground icon"))
	var icon_row := HBoxContainer.new()
	icon_row.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	identity_grid.add_child(icon_row)
	_icon = OptionButton.new()
	_icon.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_icon.item_selected.connect(_on_form_changed.unbind(1))
	icon_row.add_child(_icon)
	var import_button := Button.new()
	import_button.text = "Import PNG..."
	import_button.pressed.connect(_open_import)
	icon_row.add_child(import_button)
	_publication = _add_value_field(identity_grid, "Publication state", "No item selected")
	_classification = _add_value_field(identity_grid, "Classification", "Unknown")
	_kind = _add_value_field(identity_grid, "Authoring kind", "Unknown")
	_updated = _add_value_field(identity_grid, "Last updated", "Unknown")

	var consumable_grid := _section_grid(editor, "Consumable Behavior")
	consumable_grid.add_child(_field_label("Consumable"))
	_consumable_enabled = CheckBox.new()
	_consumable_enabled.text = "Enable consumable behavior"
	_consumable_enabled.toggled.connect(_on_consumable_toggled)
	consumable_grid.add_child(_consumable_enabled)
	_use_action = _add_option_field(consumable_grid, "Use action")
	_consume_quantity = _add_spin_field(consumable_grid, "Quantity consumed", 1, 999, 1)
	_result_item_id = _add_line_field(consumable_grid, "Result item ID", "Optional item ID")
	_success_message = _add_line_field(consumable_grid, "Success message", "Optional message")
	consumable_grid.add_child(_field_label("Usable in combat"))
	_usable_in_combat = CheckBox.new()
	_usable_in_combat.button_pressed = true
	_usable_in_combat.toggled.connect(_on_form_changed.unbind(1))
	consumable_grid.add_child(_usable_in_combat)
	_cooldown_ms = _add_spin_field(consumable_grid, "Cooldown ms", 0, 86400000, 100)
	_animation_id = _add_line_field(consumable_grid, "Use animation ID", "Optional semantic ID")
	_sound_path = _add_line_field(consumable_grid, "Sound resource path", "Optional res://assets/... path")
	editor.add_child(_row_header("Consumable Requirements", "+ Requirement", _add_consumable_requirement_row))
	_consumable_requirements = _rows()
	editor.add_child(_consumable_requirements)
	editor.add_child(_row_header("Consumable Effects", "+ Effect", _add_consumable_effect_row))
	_consumable_effects = _rows()
	editor.add_child(_consumable_effects)

	var economy_grid := _section_grid(editor, "Economy and Lifecycle")
	_reference_value = _add_line_field(economy_grid, "Reference value", "0")
	_trade_policy = _add_option_field(economy_grid, "Trade policy")
	_death_behavior = _add_option_field(economy_grid, "Death behavior")
	_death_behavior.item_selected.connect(_on_economy_policy_changed.unbind(1))
	_death_transform_item_id = _add_line_field(economy_grid, "Transform target item ID", "Required only for transform")
	_shop_policy = _add_option_field(economy_grid, "Shop policy")
	_shop_policy.item_selected.connect(_on_economy_policy_changed.unbind(1))
	_npc_buy_price = _add_line_field(economy_grid, "NPC buy price", "Non-negative integer")
	_npc_sell_price = _add_line_field(economy_grid, "NPC sell price", "Non-negative integer")
	_reclaim_policy = _add_option_field(economy_grid, "Reclaim policy")
	_reclaim_policy.item_selected.connect(_on_economy_policy_changed.unbind(1))
	_reclaim_value = _add_line_field(economy_grid, "Reclaim value", "Non-negative integer")
	_condition_policy_id = _add_line_field(economy_grid, "Reserved condition policy ID", "Draft planning only")
	_repair_policy_id = _add_line_field(economy_grid, "Reserved repair policy ID", "Draft planning only")
	var economy_note := Label.new()
	economy_note.text = "Economy and lifecycle metadata is authoring-only in V1. Death, shop, trade, reclaim, condition, and repair behavior are not executed."
	economy_note.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	economy_grid.add_child(economy_note)

	var equip_grid := _section_grid(editor, "Equipability")
	equip_grid.add_child(_field_label("Equipable"))
	_equipable = CheckBox.new()
	_equipable.text = "Item can be equipped"
	_equipable.toggled.connect(_on_equipable_toggled)
	equip_grid.add_child(_equipable)
	_equipment_slot = _add_option_field(equip_grid, "Equipment slot")
	_equipment_slot.item_selected.connect(_on_slot_changed.unbind(1))
	_required_strength = _add_spin_field(equip_grid, "Required strength", 1, 1000000, 1)
	_equip_note = Label.new()
	_equip_note.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	_equip_note.modulate = Color(0.7, 0.73, 0.79, 1)
	_equip_note.text = "Disabling equipability removes equipment requirements, modifiers, combat bonuses, and weapon profile. Tool capabilities and consumable behavior remain."
	editor.add_child(_equip_note)

	_appearance_section = VBoxContainer.new()
	_appearance_section.add_theme_constant_override("separation", 8)
	editor.add_child(_appearance_section)
	_appearance_section.add_child(_heading("Equipped Appearance", 16))
	var doll_row := HBoxContainer.new()
	doll_row.add_theme_constant_override("separation", 16)
	_appearance_section.add_child(doll_row)
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
	_appearance_enabled = CheckBox.new()
	_appearance_enabled.text = "Enable authored equipped visual metadata"
	_appearance_enabled.toggled.connect(_on_appearance_enabled_toggled)
	doll_controls.add_child(_appearance_enabled)
	_appearance_rig = OptionButton.new()
	_appearance_rig.item_selected.connect(_on_appearance_rig_changed.unbind(1))
	_add_control_row(doll_controls, "Rig", _appearance_rig)
	_appearance_binding = OptionButton.new()
	_appearance_binding.item_selected.connect(_on_appearance_binding_changed.unbind(1))
	_add_control_row(doll_controls, "Binding", _appearance_binding)
	_appearance_render_layer = OptionButton.new()
	_appearance_render_layer.item_selected.connect(_on_appearance_render_layer_changed.unbind(1))
	_add_control_row(doll_controls, "Render layer", _appearance_render_layer)
	_appearance_socket = OptionButton.new()
	_appearance_socket.item_selected.connect(_on_appearance_socket_changed.unbind(1))
	_add_control_row(doll_controls, "Socket", _appearance_socket)
	_appearance_asset_key = LineEdit.new()
	_appearance_asset_key.placeholder_text = "dark_sword"
	_appearance_asset_key.text_changed.connect(_on_form_changed.unbind(1))
	_add_control_row(doll_controls, "Visual asset key", _appearance_asset_key)
	var direction_row := HBoxContainer.new()
	direction_row.add_child(_field_label("Direction"))
	_preview_direction = OptionButton.new()
	for direction in ["N", "S", "E", "W"]:
		_preview_direction.add_item(direction)
		_preview_direction.set_item_metadata(_preview_direction.item_count - 1, direction)
	_preview_direction.item_selected.connect(_on_visual_preview_changed.unbind(1))
	direction_row.add_child(_preview_direction)
	doll_controls.add_child(direction_row)
	var frame_row := HBoxContainer.new()
	frame_row.add_child(_field_label("Frame"))
	_preview_frame = SpinBox.new()
	_preview_frame.min_value = 1
	_preview_frame.max_value = 4
	_preview_frame.step = 1
	_preview_frame.value = 3
	_preview_frame.value_changed.connect(_on_visual_preview_changed.unbind(1))
	frame_row.add_child(_preview_frame)
	doll_controls.add_child(frame_row)
	var nudge_row := HBoxContainer.new()
	nudge_row.add_theme_constant_override("separation", 6)
	nudge_row.add_child(_field_label("Nudge"))
	_appearance_nudge_x = _row_spin(-64, 64, 0)
	_appearance_nudge_x.value_changed.connect(_on_form_changed.unbind(1))
	nudge_row.add_child(_appearance_nudge_x)
	_appearance_nudge_y = _row_spin(-64, 64, 0)
	_appearance_nudge_y.value_changed.connect(_on_form_changed.unbind(1))
	nudge_row.add_child(_appearance_nudge_y)
	doll_controls.add_child(nudge_row)
	_appearance_grip_row = HBoxContainer.new()
	_appearance_grip_row.add_theme_constant_override("separation", 6)
	_appearance_grip_row.add_child(_field_label("Grip Anchor X/Y"))
	_appearance_grip_x = _row_spin(-4096, 4096, 0)
	_appearance_grip_x.value_changed.connect(_on_grip_spin_changed.unbind(1))
	_appearance_grip_row.add_child(_appearance_grip_x)
	_appearance_grip_y = _row_spin(-4096, 4096, 0)
	_appearance_grip_y.value_changed.connect(_on_grip_spin_changed.unbind(1))
	_appearance_grip_row.add_child(_appearance_grip_y)
	doll_controls.add_child(_appearance_grip_row)
	_appearance_grip_marker_legend = Label.new()
	_appearance_grip_marker_legend.text = "Grip Anchor: source-art pixel aligned to the selected actor socket. Markers: Actor Socket (gold, read-only) | Item Grip Anchor (pink, draggable)."
	_appearance_grip_marker_legend.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	doll_controls.add_child(_appearance_grip_marker_legend)
	_appearance_visible_in_pose = CheckBox.new()
	_appearance_visible_in_pose.text = "Visible in this pose"
	_appearance_visible_in_pose.button_pressed = true
	_appearance_visible_in_pose.toggled.connect(_on_appearance_visible_in_pose_toggled)
	doll_controls.add_child(_appearance_visible_in_pose)
	_appearance_item_over_grip = CheckBox.new()
	_appearance_item_over_grip.text = "Render in front of hand"
	_appearance_item_over_grip.toggled.connect(_on_appearance_item_over_grip_toggled)
	doll_controls.add_child(_appearance_item_over_grip)
	_appearance_flip_x = CheckBox.new()
	_appearance_flip_x.text = "Flip horizontally"
	_appearance_flip_x.toggled.connect(_on_appearance_flip_x_toggled)
	doll_controls.add_child(_appearance_flip_x)
	_appearance_grip_actions = HBoxContainer.new()
	_appearance_grip_actions.add_theme_constant_override("separation", 6)
	_appearance_copy_previous = Button.new()
	_appearance_copy_previous.text = "Copy Prev"
	_appearance_copy_previous.pressed.connect(_copy_previous_pose_anchor)
	_appearance_grip_actions.add_child(_appearance_copy_previous)
	_appearance_copy_next = Button.new()
	_appearance_copy_next.text = "Copy Next"
	_appearance_copy_next.pressed.connect(_copy_next_pose_anchor)
	_appearance_grip_actions.add_child(_appearance_copy_next)
	_appearance_clear_pose = Button.new()
	_appearance_clear_pose.text = "Clear Pose"
	_appearance_clear_pose.pressed.connect(_clear_current_pose_anchor)
	_appearance_grip_actions.add_child(_appearance_clear_pose)
	var nudge_left := Button.new()
	nudge_left.text = "X-"
	nudge_left.pressed.connect(_nudge_current_grip_anchor.bind(-1, 0))
	_appearance_grip_actions.add_child(nudge_left)
	var nudge_right := Button.new()
	nudge_right.text = "X+"
	nudge_right.pressed.connect(_nudge_current_grip_anchor.bind(1, 0))
	_appearance_grip_actions.add_child(nudge_right)
	var nudge_up := Button.new()
	nudge_up.text = "Y-"
	nudge_up.pressed.connect(_nudge_current_grip_anchor.bind(0, -1))
	_appearance_grip_actions.add_child(nudge_up)
	var nudge_down := Button.new()
	nudge_down.text = "Y+"
	nudge_down.pressed.connect(_nudge_current_grip_anchor.bind(0, 1))
	_appearance_grip_actions.add_child(nudge_down)
	doll_controls.add_child(_appearance_grip_actions)
	var zoom_row := HBoxContainer.new()
	zoom_row.add_theme_constant_override("separation", 6)
	_appearance_zoom_out = Button.new()
	_appearance_zoom_out.text = "-"
	_appearance_zoom_out.custom_minimum_size = Vector2(36, 0)
	_appearance_zoom_out.pressed.connect(_on_preview_zoom_out_pressed)
	zoom_row.add_child(_appearance_zoom_out)
	_appearance_zoom_label = Label.new()
	_appearance_zoom_label.custom_minimum_size = Vector2(90, 0)
	zoom_row.add_child(_appearance_zoom_label)
	_appearance_zoom_in = Button.new()
	_appearance_zoom_in.text = "+"
	_appearance_zoom_in.custom_minimum_size = Vector2(36, 0)
	_appearance_zoom_in.pressed.connect(_on_preview_zoom_in_pressed)
	zoom_row.add_child(_appearance_zoom_in)
	_appearance_fit = Button.new()
	_appearance_fit.text = "Fit"
	_appearance_fit.pressed.connect(_on_preview_zoom_fit_pressed)
	zoom_row.add_child(_appearance_fit)
	doll_controls.add_child(zoom_row)
	_appearance_actual_scale = CheckBox.new()
	_appearance_actual_scale.text = "Actual game scale"
	_appearance_actual_scale.toggled.connect(_on_preview_actual_scale_toggled)
	doll_controls.add_child(_appearance_actual_scale)
	_appearance_asset_path = Label.new()
	_appearance_asset_path.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	_appearance_asset_path.modulate = Color(0.7, 0.73, 0.79, 1)
	doll_controls.add_child(_appearance_asset_path)
	_appearance_rig_status = Label.new()
	_appearance_rig_status.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	_appearance_rig_status.modulate = Color(0.7, 0.73, 0.79, 1)
	doll_controls.add_child(_appearance_rig_status)
	_paper_doll_status = Label.new()
	_paper_doll_status.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	_paper_doll_status.modulate = Color(0.7, 0.73, 0.79, 1)
	doll_controls.add_child(_paper_doll_status)
	_paper_doll_preview.bind(_paper_doll_stage, _paper_doll_status)

	_requirements_section = VBoxContainer.new()
	_requirements_section.add_theme_constant_override("separation", 8)
	editor.add_child(_requirements_section)
	_requirements_section.add_child(_row_header("Requirements and Skill Modifiers", "+ Requirement", _add_requirement_row))
	_requirements = _rows()
	_requirements_section.add_child(_requirements)
	_requirements_section.add_child(_row_header("Skill Modifiers", "+ Modifier", _add_modifier_row))
	_modifiers = _rows()
	_requirements_section.add_child(_modifiers)

	_combat_bonus_section = VBoxContainer.new()
	_combat_bonus_section.add_theme_constant_override("separation", 8)
	editor.add_child(_combat_bonus_section)
	_combat_bonus_section.add_child(_heading("Combat Bonuses", 16))
	var bonus_grid := GridContainer.new()
	bonus_grid.columns = 4
	bonus_grid.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_combat_bonus_section.add_child(bonus_grid)
	_rebuild_bonus_grid(bonus_grid)

	_weapon_section = VBoxContainer.new()
	_weapon_section.add_theme_constant_override("separation", 8)
	editor.add_child(_weapon_section)
	_weapon_section.add_child(_heading("Weapon Profile", 16))
	_weapon_enabled = CheckBox.new()
	_weapon_enabled.text = "Weapon profile enabled"
	_weapon_enabled.toggled.connect(_on_weapon_enabled_toggled)
	_weapon_section.add_child(_weapon_enabled)
	var weapon_grid := GridContainer.new()
	weapon_grid.columns = 2
	weapon_grid.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_weapon_section.add_child(weapon_grid)
	_weapon_profile_id = _add_line_field(weapon_grid, "Profile ID", "iron_sword_melee")
	_weapon_attack_type = _add_option_field(weapon_grid, "Attack type")
	_weapon_accuracy_style = _add_option_field(weapon_grid, "Accuracy style")
	_weapon_min_range = _add_spin_field(weapon_grid, "Minimum range tiles", 0, 32, 1)
	_weapon_max_range = _add_spin_field(weapon_grid, "Maximum range tiles", 0, 32, 1)
	_weapon_speed_units = _add_spin_field(weapon_grid, "Attack speed units", 1, 60, 1)
	_weapon_timing = _add_value_field(weapon_grid, "Attack interval", "4 attack units x 600 ms = 2400 ms")

	_tool_section = VBoxContainer.new()
	_tool_section.add_theme_constant_override("separation", 8)
	editor.add_child(_tool_section)
	var tool_note := Label.new()
	tool_note.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	tool_note.modulate = Color(0.7, 0.73, 0.79, 1)
	tool_note.text = "Tool capabilities work from inventory or equipment. Equipability is optional."
	_tool_section.add_child(_row_header("Tool Capabilities", "+ Capability", _add_tool_row))
	_tool_section.add_child(tool_note)
	_tool_rows = _rows()
	_tool_section.add_child(_tool_rows)

	var preview_panel := _panel()
	preview_panel.custom_minimum_size = Vector2(330, 0)
	add_child(preview_panel)
	var preview_scroll := ScrollContainer.new()
	preview_scroll.size_flags_vertical = Control.SIZE_EXPAND_FILL
	preview_panel.add_child(preview_scroll)
	var preview := VBoxContainer.new()
	preview.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	preview.add_theme_constant_override("separation", 10)
	preview_scroll.add_child(preview)
	preview.add_child(_heading("Preview", 20))
	_icon_preview = TextureRect.new()
	_icon_preview.custom_minimum_size = Vector2(160, 160)
	_icon_preview.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	_icon_preview.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED
	preview.add_child(_icon_preview)
	preview.add_child(_heading("Operation", 16))
	_operation = OptionButton.new()
	for option in [["Save as Draft", "save_draft"], ["Publish", "publish"], ["Disable", "disable"], ["Delete", "delete"]]:
		_operation.add_item(option[0])
		_operation.set_item_metadata(_operation.item_count - 1, option[1])
	_operation.item_selected.connect(_on_operation_changed.unbind(1))
	preview.add_child(_operation)
	_preview_button = Button.new()
	_preview_button.text = "Validate and Preview Changes"
	_preview_button.pressed.connect(_preview)
	preview.add_child(_preview_button)
	_delete_button = Button.new()
	_delete_button.text = "Delete"
	_delete_button.disabled = true
	_delete_button.pressed.connect(_preview_delete)
	preview.add_child(_delete_button)
	_apply_button = Button.new()
	_apply_button.text = "Apply Previewed Operation"
	_apply_button.disabled = true
	_apply_button.pressed.connect(_apply)
	preview.add_child(_apply_button)
	_status = Label.new()
	_status.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	_status.modulate = Color(0.7, 0.73, 0.79, 1)
	_status.text = "Select an item or create a new one."
	preview.add_child(_status)
	preview.add_child(_heading("Exact Logical Changes", 16))
	_changes = VBoxContainer.new()
	preview.add_child(_changes)
	preview.add_child(_heading("Validation", 16))
	_validation = VBoxContainer.new()
	preview.add_child(_validation)

	_file_dialog = FileDialog.new()
	_file_dialog.file_mode = FileDialog.FILE_MODE_OPEN_FILE
	_file_dialog.access = FileDialog.ACCESS_FILESYSTEM
	_file_dialog.filters = PackedStringArray(["*.png ; PNG images"])
	_file_dialog.file_selected.connect(_import_selected)
	add_child(_file_dialog)

	for edit in [_item_id, _display_name, _result_item_id, _success_message, _animation_id, _sound_path, _weapon_profile_id, _reference_value, _death_transform_item_id, _npc_buy_price, _npc_sell_price, _reclaim_value, _condition_policy_id, _repair_policy_id]:
		edit.text_changed.connect(_on_form_changed.unbind(1))
	for spin in [_consume_quantity, _cooldown_ms, _required_strength, _weapon_min_range, _weapon_max_range]:
		spin.value_changed.connect(_on_form_changed.unbind(1))
	_weapon_speed_units.value_changed.connect(_on_weapon_speed_changed.unbind(1))
	_update_contextual_sections()
	_update_weapon_timing()


func _on_health_received(payload: Dictionary) -> void:
	_game_client_assets_root = ""
	for variant in payload.get("asset_roots", []) as Array:
		if variant is Dictionary and str((variant as Dictionary).get("id", "")) == "game_client_assets":
			_game_client_assets_root = str((variant as Dictionary).get("path", ""))
			break
	_paper_doll_preview.game_client_assets_root = _game_client_assets_root
	_paper_doll_preview.clear_cache()
	_update_paper_doll_preview()


func _on_assets_received(payload: Dictionary) -> void:
	_assets = payload.get("assets", []) as Array
	_rebuild_asset_options(_selected_metadata(_icon))


func _on_asset_imported(payload: Dictionary) -> void:
	var asset := payload.get("asset", {}) as Dictionary
	var resource_path := str(asset.get("resource_path", ""))
	if not resource_path.is_empty() and not _asset_by_path.has(resource_path):
		_assets.append(asset)
	_rebuild_asset_options(resource_path)
	_status.text = str(payload.get("message", "Item asset imported."))
	_clear_preview()


func _on_options_received(payload: Dictionary) -> void:
	_options = payload
	_fill_option(_equipment_slot, _option_array("equipment_slots", DEFAULT_EQUIPMENT_SLOTS))
	_fill_option(_use_action, _option_array("use_actions", [{"id": "use", "display_name": "Use"}]))
	_fill_option(_trade_policy, _option_array("trade_policies", [{"id": "tradeable", "display_name": "Tradeable"}, {"id": "untradeable", "display_name": "Untradeable"}]))
	_fill_option(_death_behavior, _option_array("death_behaviors", [{"id": "ordinary", "display_name": "Ordinary"}, {"id": "always_keep", "display_name": "Always Keep"}, {"id": "always_destroy", "display_name": "Always Destroy"}, {"id": "transform", "display_name": "Transform"}, {"id": "reclaim", "display_name": "Reclaim"}]))
	_fill_option(_shop_policy, _option_array("shop_policies", [{"id": "not_shop_traded", "display_name": "Not Shop Traded"}, {"id": "npc_buys", "display_name": "NPC Buys"}, {"id": "npc_sells", "display_name": "NPC Sells"}, {"id": "npc_buys_and_sells", "display_name": "NPC Buys and Sells"}]))
	_fill_option(_reclaim_policy, _option_array("reclaim_policies", [{"id": "none", "display_name": "None"}, {"id": "fixed_cost", "display_name": "Fixed Cost"}]))
	_fill_option(_weapon_attack_type, _option_array("attack_families", [{"id": "melee", "display_name": "Melee"}]))
	_fill_option(_weapon_accuracy_style, _option_array("attack_styles", [{"id": "slash", "display_name": "Slash"}, {"id": "crush", "display_name": "Crush"}, {"id": "thrust", "display_name": "Thrust"}]))
	_fill_option(_appearance_binding, _option_array("equipped_visual_binding_types", [{"id": "rig_layer", "display_name": "Rig Layer"}, {"id": "socket", "display_name": "Socket"}]))
	_apply_actor_rig_catalog(payload.get("actor_rig_catalog", {}))
	_rebuild_bonus_grid(_bonus_controls.get("_grid", null))
	_update_weapon_timing()
	_update_economy_controls()


func _on_catalog_received(payload: Dictionary) -> void:
	_items = payload.get("items", []) as Array
	_rebuild_list()
	if not _reload_item_id.is_empty():
		var item_id := _reload_item_id
		_reload_item_id = ""
		_client.load_item_definition(item_id)


func _on_definition_received(payload: Dictionary) -> void:
	_is_loading = true
	_mutation_reload_pending = false
	_reload_item_id = ""
	_cancel_paper_doll_drag()
	_current_item = payload.duplicate(true)
	_item_id.text = str(payload.get("item_id", ""))
	_item_id.editable = false
	_display_name.text = str(payload.get("display_name", ""))
	_rebuild_asset_options(str(payload.get("icon_texture_path", "")))
	_publication.text = str(payload.get("publication_state", "Unknown"))
	_classification.text = str(payload.get("classification_label", "Unknown"))
	_kind.text = str(payload.get("authoring_kind", "Unknown"))
	_updated.text = str(payload.get("updated_at_utc", "Unknown"))
	_apply_consumable(payload.get("consumable_behavior", null))
	_apply_economy(payload.get("economy_lifecycle", {}))
	_apply_equipment(payload.get("equipment", null))
	_clear_rows(_tool_rows)
	for variant in payload.get("tool_capabilities", []) as Array:
		if variant is Dictionary:
			_add_tool_row(variant as Dictionary)
	_update_operation_default()
	_set_form_enabled(true)
	_update_icon_preview(str(payload.get("asset_preview_file_path", "")))
	_update_paper_doll_preview()
	_is_loading = false
	_update_contextual_sections()
	_clear_preview()
	if _apply_pending_grip_anchor_handoff():
		return
	_status.text = "Loaded %s." % _item_id.text


func _apply_pending_grip_anchor_handoff() -> bool:
	if str(_pending_grip_anchor_handoff.get("item_id", "")) != _item_id.text:
		return false
	var anchors_variant: Variant = _pending_grip_anchor_handoff.get("grip_anchors", {})
	_pending_grip_anchor_handoff = {}
	if not _appearance_enabled.button_pressed or _selected_metadata(_appearance_binding) != "socket" or not (anchors_variant is Dictionary):
		_status.text = "The alignment workspace handoff could not be applied because this item no longer has a socket-bound equipped visual."
		return true
	_equipped_visual_grip_anchors = (anchors_variant as Dictionary).duplicate(true)
	_update_grip_pose_controls()
	_update_paper_doll_preview()
	_on_form_changed()
	_status.text = "Loaded grip-anchor edits from the alignment workspace. Validate and save this complete item draft to persist them."
	return true


func _on_preview_received(payload: Dictionary) -> void:
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
	_update_icon_preview(str(payload.get("asset_preview_file_path", "")))
	_status.text = "Preview ready." if applicable else "Preview contains blocking validation errors."


func _on_mutation_completed(payload: Dictionary) -> void:
	var operation := str(payload.get("operation", "operation"))
	var item := payload.get("item", {}) as Dictionary
	var item_id := str(item.get("item_id", _item_id.text))
	_current_item = item.duplicate(true)
	_reload_item_id = item_id
	_mutation_reload_pending = true
	_updated.text = str(_current_item.get("updated_at_utc", _updated.text))
	_publication.text = str(_current_item.get("publication_state", _publication.text))
	_classification.text = str(_current_item.get("classification_label", _classification.text))
	_kind.text = str(_current_item.get("authoring_kind", _kind.text))
	_clear_preview()
	_set_form_enabled(false)
	_status.text = "%s completed. Reloading complete aggregate..." % _workspace_support.operation_name(operation)
	_client.search_item_catalog(_search.text)


func _on_delete_completed(payload: Dictionary) -> void:
	var deleted_id := str(payload.get("deleted_id", _item_id.text))
	_start_new()
	_status.text = "Deleted %s." % deleted_id
	_client.search_item_catalog(_search.text)


func _on_request_failed(operation: String, message: String, errors: Array) -> void:
	if not operation.begins_with("item") and operation != "item_asset_import":
		return
	_status.text = "%s failed: %s" % [operation, message]
	_workspace_support.render_validation(_validation, errors)
	_apply_button.disabled = true
	if _has_error_code(errors, "item_version_conflict"):
		_status.text = "Version conflict. Reload the item definition before applying changes."
	if _mutation_reload_pending and operation in ["items", "item"]:
		_reload_item_id = ""
		_mutation_reload_pending = false
		_status.text = "Reload after item mutation failed: %s" % message


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
			item.get("classification_label", item.get("authoring_kind", "")),
		]
		if not query.is_empty() and not haystack.to_lower().contains(query):
			continue
		var button := Button.new()
		button.alignment = HORIZONTAL_ALIGNMENT_LEFT
		button.text = "%s\n%s | %s | %s" % [
			str(item.get("display_name", "Unnamed item")),
			str(item.get("item_id", "")),
			str(item.get("publication_state", "Unknown")),
			str(item.get("classification_label", item.get("authoring_kind", "Unknown"))),
		]
		button.tooltip_text = str(item.get("updated_at_utc", ""))
		button.pressed.connect(_client.load_item_definition.bind(str(item.get("item_id", ""))))
		_list.add_child(button)


func _start_new() -> void:
	_is_loading = true
	_mutation_reload_pending = false
	_reload_item_id = ""
	_cancel_paper_doll_drag()
	_current_item = {}
	_has_persisted_equipped_visual = false
	_appearance_defaults_initialized = false
	_item_id.text = ""
	_item_id.editable = true
	_display_name.text = ""
	_select_option(_icon, "")
	_publication.text = "Unsaved"
	_classification.text = "Basic"
	_kind.text = "Unified"
	_updated.text = "Not saved"
	_apply_consumable(null)
	_apply_economy({})
	_apply_equipment(null)
	_clear_rows(_tool_rows)
	_select_option(_operation, "save_draft")
	_set_form_enabled(true)
	_update_icon_preview("")
	_update_paper_doll_preview()
	_is_loading = false
	_update_contextual_sections()
	_clear_preview()
	_item_id.grab_focus()
	_status.text = "Creating a new complete item aggregate."


func _apply_consumable(value: Variant) -> void:
	var enabled := value is Dictionary
	var consumable := value as Dictionary if enabled else {}
	_consumable_enabled.button_pressed = enabled
	_select_option(_use_action, str(consumable.get("use_action", "use")))
	_consume_quantity.value = float(consumable.get("consume_quantity", 1))
	_result_item_id.text = _nullable_string(consumable.get("result_item_id", ""))
	_success_message.text = _nullable_string(consumable.get("success_message", ""))
	_usable_in_combat.button_pressed = bool(consumable.get("usable_in_combat", true))
	_cooldown_ms.value = float(consumable.get("cooldown_ms", 0))
	_animation_id.text = _nullable_string(consumable.get("use_animation_id", ""))
	_sound_path.text = _nullable_string(consumable.get("use_sound_resource_path", ""))
	_clear_rows(_consumable_requirements)
	for variant in consumable.get("requirements", []) as Array:
		if variant is Dictionary:
			_add_consumable_requirement_row(variant as Dictionary)
	_clear_rows(_consumable_effects)
	for variant in consumable.get("effects", []) as Array:
		if variant is Dictionary:
			_add_consumable_effect_row(variant as Dictionary)


func _apply_equipment(value: Variant) -> void:
	var enabled := value is Dictionary
	var equipment := value as Dictionary if enabled else {}
	_equipable.button_pressed = enabled
	_select_option(_equipment_slot, str(equipment.get("equipment_slot_id", "right_hand")))
	_required_strength.value = float(equipment.get("required_strength", 1))
	_clear_rows(_requirements)
	for variant in equipment.get("requirements", []) as Array:
		if variant is Dictionary:
			_add_requirement_row(variant as Dictionary)
	_clear_rows(_modifiers)
	for variant in equipment.get("skill_modifiers", []) as Array:
		if variant is Dictionary:
			_add_modifier_row(variant as Dictionary)
	var bonuses := equipment.get("combat_bonuses", {}) as Dictionary
	_apply_bonus_values(bonuses)
	_apply_weapon_profile(equipment.get("weapon_profile", null))
	_apply_equipped_visual(equipment.get("equipped_visual", null))


func _payload() -> Dictionary:
	return {
		"display_name": _display_name.text,
		"icon_texture_path": _selected_metadata(_icon),
		"consumable_behavior": _consumable_payload() if _consumable_enabled.button_pressed else null,
		"equipment": _equipment_payload() if _equipable.button_pressed else null,
		"tool_capabilities": _collect_tool_capabilities(),
		"economy_lifecycle": _economy_payload(),
		"expected_updated_at_utc": _current_item.get("updated_at_utc", null),
		"preview_signature": null,
	}


func _preview() -> void:
	var item_id := _item_id.text.strip_edges()
	if item_id.is_empty():
		_status.text = "Enter a stable item ID before previewing."
		return
	if not _has_valid_economy_integers():
		_status.text = "Economy values must be exact non-negative 64-bit integers."
		return
	var payload := _payload()
	payload.erase("preview_signature")
	payload["target_operation"] = _selected_metadata(_operation)
	_client.preview_item_operation(item_id, payload)
	_status.text = "Calculating validation and exact logical changes..."


func _preview_delete() -> void:
	if _current_item.is_empty():
		_status.text = "Select a saved disabled item before deleting."
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
	if not _has_valid_economy_integers():
		_status.text = "Economy values must be exact non-negative 64-bit integers."
		return
	var item_id := _item_id.text.strip_edges()
	var expected: Variant = _current_item.get("updated_at_utc", null)
	match operation:
		"publish":
			_client.publish_item(item_id, expected, preview_signature)
		"disable":
			_client.disable_item(item_id, expected, preview_signature)
		"delete":
			_client.delete_item(item_id, expected, preview_signature)
		_:
			var payload := _payload()
			payload["preview_signature"] = preview_signature
			_client.save_complete_item_draft(item_id, payload)
	_apply_button.disabled = true
	_status.text = "Applying unified item operation..."


func _consumable_payload() -> Dictionary:
	return {
		"use_action": _selected_metadata(_use_action),
		"consume_quantity": int(_consume_quantity.value),
		"result_item_id": _optional_payload(_result_item_id.text),
		"success_message": _optional_payload(_success_message.text),
		"usable_in_combat": _usable_in_combat.button_pressed,
		"cooldown_ms": int(_cooldown_ms.value),
		"use_animation_id": _optional_payload(_animation_id.text),
		"use_sound_resource_path": _optional_payload(_sound_path.text),
		"requirements": _collect_consumable_requirements(),
		"effects": _collect_consumable_effects(),
	}


func _economy_payload() -> Dictionary:
	var transform := _selected_metadata(_death_behavior) == "transform"
	var shop := _selected_metadata(_shop_policy)
	var reclaim := _selected_metadata(_death_behavior) == "reclaim"
	return {
		"reference_value": _economic_integer(_reference_value),
		"trade_policy": _selected_metadata(_trade_policy),
		"death_behavior": _selected_metadata(_death_behavior),
		"death_transform_item_id": _optional_payload(_death_transform_item_id.text) if transform else null,
		"shop_policy": shop,
		"npc_buy_price": _economic_integer(_npc_buy_price) if shop == "npc_buys" or shop == "npc_buys_and_sells" else null,
		"npc_sell_price": _economic_integer(_npc_sell_price) if shop == "npc_sells" or shop == "npc_buys_and_sells" else null,
		"reclaim_policy": _selected_metadata(_reclaim_policy) if reclaim else "none",
		"reclaim_value": _economic_integer(_reclaim_value) if reclaim else null,
		"condition_policy_id": _optional_payload(_condition_policy_id.text),
		"repair_policy_id": _optional_payload(_repair_policy_id.text),
	}


func _apply_economy(value: Variant) -> void:
	var economy: Dictionary = value as Dictionary if value is Dictionary else {}
	_reference_value.text = _integer_text(economy.get("reference_value", 0))
	_select_option(_trade_policy, str(economy.get("trade_policy", "tradeable")))
	_select_option(_death_behavior, str(economy.get("death_behavior", "ordinary")))
	_death_transform_item_id.text = _nullable_string(economy.get("death_transform_item_id", null))
	_select_option(_shop_policy, str(economy.get("shop_policy", "not_shop_traded")))
	_npc_buy_price.text = _integer_text(economy.get("npc_buy_price", null))
	_npc_sell_price.text = _integer_text(economy.get("npc_sell_price", null))
	_select_option(_reclaim_policy, str(economy.get("reclaim_policy", "none")))
	_reclaim_value.text = _integer_text(economy.get("reclaim_value", null))
	_condition_policy_id.text = _nullable_string(economy.get("condition_policy_id", null))
	_repair_policy_id.text = _nullable_string(economy.get("repair_policy_id", null))
	_update_economy_controls()


func _on_economy_policy_changed() -> void:
	_update_economy_controls()
	_on_form_changed()


func _update_economy_controls() -> void:
	var transform := _selected_metadata(_death_behavior) == "transform"
	var reclaim := _selected_metadata(_death_behavior) == "reclaim"
	var shop := _selected_metadata(_shop_policy)
	_death_transform_item_id.editable = transform
	_npc_buy_price.editable = shop == "npc_buys" or shop == "npc_buys_and_sells"
	_npc_sell_price.editable = shop == "npc_sells" or shop == "npc_buys_and_sells"
	_reclaim_policy.disabled = not reclaim
	_reclaim_value.editable = reclaim


func _economic_integer(control: LineEdit) -> int:
	return int(control.text.strip_edges())


func _has_valid_economy_integers() -> bool:
	for control in [_reference_value, _npc_buy_price, _npc_sell_price, _reclaim_value]:
		var line_edit := control as LineEdit
		var value: String = line_edit.text.strip_edges()
		if not value.is_empty() and (not value.is_valid_int() or value.begins_with("-")):
			return false
	if _reference_value.text.strip_edges().is_empty():
		return false
	var shop := _selected_metadata(_shop_policy)
	if (shop == "npc_buys" or shop == "npc_buys_and_sells") and _npc_buy_price.text.strip_edges().is_empty():
		return false
	if (shop == "npc_sells" or shop == "npc_buys_and_sells") and _npc_sell_price.text.strip_edges().is_empty():
		return false
	if _selected_metadata(_death_behavior) == "reclaim" and _reclaim_value.text.strip_edges().is_empty():
		return false
	return true


func _equipment_payload() -> Dictionary:
	return {
		"equipment_slot_id": _selected_metadata(_equipment_slot),
		"required_strength": int(_required_strength.value),
		"requirements": _collect_requirements(),
		"skill_modifiers": _collect_modifiers(),
		"combat_bonuses": _collect_bonuses(),
		"weapon_profile": _weapon_profile_payload(),
		"equipped_visual": _equipped_visual_payload(),
	}


func _weapon_profile_payload() -> Variant:
	if not _weapon_enabled.button_pressed or not _is_weapon_capable_slot(_selected_metadata(_equipment_slot)):
		return null
	return {
		"profile_id": _weapon_profile_id.text.strip_edges(),
		"attack_type": _selected_metadata(_weapon_attack_type),
		"accuracy_style": _selected_metadata(_weapon_accuracy_style),
		"minimum_range_tiles": int(_weapon_min_range.value),
		"maximum_range_tiles": int(_weapon_max_range.value),
		"attack_speed_units": int(_weapon_speed_units.value),
	}


func _equipped_visual_payload() -> Variant:
	if not _appearance_enabled.button_pressed:
		return null
	var binding_type := _selected_metadata(_appearance_binding)
	var payload := {
		"asset_key": _optional_payload(_appearance_asset_key.text),
		"rig_id": _selected_metadata(_appearance_rig),
		"binding_type": binding_type,
		"render_layer_id": _selected_metadata(_appearance_render_layer),
		"socket_id": _optional_payload(_selected_metadata(_appearance_socket)) if binding_type == "socket" else null,
		"secondary_socket_id": null,
		"nudge": {
			"x": int(_appearance_nudge_x.value),
			"y": int(_appearance_nudge_y.value),
		},
		"grip_anchors": _copy_grip_anchor_payload(),
		"flip_x": _copy_flip_x_payload(),
		"hidden_poses": _copy_hidden_pose_payload(),
		"item_over_grip": _copy_item_over_grip_payload(),
	}
	return payload


func _copy_grip_anchor_payload() -> Dictionary:
	var copied: Dictionary = {}
	for direction_variant: Variant in _equipped_visual_grip_anchors.keys():
		var direction := str(direction_variant)
		var frames_variant: Variant = _equipped_visual_grip_anchors.get(direction, {})
		if not (frames_variant is Dictionary):
			continue
		var frames := frames_variant as Dictionary
		var copied_frames: Dictionary = {}
		for frame_variant: Variant in frames.keys():
			var frame := str(frame_variant)
			var point_variant: Variant = frames.get(frame, null)
			if point_variant is Dictionary:
				var point := point_variant as Dictionary
				copied_frames[frame] = {
					"x": _normalize_attachment_anchor_coordinate(point.get("x", 0)),
					"y": _normalize_attachment_anchor_coordinate(point.get("y", 0)),
				}
		if not copied_frames.is_empty():
			copied[direction] = copied_frames
	return copied


func _copy_flip_x_payload() -> Dictionary:
	var copied: Dictionary = {}
	for direction_variant: Variant in _equipped_visual_flip_x.keys():
		var direction := str(direction_variant)
		var frames_variant: Variant = _equipped_visual_flip_x.get(direction, {})
		if not (frames_variant is Dictionary):
			continue
		var copied_frames: Dictionary = {}
		for frame_variant: Variant in (frames_variant as Dictionary).keys():
			var frame := str(frame_variant)
			if bool((frames_variant as Dictionary).get(frame, false)):
				copied_frames[frame] = true
		if not copied_frames.is_empty():
			copied[direction] = copied_frames
	return copied


func _copy_hidden_pose_payload() -> Dictionary:
	var copied: Dictionary = {}
	for direction_variant: Variant in _equipped_visual_hidden_poses.keys():
		var direction := str(direction_variant)
		var frames_variant: Variant = _equipped_visual_hidden_poses.get(direction, {})
		if not (frames_variant is Dictionary):
			continue
		var copied_frames: Dictionary = {}
		for frame_variant: Variant in (frames_variant as Dictionary).keys():
			var frame := str(frame_variant)
			if bool((frames_variant as Dictionary).get(frame, false)):
				copied_frames[frame] = true
		if not copied_frames.is_empty():
			copied[direction] = copied_frames
	return copied


func _copy_item_over_grip_payload() -> Dictionary:
	var copied: Dictionary = {}
	for direction_variant: Variant in _equipped_visual_item_over_grip.keys():
		var direction := str(direction_variant)
		var frames_variant: Variant = _equipped_visual_item_over_grip.get(direction, {})
		if not (frames_variant is Dictionary):
			continue
		var copied_frames: Dictionary = {}
		for frame_variant: Variant in (frames_variant as Dictionary).keys():
			var frame := str(frame_variant)
			if bool((frames_variant as Dictionary).get(frame, false)):
				copied_frames[frame] = true
		if not copied_frames.is_empty():
			copied[direction] = copied_frames
	return copied


func _apply_equipped_visual(value: Variant) -> void:
	_cancel_paper_doll_drag()
	_equipped_visual_grip_anchors.clear()
	_equipped_visual_flip_x.clear()
	_equipped_visual_hidden_poses.clear()
	_equipped_visual_item_over_grip.clear()
	var has_visual := value is Dictionary
	var equipped_visual := value as Dictionary if has_visual else {}
	_has_persisted_equipped_visual = has_visual
	_appearance_defaults_initialized = has_visual
	_appearance_enabled.button_pressed = has_visual
	_appearance_asset_key.text = str(equipped_visual.get("asset_key", ""))
	_select_option(_appearance_rig, str(equipped_visual.get("rig_id", _first_actor_rig_id())))
	_rebuild_actor_rig_controls()
	_select_option(_appearance_binding, str(equipped_visual.get("binding_type", "rig_layer")))
	_select_option(_appearance_render_layer, str(equipped_visual.get("render_layer_id", _selected_metadata(_equipment_slot))))
	_select_option(_appearance_socket, str(equipped_visual.get("socket_id", "")))
	var nudge := equipped_visual.get("nudge", {}) as Dictionary
	_appearance_nudge_x.value = float(nudge.get("x", 0))
	_appearance_nudge_y.value = float(nudge.get("y", 0))
	var grip_anchors := equipped_visual.get("grip_anchors", {}) as Dictionary
	for direction_variant: Variant in grip_anchors.keys():
		var direction := str(direction_variant)
		var frames_variant: Variant = grip_anchors.get(direction, {})
		if frames_variant is Dictionary:
			_equipped_visual_grip_anchors[direction] = (frames_variant as Dictionary).duplicate(true)
	var flip_x_by_pose := equipped_visual.get("flip_x", {}) as Dictionary
	for direction_variant: Variant in flip_x_by_pose.keys():
		var direction := str(direction_variant)
		var frames_variant: Variant = flip_x_by_pose.get(direction, {})
		if frames_variant is Dictionary:
			_equipped_visual_flip_x[direction] = (frames_variant as Dictionary).duplicate(true)
	var hidden_poses := equipped_visual.get("hidden_poses", {}) as Dictionary
	for direction_variant: Variant in hidden_poses.keys():
		var direction := str(direction_variant)
		var frames_variant: Variant = hidden_poses.get(direction, {})
		if frames_variant is Dictionary:
			_equipped_visual_hidden_poses[direction] = (frames_variant as Dictionary).duplicate(true)
	var item_over_grip := equipped_visual.get("item_over_grip", {}) as Dictionary
	for direction_variant: Variant in item_over_grip.keys():
		var direction := str(direction_variant)
		var frames_variant: Variant = item_over_grip.get(direction, {})
		if frames_variant is Dictionary:
			_equipped_visual_item_over_grip[direction] = (frames_variant as Dictionary).duplicate(true)
	_update_grip_pose_controls()


func _apply_actor_rig_catalog(catalog_variant: Variant) -> void:
	var catalog := catalog_variant as Dictionary if catalog_variant is Dictionary else {}
	_paper_doll_preview.configure_rig_catalog(catalog)
	_fill_rig_options(catalog.get("rigs", []) as Array if catalog.get("rigs", []) is Array else [])
	_appearance_rig_status.text = _actor_rig_catalog_status(catalog)
	_rebuild_actor_rig_controls()
	_update_contextual_sections()


func _fill_rig_options(rigs: Array) -> void:
	var selected := _selected_metadata(_appearance_rig)
	var first_rig_id := ""
	_appearance_rig.clear()
	for variant in rigs:
		if variant is Dictionary:
			var rig := variant as Dictionary
			var rig_id := str(rig.get("rig_id", ""))
			if rig_id.is_empty():
				continue
			if first_rig_id.is_empty():
				first_rig_id = rig_id
			_appearance_rig.add_item(rig_id)
			_appearance_rig.set_item_metadata(_appearance_rig.item_count - 1, rig_id)
	if _appearance_rig.item_count > 0:
		_select_option(_appearance_rig, selected if not selected.is_empty() else first_rig_id)


func _rebuild_actor_rig_controls() -> void:
	var rig := _selected_actor_rig()
	var selected_layer := _selected_metadata(_appearance_render_layer)
	var selected_socket := _selected_metadata(_appearance_socket)
	var first_layer_id := ""
	var first_socket_id := ""
	_appearance_render_layer.clear()
	_appearance_socket.clear()
	if rig.is_empty():
		return
	for variant in rig.get("layers", []) as Array:
		if variant is Dictionary:
			var layer := variant as Dictionary
			var layer_id := str(layer.get("layer_id", ""))
			if layer_id.is_empty():
				continue
			if first_layer_id.is_empty():
				first_layer_id = layer_id
			_appearance_render_layer.add_item(layer_id)
			_appearance_render_layer.set_item_metadata(_appearance_render_layer.item_count - 1, layer_id)
	for variant in rig.get("sockets", []) as Array:
		if variant is Dictionary:
			var socket := variant as Dictionary
			var socket_id := str(socket.get("socket_id", ""))
			if socket_id.is_empty():
				continue
			if first_socket_id.is_empty():
				first_socket_id = socket_id
			_appearance_socket.add_item(socket_id)
			_appearance_socket.set_item_metadata(_appearance_socket.item_count - 1, socket_id)
	if _appearance_render_layer.item_count > 0:
		_select_option(_appearance_render_layer, selected_layer if not selected_layer.is_empty() else first_layer_id)
	if _appearance_socket.item_count > 0:
		_select_option(_appearance_socket, selected_socket if not selected_socket.is_empty() else first_socket_id)


func _selected_actor_rig() -> Dictionary:
	var rigs_variant: Variant = _actor_rig_catalog().get("rigs", [])
	if not (rigs_variant is Array):
		return {}
	for variant in rigs_variant:
		if variant is Dictionary and str((variant as Dictionary).get("rig_id", "")) == _selected_metadata(_appearance_rig):
			return variant as Dictionary
	return {}


func _actor_rig_catalog() -> Dictionary:
	var catalog_variant: Variant = _options.get("actor_rig_catalog", {})
	return catalog_variant as Dictionary if catalog_variant is Dictionary else {}


func _first_actor_rig_id() -> String:
	var rigs_variant: Variant = _actor_rig_catalog().get("rigs", [])
	if not (rigs_variant is Array):
		return ""
	for variant in rigs_variant:
		if variant is Dictionary:
			var rig_id := str((variant as Dictionary).get("rig_id", ""))
			if not rig_id.is_empty():
				return rig_id
	return ""


func _actor_rig_catalog_available() -> bool:
	var catalog := _actor_rig_catalog()
	if not bool(catalog.get("available", false)):
		return false
	return not _first_actor_rig_id().is_empty()


func _actor_rig_catalog_status(catalog: Dictionary) -> String:
	var source_path := str(catalog.get("source_path", ""))
	var message := str(catalog.get("message", ""))
	var first_rig_id := ""
	var rigs_variant: Variant = catalog.get("rigs", [])
	if rigs_variant is Array:
		for variant in rigs_variant:
			if variant is Dictionary:
				first_rig_id = str((variant as Dictionary).get("rig_id", ""))
				if not first_rig_id.is_empty():
					break
	if bool(catalog.get("available", false)) and not first_rig_id.is_empty():
		return "Loaded %s from %s." % [first_rig_id, source_path] if not source_path.is_empty() else "Loaded %s." % first_rig_id
	if message.is_empty():
		message = "The canonical actor rig catalog is unavailable."
	if not source_path.is_empty():
		return "%s Path: %s" % [message, source_path]
	return message


func _on_appearance_enabled_toggled(_value: bool) -> void:
	if _is_loading:
		return
	if not _value:
		_cancel_paper_doll_drag()
	elif not _has_persisted_equipped_visual and not _appearance_defaults_initialized:
		_initialize_authored_appearance_defaults()
	_rebuild_actor_rig_controls()
	_update_contextual_sections()
	_on_form_changed()


func _on_appearance_rig_changed() -> void:
	if _is_loading:
		return
	_cancel_paper_doll_drag()
	_rebuild_actor_rig_controls()
	_update_contextual_sections()
	_on_form_changed()


func _on_appearance_binding_changed() -> void:
	if _is_loading:
		return
	_cancel_paper_doll_drag()
	if _selected_metadata(_appearance_binding) == "socket":
		_select_option(_appearance_socket, _default_socket_id_for_equipment_slot(_selected_metadata(_equipment_slot)))
	else:
		_appearance_socket.visible = false
	_update_contextual_sections()
	_on_form_changed()


func _on_appearance_render_layer_changed() -> void:
	if _is_loading:
		return
	_cancel_paper_doll_drag()
	_on_form_changed()


func _on_appearance_socket_changed() -> void:
	if _is_loading:
		return
	_cancel_paper_doll_drag()
	_on_form_changed()


func _on_grip_spin_changed() -> void:
	if _is_loading or _appearance_updating or not _can_edit_grip_anchor():
		return
	_set_current_pose_anchor(Vector2i(int(_appearance_grip_x.value), int(_appearance_grip_y.value)))
	_on_form_changed()


func _on_appearance_flip_x_toggled(_enabled: bool) -> void:
	if _is_loading or _appearance_updating or not _appearance_enabled.button_pressed:
		return
	_set_current_pose_flip_x(_appearance_flip_x.button_pressed)
	_on_form_changed()


func _on_appearance_visible_in_pose_toggled(_visible: bool) -> void:
	if _is_loading or _appearance_updating or not _appearance_enabled.button_pressed:
		return
	_cancel_paper_doll_drag()
	_set_current_pose_hidden(not _appearance_visible_in_pose.button_pressed)
	_update_grip_pose_controls()
	_update_contextual_sections()
	_update_paper_doll_preview()
	_on_form_changed()


func _on_appearance_item_over_grip_toggled(_enabled: bool) -> void:
	if _is_loading or _appearance_updating or not _appearance_enabled.button_pressed:
		return
	_set_current_pose_item_over_grip(_appearance_item_over_grip.button_pressed)
	_on_form_changed()


func _on_paper_doll_grip_anchor_changed(direction: String, frame: int, x: int, y: int) -> void:
	if not _can_edit_grip_anchor():
		return
	_set_pose_anchor(direction, frame, Vector2i(x, y))
	if direction == _selected_metadata(_preview_direction) and frame == int(_preview_frame.value):
		_update_grip_pose_controls()
	_on_form_changed()


func _set_pose_anchor(direction: String, frame: int, point: Vector2i) -> void:
	var frame_key := str(frame)
	var frames_variant: Variant = _equipped_visual_grip_anchors.get(direction, {})
	var frames: Dictionary = frames_variant as Dictionary if frames_variant is Dictionary else {}
	frames[frame_key] = {"x": point.x, "y": point.y}
	_equipped_visual_grip_anchors[direction] = frames


func _set_current_pose_anchor(point: Vector2i) -> void:
	_set_pose_anchor(_selected_metadata(_preview_direction), int(_preview_frame.value), point)
	_update_grip_pose_controls()


func _set_current_pose_flip_x(flip_x: bool) -> void:
	var direction := _selected_metadata(_preview_direction)
	var frame_key := str(int(_preview_frame.value))
	var frames_variant: Variant = _equipped_visual_flip_x.get(direction, {})
	var frames: Dictionary = frames_variant as Dictionary if frames_variant is Dictionary else {}
	if flip_x:
		frames[frame_key] = true
	else:
		frames.erase(frame_key)
	if frames.is_empty():
		_equipped_visual_flip_x.erase(direction)
	else:
		_equipped_visual_flip_x[direction] = frames


func _set_current_pose_hidden(hidden: bool) -> void:
	var direction := _selected_metadata(_preview_direction)
	var frame_key := str(int(_preview_frame.value))
	var frames_variant: Variant = _equipped_visual_hidden_poses.get(direction, {})
	var frames: Dictionary = frames_variant as Dictionary if frames_variant is Dictionary else {}
	if hidden:
		frames[frame_key] = true
	else:
		frames.erase(frame_key)
	if frames.is_empty():
		_equipped_visual_hidden_poses.erase(direction)
	else:
		_equipped_visual_hidden_poses[direction] = frames


func _set_current_pose_item_over_grip(item_over_grip: bool) -> void:
	var direction := _selected_metadata(_preview_direction)
	var frame_key := str(int(_preview_frame.value))
	var frames_variant: Variant = _equipped_visual_item_over_grip.get(direction, {})
	var frames: Dictionary = frames_variant as Dictionary if frames_variant is Dictionary else {}
	if item_over_grip:
		frames[frame_key] = true
	else:
		frames.erase(frame_key)
	if frames.is_empty():
		_equipped_visual_item_over_grip.erase(direction)
	else:
		_equipped_visual_item_over_grip[direction] = frames


func _get_current_pose_flip_x() -> bool:
	var frames_variant: Variant = _equipped_visual_flip_x.get(_selected_metadata(_preview_direction), {})
	return bool((frames_variant as Dictionary).get(str(int(_preview_frame.value)), false)) if frames_variant is Dictionary else false


func _get_current_pose_hidden() -> bool:
	var frames_variant: Variant = _equipped_visual_hidden_poses.get(_selected_metadata(_preview_direction), {})
	return bool((frames_variant as Dictionary).get(str(int(_preview_frame.value)), false)) if frames_variant is Dictionary else false


func _get_current_pose_item_over_grip() -> bool:
	var frames_variant: Variant = _equipped_visual_item_over_grip.get(_selected_metadata(_preview_direction), {})
	return bool((frames_variant as Dictionary).get(str(int(_preview_frame.value)), false)) if frames_variant is Dictionary else false


func _copy_previous_pose_anchor() -> void:
	if not _can_edit_grip_anchor():
		return
	var frame := int(_preview_frame.value)
	if frame <= 1:
		return
	var anchor: Variant = _get_pose_anchor(_selected_metadata(_preview_direction), frame - 1)
	if anchor == null:
		return
	_set_current_pose_anchor(anchor)
	_on_form_changed()


func _copy_next_pose_anchor() -> void:
	if not _can_edit_grip_anchor():
		return
	var frame := int(_preview_frame.value)
	if frame >= 4:
		return
	var anchor: Variant = _get_pose_anchor(_selected_metadata(_preview_direction), frame + 1)
	if anchor == null:
		return
	_set_current_pose_anchor(anchor)
	_on_form_changed()


func _clear_current_pose_anchor() -> void:
	if not _can_edit_grip_anchor():
		return
	var direction := _selected_metadata(_preview_direction)
	var frame_key := str(int(_preview_frame.value))
	var frames_variant: Variant = _equipped_visual_grip_anchors.get(direction, {})
	if not (frames_variant is Dictionary):
		return
	var frames := frames_variant as Dictionary
	frames.erase(frame_key)
	if frames.is_empty():
		_equipped_visual_grip_anchors.erase(direction)
	else:
		_equipped_visual_grip_anchors[direction] = frames
	_update_grip_pose_controls()
	_on_form_changed()


func _nudge_current_grip_anchor(delta_x: int, delta_y: int) -> void:
	if not _can_edit_grip_anchor():
		return
	var anchor: Variant = _get_current_pose_anchor()
	var next_anchor: Vector2i = anchor if anchor != null else Vector2i.ZERO
	next_anchor.x += delta_x
	next_anchor.y += delta_y
	_set_current_pose_anchor(next_anchor)
	_on_form_changed()


func _get_current_pose_anchor():
	return _get_pose_anchor(_selected_metadata(_preview_direction), int(_preview_frame.value))


func _get_pose_anchor(direction: String, frame: int):
	var frames_variant: Variant = _equipped_visual_grip_anchors.get(direction, {})
	if not (frames_variant is Dictionary):
		return null
	var point_variant: Variant = (frames_variant as Dictionary).get(str(frame), null)
	if not (point_variant is Dictionary):
		return null
	var point := point_variant as Dictionary
	return Vector2i(
		_normalize_attachment_anchor_coordinate(point.get("x", 0)),
		_normalize_attachment_anchor_coordinate(point.get("y", 0)))


func _normalize_attachment_anchor_coordinate(value: Variant) -> int:
	if value is int:
		return clampi(value, -ATTACHMENT_ANCHOR_LIMIT, ATTACHMENT_ANCHOR_LIMIT)
	if value is float:
		var numeric := float(value)
		if is_finite(numeric):
			return clampi(
				int(round(clampf(numeric, -ATTACHMENT_ANCHOR_LIMIT, ATTACHMENT_ANCHOR_LIMIT))),
				-ATTACHMENT_ANCHOR_LIMIT,
				ATTACHMENT_ANCHOR_LIMIT)
	return 0


func _update_grip_pose_controls() -> void:
	_appearance_updating = true
	var anchor = _get_current_pose_anchor()
	_appearance_grip_x.value = float(anchor.x if anchor != null else 0)
	_appearance_grip_y.value = float(anchor.y if anchor != null else 0)
	_appearance_visible_in_pose.button_pressed = not _get_current_pose_hidden()
	_appearance_item_over_grip.button_pressed = _get_current_pose_item_over_grip()
	_appearance_flip_x.button_pressed = _get_current_pose_flip_x()
	_appearance_updating = false


func _add_consumable_requirement_row(initial: Dictionary = {}) -> void:
	var row := HBoxContainer.new()
	row.add_theme_constant_override("separation", 6)
	var type := OptionButton.new()
	_fill_option(type, _option_array("requirement_types", [{"id": "skill_minimum", "display_name": "Skill Minimum"}]))
	_select_option(type, str(initial.get("requirement_type", "skill_minimum")))
	row.add_child(type)
	var target := OptionButton.new()
	target.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_fill_option(target, _option_array("skills", []))
	_select_option(target, str(initial.get("target_id", "")))
	row.add_child(target)
	var minimum := _row_spin(1, 1000000, float(initial.get("minimum_value", 1)))
	row.add_child(minimum)
	var remove := _remove_button(row)
	row.add_child(remove)
	row.set_meta("type", type)
	row.set_meta("target", target)
	row.set_meta("value", minimum)
	_connect_row_controls(row)
	_consumable_requirements.add_child(row)
	_clear_preview()


func _add_consumable_effect_row(initial: Dictionary = {}) -> void:
	var row := HBoxContainer.new()
	row.add_theme_constant_override("separation", 6)
	var type := OptionButton.new()
	_fill_option(type, _option_array("effect_types", [{"id": "restore_resource", "display_name": "Restore Resource"}]))
	_select_option(type, str(initial.get("effect_type", "restore_resource")))
	row.add_child(type)
	var target := OptionButton.new()
	target.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_fill_option(target, _option_array("resource_targets", [{"id": "health", "display_name": "Health"}]))
	_select_option(target, str(initial.get("target_id", "health")))
	row.add_child(target)
	var minimum := _row_spin(1, 1000000, float(initial.get("minimum_amount", 1)))
	row.add_child(minimum)
	var maximum := _row_spin(1, 1000000, float(initial.get("maximum_amount", initial.get("minimum_amount", 1))))
	row.add_child(maximum)
	var remove := _remove_button(row)
	row.add_child(remove)
	row.set_meta("type", type)
	row.set_meta("target", target)
	row.set_meta("minimum", minimum)
	row.set_meta("maximum", maximum)
	_connect_row_controls(row)
	_consumable_effects.add_child(row)
	_clear_preview()


func _add_requirement_row(initial: Dictionary = {}) -> void:
	var row := HBoxContainer.new()
	row.add_theme_constant_override("separation", 6)
	var skill := OptionButton.new()
	skill.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_fill_option(skill, _option_array("skills", []))
	_select_option(skill, str(initial.get("skill_id", "")))
	row.add_child(skill)
	var value := _row_spin(1, 1000000, float(initial.get("required_value", 1)))
	row.add_child(value)
	var remove := _remove_button(row)
	row.add_child(remove)
	row.set_meta("skill", skill)
	row.set_meta("value", value)
	_connect_row_controls(row)
	_requirements.add_child(row)
	_clear_preview()


func _add_modifier_row(initial: Dictionary = {}) -> void:
	var row := HBoxContainer.new()
	row.add_theme_constant_override("separation", 6)
	var skill := OptionButton.new()
	skill.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_fill_option(skill, _option_array("skills", []))
	_select_option(skill, str(initial.get("skill_id", "")))
	row.add_child(skill)
	var value := _row_spin(-1000000, 1000000, float(initial.get("modifier_value", 0)))
	row.add_child(value)
	var remove := _remove_button(row)
	row.add_child(remove)
	row.set_meta("skill", skill)
	row.set_meta("value", value)
	_connect_row_controls(row)
	_modifiers.add_child(row)
	_clear_preview()


func _add_tool_row(initial: Dictionary = {}) -> void:
	var row := HBoxContainer.new()
	row.add_theme_constant_override("separation", 6)
	var capability := OptionButton.new()
	capability.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_fill_option(capability, _option_array("tool_capabilities", []))
	_select_option(capability, str(initial.get("capability_id", "")))
	row.add_child(capability)
	var maximum_power := int(_options.get("maximum_tool_power_tier", 1000))
	var power := _row_spin(1, maximum_power, float(initial.get("power_tier", 1)))
	row.add_child(power)
	var action := LineEdit.new()
	action.placeholder_text = "action animation ID"
	action.text = _nullable_string(initial.get("action_animation_id", ""))
	action.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	row.add_child(action)
	var effect := LineEdit.new()
	effect.placeholder_text = "effect resource ID"
	effect.text = _nullable_string(initial.get("effect_resource_id", ""))
	effect.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	row.add_child(effect)
	var up := Button.new()
	up.text = "Up"
	up.pressed.connect(_move_row.bind(row, _tool_rows, -1))
	row.add_child(up)
	var down := Button.new()
	down.text = "Down"
	down.pressed.connect(_move_row.bind(row, _tool_rows, 1))
	row.add_child(down)
	var remove := _remove_button(row)
	row.add_child(remove)
	row.set_meta("capability", capability)
	row.set_meta("power", power)
	row.set_meta("action", action)
	row.set_meta("effect", effect)
	_connect_row_controls(row)
	_tool_rows.add_child(row)
	_clear_preview()


func _collect_consumable_requirements() -> Array:
	var values: Array = []
	for row in _consumable_requirements.get_children():
		if row.has_meta("type"):
			values.append({
				"requirement_index": values.size(),
				"requirement_type": _selected_metadata(row.get_meta("type") as OptionButton),
				"target_id": _selected_metadata(row.get_meta("target") as OptionButton),
				"minimum_value": int((row.get_meta("value") as SpinBox).value),
			})
	return values


func _collect_consumable_effects() -> Array:
	var values: Array = []
	for row in _consumable_effects.get_children():
		if row.has_meta("type"):
			values.append({
				"effect_index": values.size(),
				"effect_type": _selected_metadata(row.get_meta("type") as OptionButton),
				"target_id": _selected_metadata(row.get_meta("target") as OptionButton),
				"minimum_amount": int((row.get_meta("minimum") as SpinBox).value),
				"maximum_amount": int((row.get_meta("maximum") as SpinBox).value),
			})
	return values


func _collect_requirements() -> Array:
	var values: Array = []
	for row in _requirements.get_children():
		if row.has_meta("skill"):
			values.append({
				"skill_id": _selected_metadata(row.get_meta("skill") as OptionButton),
				"required_value": int((row.get_meta("value") as SpinBox).value),
			})
	return values


func _collect_modifiers() -> Array:
	var values: Array = []
	for row in _modifiers.get_children():
		if row.has_meta("skill"):
			values.append({
				"skill_id": _selected_metadata(row.get_meta("skill") as OptionButton),
				"modifier_value": int((row.get_meta("value") as SpinBox).value),
			})
	return values


func _collect_bonuses() -> Dictionary:
	var values: Dictionary = {}
	for id in _bonus_controls:
		if id == "_grid":
			continue
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


func _apply_bonus_values(values: Dictionary) -> void:
	for id in _bonus_controls:
		if id != "_grid":
			(_bonus_controls[id] as SpinBox).value = float(values.get(id, 0))


func _apply_weapon_profile(profile_variant: Variant) -> void:
	var has_profile := profile_variant is Dictionary
	var profile := profile_variant as Dictionary if has_profile else {}
	_weapon_enabled.button_pressed = has_profile
	_weapon_profile_id.text = str(profile.get("profile_id", ""))
	_select_option(_weapon_attack_type, str(profile.get("attack_type", "melee")))
	_select_option(_weapon_accuracy_style, str(profile.get("accuracy_style", "slash")))
	_weapon_min_range.value = float(profile.get("minimum_range_tiles", 1))
	_weapon_max_range.value = float(profile.get("maximum_range_tiles", 1))
	_weapon_speed_units.value = float(profile.get("attack_speed_units", 4))
	_update_weapon_timing()


func _on_search_changed() -> void:
	_rebuild_list()


func _refresh_catalog() -> void:
	_client.search_item_catalog(_search.text)


func _on_consumable_toggled(_value: bool) -> void:
	if _is_loading:
		return
	if _value and _consumable_effects.get_child_count() == 0:
		_add_consumable_effect_row({"effect_type": "restore_resource", "target_id": "health", "minimum_amount": 1, "maximum_amount": 1})
	_update_contextual_sections()
	_on_form_changed()


func _on_equipable_toggled(_value: bool) -> void:
	if _is_loading:
		return
	if not _value:
		_cancel_paper_doll_drag()
		_weapon_enabled.button_pressed = false
	_update_contextual_sections()
	_on_form_changed()


func _on_slot_changed() -> void:
	if _is_loading:
		return
	_cancel_paper_doll_drag()
	if not _is_weapon_capable_slot(_selected_metadata(_equipment_slot)):
		_weapon_enabled.button_pressed = false
	if _selected_metadata(_appearance_render_layer).is_empty():
		_rebuild_actor_rig_controls()
	_update_contextual_sections()
	_on_form_changed()


func _on_weapon_enabled_toggled(_value: bool) -> void:
	_update_contextual_sections()
	if not _is_loading:
		_on_form_changed()


func _on_weapon_speed_changed() -> void:
	_update_weapon_timing()
	_on_form_changed()


func _on_operation_changed() -> void:
	_clear_preview()


func _on_visual_preview_changed() -> void:
	_cancel_paper_doll_drag()
	_update_grip_pose_controls()
	_update_paper_doll_preview()


func _on_preview_zoom_out_pressed() -> void:
	_cancel_paper_doll_drag()
	_paper_doll_preview.zoom_out()
	_appearance_actual_scale.button_pressed = false
	_refresh_preview_zoom_controls()
	_update_paper_doll_preview()


func _on_preview_zoom_in_pressed() -> void:
	_cancel_paper_doll_drag()
	_paper_doll_preview.zoom_in()
	_appearance_actual_scale.button_pressed = false
	_refresh_preview_zoom_controls()
	_update_paper_doll_preview()


func _on_preview_zoom_fit_pressed() -> void:
	_cancel_paper_doll_drag()
	_paper_doll_preview.reset_fit_view()
	_appearance_actual_scale.button_pressed = false
	_refresh_preview_zoom_controls()
	_update_paper_doll_preview()


func _on_preview_actual_scale_toggled(enabled: bool) -> void:
	_cancel_paper_doll_drag()
	_paper_doll_preview.set_actual_scale_enabled(enabled)
	_refresh_preview_zoom_controls()
	_update_paper_doll_preview()


func _on_form_changed() -> void:
	if _is_loading:
		return
	_clear_preview()
	_update_icon_preview()
	_update_paper_doll_preview()


func _update_contextual_sections() -> void:
	var equipment_enabled := _equipable.button_pressed
	var weapon_capable := equipment_enabled and _is_weapon_capable_slot(_selected_metadata(_equipment_slot))
	var authored_visual := equipment_enabled and _appearance_enabled.button_pressed
	var socket_binding := authored_visual and _selected_metadata(_appearance_binding) == "socket"
	_appearance_section.visible = equipment_enabled
	_requirements_section.visible = equipment_enabled
	_combat_bonus_section.visible = equipment_enabled
	_weapon_section.visible = weapon_capable
	_tool_section.visible = true
	_set_consumable_controls_enabled(_consumable_enabled.button_pressed)
	_set_equipment_controls_enabled(equipment_enabled)
	_set_weapon_controls_enabled(weapon_capable and _weapon_enabled.button_pressed)
	_set_appearance_controls_enabled(equipment_enabled, authored_visual, socket_binding)


func _set_form_enabled(enabled: bool) -> void:
	for edit in [_item_id, _display_name, _result_item_id, _success_message, _animation_id, _sound_path, _weapon_profile_id, _reference_value, _npc_buy_price, _npc_sell_price, _reclaim_value, _death_transform_item_id, _condition_policy_id, _repair_policy_id]:
		edit.editable = enabled and (edit != _item_id or _current_item.is_empty())
	for option in [_icon, _use_action, _equipment_slot, _weapon_attack_type, _weapon_accuracy_style, _operation]:
		option.disabled = not enabled
	for spin in [_consume_quantity, _cooldown_ms, _required_strength, _weapon_min_range, _weapon_max_range, _weapon_speed_units]:
		spin.editable = enabled
	for toggle in [_consumable_enabled, _usable_in_combat, _equipable, _weapon_enabled]:
		toggle.disabled = not enabled
	_preview_button.disabled = not enabled
	_delete_button.disabled = not enabled or _current_item.is_empty()
	if not enabled:
		_apply_button.disabled = true
	_update_contextual_sections()


func _set_consumable_controls_enabled(enabled: bool) -> void:
	_use_action.disabled = not enabled
	for control in [_result_item_id, _success_message, _animation_id, _sound_path]:
		control.editable = enabled
	for spin in [_consume_quantity, _cooldown_ms]:
		spin.editable = enabled
	_usable_in_combat.disabled = not enabled
	for row in _consumable_requirements.get_children() + _consumable_effects.get_children():
		_set_row_enabled(row, enabled)


func _set_equipment_controls_enabled(enabled: bool) -> void:
	_equipment_slot.disabled = not enabled
	_required_strength.editable = enabled
	for row in _requirements.get_children() + _modifiers.get_children():
		_set_row_enabled(row, enabled)
	for id in _bonus_controls:
		if id != "_grid":
			(_bonus_controls[id] as SpinBox).editable = enabled


func _set_weapon_controls_enabled(enabled: bool) -> void:
	_weapon_profile_id.editable = enabled
	_weapon_attack_type.disabled = not enabled
	_weapon_accuracy_style.disabled = not enabled
	_weapon_min_range.editable = enabled
	_weapon_max_range.editable = enabled
	_weapon_speed_units.editable = enabled


func _set_appearance_controls_enabled(equipment_enabled: bool, authored_visual: bool, socket_binding: bool) -> void:
	var catalog_available := _actor_rig_catalog_available()
	var pose_visible := not _get_current_pose_hidden()
	var grip_editable := socket_binding and pose_visible and catalog_available and _grip_pose_art_available
	_appearance_enabled.disabled = not equipment_enabled
	_appearance_rig.disabled = not authored_visual or not catalog_available
	_appearance_binding.disabled = not authored_visual or not catalog_available
	_appearance_render_layer.disabled = not authored_visual or not catalog_available
	_appearance_socket.visible = socket_binding
	_appearance_socket.disabled = not socket_binding or not catalog_available
	_appearance_asset_key.editable = authored_visual and catalog_available
	_appearance_nudge_x.editable = authored_visual and catalog_available
	_appearance_nudge_y.editable = authored_visual and catalog_available
	_appearance_actual_scale.disabled = not equipment_enabled or not catalog_available
	_appearance_zoom_out.disabled = not equipment_enabled or not catalog_available or not _paper_doll_preview.can_zoom_out()
	_appearance_zoom_in.disabled = not equipment_enabled or not catalog_available or not _paper_doll_preview.can_zoom_in()
	_appearance_fit.disabled = not equipment_enabled or not catalog_available
	_appearance_zoom_label.modulate = Color(0.7, 0.73, 0.79, 1) if equipment_enabled and catalog_available else Color(0.45, 0.48, 0.54, 1)
	_appearance_grip_row.visible = socket_binding
	_appearance_grip_actions.visible = socket_binding
	_appearance_grip_marker_legend.visible = socket_binding
	_appearance_grip_x.editable = grip_editable
	_appearance_grip_y.editable = grip_editable
	_appearance_visible_in_pose.disabled = not authored_visual or not catalog_available
	_appearance_item_over_grip.disabled = not authored_visual or not pose_visible or not catalog_available
	_appearance_flip_x.disabled = not authored_visual or not pose_visible or not catalog_available
	_appearance_clear_pose.disabled = not grip_editable
	_appearance_copy_previous.disabled = not grip_editable
	_appearance_copy_next.disabled = not grip_editable
	for control in _appearance_grip_actions.get_children():
		if control is Button:
			(control as Button).disabled = not grip_editable
	if not grip_editable:
		_cancel_paper_doll_drag()
	_refresh_preview_zoom_controls()


func _can_edit_grip_anchor() -> bool:
	return _appearance_enabled.button_pressed \
		and _selected_metadata(_appearance_binding) == "socket" \
		and _actor_rig_catalog_available() \
		and not _selected_metadata(_appearance_rig).is_empty() \
		and not _selected_metadata(_appearance_socket).is_empty() \
		and not _get_current_pose_hidden() \
		and _grip_pose_art_available


func _set_row_enabled(row: Node, enabled: bool) -> void:
	for child in row.get_children():
		if child is OptionButton:
			(child as OptionButton).disabled = not enabled
		elif child is SpinBox:
			(child as SpinBox).editable = enabled
		elif child is LineEdit:
			(child as LineEdit).editable = enabled
		elif child is Button:
			(child as Button).disabled = not enabled


func _update_operation_default() -> void:
	var state := str(_current_item.get("publication_state", "Unsaved"))
	_select_option(_operation, "disable" if state == "Published" else "save_draft")


func _update_weapon_timing() -> void:
	var units := int(_weapon_speed_units.value) if _weapon_speed_units != null else 4
	var milliseconds := int(_options.get("combat_unit_milliseconds", COMBAT_UNIT_MILLISECONDS))
	_weapon_timing.text = "%d attack units x %d ms = %d ms" % [units, milliseconds, units * milliseconds]


func _rebuild_asset_options(selected_path: String = "") -> void:
	var previous := selected_path if not selected_path.is_empty() else _selected_metadata(_icon)
	_asset_by_path.clear()
	_icon.clear()
	_icon.add_item("Select an item icon...")
	_icon.set_item_metadata(0, "")
	for variant in _assets:
		if variant is Dictionary:
			var asset := variant as Dictionary
			var path := str(asset.get("resource_path", ""))
			_asset_by_path[path] = asset
			_icon.add_item(str(asset.get("display_name", path)))
			_icon.set_item_metadata(_icon.item_count - 1, path)
	if not previous.is_empty() and not _asset_by_path.has(previous):
		_icon.add_item("Unavailable current icon: %s" % previous)
		_icon.set_item_metadata(_icon.item_count - 1, previous)
	_select_option(_icon, previous)
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


func _update_paper_doll_preview() -> void:
	var direction := _selected_metadata(_preview_direction)
	var socket_grip_authoring := _appearance_enabled.button_pressed and _selected_metadata(_appearance_binding) == "socket"
	var equipped_visual_payload_variant: Variant = _equipped_visual_payload()
	var equipped_visual_payload: Dictionary = equipped_visual_payload_variant if equipped_visual_payload_variant is Dictionary else {}
	_paper_doll_preview.set_actual_scale_enabled(_appearance_actual_scale.button_pressed)
	_refresh_preview_zoom_controls()
	var preview_state: Dictionary = _paper_doll_preview.update(
		_equipable.button_pressed,
		_selected_metadata(_equipment_slot) if _equipable.button_pressed else "",
		_paper_doll_preview.normalize_visual_key(_display_name.text.strip_edges()),
		direction if not direction.is_empty() else "N",
		int(_preview_frame.value),
		["head", "cape", "body", "legs", "boots", "gloves", "right_hand", "left_hand"],
		equipped_visual_payload,
		socket_grip_authoring
	)
	_grip_pose_art_available = bool(preview_state.get("selected_item_pose_available", false)) if socket_grip_authoring else false
	_update_contextual_sections()
	var resolved_asset_path := str(preview_state.get("resolved_asset_path", ""))
	if _appearance_enabled.button_pressed:
		if socket_grip_authoring and not _grip_pose_art_available:
			_appearance_asset_path.text = "Item art: unavailable for %s/F%d" % [direction if not direction.is_empty() else "N", int(_preview_frame.value)]
		elif socket_grip_authoring and bool(preview_state.get("selected_item_pose_hidden", false)):
			_appearance_asset_path.text = "Item art: hidden for %s/F%d" % [direction if not direction.is_empty() else "N", int(_preview_frame.value)]
		elif socket_grip_authoring:
			_appearance_asset_path.text = "Item art: %s" % resolved_asset_path.get_file()
		else:
			_appearance_asset_path.text = resolved_asset_path
	else:
		var legacy_visual_key: String = _paper_doll_preview.normalize_visual_key(_display_name.text.strip_edges())
		_appearance_asset_path.text = "" if legacy_visual_key.is_empty() else "Legacy preview visual: %s" % legacy_visual_key


func _open_import() -> void:
	_file_dialog.popup_centered_ratio(0.75)


func _import_selected(path: String) -> void:
	_client.import_item_asset(path, path.get_file())
	_status.text = "Importing PNG into the canonical item asset directory..."


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


func _add_control_row(parent: Node, label_text: String, control: Control) -> void:
	var row := HBoxContainer.new()
	row.add_theme_constant_override("separation", 8)
	row.add_child(_field_label(label_text))
	control.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	row.add_child(control)
	parent.add_child(row)


func _section_grid(parent: Node, title: String) -> GridContainer:
	parent.add_child(_heading(title, 16))
	var grid := GridContainer.new()
	grid.columns = 2
	grid.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	parent.add_child(grid)
	return grid


func _row_header(title: String, button_text: String, callable: Callable) -> HBoxContainer:
	var row := HBoxContainer.new()
	row.add_theme_constant_override("separation", 8)
	var label := _heading(title, 16)
	label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	row.add_child(label)
	var button := Button.new()
	button.text = button_text
	button.pressed.connect(callable)
	row.add_child(button)
	return row


func _rows() -> VBoxContainer:
	var rows := VBoxContainer.new()
	rows.add_theme_constant_override("separation", 6)
	rows.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	return rows


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


func _row_spin(minimum: float, maximum: float, value: float) -> SpinBox:
	var spin := SpinBox.new()
	spin.min_value = minimum
	spin.max_value = maximum
	spin.value = value
	spin.custom_minimum_size = Vector2(110, 0)
	return spin


func _remove_button(row: Control) -> Button:
	var remove := Button.new()
	remove.text = "Remove"
	remove.pressed.connect(_remove_row.bind(row))
	return remove


func _connect_row_controls(row: Node) -> void:
	for child in row.get_children():
		if child is OptionButton:
			(child as OptionButton).item_selected.connect(_on_form_changed.unbind(1))
		elif child is SpinBox:
			(child as SpinBox).value_changed.connect(_on_form_changed.unbind(1))
		elif child is LineEdit:
			(child as LineEdit).text_changed.connect(_on_form_changed.unbind(1))


func _remove_row(row: Control) -> void:
	var parent := row.get_parent()
	if parent != null:
		parent.remove_child(row)
	row.queue_free()
	_clear_preview()


func _move_row(row: Control, container: Node, delta: int) -> void:
	var index := row.get_index()
	var target := clampi(index + delta, 0, container.get_child_count() - 1)
	if target == index:
		return
	container.move_child(row, target)
	_clear_preview()


func _rebuild_bonus_grid(grid_variant: Variant) -> void:
	var grid := grid_variant as GridContainer
	if grid == null and _bonus_controls.has("_grid"):
		grid = _bonus_controls["_grid"] as GridContainer
	if grid == null:
		return
	_clear_rows(grid)
	_bonus_controls.clear()
	_bonus_controls["_grid"] = grid
	for variant in _option_array("combat_bonus_fields", DEFAULT_BONUS_FIELDS):
		if variant is not Dictionary:
			continue
		var option := variant as Dictionary
		var id := str(option.get("id", ""))
		grid.add_child(_field_label(str(option.get("display_name", id))))
		var spin := SpinBox.new()
		spin.min_value = -1000000
		spin.max_value = 1000000
		spin.value_changed.connect(_on_form_changed.unbind(1))
		grid.add_child(spin)
		_bonus_controls[id] = spin


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


func _option_array(key: String, fallback: Array) -> Array:
	var values: Variant = _options.get(key, [])
	if values is Array and not (values as Array).is_empty():
		return values as Array
	return fallback


func _is_weapon_capable_slot(slot_id: String) -> bool:
	for variant in _option_array("weapon_capable_slots", DEFAULT_WEAPON_CAPABLE_SLOTS):
		if variant is Dictionary and str((variant as Dictionary).get("id", "")) == slot_id:
			return true
	return false


func _nullable_string(value: Variant) -> String:
	return "" if value == null else str(value)


func _integer_text(value: Variant) -> String:
	if value == null:
		return ""
	if value is int:
		return str(value)
	if value is float and is_equal_approx(float(value), float(int(value))):
		return str(int(value))
	return str(value)


func _optional_payload(value: String) -> Variant:
	var trimmed := value.strip_edges()
	return null if trimmed.is_empty() else trimmed


func _has_error_code(errors: Array, code: String) -> bool:
	for variant in errors:
		if variant is Dictionary and str((variant as Dictionary).get("code", "")) == code:
			return true
	return false


func _clear_preview() -> void:
	_workspace_support.clear_preview(_apply_button, _changes, _validation)


func _initialize_authored_appearance_defaults() -> void:
	if _appearance_asset_key.text.strip_edges().is_empty():
		_appearance_asset_key.text = _paper_doll_preview.normalize_visual_key(_display_name.text.strip_edges())
	var slot_id := _selected_metadata(_equipment_slot)
	var rig_id := _first_actor_rig_id()
	if not rig_id.is_empty():
		_select_option(_appearance_rig, rig_id)
	_rebuild_actor_rig_controls()
	if slot_id == "right_hand" or slot_id == "left_hand":
		_select_option(_appearance_binding, "socket")
		_select_option(_appearance_render_layer, slot_id)
		_select_option(_appearance_socket, _default_socket_id_for_equipment_slot(slot_id))
	else:
		_select_option(_appearance_binding, "rig_layer")
		_select_option(_appearance_render_layer, slot_id)
		_select_option(_appearance_socket, "")
	_appearance_nudge_x.value = 0
	_appearance_nudge_y.value = 0
	_appearance_defaults_initialized = true
	_update_grip_pose_controls()


func _default_socket_id_for_equipment_slot(slot_id: String) -> String:
	if slot_id == "left_hand":
		return "left_hand_primary"
	return "right_hand_primary"


func _cancel_paper_doll_drag() -> void:
	if _paper_doll_preview != null:
		_paper_doll_preview.cancel_drag()


func _refresh_preview_zoom_controls() -> void:
	if _appearance_zoom_label == null:
		return
	_appearance_zoom_label.text = _paper_doll_preview.get_view_scale_label()


func _clear_rows(container: Node) -> void:
	for child in container.get_children():
		container.remove_child(child)
		child.queue_free()
