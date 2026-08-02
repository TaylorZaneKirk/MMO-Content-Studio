extends SceneTree

const EXPECTED_API_VERSION := "1"


func _initialize() -> void:
	var main_scene := load("res://scenes/Main.tscn") as PackedScene
	if main_scene == null:
		push_error("T2 main scene or one of its scripts failed to parse")
		quit(1)
		return

	var envelope := {
		"api_version": "1",
		"request_id": "fixture",
		"success": true,
		"data": {
			"target_operation": "save_draft",
			"valid_for_draft": true,
			"valid_for_publication": true,
			"messages": [],
			"changes": [
				{"field": "effect", "before": null, "after": "restore_resource:health:3-5"},
			],
		},
		"errors": [],
	}

	if envelope.api_version != EXPECTED_API_VERSION:
		push_error("API version fixture mismatch")
		quit(1)
		return

	if not envelope.data.valid_for_draft or envelope.data.changes.size() != 1:
		push_error("T2 consumable-preview fixture mismatch")
		quit(1)
		return

	print("[content-studio-contract-fixture] passed")
	quit(0)
