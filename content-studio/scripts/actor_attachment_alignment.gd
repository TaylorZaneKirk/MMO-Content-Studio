extends RefCounted
class_name ActorAttachmentAlignment


static func resolve_effective_grip_anchor(anchor: Vector2i, texture_width: int, flip_x: bool) -> Vector2i:
	if not flip_x:
		return anchor
	return Vector2i((maxi(texture_width, 1) - 1) - anchor.x, anchor.y)


static func resolve_item_position(socket: Vector2i, effective_grip_anchor: Vector2i, nudge: Vector2i = Vector2i.ZERO) -> Vector2i:
	return socket - effective_grip_anchor + nudge


static func resolve_authored_grip_anchor(effective_anchor: Vector2i, texture_width: int, flip_x: bool) -> Vector2i:
	return resolve_effective_grip_anchor(effective_anchor, texture_width, flip_x)


static func mirror_effective_point(point: Vector2i, target_width: int) -> Vector2i:
	return Vector2i((maxi(target_width, 1) - 1) - point.x, point.y)
