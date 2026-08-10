extends Control
class_name ActorSocketCalibrationCanvas

const RIGGED_PREVIEW_LAYOUT := preload("res://scripts/rigged_sprite_preview_layout.gd")
const PADDING := 64.0
const MARKER_RADIUS := 5.0
const MARKER_HIT_RADIUS := 14.0

signal marker_dragged(point: Vector2i)

var _texture: Texture2D
var _source_size := Vector2i.ZERO
var _marker := Vector2i.ZERO
var _has_marker := false
var _marker_is_override := false
var _drag_enabled := false
var _zoom_scale := 1.0
var _drag_offset_source := Vector2.ZERO
var _dragging := false


func _ready() -> void:
	texture_filter = CanvasItem.TEXTURE_FILTER_NEAREST
	mouse_filter = Control.MOUSE_FILTER_STOP


func set_frame(frame: Dictionary) -> void:
	_texture = null
	_source_size = Vector2i(int(frame.get("source_width", 0)), int(frame.get("source_height", 0)))
	var file_path := str(frame.get("file_path", ""))
	if not file_path.is_empty():
		var image := Image.load_from_file(file_path)
		if image != null and not image.is_empty():
			_texture = ImageTexture.create_from_image(image)
			_source_size = Vector2i(image.get_width(), image.get_height())
	_update_canvas_size()
	queue_redraw()


func set_zoom_scale(scale: float) -> void:
	_zoom_scale = maxf(scale, 0.01)
	_update_canvas_size()
	queue_redraw()


func set_marker(point: Vector2i, is_override: bool, drag_enabled: bool) -> void:
	_marker = point
	_has_marker = true
	_marker_is_override = is_override
	_drag_enabled = drag_enabled
	queue_redraw()


func clear_marker(drag_enabled: bool = false) -> void:
	_has_marker = false
	_drag_enabled = drag_enabled
	_dragging = false
	queue_redraw()


func source_to_preview(source_point: Vector2) -> Vector2:
	return RIGGED_PREVIEW_LAYOUT.source_to_preview_with_transform(source_point, _transform())


func preview_to_source(preview_point: Vector2) -> Vector2:
	return RIGGED_PREVIEW_LAYOUT.preview_to_source_with_transform(preview_point, _transform())


func source_bounds() -> Rect2:
	return Rect2(Vector2.ZERO, Vector2(_source_size))


func _gui_input(event: InputEvent) -> void:
	if not _drag_enabled or not _has_marker or _source_size.x <= 0 or _source_size.y <= 0:
		return
	if event is InputEventMouseButton and event.button_index == MOUSE_BUTTON_LEFT:
		if event.pressed:
			var marker_preview := source_to_preview(Vector2(_marker))
			if event.position.distance_to(marker_preview) <= MARKER_HIT_RADIUS:
				_dragging = true
				_drag_offset_source = preview_to_source(event.position) - Vector2(_marker)
				accept_event()
		elif _dragging:
			_dragging = false
			accept_event()
	elif event is InputEventMouseMotion and _dragging and (event.button_mask & MOUSE_BUTTON_MASK_LEFT) != 0:
		var source_point := preview_to_source(event.position) - _drag_offset_source
		var clamped := Vector2(
			clampf(source_point.x, 0.0, float(_source_size.x - 1)),
			clampf(source_point.y, 0.0, float(_source_size.y - 1)))
		var quantized := Vector2i(
			RIGGED_PREVIEW_LAYOUT.quantize_source_pixel(clamped.x),
			RIGGED_PREVIEW_LAYOUT.quantize_source_pixel(clamped.y))
		if quantized != _marker:
			_marker = quantized
			marker_dragged.emit(quantized)
			queue_redraw()
		accept_event()


func _draw() -> void:
	draw_rect(Rect2(Vector2.ZERO, size), Color(0.045, 0.052, 0.065), true)
	if _texture == null or _source_size.x <= 0 or _source_size.y <= 0:
		return
	var image_rect := Rect2(_transform().get("origin", Vector2.ZERO), Vector2(_source_size) * _zoom_scale)
	draw_texture_rect(_texture, image_rect, false)
	draw_rect(image_rect, Color(0.36, 0.56, 0.72, 0.95), false, 1.0)
	if _zoom_scale >= 6.0:
		_draw_pixel_grid(image_rect)
	if _has_marker and source_bounds().has_point(Vector2(_marker)):
		_draw_marker(source_to_preview(Vector2(_marker)))


func _draw_pixel_grid(image_rect: Rect2) -> void:
	var color := Color(0.86, 0.9, 0.98, 0.2)
	for x in range(_source_size.x + 1):
		var position := image_rect.position.x + float(x) * _zoom_scale
		draw_line(Vector2(position, image_rect.position.y), Vector2(position, image_rect.end.y), color, 1.0)
	for y in range(_source_size.y + 1):
		var position := image_rect.position.y + float(y) * _zoom_scale
		draw_line(Vector2(image_rect.position.x, position), Vector2(image_rect.end.x, position), color, 1.0)


func _draw_marker(position: Vector2) -> void:
	var color := Color(0.95, 0.82, 0.36) if _marker_is_override else Color(0.36, 0.86, 0.98)
	if _marker_is_override:
		draw_circle(position, MARKER_RADIUS, color)
	else:
		draw_arc(position, MARKER_RADIUS, 0.0, TAU, 20, color, 2.0)
	draw_line(position + Vector2(-MARKER_RADIUS - 3, 0), position + Vector2(MARKER_RADIUS + 3, 0), color, 1.5)
	draw_line(position + Vector2(0, -MARKER_RADIUS - 3), position + Vector2(0, MARKER_RADIUS + 3), color, 1.5)


func _transform() -> Dictionary:
	return {"scale": _zoom_scale, "origin": Vector2(PADDING, PADDING)}


func _update_canvas_size() -> void:
	var image_size := Vector2(_source_size) * _zoom_scale
	custom_minimum_size = image_size + Vector2(PADDING * 2.0, PADDING * 2.0)
	size = custom_minimum_size
