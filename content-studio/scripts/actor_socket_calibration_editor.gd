extends VBoxContainer
class_name ActorSocketCalibrationEditor

const STATE_SCRIPT := preload("res://scripts/actor_socket_calibration_state.gd")
const CANVAS_SCRIPT := preload("res://scripts/actor_socket_calibration_canvas.gd")
const ALIGNMENT_CANVAS_SCRIPT := preload("res://scripts/actor_item_alignment_canvas.gd")
const ATTACHMENT_ALIGNMENT := preload("res://scripts/actor_attachment_alignment.gd")
const RIGGED_PREVIEW_LAYOUT := preload("res://scripts/rigged_sprite_preview_layout.gd")
const CALIBRATION_ID_PATTERN := "^[a-z0-9][a-z0-9_]{0,63}$"

signal use_calibration_for_actor(calibration_id: String)
signal calibration_saved(calibration_id: String, rig_id: String)
signal item_grip_handoff_requested(item_id: String, grip_anchors: Dictionary)

var _client: AuthoringHostClient
var _state = STATE_SCRIPT.new()
var _context: Dictionary = {}
var _pending_context: Dictionary = {}
var _frames: Dictionary = {}
var _awaiting_load := false
var _awaiting_save := false
var _awaiting_frames := false
var _conflicted := false
var _reload_confirm_pending := false
var _active_operation := ""
var _frames_after_load := false
var _queued_load := false
var _syncing := false
var _displayed_frame_key := ""
var _grip_item_id := ""
var _grip_anchors: Dictionary = {}

var _disabled_message: Label
var _content: VBoxContainer
var _rig_value: Label
var _calibration_id: LineEdit
var _load_button: Button
var _use_button: Button
var _mode: OptionButton
var _socket: OptionButton
var _overlay: OptionButton
var _direction: OptionButton
var _frame: OptionButton
var _source: Label
var _socket_x: SpinBox
var _socket_y: SpinBox
var _overlay_x: SpinBox
var _overlay_y: SpinBox
var _overlay_width: SpinBox
var _overlay_height: SpinBox
var _create_override_button: Button
var _revert_button: Button
var _zoom: OptionButton
var _canvas_scroll: ScrollContainer
var _canvas: ActorSocketCalibrationCanvas
var _alignment_scroll: ScrollContainer
var _alignment_canvas: ActorItemAlignmentCanvas
var _grip_x: SpinBox
var _grip_y: SpinBox
var _copy_direction: OptionButton
var _copy_frame: OptionButton
var _copy_button: Button
var _mirror_button: Button
var _open_item_button: Button
var _alignment_status: Label
var _save_button: Button
var _reload_button: Button
var _discard_button: Button
var _dirty: Label
var _status: Label


func configure_client(client: AuthoringHostClient) -> void:
	if _client == client:
		return
	_client = client
	if is_instance_valid(_client):
		_client.actor_calibration_received.connect(_on_calibration_received)
		_client.actor_calibration_saved.connect(_on_calibration_saved)
		_client.actor_calibration_frames_received.connect(_on_frames_received)
		_client.request_failed.connect(_on_request_failed)


func configure_context(context: Dictionary) -> void:
	_ensure_ui()
	if _same_context(context, _context):
		_context = context.duplicate(true)
		_refresh_view()
		return
	if not _active_operation.is_empty():
		_pending_context = context.duplicate(true)
		_status.text = "Waiting for the active calibration request before changing calibration context."
		return
	if _state.is_dirty() and not _same_context(context, _context):
		_pending_context = context.duplicate(true)
		_status.text = "Unsaved calibration changes. Discard Calibration Changes before changing calibration context."
		return
	_apply_context(context)


func _ready() -> void:
	_ensure_ui()


func _ensure_ui() -> void:
	if _content != null:
		return
	add_theme_constant_override("separation", 8)
	_disabled_message = Label.new()
	_disabled_message.text = "Actor attachment calibration is available for Rigged Sprite actors."
	_disabled_message.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	add_child(_disabled_message)
	_content = VBoxContainer.new()
	_content.add_theme_constant_override("separation", 8)
	add_child(_content)
	var context_grid := _grid(_content)
	context_grid.add_child(_label("Rig"))
	_rig_value = Label.new()
	context_grid.add_child(_rig_value)
	context_grid.add_child(_label("Calibration ID"))
	_calibration_id = LineEdit.new()
	_calibration_id.placeholder_text = "orc_v1"
	_calibration_id.text_changed.connect(_on_calibration_id_changed)
	context_grid.add_child(_calibration_id)
	context_grid.add_child(Label.new())
	_load_button = Button.new()
	_load_button.text = "Load / Create Calibration"
	_load_button.pressed.connect(_load_calibration)
	context_grid.add_child(_load_button)
	context_grid.add_child(Label.new())
	_use_button = Button.new()
	_use_button.text = "Use This Calibration for Actor"
	_use_button.pressed.connect(_use_calibration_for_actor)
	context_grid.add_child(_use_button)

	var selection_grid := _grid(_content)
	selection_grid.add_child(_label("Edit"))
	_mode = OptionButton.new()
	_mode.add_item("Socket")
	_mode.set_item_metadata(_mode.item_count - 1, "socket")
	_mode.add_item("Item Grip Anchor")
	_mode.set_item_metadata(_mode.item_count - 1, "grip")
	_mode.add_item("Foreground Grip Overlay")
	_mode.set_item_metadata(_mode.item_count - 1, "foreground_overlay")
	_mode.item_selected.connect(_on_selection_changed.unbind(1))
	selection_grid.add_child(_mode)
	selection_grid.add_child(_label("Socket"))
	_socket = OptionButton.new()
	_socket.item_selected.connect(_on_selection_changed.unbind(1))
	selection_grid.add_child(_socket)
	selection_grid.add_child(_label("Foreground overlay"))
	_overlay = OptionButton.new()
	_overlay.item_selected.connect(_on_selection_changed.unbind(1))
	selection_grid.add_child(_overlay)
	selection_grid.add_child(_label("Direction"))
	_direction = OptionButton.new()
	for entry in [["N", "North"], ["E", "East"], ["S", "South"], ["W", "West"]]:
		_direction.add_item(str(entry[1]))
		_direction.set_item_metadata(_direction.item_count - 1, str(entry[0]))
	_direction.item_selected.connect(_on_selection_changed.unbind(1))
	selection_grid.add_child(_direction)
	selection_grid.add_child(_label("Frame"))
	_frame = OptionButton.new()
	for index in [1, 2, 3, 4]:
		_frame.add_item("F%d" % index)
		_frame.set_item_metadata(_frame.item_count - 1, index)
	_frame.item_selected.connect(_on_selection_changed.unbind(1))
	selection_grid.add_child(_frame)
	selection_grid.add_child(_label("Socket source"))
	_source = Label.new()
	_source.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	selection_grid.add_child(_source)
	selection_grid.add_child(_label("Socket X"))
	_socket_x = _coordinate_field()
	_socket_x.value_changed.connect(_on_coordinate_changed.unbind(1))
	selection_grid.add_child(_socket_x)
	selection_grid.add_child(_label("Socket Y"))
	_socket_y = _coordinate_field()
	_socket_y.value_changed.connect(_on_coordinate_changed.unbind(1))
	selection_grid.add_child(_socket_y)
	selection_grid.add_child(_label("Grip anchor X"))
	_grip_x = _coordinate_field()
	_grip_x.value_changed.connect(_on_grip_coordinate_changed.unbind(1))
	selection_grid.add_child(_grip_x)
	selection_grid.add_child(_label("Grip anchor Y"))
	_grip_y = _coordinate_field()
	_grip_y.value_changed.connect(_on_grip_coordinate_changed.unbind(1))
	selection_grid.add_child(_grip_y)
	selection_grid.add_child(_label("Overlay X"))
	_overlay_x = _overlay_coordinate_field()
	_overlay_x.value_changed.connect(_on_rectangle_coordinate_changed.unbind(1))
	selection_grid.add_child(_overlay_x)
	selection_grid.add_child(_label("Overlay Y"))
	_overlay_y = _overlay_coordinate_field()
	_overlay_y.value_changed.connect(_on_rectangle_coordinate_changed.unbind(1))
	selection_grid.add_child(_overlay_y)
	selection_grid.add_child(_label("Overlay width"))
	_overlay_width = _overlay_coordinate_field()
	_overlay_width.value_changed.connect(_on_rectangle_coordinate_changed.unbind(1))
	selection_grid.add_child(_overlay_width)
	selection_grid.add_child(_label("Overlay height"))
	_overlay_height = _overlay_coordinate_field()
	_overlay_height.value_changed.connect(_on_rectangle_coordinate_changed.unbind(1))
	selection_grid.add_child(_overlay_height)
	selection_grid.add_child(Label.new())
	_create_override_button = Button.new()
	_create_override_button.text = "Create Actor Override"
	_create_override_button.pressed.connect(_create_foreground_overlay_override)
	selection_grid.add_child(_create_override_button)
	selection_grid.add_child(Label.new())
	_revert_button = Button.new()
	_revert_button.text = "Revert to Rig Default"
	_revert_button.pressed.connect(_revert_current_pose)
	selection_grid.add_child(_revert_button)

	var copy_grid := _grid(_content)
	copy_grid.add_child(_label("Copy/mirror target direction"))
	_copy_direction = OptionButton.new()
	for entry in [["N", "North"], ["E", "East"], ["S", "South"], ["W", "West"]]:
		_copy_direction.add_item(str(entry[1]))
		_copy_direction.set_item_metadata(_copy_direction.item_count - 1, str(entry[0]))
	copy_grid.add_child(_copy_direction)
	copy_grid.add_child(_label("Copy/mirror target frame"))
	_copy_frame = OptionButton.new()
	for index in [1, 2, 3, 4]:
		_copy_frame.add_item("F%d" % index)
		_copy_frame.set_item_metadata(_copy_frame.item_count - 1, index)
	copy_grid.add_child(_copy_frame)
	copy_grid.add_child(Label.new())
	_copy_button = Button.new()
	_copy_button.text = "Copy Current Value to Target"
	_copy_button.pressed.connect(_copy_current_value_to_target)
	copy_grid.add_child(_copy_button)
	copy_grid.add_child(Label.new())
	_mirror_button = Button.new()
	_mirror_button.text = "Mirror Current Value to Target"
	_mirror_button.pressed.connect(_mirror_current_value_to_target)
	copy_grid.add_child(_mirror_button)
	copy_grid.add_child(Label.new())
	_open_item_button = Button.new()
	_open_item_button.text = "Open Item Save Workflow"
	_open_item_button.pressed.connect(_open_item_save_workflow)
	copy_grid.add_child(_open_item_button)

	var zoom_row := HBoxContainer.new()
	zoom_row.add_child(_label("Zoom"))
	_zoom = OptionButton.new()
	for option in [["Fit", 0.0], ["100%", 1.0], ["200%", 2.0], ["400%", 4.0], ["800%", 8.0]]:
		_zoom.add_item(str(option[0]))
		_zoom.set_item_metadata(_zoom.item_count - 1, float(option[1]))
	_zoom.item_selected.connect(_on_zoom_changed.unbind(1))
	zoom_row.add_child(_zoom)
	_content.add_child(zoom_row)
	var alignment_heading := _label("Combined Actor + Item Alignment")
	alignment_heading.add_theme_font_size_override("font_size", 16)
	_content.add_child(alignment_heading)
	_alignment_scroll = ScrollContainer.new()
	_alignment_scroll.custom_minimum_size = Vector2(0, 360)
	_alignment_scroll.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_alignment_scroll.size_flags_vertical = Control.SIZE_EXPAND_FILL
	_content.add_child(_alignment_scroll)
	_alignment_canvas = ALIGNMENT_CANVAS_SCRIPT.new()
	_alignment_canvas.socket_dragged.connect(_on_alignment_socket_dragged)
	_alignment_canvas.grip_anchor_dragged.connect(_on_alignment_grip_anchor_dragged)
	_alignment_scroll.add_child(_alignment_canvas)
	_alignment_status = Label.new()
	_alignment_status.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	_content.add_child(_alignment_status)
	_canvas_scroll = ScrollContainer.new()
	_canvas_scroll.custom_minimum_size = Vector2(0, 360)
	_canvas_scroll.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_canvas_scroll.size_flags_vertical = Control.SIZE_EXPAND_FILL
	_content.add_child(_canvas_scroll)
	_canvas = CANVAS_SCRIPT.new()
	_canvas.marker_dragged.connect(_on_marker_dragged)
	_canvas.rectangle_changed.connect(_on_rectangle_changed)
	_canvas_scroll.add_child(_canvas)

	var actions := HBoxContainer.new()
	actions.add_theme_constant_override("separation", 8)
	_save_button = Button.new()
	_save_button.text = "Save Calibration"
	_save_button.pressed.connect(_save_calibration)
	actions.add_child(_save_button)
	_reload_button = Button.new()
	_reload_button.text = "Reload Calibration"
	_reload_button.pressed.connect(_reload_calibration)
	actions.add_child(_reload_button)
	_discard_button = Button.new()
	_discard_button.text = "Discard Calibration Changes"
	_discard_button.pressed.connect(_discard_changes)
	actions.add_child(_discard_button)
	_content.add_child(actions)
	_dirty = Label.new()
	_content.add_child(_dirty)
	_status = Label.new()
	_status.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	content_add_status()
	_select_metadata(_direction, "S")
	_select_metadata(_frame, 1)
	_select_metadata(_mode, "socket")
	_select_metadata(_copy_direction, "S")
	_select_metadata(_copy_frame, 1)
	_select_metadata(_zoom, 0.0)
	_set_enabled(false)


func content_add_status() -> void:
	_content.add_child(_status)


func _apply_context(context: Dictionary) -> void:
	_context = context.duplicate(true)
	_pending_context = {}
	_conflicted = false
	_reload_confirm_pending = false
	_awaiting_load = false
	_awaiting_save = false
	_awaiting_frames = false
	_active_operation = ""
	_frames_after_load = false
	_queued_load = false
	_frames.clear()
	_displayed_frame_key = ""
	var rig := _context.get("rig", {}) as Dictionary
	var rig_id := str(_context.get("rig_id", ""))
	var composite := bool(_context.get("composite", false)) and not rig_id.is_empty() and not rig.is_empty()
	_disabled_message.visible = not composite
	_content.visible = composite
	if not composite:
		return
	_state.configure(rig, str(_context.get("calibration_id", "")))
	_syncing = true
	_rig_value.text = rig_id
	_calibration_id.text = _state.calibration_id
	_populate_sockets()
	_populate_foreground_overlays()
	_syncing = false
	if not bool(_context.get("calibrations_available", true)):
		_state.clear_loaded_state()
		_status.text = "Actor calibration catalog unavailable: %s" % str(_context.get("calibration_message", "The catalog could not be loaded."))
		_refresh_view()
	elif not _state.target_calibration_id.is_empty():
		_frames_after_load = true
		_begin_calibration_load(_state.target_calibration_id)
	else:
		_state.load_response({"exists": false, "catalog_hash": "", "calibration": {}})
		_request_frames()


func _request_frames() -> void:
	if _client == null or not _active_operation.is_empty():
		return
	var visual_texture_path := str(_context.get("visual_texture_path", ""))
	if visual_texture_path.is_empty():
		_status.text = "Select an actor texture before loading exact calibration frames."
		_refresh_view()
		return
	_awaiting_frames = true
	_active_operation = "frames"
	_client.load_actor_calibration_frames({
		"actor_kind": str(_context.get("actor_kind", "")),
		"visual_texture_path": visual_texture_path,
	})


func _load_calibration() -> void:
	var calibration_id := _calibration_id.text.strip_edges()
	if not _is_valid_calibration_id(calibration_id):
		_status.text = "Calibration ID must use lowercase letters, digits, and underscores."
		_refresh_view()
		return
	if not bool(_context.get("calibrations_available", true)):
		_status.text = "Actor calibration catalog unavailable: %s" % str(_context.get("calibration_message", "The catalog could not be loaded."))
		_refresh_view()
		return
	if _state.is_dirty() and calibration_id != _state.loaded_calibration_id:
		_status.text = "Unsaved calibration changes. Discard them before changing calibration target."
		_refresh_view()
		return
	if not _active_operation.is_empty():
		_queued_load = true
		_status.text = "Load / Create Calibration is queued until the active request completes."
		_refresh_view()
		return
	_begin_calibration_load(calibration_id)


func _begin_calibration_load(calibration_id: String) -> void:
	if _client == null:
		return
	_state.begin_load(calibration_id)
	_awaiting_load = true
	_active_operation = "load"
	_client.load_actor_calibration(calibration_id)
	_status.text = "Loading calibration..."
	_refresh_view()


func _on_calibration_received(payload: Dictionary) -> void:
	if not _awaiting_load:
		return
	_awaiting_load = false
	_active_operation = ""
	_conflicted = false
	_state.load_response(payload)
	_status.text = "Calibration loaded." if _state.exists else "Calibration will be created on its first saved override."
	if _frames_after_load:
		_frames_after_load = false
		if not _pending_context.is_empty():
			_process_queued_request()
		else:
			_request_frames()
	else:
		_process_queued_request()
	_refresh_view()


func _on_calibration_saved(payload: Dictionary) -> void:
	if not _awaiting_save:
		return
	_awaiting_save = false
	_active_operation = ""
	_conflicted = false
	_state.apply_saved_response(payload)
	_status.text = "Calibration saved."
	calibration_saved.emit(_state.calibration_id, str(_state.rig.get("rig_id", "")))
	_process_queued_request()
	_refresh_view()


func _on_frames_received(payload: Dictionary) -> void:
	if not _awaiting_frames:
		return
	if str(payload.get("actor_kind", "")) != str(_context.get("actor_kind", "")) or str(payload.get("visual_texture_path", "")) != str(_context.get("visual_texture_path", "")):
		return
	_awaiting_frames = false
	_active_operation = ""
	_frames.clear()
	for frame_variant in payload.get("frames", []) as Array:
		var frame := frame_variant as Dictionary
		_frames[_frame_key(str(frame.get("direction", "")), int(frame.get("frame", 0)))] = frame.duplicate(true)
	_status.text = ""
	_process_queued_request()
	_refresh_view()


func _on_request_failed(operation: String, message: String, errors: Array) -> void:
	if operation == "actor_calibration" and _awaiting_load:
		_awaiting_load = false
		_active_operation = ""
		_status.text = "Calibration load failed: %s" % message
	elif operation == "actor_calibration_save" and _awaiting_save:
		_awaiting_save = false
		_active_operation = ""
		_conflicted = _has_error_code(errors, "actor_calibration_catalog_conflict")
		_status.text = "Calibration changed on disk. Reload before saving." if _conflicted else "Calibration save failed: %s" % message
	elif operation == "actor_calibration_frames" and _awaiting_frames:
		_awaiting_frames = false
		_active_operation = ""
		_status.text = "Exact calibration frames could not be loaded: %s" % message
	else:
		return
	_process_queued_request()
	_refresh_view()


func _on_calibration_id_changed(_value: String) -> void:
	if _syncing:
		return
	_refresh_view()


func _on_selection_changed() -> void:
	_refresh_view()


func _on_coordinate_changed() -> void:
	if _syncing or _selected_mode() != "socket" or not _can_mutate_selected_pose():
		return
	_state.set_override(_selected_socket_id(), _selected_direction(), _selected_frame(), Vector2i(int(_socket_x.value), int(_socket_y.value)))
	_refresh_view()


func _on_grip_coordinate_changed() -> void:
	if _syncing or _selected_mode() != "grip" or not _can_edit_grip_anchor():
		return
	_set_current_grip_anchor(Vector2i(int(_grip_x.value), int(_grip_y.value)))
	_refresh_view()


func _on_marker_dragged(point: Vector2i) -> void:
	if _selected_mode() != "socket" or not _can_mutate_selected_pose():
		return
	_state.set_override(_selected_socket_id(), _selected_direction(), _selected_frame(), point)
	_refresh_view()


func _on_alignment_socket_dragged(point: Vector2i) -> void:
	if _selected_mode() != "socket" or not _can_mutate_selected_pose():
		return
	_state.set_override(_selected_socket_id(), _selected_direction(), _selected_frame(), point)
	_refresh_view()


func _on_alignment_grip_anchor_dragged(point: Vector2i) -> void:
	if _selected_mode() != "grip" or not _can_edit_grip_anchor():
		return
	_set_current_grip_anchor(point)
	_refresh_view()


func _on_rectangle_changed(rectangle: Dictionary) -> void:
	if _selected_mode() != "foreground_overlay" or not _can_mutate_selected_pose():
		return
	_state.set_foreground_overlay_override(_selected_overlay_id(), _selected_direction(), _selected_frame(), rectangle)
	_refresh_view()


func _on_rectangle_coordinate_changed() -> void:
	if _syncing or _selected_mode() != "foreground_overlay" or not _can_mutate_selected_pose():
		return
	var source_size := _canvas.source_bounds().size
	var x := clampi(int(_overlay_x.value), 0, max(0, int(source_size.x) - 1))
	var y := clampi(int(_overlay_y.value), 0, max(0, int(source_size.y) - 1))
	var width := clampi(int(_overlay_width.value), 1, max(1, int(source_size.x) - x))
	var height := clampi(int(_overlay_height.value), 1, max(1, int(source_size.y) - y))
	_state.set_foreground_overlay_override(_selected_overlay_id(), _selected_direction(), _selected_frame(), {"x": x, "y": y, "width": width, "height": height})
	_refresh_view()


func _create_foreground_overlay_override() -> void:
	if _selected_mode() != "foreground_overlay" or not _can_mutate_selected_pose():
		return
	var source_size := _canvas.source_bounds().size
	if source_size.x <= 0 or source_size.y <= 0:
		return
	var overlay := _state.get_overlay(_selected_overlay_id())
	var socket := _state.resolve_effective_point(str(overlay.get("socket_id", "")), _selected_direction(), _selected_frame())
	var point := socket.get("point", {}) as Dictionary
	var width := mini(16, int(source_size.x))
	var height := mini(16, int(source_size.y))
	var x := clampi(int(point.get("x", 0)) - width / 2, 0, int(source_size.x) - width)
	var y := clampi(int(point.get("y", 0)) - height / 2, 0, int(source_size.y) - height)
	_state.set_foreground_overlay_override(_selected_overlay_id(), _selected_direction(), _selected_frame(), {"x": x, "y": y, "width": width, "height": height})
	_status.text = "Created an unsaved actor override rectangle."
	_refresh_view()


func _on_zoom_changed() -> void:
	_refresh_canvas_zoom()


func _revert_current_pose() -> void:
	var reverted := false
	if _can_mutate_selected_pose():
		if _selected_mode() == "socket":
			reverted = _state.revert_override(_selected_socket_id(), _selected_direction(), _selected_frame())
		else:
			reverted = _state.revert_foreground_overlay_override(_selected_overlay_id(), _selected_direction(), _selected_frame())
	if reverted:
		_status.text = "Current pose override removed."
	_refresh_view()


func _save_calibration() -> void:
	if not _can_save() or _client == null:
		return
	_awaiting_save = true
	_active_operation = "save"
	var payload := _state.save_payload()
	payload["actor_kind"] = str(_context.get("actor_kind", ""))
	payload["visual_texture_path"] = str(_context.get("visual_texture_path", ""))
	_client.save_actor_calibration(_calibration_id.text.strip_edges(), payload)
	_status.text = "Saving calibration..."
	_refresh_view()


func _reload_calibration() -> void:
	if _state.is_dirty() and not _reload_confirm_pending:
		_reload_confirm_pending = true
		_status.text = "Reload will discard calibration changes. Press Reload Calibration again to confirm."
		_refresh_view()
		return
	_reload_confirm_pending = false
	if _active_operation.is_empty():
		_load_calibration()
	else:
		_queued_load = true
		_status.text = "Reload Calibration is queued until the active request completes."
		_refresh_view()


func _discard_changes() -> void:
	_state.discard_changes()
	_reload_confirm_pending = false
	if not _pending_context.is_empty():
		_apply_context(_pending_context)
		return
	_status.text = "Calibration changes discarded."
	_refresh_view()


func _use_calibration_for_actor() -> void:
	if _state.exists and _is_valid_calibration_id(_state.calibration_id):
		use_calibration_for_actor.emit(_state.calibration_id)


func _refresh_view() -> void:
	if _content == null or not _content.visible:
		return
	var frame := _selected_frame_payload()
	var available := bool(frame.get("available", false))
	var socket_mode := _selected_mode() == "socket"
	var grip_mode := _selected_mode() == "grip"
	var effective := _state.resolve_effective_point(_selected_socket_id(), _selected_direction(), _selected_frame()) if socket_mode else _state.resolve_effective_rectangle(_selected_overlay_id(), _selected_direction(), _selected_frame())
	var has_coordinate := bool(effective.get("available", false))
	var is_override := bool(effective.get("is_override", false))
	_syncing = true
	if socket_mode and has_coordinate:
		var point := effective.get("point", {}) as Dictionary
		_socket_x.value = int(point.get("x", 0))
		_socket_y.value = int(point.get("y", 0))
		_source.text = "Actor override" if is_override else "Inherited from %s" % str(_state.rig.get("rig_id", ""))
	elif socket_mode:
		_socket_x.value = 0
		_socket_y.value = 0
		_source.text = "No coordinate for this pose"
	elif grip_mode:
		var grip_anchor := _current_grip_anchor()
		_grip_x.value = grip_anchor.x
		_grip_y.value = grip_anchor.y
		_source.text = "Item metadata (unsaved until transferred to the Item workspace)"
	else:
		var rectangle := effective.get("rectangle", {}) as Dictionary
		_overlay_x.value = int(rectangle.get("x", 0))
		_overlay_y.value = int(rectangle.get("y", 0))
		_overlay_width.value = int(rectangle.get("width", 1))
		_overlay_height.value = int(rectangle.get("height", 1))
		var overlay := _state.get_overlay(_selected_overlay_id())
		var socket_id := str(overlay.get("socket_id", ""))
		if has_coordinate:
			_source.text = "Actor Override" if is_override else "Inherited Rig Rectangle"
		else:
			_source.text = "No Rectangle for This Pose\nNo inherited rectangle for this pose. Socket: %s" % socket_id
	_syncing = false
	_update_canvas(frame, available, has_coordinate, effective, socket_mode)
	_refresh_alignment_workspace(frame, available)
	var editable := _can_mutate_selected_pose()
	_socket_x.editable = editable and socket_mode
	_socket_y.editable = editable and socket_mode
	_grip_x.editable = grip_mode and _can_edit_grip_anchor()
	_grip_y.editable = grip_mode and _can_edit_grip_anchor()
	_overlay_x.editable = editable and not socket_mode and has_coordinate
	_overlay_y.editable = editable and not socket_mode and has_coordinate
	_overlay_width.editable = editable and not socket_mode and has_coordinate
	_overlay_height.editable = editable and not socket_mode and has_coordinate
	_create_override_button.visible = not socket_mode and not grip_mode
	_create_override_button.disabled = not editable or socket_mode or grip_mode or has_coordinate
	_revert_button.visible = not grip_mode
	_revert_button.disabled = not editable or (_state.has_override(_selected_socket_id(), _selected_direction(), _selected_frame()) if socket_mode else _state.has_foreground_overlay_override(_selected_overlay_id(), _selected_direction(), _selected_frame()))
	_copy_direction.disabled = not socket_mode and not grip_mode
	_copy_frame.disabled = not socket_mode and not grip_mode
	_copy_button.visible = socket_mode or grip_mode
	_mirror_button.visible = socket_mode or grip_mode
	_copy_button.disabled = not _can_copy_current_value()
	_mirror_button.disabled = not _can_mirror_current_value()
	_open_item_button.visible = grip_mode
	_open_item_button.disabled = not _can_edit_grip_anchor()
	_load_button.disabled = not bool(_context.get("calibrations_available", true)) or not _is_valid_calibration_id(_calibration_id.text.strip_edges())
	_use_button.disabled = not _state.exists or not _state.is_loaded_target(_calibration_id.text.strip_edges()) or _state.calibration_id == str(_context.get("calibration_id", ""))
	_save_button.disabled = not _can_save()
	_reload_button.disabled = not bool(_context.get("calibrations_available", true)) or not _is_valid_calibration_id(_calibration_id.text.strip_edges())
	_discard_button.disabled = not _state.is_dirty() or _awaiting_save
	_dirty.text = "Unsaved calibration changes" if _state.is_dirty() else "Saved"
	_canvas_scroll.visible = not socket_mode and not grip_mode
	if not available:
		_status.text = "Selected exact pose is unavailable. No compatibility frame is used."
	elif socket_mode and has_coordinate and not _canvas.source_bounds().has_point(Vector2(_socket_x.value, _socket_y.value)):
		_status.text = "Socket is outside this source frame."
	elif _status.text.is_empty():
		if socket_mode:
			_status.text = "Drag the gold socket marker or edit integer source coordinates."
		elif grip_mode:
			_status.text = "Drag the item art or edit the pink grip anchor in integer source pixels."
		else:
			_status.text = "Drag or resize the foreground rectangle using integer source pixels."
	_refresh_canvas_zoom()


func _update_canvas(frame: Dictionary, available: bool, has_coordinate: bool, effective: Dictionary, socket_mode: bool) -> void:
	var key := _frame_key(_selected_direction(), _selected_frame())
	if available and key != _displayed_frame_key:
		_canvas.set_frame(frame)
		_displayed_frame_key = key
	elif not available:
		_canvas.set_frame({})
		_displayed_frame_key = ""
	if socket_mode and available and has_coordinate:
		var point := effective.get("point", {}) as Dictionary
		_canvas.set_marker(Vector2i(int(point.get("x", 0)), int(point.get("y", 0))), bool(effective.get("is_override", false)), _can_mutate_selected_pose())
	else:
		_canvas.clear_marker(false)
	if socket_mode:
		_canvas.clear_rectangle()
		return
	var overlay := _state.get_overlay(_selected_overlay_id())
	var socket := _state.resolve_effective_point(str(overlay.get("socket_id", "")), _selected_direction(), _selected_frame())
	if available and bool(socket.get("available", false)):
		var point := socket.get("point", {}) as Dictionary
		_canvas.set_marker(Vector2i(int(point.get("x", 0)), int(point.get("y", 0))), bool(socket.get("is_override", false)), false)
	if available and has_coordinate:
		_canvas.set_rectangle(effective.get("rectangle", {}) as Dictionary, bool(effective.get("is_override", false)), _can_mutate_selected_pose())
	else:
		_canvas.clear_rectangle()


func _refresh_alignment_workspace(actor_frame: Dictionary, actor_frame_available: bool) -> void:
	if _alignment_canvas == null:
		return
	if not actor_frame_available:
		_alignment_canvas.set_composition({}, {}, Vector2i.ZERO, Vector2i.ZERO, Vector2i.ZERO, false, 0, {}, 0, _selected_mode(), false)
		_alignment_status.text = "Combined alignment requires the selected exact actor pose."
		return
	var visual := _selected_equipped_visual()
	if visual.is_empty():
		_alignment_canvas.set_composition(actor_frame, {}, Vector2i.ZERO, Vector2i.ZERO, Vector2i.ZERO, false, 0, {}, 0, _selected_mode(), false)
		_alignment_status.text = "No socket-bound equipped visual is selected for %s." % _selected_socket_id()
		return
	_ensure_grip_state(visual)
	var socket_result := _state.resolve_effective_point(_selected_socket_id(), _selected_direction(), _selected_frame())
	if not bool(socket_result.get("available", false)):
		_alignment_canvas.set_composition(actor_frame, {}, Vector2i.ZERO, Vector2i.ZERO, Vector2i.ZERO, false, 0, {}, 0, _selected_mode(), false)
		_alignment_status.text = "The selected socket has no effective coordinate for this pose."
		return
	var item_frame := _selected_item_frame(visual, _selected_direction(), _selected_frame())
	if item_frame.is_empty():
		_alignment_canvas.set_composition(actor_frame, {}, Vector2i.ZERO, Vector2i.ZERO, Vector2i.ZERO, false, 0, {}, 0, _selected_mode(), false)
		_alignment_status.text = "Exact item art is unavailable for %s / F%d." % [_selected_direction(), _selected_frame()]
		return
	if _pose_enabled(visual.get("hidden_poses", {}), _selected_direction(), _selected_frame()):
		_alignment_canvas.set_composition(actor_frame, {}, Vector2i.ZERO, Vector2i.ZERO, Vector2i.ZERO, false, 0, {}, 0, _selected_mode(), false)
		_alignment_status.text = "The selected equipped visual is hidden for this pose."
		return
	var socket_data := socket_result.get("point", {}) as Dictionary
	var socket := Vector2i(int(socket_data.get("x", 0)), int(socket_data.get("y", 0)))
	var authored_grip := _current_grip_anchor()
	var flip_x := _pose_enabled(visual.get("flip_poses", {}), _selected_direction(), _selected_frame())
	var item_width := int(item_frame.get("source_width", 0))
	var effective_grip := ATTACHMENT_ALIGNMENT.resolve_effective_grip_anchor(authored_grip, item_width, flip_x)
	var nudge_data := visual.get("nudge", {}) as Dictionary
	var nudge := Vector2i(int(nudge_data.get("x", 0)), int(nudge_data.get("y", 0)))
	var item_position := ATTACHMENT_ALIGNMENT.resolve_item_position(socket, effective_grip, nudge)
	var overlay := _selected_socket_overlay()
	var overlay_rectangle: Dictionary = {}
	var overlay_z_index := 0
	if not overlay.is_empty():
		var overlay_result := _state.resolve_effective_rectangle(str(overlay.get("overlay_id", "")), _selected_direction(), _selected_frame())
		overlay_rectangle = overlay_result.get("rectangle", {}) as Dictionary
		overlay_z_index = _layer_z_index(overlay.get("z_index_by_direction", {}) as Dictionary) - _base_z_index()
	var item_z_index := _layer_z_index_for_id(str(visual.get("render_layer_id", ""))) - _base_z_index()
	if _pose_enabled(visual.get("item_over_grip_poses", {}), _selected_direction(), _selected_frame()) and not overlay.is_empty():
		item_z_index = overlay_z_index + 1
	elif _selected_direction() == "N":
		item_z_index = mini(item_z_index, -1)
	_alignment_canvas.set_composition(
		actor_frame,
		item_frame,
		socket,
		effective_grip,
		item_position,
		flip_x,
		item_z_index,
		overlay_rectangle,
		overlay_z_index,
		_selected_mode(),
		_can_mutate_selected_pose() if _selected_mode() == "socket" else _can_edit_grip_anchor())
	match _selected_mode():
		"socket":
			_alignment_status.text = "Socket edit mode: drag gold to change only the actor calibration."
		"grip":
			_alignment_status.text = "Item grip edit mode: drag pink, then open the Item workspace to validate and save item metadata."
		_:
			_alignment_status.text = "Foreground overlay edit mode remains actor-owned and uses the separate rectangle editor below."


func _selected_equipped_visual() -> Dictionary:
	var cosmetics := _context.get("cosmetic_item_ids", {}) as Dictionary
	for visual_variant in _context.get("equipped_visuals", []) as Array:
		if not (visual_variant is Dictionary):
			continue
		var visual := visual_variant as Dictionary
		if str(visual.get("binding_type", "")) != "socket" or str(visual.get("socket_id", "")) != _selected_socket_id():
			continue
		var render_layer_id := str(visual.get("render_layer_id", ""))
		if str(cosmetics.get(render_layer_id, "")) == str(visual.get("item_id", "")):
			return visual.duplicate(true)
	return {}


func _selected_item_frame(visual: Dictionary, direction: String, frame: int) -> Dictionary:
	var root := str(_context.get("game_client_assets_root", ""))
	var render_layer_id := str(visual.get("render_layer_id", ""))
	var asset_key := str(visual.get("asset_key", ""))
	if root.is_empty() or render_layer_id.is_empty() or asset_key.is_empty():
		return {}
	var file_path := root.path_join("actors/player/%s/%s-F%d-%s.png" % [render_layer_id, asset_key, frame, direction])
	if not FileAccess.file_exists(file_path):
		return {}
	var image := Image.load_from_file(file_path)
	if image == null or image.is_empty():
		return {}
	return {"file_path": file_path, "source_width": image.get_width(), "source_height": image.get_height()}


func _selected_socket_overlay() -> Dictionary:
	for overlay_variant in _state.get_foreground_overlays():
		var overlay := overlay_variant as Dictionary
		if str(overlay.get("socket_id", "")) == _selected_socket_id():
			return overlay
	return {}


func _base_z_index() -> int:
	var base_layer_id := str(_state.rig.get("solid_sprite_base_layer_id", "body"))
	return _layer_z_index_for_id(base_layer_id)


func _layer_z_index_for_id(layer_id: String) -> int:
	for layer_variant in _state.rig.get("layers", []) as Array:
		var layer := layer_variant as Dictionary
		if str(layer.get("layer_id", "")) == layer_id:
			return _layer_z_index(layer.get("z_index_by_direction", {}) as Dictionary)
	return 0


func _layer_z_index(z_indexes: Dictionary) -> int:
	return int(z_indexes.get(_selected_direction(), 0))


func _ensure_grip_state(visual: Dictionary) -> void:
	var item_id := str(visual.get("item_id", ""))
	if item_id == _grip_item_id:
		return
	_grip_item_id = item_id
	var anchors_variant: Variant = visual.get("grip_anchors", {})
	_grip_anchors = (anchors_variant as Dictionary).duplicate(true) if anchors_variant is Dictionary else {}


func _current_grip_anchor() -> Vector2i:
	var directions := _grip_anchors.get(_selected_direction(), {}) as Dictionary
	var point := directions.get(str(_selected_frame()), {}) as Dictionary
	return Vector2i(int(point.get("x", 0)), int(point.get("y", 0)))


func _set_current_grip_anchor(point: Vector2i) -> void:
	var directions := _grip_anchors.get(_selected_direction(), {}) as Dictionary
	directions[str(_selected_frame())] = {"x": clampi(point.x, -4096, 4096), "y": clampi(point.y, -4096, 4096)}
	_grip_anchors[_selected_direction()] = directions


func _set_grip_anchor(direction: String, frame: int, point: Vector2i) -> void:
	var directions := _grip_anchors.get(direction, {}) as Dictionary
	directions[str(frame)] = {"x": clampi(point.x, -4096, 4096), "y": clampi(point.y, -4096, 4096)}
	_grip_anchors[direction] = directions


func _pose_enabled(values_variant: Variant, direction: String, frame: int) -> bool:
	if not (values_variant is Dictionary):
		return false
	var frames_variant: Variant = (values_variant as Dictionary).get(direction, {})
	return bool((frames_variant as Dictionary).get(str(frame), false)) if frames_variant is Dictionary else false


func _can_edit_grip_anchor() -> bool:
	if not _selected_frame_available() \
		or not _active_operation.is_empty() \
		or _awaiting_load \
		or _awaiting_save \
		or _awaiting_frames:
		return false
	var visual := _selected_equipped_visual()
	return not visual.is_empty() and not _selected_item_frame(visual, _selected_direction(), _selected_frame()).is_empty() and not _pose_enabled(visual.get("hidden_poses", {}), _selected_direction(), _selected_frame())


func _target_direction() -> String:
	return str(_selected_metadata(_copy_direction))


func _target_frame() -> int:
	return int(_selected_metadata(_copy_frame))


func _can_copy_current_value() -> bool:
	if _selected_mode() == "socket":
		return _can_mutate_selected_pose() and _target_actor_frame().get("available", false)
	if _selected_mode() == "grip":
		return _can_edit_grip_anchor() and not _target_item_frame().is_empty()
	return false


func _can_mirror_current_value() -> bool:
	return _can_copy_current_value()


func _target_actor_frame() -> Dictionary:
	return _frames.get(_frame_key(_target_direction(), _target_frame()), {}) as Dictionary


func _target_item_frame() -> Dictionary:
	var visual := _selected_equipped_visual()
	return _selected_item_frame(visual, _target_direction(), _target_frame()) if not visual.is_empty() else {}


func _copy_current_value_to_target() -> void:
	if not _can_copy_current_value():
		return
	if _selected_mode() == "socket":
		var current := _state.resolve_effective_point(_selected_socket_id(), _selected_direction(), _selected_frame())
		var point := current.get("point", {}) as Dictionary
		_state.set_override(_selected_socket_id(), _target_direction(), _target_frame(), Vector2i(int(point.get("x", 0)), int(point.get("y", 0))))
	else:
		_set_grip_anchor(_target_direction(), _target_frame(), _current_grip_anchor())
	_status.text = "Copied the current %s value to %s / F%d." % ["socket" if _selected_mode() == "socket" else "item grip anchor", _target_direction(), _target_frame()]
	_refresh_view()


func _mirror_current_value_to_target() -> void:
	if not _can_mirror_current_value():
		return
	if _selected_mode() == "socket":
		var current := _state.resolve_effective_point(_selected_socket_id(), _selected_direction(), _selected_frame())
		var point := current.get("point", {}) as Dictionary
		var target_width := int(_target_actor_frame().get("source_width", 0))
		_state.set_override(_selected_socket_id(), _target_direction(), _target_frame(), ATTACHMENT_ALIGNMENT.mirror_effective_point(Vector2i(int(point.get("x", 0)), int(point.get("y", 0))), target_width))
	else:
		var visual := _selected_equipped_visual()
		var source_frame := _selected_item_frame(visual, _selected_direction(), _selected_frame())
		var target_frame := _target_item_frame()
		var source_flip := _pose_enabled(visual.get("flip_poses", {}), _selected_direction(), _selected_frame())
		var target_flip := _pose_enabled(visual.get("flip_poses", {}), _target_direction(), _target_frame())
		var effective := ATTACHMENT_ALIGNMENT.resolve_effective_grip_anchor(_current_grip_anchor(), int(source_frame.get("source_width", 0)), source_flip)
		var mirrored := ATTACHMENT_ALIGNMENT.mirror_effective_point(effective, int(target_frame.get("source_width", 0)))
		_set_grip_anchor(_target_direction(), _target_frame(), ATTACHMENT_ALIGNMENT.resolve_authored_grip_anchor(mirrored, int(target_frame.get("source_width", 0)), target_flip))
	_status.text = "Mirrored the current %s value to %s / F%d using exact source widths and pose flip metadata." % ["socket" if _selected_mode() == "socket" else "item grip anchor", _target_direction(), _target_frame()]
	_refresh_view()


func _open_item_save_workflow() -> void:
	if not _can_edit_grip_anchor() or _grip_item_id.is_empty():
		return
	item_grip_handoff_requested.emit(_grip_item_id, _grip_anchors.duplicate(true))
	_alignment_status.text = "Item grip edits were handed to the Item workspace. Validate and save the item there; calibration data was not changed."


func _refresh_canvas_zoom() -> void:
	if _canvas == null:
		return
	var requested := float(_selected_metadata(_zoom))
	if requested <= 0.0:
		var frame := _selected_frame_payload()
		var source_size := Vector2(float(frame.get("source_width", 1)), float(frame.get("source_height", 1)))
		requested = RIGGED_PREVIEW_LAYOUT.fit_scale(source_size + Vector2(128.0, 128.0), _canvas_scroll.size, 0.0)
	_canvas.set_zoom_scale(requested)
	if _alignment_canvas != null:
		_alignment_canvas.set_zoom_scale(requested)


func _populate_sockets() -> void:
	var selected := _selected_socket_id()
	_socket.clear()
	for socket_id in _state.get_socket_ids():
		_socket.add_item(socket_id.replace("_", " ").capitalize())
		_socket.set_item_metadata(_socket.item_count - 1, socket_id)
	_select_metadata(_socket, selected if not selected.is_empty() else "right_hand_primary")


func _populate_foreground_overlays() -> void:
	var selected := _selected_overlay_id()
	_overlay.clear()
	for overlay_variant in _state.get_foreground_overlays():
		var overlay := overlay_variant as Dictionary
		var overlay_id := str(overlay.get("overlay_id", ""))
		if overlay_id.is_empty():
			continue
		_overlay.add_item(overlay_id.replace("_", " ").capitalize())
		_overlay.set_item_metadata(_overlay.item_count - 1, overlay_id)
	_select_metadata(_overlay, selected if not selected.is_empty() else "right_hand_primary_grip")


func _selected_frame_payload() -> Dictionary:
	return _frames.get(_frame_key(_selected_direction(), _selected_frame()), {}) as Dictionary


func _selected_frame_available() -> bool:
	return bool(_selected_frame_payload().get("available", false))


func _selected_socket_id() -> String:
	return str(_selected_metadata(_socket))


func _selected_mode() -> String:
	return str(_selected_metadata(_mode))


func _selected_overlay_id() -> String:
	return str(_selected_metadata(_overlay))


func _selected_direction() -> String:
	return str(_selected_metadata(_direction))


func _selected_frame() -> int:
	return int(_selected_metadata(_frame))


func _can_save() -> bool:
	return _state.is_dirty() and _can_mutate_selected_pose() and _state.is_loaded_target(_calibration_id.text.strip_edges()) and _is_valid_calibration_id(_calibration_id.text.strip_edges()) and not str(_state.rig.get("rig_id", "")).is_empty()


func _can_mutate_selected_pose() -> bool:
	return _selected_frame_available() \
		and bool(_context.get("calibrations_available", true)) \
		and _active_operation.is_empty() \
		and not _awaiting_load \
		and not _awaiting_save \
		and not _conflicted \
		and _state.is_loaded_target(_calibration_id.text.strip_edges())


func _process_queued_request() -> void:
	if not _active_operation.is_empty():
		return
	if not _pending_context.is_empty():
		var pending := _pending_context.duplicate(true)
		_pending_context = {}
		_apply_context(pending)
		return
	if _queued_load:
		_queued_load = false
		_load_calibration()


func _set_enabled(enabled: bool) -> void:
	_disabled_message.visible = not enabled
	_content.visible = enabled


func _same_context(left: Dictionary, right: Dictionary) -> bool:
	return str(left.get("actor_kind", "")) == str(right.get("actor_kind", "")) and str(left.get("visual_texture_path", "")) == str(right.get("visual_texture_path", "")) and str(left.get("rig_id", "")) == str(right.get("rig_id", "")) and str(left.get("calibration_id", "")) == str(right.get("calibration_id", "")) and bool(left.get("calibrations_available", true)) == bool(right.get("calibrations_available", true))


func _frame_key(direction: String, frame: int) -> String:
	return "%s:%d" % [direction, frame]


func _is_valid_calibration_id(value: String) -> bool:
	var expression := RegEx.new()
	return expression.compile(CALIBRATION_ID_PATTERN) == OK and expression.search(value.strip_edges()) != null


func _has_error_code(errors: Array, code: String) -> bool:
	for error_variant in errors:
		if error_variant is Dictionary and str((error_variant as Dictionary).get("code", "")) == code:
			return true
	return false


func _coordinate_field() -> SpinBox:
	var field := SpinBox.new()
	field.min_value = -4096
	field.max_value = 4096
	field.step = 1
	field.allow_greater = false
	field.allow_lesser = false
	field.rounded = true
	return field


func _overlay_coordinate_field() -> SpinBox:
	var field := SpinBox.new()
	field.min_value = 0
	field.max_value = 4096
	field.step = 1
	field.allow_greater = false
	field.allow_lesser = false
	field.rounded = true
	return field


func _grid(parent: Node) -> GridContainer:
	var grid := GridContainer.new()
	grid.columns = 2
	grid.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	grid.add_theme_constant_override("h_separation", 10)
	grid.add_theme_constant_override("v_separation", 6)
	parent.add_child(grid)
	return grid


func _label(text: String) -> Label:
	var label := Label.new()
	label.text = text
	label.custom_minimum_size = Vector2(120, 0)
	return label


func _select_metadata(control: OptionButton, value: Variant) -> void:
	for index in range(control.item_count):
		if control.get_item_metadata(index) == value:
			control.select(index)
			return
	if control.item_count > 0:
		control.select(0)


func _selected_metadata(control: OptionButton) -> Variant:
	return control.get_item_metadata(control.selected) if control != null and control.selected >= 0 else ""
