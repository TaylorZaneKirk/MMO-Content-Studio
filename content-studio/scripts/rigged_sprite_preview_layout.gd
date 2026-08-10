extends RefCounted
class_name RiggedSpritePreviewLayout

static func build_draw_list(manifest: Dictionary) -> Array:
	if manifest.is_empty():
		return []
	var draw_list: Array = [{"kind": "base", "id": "base", "z_index": 0}]
	for cosmetic_variant in manifest.get("cosmetics", []) as Array:
		var cosmetic := cosmetic_variant as Dictionary
		draw_list.append({"kind": "cosmetic", "id": str(cosmetic.get("item_id", "")), "z_index": int(cosmetic.get("z_index", 0)), "payload": cosmetic})
	for overlay_variant in manifest.get("foreground_overlays", []) as Array:
		var overlay := overlay_variant as Dictionary
		draw_list.append({"kind": "overlay", "id": str(overlay.get("overlay_id", "")), "z_index": int(overlay.get("z_index", 0)), "payload": overlay})
	draw_list.sort_custom(_draw_entry_precedes)
	return draw_list


static func fit_scale(canvas_size: Vector2, pane_size: Vector2, padding: float = 16.0) -> float:
	var available := (pane_size - Vector2(padding, padding) * 2.0).max(Vector2.ONE)
	return min(available.x / max(1.0, canvas_size.x), available.y / max(1.0, canvas_size.y))


static func fit_scale_or_default(
	canvas_size: Vector2,
	pane_size: Vector2,
	padding: float = 16.0,
	fallback_scale: float = 1.0,
	minimum_viewport_size: Vector2 = Vector2(32.0, 32.0)
) -> float:
	var minimum := minimum_viewport_size.max(Vector2(padding, padding) * 2.0 + Vector2.ONE)
	if pane_size.x < minimum.x or pane_size.y < minimum.y:
		return fallback_scale
	return fit_scale(canvas_size, pane_size, padding)


static func preview_transform(canvas_size: Vector2, pane_size: Vector2, padding: float = 16.0) -> Dictionary:
	var scale := fit_scale(canvas_size, pane_size, padding)
	return {
		"scale": scale,
		"origin": (pane_size - canvas_size * scale) * 0.5,
	}


static func source_to_preview(source_point: Vector2, canvas_size: Vector2, pane_size: Vector2, padding: float = 16.0) -> Vector2:
	var transform := preview_transform(canvas_size, pane_size, padding)
	return transform.get("origin", Vector2.ZERO) + source_point * float(transform.get("scale", 1.0))


static func preview_to_source(preview_point: Vector2, canvas_size: Vector2, pane_size: Vector2, padding: float = 16.0) -> Vector2:
	var transform := preview_transform(canvas_size, pane_size, padding)
	return preview_to_source_with_transform(preview_point, transform)


static func source_to_preview_with_transform(source_point: Vector2, transform: Dictionary) -> Vector2:
	return transform.get("origin", Vector2.ZERO) + source_point * float(transform.get("scale", 1.0))


static func preview_to_source_with_transform(preview_point: Vector2, transform: Dictionary) -> Vector2:
	var scale := float(transform.get("scale", 1.0))
	return (preview_point - transform.get("origin", Vector2.ZERO)) / maxf(scale, 0.000001)


static func quantize_source_pixel(source_coordinate: float) -> int:
	return floori(source_coordinate + 0.5) if source_coordinate >= 0.0 else ceili(source_coordinate - 0.5)


static func _draw_entry_precedes(left: Dictionary, right: Dictionary) -> bool:
	var left_z := int(left.get("z_index", 0))
	var right_z := int(right.get("z_index", 0))
	if left_z != right_z:
		return left_z < right_z
	var left_kind := _kind_order(str(left.get("kind", "")))
	var right_kind := _kind_order(str(right.get("kind", "")))
	if left_kind != right_kind:
		return left_kind < right_kind
	return str(left.get("id", "")) < str(right.get("id", ""))


static func _kind_order(kind: String) -> int:
	match kind:
		"base": return 0
		"cosmetic": return 1
		"overlay": return 2
		_: return 3
