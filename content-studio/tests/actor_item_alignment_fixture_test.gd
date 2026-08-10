extends SceneTree

const ALIGNMENT := preload("res://scripts/actor_attachment_alignment.gd")
const CANVAS := preload("res://scripts/actor_item_alignment_canvas.gd")


func _initialize() -> void:
	call_deferred("_run_fixture")


func _run_fixture() -> void:
	_verify_alignment_math()
	await _verify_canvas_composition_and_dragging()
	print("[actor-item-alignment-fixture] passed")
	quit(0)


func _verify_alignment_math() -> void:
	var socket := Vector2i(40, 72)
	var authored_grip := Vector2i(16, 8)
	var effective_grip := ALIGNMENT.resolve_effective_grip_anchor(authored_grip, 24, true)
	_expect(effective_grip == Vector2i(7, 8), "Flipped anchors must resolve from the exact item texture width")
	var item_position := ALIGNMENT.resolve_item_position(socket, effective_grip, Vector2i(-2, 3))
	_expect(item_position == Vector2i(31, 67), "Combined placement must remain socket minus grip plus nudge")
	_expect(item_position + effective_grip == Vector2i(38, 75), "Resolved item grip must land on the nudged actor socket")
	_expect(ALIGNMENT.resolve_authored_grip_anchor(effective_grip, 24, true) == authored_grip, "Flipped grip conversion must round trip without subpixels")
	_expect(ALIGNMENT.mirror_effective_point(Vector2i(7, 8), 30) == Vector2i(22, 8), "Explicit mirroring must use the selected target texture width")


func _verify_canvas_composition_and_dragging() -> void:
	var temporary_root := ProjectSettings.globalize_path("user://actor-item-alignment-fixture")
	DirAccess.make_dir_recursive_absolute(temporary_root)
	var actor_path := temporary_root.path_join("actor.png")
	var item_path := temporary_root.path_join("item.png")
	_write_fixture_png(actor_path, Vector2i(20, 16), Color(0.25, 0.65, 0.95, 1.0))
	_write_fixture_png(item_path, Vector2i(10, 8), Color(0.95, 0.55, 0.25, 1.0))

	var canvas = CANVAS.new()
	root.add_child(canvas)
	canvas.set_composition(
		{"file_path": actor_path, "source_width": 20, "source_height": 16},
		{"file_path": item_path, "source_width": 10, "source_height": 8},
		Vector2i(3, 6),
		Vector2i(7, 2),
		Vector2i(-4, 4),
		false,
		-1,
		{"x": 1, "y": 1, "width": 4, "height": 4},
		1,
		"socket",
		true)
	_expect(canvas._composition_bounds.position == Vector2(-4, 0), "Combined canvas must retain item art extending west of the actor")
	_expect(canvas._composition_bounds.size == Vector2(24, 16), "Combined canvas must cover both actor and item source bounds")

	var socket_dragged: Array = []
	canvas.socket_dragged.connect(func(point: Vector2i) -> void: socket_dragged.append(point))
	var press := InputEventMouseButton.new()
	press.button_index = MOUSE_BUTTON_LEFT
	press.pressed = true
	press.position = canvas.source_to_preview(Vector2(3, 6))
	canvas._gui_input(press)
	var move := InputEventMouseMotion.new()
	move.button_mask = MOUSE_BUTTON_MASK_LEFT
	move.position = canvas.source_to_preview(Vector2(19, 15))
	canvas._gui_input(move)
	_expect(socket_dragged == [Vector2i(19, 15)], "Socket mode must drag only the actor-owned socket marker")

	canvas.set_composition(
		{"file_path": actor_path, "source_width": 20, "source_height": 16},
		{"file_path": item_path, "source_width": 10, "source_height": 8},
		Vector2i(3, 6),
		Vector2i(7, 2),
		Vector2i(-4, 4),
		true,
		1,
		{},
		0,
		"grip",
		true)
	canvas.set_zoom_scale(4.0)
	var grip_dragged: Array = []
	canvas.grip_anchor_dragged.connect(func(point: Vector2i) -> void: grip_dragged.append(point))
	press.position = canvas.source_to_preview(Vector2(-2, 5))
	canvas._gui_input(press)
	move.position = canvas.source_to_preview(Vector2(-1, 11))
	canvas._gui_input(move)
	_expect(grip_dragged == [Vector2i(6, 7)], "Grip mode must convert a flipped effective point back to authored item metadata")
	_expect(canvas.preview_to_source(canvas.source_to_preview(Vector2(-4, 4))).is_equal_approx(Vector2(-4, 4)), "Fit and manual zoom transforms must preserve combined source registration")

	canvas.queue_free()
	await process_frame


func _write_fixture_png(path: String, size: Vector2i, color: Color) -> void:
	var image := Image.create(size.x, size.y, false, Image.FORMAT_RGBA8)
	image.fill(color)
	image.save_png(path)


func _expect(condition: bool, message: String) -> void:
	if condition:
		return
	push_error(message)
	quit(1)
