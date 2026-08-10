extends VBoxContainer
class_name ActorSocketCalibrationEditor

const STATE_SCRIPT := preload("res://scripts/actor_socket_calibration_state.gd")
const CANVAS_SCRIPT := preload("res://scripts/actor_socket_calibration_canvas.gd")
const RIGGED_PREVIEW_LAYOUT := preload("res://scripts/rigged_sprite_preview_layout.gd")
const CALIBRATION_ID_PATTERN := "^[a-z0-9][a-z0-9_]{0,63}$"

signal use_calibration_for_actor(calibration_id: String)
signal calibration_saved(calibration_id: String, rig_id: String)

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

var _disabled_message: Label
var _content: VBoxContainer
var _rig_value: Label
var _calibration_id: LineEdit
var _load_button: Button
var _use_button: Button
var _socket: OptionButton
var _direction: OptionButton
var _frame: OptionButton
var _source: Label
var _socket_x: SpinBox
var _socket_y: SpinBox
var _revert_button: Button
var _zoom: OptionButton
var _canvas_scroll: ScrollContainer
var _canvas: ActorSocketCalibrationCanvas
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
	_disabled_message.text = "Socket calibration is available for Rigged Sprite actors."
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
	selection_grid.add_child(_label("Socket"))
	_socket = OptionButton.new()
	_socket.item_selected.connect(_on_selection_changed.unbind(1))
	selection_grid.add_child(_socket)
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
	selection_grid.add_child(Label.new())
	_revert_button = Button.new()
	_revert_button.text = "Revert to Rig Default"
	_revert_button.pressed.connect(_revert_current_pose)
	selection_grid.add_child(_revert_button)

	var zoom_row := HBoxContainer.new()
	zoom_row.add_child(_label("Zoom"))
	_zoom = OptionButton.new()
	for option in [["Fit", 0.0], ["100%", 1.0], ["200%", 2.0], ["400%", 4.0], ["800%", 8.0]]:
		_zoom.add_item(str(option[0]))
		_zoom.set_item_metadata(_zoom.item_count - 1, float(option[1]))
	_zoom.item_selected.connect(_on_zoom_changed.unbind(1))
	zoom_row.add_child(_zoom)
	_content.add_child(zoom_row)
	_canvas_scroll = ScrollContainer.new()
	_canvas_scroll.custom_minimum_size = Vector2(0, 360)
	_canvas_scroll.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_canvas_scroll.size_flags_vertical = Control.SIZE_EXPAND_FILL
	_content.add_child(_canvas_scroll)
	_canvas = CANVAS_SCRIPT.new()
	_canvas.marker_dragged.connect(_on_marker_dragged)
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
	if _syncing or not _can_mutate_selected_pose():
		return
	_state.set_override(_selected_socket_id(), _selected_direction(), _selected_frame(), Vector2i(int(_socket_x.value), int(_socket_y.value)))
	_refresh_view()


func _on_marker_dragged(point: Vector2i) -> void:
	if not _can_mutate_selected_pose():
		return
	_state.set_override(_selected_socket_id(), _selected_direction(), _selected_frame(), point)
	_refresh_view()


func _on_zoom_changed() -> void:
	_refresh_canvas_zoom()


func _revert_current_pose() -> void:
	if _can_mutate_selected_pose() and _state.revert_override(_selected_socket_id(), _selected_direction(), _selected_frame()):
		_status.text = "Current pose override removed."
	_refresh_view()


func _save_calibration() -> void:
	if not _can_save() or _client == null:
		return
	_awaiting_save = true
	_active_operation = "save"
	_client.save_actor_calibration(_calibration_id.text.strip_edges(), _state.save_payload())
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
	var effective := _state.resolve_effective_point(_selected_socket_id(), _selected_direction(), _selected_frame())
	var has_coordinate := bool(effective.get("available", false))
	var is_override := bool(effective.get("is_override", false))
	_syncing = true
	if has_coordinate:
		var point := effective.get("point", {}) as Dictionary
		_socket_x.value = int(point.get("x", 0))
		_socket_y.value = int(point.get("y", 0))
		_source.text = "Actor override" if is_override else "Inherited from %s" % str(_state.rig.get("rig_id", ""))
	else:
		_socket_x.value = 0
		_socket_y.value = 0
		_source.text = "No coordinate for this pose"
	_syncing = false
	_update_canvas(frame, available, has_coordinate, effective)
	var editable := _can_mutate_selected_pose()
	_socket_x.editable = editable
	_socket_y.editable = editable
	_revert_button.disabled = not editable or not _state.has_override(_selected_socket_id(), _selected_direction(), _selected_frame())
	_load_button.disabled = not bool(_context.get("calibrations_available", true)) or not _is_valid_calibration_id(_calibration_id.text.strip_edges())
	_use_button.disabled = not _state.exists or not _state.is_loaded_target(_calibration_id.text.strip_edges()) or _state.calibration_id == str(_context.get("calibration_id", ""))
	_save_button.disabled = not _can_save()
	_reload_button.disabled = not bool(_context.get("calibrations_available", true)) or not _is_valid_calibration_id(_calibration_id.text.strip_edges())
	_discard_button.disabled = not _state.is_dirty() or _awaiting_save
	_dirty.text = "Unsaved calibration changes" if _state.is_dirty() else "Saved"
	if not available:
		_status.text = "Selected exact pose is unavailable. No compatibility frame is used."
	elif has_coordinate and not _canvas.source_bounds().has_point(Vector2(_socket_x.value, _socket_y.value)):
		_status.text = "Socket is outside this source frame."
	elif _status.text.is_empty():
		_status.text = "Drag the marker or edit integer source coordinates."
	_refresh_canvas_zoom()


func _update_canvas(frame: Dictionary, available: bool, has_coordinate: bool, effective: Dictionary) -> void:
	var key := _frame_key(_selected_direction(), _selected_frame())
	if available and key != _displayed_frame_key:
		_canvas.set_frame(frame)
		_displayed_frame_key = key
	elif not available:
		_canvas.set_frame({})
		_displayed_frame_key = ""
	if available and has_coordinate:
		var point := effective.get("point", {}) as Dictionary
		_canvas.set_marker(Vector2i(int(point.get("x", 0)), int(point.get("y", 0))), bool(effective.get("is_override", false)), _can_mutate_selected_pose())
	else:
		_canvas.clear_marker(false)


func _refresh_canvas_zoom() -> void:
	if _canvas == null:
		return
	var requested := float(_selected_metadata(_zoom))
	if requested <= 0.0:
		var frame := _selected_frame_payload()
		var source_size := Vector2(float(frame.get("source_width", 1)), float(frame.get("source_height", 1)))
		requested = RIGGED_PREVIEW_LAYOUT.fit_scale(source_size + Vector2(128.0, 128.0), _canvas_scroll.size, 0.0)
	_canvas.set_zoom_scale(requested)


func _populate_sockets() -> void:
	var selected := _selected_socket_id()
	_socket.clear()
	for socket_id in _state.get_socket_ids():
		_socket.add_item(socket_id.replace("_", " ").capitalize())
		_socket.set_item_metadata(_socket.item_count - 1, socket_id)
	_select_metadata(_socket, selected if not selected.is_empty() else "right_hand_primary")


func _selected_frame_payload() -> Dictionary:
	return _frames.get(_frame_key(_selected_direction(), _selected_frame()), {}) as Dictionary


func _selected_frame_available() -> bool:
	return bool(_selected_frame_payload().get("available", false))


func _selected_socket_id() -> String:
	return str(_selected_metadata(_socket))


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
