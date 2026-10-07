# Owns this focused editor's fields and preview/apply decision. Host owns durable validation.
extends VBoxContainer
@onready var _client: AuthoringHostClient = %AuthoringHostClient
const DEFAULTS := {"saw_item_id": "inventory_175_saw", "hammer_item_id": "blacksmithing_hammer", "log_item_id": "inventory_174_logs", "plank_item_id": "normal_plank", "nails_item_id": "bronze_nails", "chair_item_id": "crude_wooden_chair", "station_definition_id": "home_workbench", "saw_level": 1, "saw_xp_tenths": 25, "saw_duration_ms": 3000, "chair_level": 1, "chair_xp_tenths": 580, "chair_duration_ms": 3000, "chair_planks": 2, "chair_nails": 2, "placement_level": 1, "footprint_width_tiles": 1, "footprint_height_tiles": 1, "occupies_furniture_space": true, "blocks_movement": false, "east_texture_path": "res://assets/maps/objects/world_objects/MapGFX_83_C4.png", "west_texture_path": "res://assets/maps/objects/world_objects/MapGFX_84_D4.png"}
const FIELDS := [["saw_item_id", "Saw item id"], ["hammer_item_id", "Hammer item id"], ["log_item_id", "Log item id"], ["plank_item_id", "Plank item id"], ["nails_item_id", "Nails item id"], ["chair_item_id", "Chair item id"], ["station_definition_id", "Station definition id"], ["saw_level", "Saw level"], ["saw_xp_tenths", "Saw xp tenths"], ["saw_duration_ms", "Saw duration ms"], ["chair_level", "Chair level"], ["chair_xp_tenths", "Chair xp tenths"], ["chair_duration_ms", "Chair duration ms"], ["chair_planks", "Chair planks"], ["chair_nails", "Chair nails"], ["placement_level", "Placement level"], ["footprint_width_tiles", "Footprint width tiles"], ["footprint_height_tiles", "Footprint height tiles"], ["occupies_furniture_space", "Occupies furniture space"], ["blocks_movement", "Blocks movement"], ["east_texture_path", "East texture path"], ["west_texture_path", "West texture path"]]
var _fields: Dictionary = {}
var _current: Dictionary = {}
var _request: Dictionary = {}
var _operation: OptionButton
var _apply: Button
var _status: RichTextLabel
var _busy := false

func _ready() -> void:
	var title := Label.new()
	title.text = "Crafting — planks and home furniture"
	add_child(title)
	var reload_button := Button.new()
	reload_button.text = "Load / reload saved settings"
	reload_button.pressed.connect(func():
		if not _busy:
			_apply.disabled = true
			_set_busy(true)
			_client.load_crafting("v1"))
	add_child(reload_button)
	var scroll := ScrollContainer.new()
	scroll.size_flags_vertical = Control.SIZE_EXPAND_FILL
	add_child(scroll)
	var form := VBoxContainer.new()
	form.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	scroll.add_child(form)
	for spec: Array in FIELDS:
		var label := Label.new()
		label.text = str(spec[1])
		form.add_child(label)
		var field := LineEdit.new()
		field.text_changed.connect(func(_value: String): _apply.disabled = true)
		form.add_child(field)
		_fields[spec[0]] = field
	_operation = OptionButton.new()
	_operation.add_item("Save draft")
	_operation.add_item("Publish saved settings")
	_operation.add_item("Disable")
	_operation.add_item("Delete")
	_operation.item_selected.connect(func(_index: int): _apply.disabled = true)
	add_child(_operation)
	var preview := Button.new()
	preview.text = "Preview changes"
	preview.pressed.connect(_preview)
	add_child(preview)
	_apply = Button.new()
	_apply.text = "Apply reviewed changes"
	_apply.disabled = true
	_apply.pressed.connect(_mutate)
	add_child(_apply)
	_status = RichTextLabel.new()
	_status.custom_minimum_size.y = 130
	_status.scroll_active = true
	_status.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	add_child(_status)
	_client.crafting_definition_received.connect(_loaded)
	_client.crafting_preview_received.connect(_previewed)
	_client.crafting_mutation_completed.connect(_completed)
	_client.request_failed.connect(_failed)

func open_resource(_id: String) -> void:
	if _busy:
		return
	_set_busy(true)
	_apply.disabled = true
	_client.load_crafting("v1")

func _loaded(data: Dictionary) -> void:
	_set_busy(false)
	_current = data
	for key: String in _fields:
		_fields[key].text = str(data.get("draft", {}).get(key, ""))
	_apply.disabled = true
	_status.text = "Loaded %s. Draft edits retain the previous published rules." % str(data.get("publication_state", ""))

func _preview() -> void:
	if _busy or _current.is_empty():
		return
	var draft: Dictionary = {}
	for key: String in _fields:
		var value: String = _fields[key].text.strip_edges()
		if key in ["saw_item_id", "hammer_item_id", "log_item_id", "plank_item_id", "nails_item_id", "chair_item_id", "station_definition_id", "east_texture_path", "west_texture_path"]:
			draft[key] = value
		elif key in ["occupies_furniture_space", "blocks_movement"]:
			if value.to_lower() not in ["true", "false"]:
				_status.text = "Enter true or false for " + key
				return
			draft[key] = value.to_lower() == "true"
		elif not value.is_valid_int():
			_status.text = "Enter a number for " + key
			return
		else:
			draft[key] = int(value)
	_request = {"draft": draft, "expected_updated_at_utc": _current.get("updated_at_utc"), "target_operation": ["save_draft", "publish", "disable", "delete"][_operation.selected]}
	_apply.disabled = true
	_set_busy(true)
	_client.preview_crafting("v1", _request)

func _previewed(data: Dictionary) -> void:
	_set_busy(false)
	_request["preview_signature"] = data.get("preview_signature", "")
	_apply.disabled = not bool(data.get("applicable", false))
	_status.text = JSON.stringify(data.get("changes", []), "  ") + "\n" + JSON.stringify(data.get("messages", []), "  ")

func _mutate() -> void:
	_set_busy(true)
	_apply.disabled = true
	_client.mutate_crafting("v1", str(_request["target_operation"]), _request)

func _completed(data: Dictionary) -> void:
	_set_busy(false)
	var definition: Variant = data.get("definition")
	_loaded(definition if definition is Dictionary else {"draft": DEFAULTS.duplicate(true), "publication_state": "Deleted — new draft"})
	_status.text = "Database saved. " + _status.text
	for message: Dictionary in data.get("messages", []):
		_status.text += "\n" + str(message.get("message", ""))
	_status.text += "\nThe live game has not been restarted."

func _failed(_operation_name: String, _message: String, _errors: Array) -> void:
	if not _operation_name.begins_with("crafting_"):
		return
	_set_busy(false)
	for error: Dictionary in _errors:
		if str(error.get("code", "")) == "crafting_not_found":
			_loaded({"draft": DEFAULTS.duplicate(true), "publication_state": "New draft"})
			return
	_apply.disabled = true
	_status.text = "Request failed. Reload saved settings before trying again."


func _set_busy(value: bool) -> void:
	_busy = value
	for field: LineEdit in _fields.values():
		field.editable = not value
	_operation.disabled = value
