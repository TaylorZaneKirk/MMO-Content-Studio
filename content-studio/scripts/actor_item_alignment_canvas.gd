extends Control
class_name ActorItemAlignmentCanvas

const ALIGNMENT := preload("res://scripts/actor_attachment_alignment.gd")
const RIGGED_PREVIEW_LAYOUT := preload("res://scripts/rigged_sprite_preview_layout.gd")
const PADDING := 64.0
const MARKER_RADIUS := 5.0
const MARKER_HIT_RADIUS := 14.0

signal socket_dragged(point: Vector2i)
signal grip_anchor_dragged(point: Vector2i)

var _actor_texture: Texture2D
var _actor_size := Vector2i.ZERO
var _item_texture: Texture2D
var _item_position := Vector2i.ZERO
var _item_flip_x := false
var _item_z_index := 0
var _overlay_z_index := 0
var _socket := Vector2i.ZERO
var _effective_grip_anchor := Vector2i.ZERO
var _overlay_rectangle: Dictionary = {}
var _mode := "socket"
var _editable := false
var _zoom_scale := 1.0
var _composition_bounds := Rect2()
var _dragging := false


func _ready() -> void:
	texture_filter = CanvasItem.TEXTURE_FILTER_NEAREST
	mouse_filter = Control.MOUSE_FILTER_STOP


func set_composition(
	actor_frame: Dictionary,
	item_frame: Dictionary,
	socket: Vector2i,
	effective_grip_anchor: Vector2i,
	item_position: Vector2i,
	item_flip_x: bool,
	item_z_index: int,
	overlay_rectangle: Dictionary,
	overlay_z_index: int,
	mode: String,
	editable: bool
) -> void:
	_actor_texture = _load_texture(str(actor_frame.get("file_path", "")))
	_actor_size = Vector2i(int(actor_frame.get("source_width", 0)), int(actor_frame.get("source_height", 0)))
	if _actor_texture != null:
		_actor_size = Vector2i(_actor_texture.get_width(), _actor_texture.get_height())
	_item_texture = _load_texture(str(item_frame.get("file_path", "")))
	_item_position = item_position
	_item_flip_x = item_flip_x
	_item_z_index = item_z_index
	_overlay_z_index = overlay_z_index
	_socket = socket
	_effective_grip_anchor = effective_grip_anchor
	_overlay_rectangle = overlay_rectangle.duplicate(true)
	_mode = mode
	_editable = editable
	_dragging = false
	_update_composition_bounds()
	_update_canvas_size()
	queue_redraw()


func set_zoom_scale(scale: float) -> void:
	_zoom_scale = maxf(scale, 0.01)
	_update_canvas_size()
	queue_redraw()


func actor_source_bounds() -> Rect2:
	return Rect2(Vector2.ZERO, Vector2(_actor_size))


func fit_content_size() -> Vector2:
	return _composition_bounds.size


func fit_padding() -> float:
	return PADDING


func source_to_preview(source_point: Vector2) -> Vector2:
	return _preview_origin() + (source_point - _composition_bounds.position) * _zoom_scale


func preview_to_source(preview_point: Vector2) -> Vector2:
	return ((preview_point - _preview_origin()) / _zoom_scale) + _composition_bounds.position


func _gui_input(event: InputEvent) -> void:
	if _actor_size.x <= 0 or _actor_size.y <= 0 or not _editable:
		return
	if event is InputEventMouseButton and event.button_index == MOUSE_BUTTON_LEFT:
		if event.pressed and _can_begin_drag(event.position):
			_dragging = true
			accept_event()
		elif not event.pressed and _dragging:
			_dragging = false
			accept_event()
	elif event is InputEventMouseMotion and _dragging and (event.button_mask & MOUSE_BUTTON_MASK_LEFT) != 0:
		_apply_drag(event.position)
		accept_event()


func _can_begin_drag(position: Vector2) -> bool:
	match _mode:
		"socket":
			return position.distance_to(source_to_preview(Vector2(_socket))) <= MARKER_HIT_RADIUS
		"grip":
			return _item_texture != null and _item_preview_rectangle().grow(MARKER_HIT_RADIUS).has_point(position)
	return false


func _apply_drag(position: Vector2) -> void:
	var source_point := preview_to_source(position)
	if _mode == "socket":
		var clamped := Vector2(
			clampf(source_point.x, 0.0, float(_actor_size.x - 1)),
			clampf(source_point.y, 0.0, float(_actor_size.y - 1)))
		socket_dragged.emit(Vector2i(
			RIGGED_PREVIEW_LAYOUT.quantize_source_pixel(clamped.x),
			RIGGED_PREVIEW_LAYOUT.quantize_source_pixel(clamped.y)))
		return
	if _mode != "grip" or _item_texture == null:
		return
	var local_effective := source_point - Vector2(_item_position)
	var clamped_effective := Vector2i(
		clampi(RIGGED_PREVIEW_LAYOUT.quantize_source_pixel(local_effective.x), 0, _item_texture.get_width() - 1),
		clampi(RIGGED_PREVIEW_LAYOUT.quantize_source_pixel(local_effective.y), 0, _item_texture.get_height() - 1))
	grip_anchor_dragged.emit(ALIGNMENT.resolve_authored_grip_anchor(clamped_effective, _item_texture.get_width(), _item_flip_x))


func _draw() -> void:
	draw_rect(Rect2(Vector2.ZERO, size), Color(0.045, 0.052, 0.065), true)
	if _actor_texture == null or _actor_size.x <= 0 or _actor_size.y <= 0:
		return
	var actor_rect := Rect2(source_to_preview(Vector2.ZERO), Vector2(_actor_size) * _zoom_scale)
	if _item_texture != null and _item_z_index < 0:
		_draw_item()
	draw_texture_rect(_actor_texture, actor_rect, false)
	if _item_texture != null and _item_z_index >= 0 and _item_z_index <= _overlay_z_index:
		_draw_item()
	_draw_overlay()
	if _item_texture != null and _item_z_index > _overlay_z_index:
		_draw_item()
	_draw_markers()


func _draw_item() -> void:
	var destination := _item_preview_rectangle()
	draw_set_transform(destination.get_center(), 0.0, Vector2(-1, 1) if _item_flip_x else Vector2.ONE)
	draw_texture_rect(_item_texture, Rect2(-destination.size * 0.5, destination.size), false)
	draw_set_transform(Vector2.ZERO)


func _draw_overlay() -> void:
	if _overlay_rectangle.is_empty():
		return
	var source := Rect2(
		float(_overlay_rectangle.get("x", 0)),
		float(_overlay_rectangle.get("y", 0)),
		float(_overlay_rectangle.get("width", 0)),
		float(_overlay_rectangle.get("height", 0)))
	if source.size.x <= 0.0 or source.size.y <= 0.0:
		return
	draw_texture_rect_region(_actor_texture, Rect2(source_to_preview(source.position), source.size * _zoom_scale), source)


func _draw_markers() -> void:
	var socket_position := source_to_preview(Vector2(_socket))
	var grip_position := source_to_preview(Vector2(_item_position + _effective_grip_anchor))
	draw_line(socket_position, grip_position, Color(0.85, 0.86, 0.94, 0.75), 1.0)
	_draw_marker(socket_position, Color(0.95, 0.82, 0.36), true)
	_draw_marker(grip_position, Color(0.96, 0.44, 0.74), false)


func _draw_marker(position: Vector2, color: Color, filled: bool) -> void:
	if filled:
		draw_circle(position, MARKER_RADIUS, color)
	else:
		draw_arc(position, MARKER_RADIUS, 0.0, TAU, 20, color, 2.0)
	draw_line(position + Vector2(-MARKER_RADIUS - 3, 0), position + Vector2(MARKER_RADIUS + 3, 0), color, 1.5)
	draw_line(position + Vector2(0, -MARKER_RADIUS - 3), position + Vector2(0, MARKER_RADIUS + 3), color, 1.5)


func _item_preview_rectangle() -> Rect2:
	if _item_texture == null:
		return Rect2()
	return Rect2(source_to_preview(Vector2(_item_position)), _item_texture.get_size() * _zoom_scale)


func _update_composition_bounds() -> void:
	_composition_bounds = Rect2(Vector2.ZERO, Vector2(_actor_size))
	if _item_texture != null:
		_composition_bounds = _composition_bounds.merge(Rect2(Vector2(_item_position), _item_texture.get_size()))
	if _composition_bounds.size.x <= 0.0 or _composition_bounds.size.y <= 0.0:
		_composition_bounds = Rect2(Vector2.ZERO, Vector2.ONE)


func _preview_origin() -> Vector2:
	return Vector2(PADDING, PADDING)


func _update_canvas_size() -> void:
	custom_minimum_size = fit_content_size() * _zoom_scale + Vector2(PADDING * 2.0, PADDING * 2.0)
	size = custom_minimum_size


func _load_texture(file_path: String) -> Texture2D:
	if file_path.is_empty() or not FileAccess.file_exists(file_path):
		return null
	var image := Image.load_from_file(file_path)
	return ImageTexture.create_from_image(image) if image != null and not image.is_empty() else null
