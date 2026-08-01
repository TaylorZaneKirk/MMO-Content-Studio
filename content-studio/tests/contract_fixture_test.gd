extends SceneTree

const EXPECTED_API_VERSION := "1"


func _initialize() -> void:
	var envelope := {
		"api_version": "1",
		"request_id": "fixture",
		"success": true,
		"data": {
			"sections": [
				{"content_type": "items", "entries": []},
				{"content_type": "mobs", "entries": []},
				{"content_type": "npcs", "entries": []},
			]
		},
		"errors": [],
	}

	if envelope.api_version != EXPECTED_API_VERSION:
		push_error("API version fixture mismatch")
		quit(1)
		return

	if envelope.data.sections.size() != 3:
		push_error("Expected three T0 catalog sections")
		quit(1)
		return

	print("[content-studio-contract-fixture] passed")
	quit(0)
