# Owns this focused editor's fields and preview/apply decision. Host owns durable validation.
extends VBoxContainer
@onready var _client: AuthoringHostClient = %AuthoringHostClient
const DEFAULTS := {"saw_item_id": "inventory_175_saw", "hammer_item_id": "blacksmithing_hammer", "log_item_id": "inventory_174_logs", "nails_item_id": "bronze_nails", "station_definition_id": "home_workbench", "furniture": [{"recipe_id": "craft_chair", "display_name": "Crude wooden chair", "item_id": "crude_wooden_chair", "level": 1, "xp_tenths": 580, "duration_ms": 3000, "inputs": [{"item_id": "normal_plank", "quantity": 2}, {"item_id": "bronze_nails", "quantity": 2}], "required_tool_ids": ["blacksmithing_hammer", "inventory_175_saw"], "placement_layer": "furniture", "south_texture_path": "", "placement_level": 1, "footprint_width_tiles": 1, "footprint_height_tiles": 1, "occupies_furniture_space": true, "blocks_movement": false, "east_texture_path": "res://assets/maps/objects/world_objects/MapGFX_83_C4.png", "west_texture_path": "res://assets/maps/objects/world_objects/MapGFX_84_D4.png"}], "sawing": [{"recipe_id": "saw_plank", "display_name": "Saw plank", "log_item_id": "inventory_174_logs", "plank_item_id": "normal_plank", "level": 1, "xp_tenths": 25, "duration_ms": 3000}]}
const FIELDS := [["saw_item_id", "Saw item id"], ["hammer_item_id", "Hammer item id"], ["log_item_id", "Arrow shaft log item id"], ["nails_item_id", "Nails item id"], ["station_definition_id", "Station definition id"]]
const ARROW_DEFAULTS := {"knife_item_id": "crafting_knife", "shaft_item_id": "arrow_shafts", "headless_item_id": "headless_arrows", "feather_item_id": "feather", "bronze_head_item_id": "bronze_arrowheads", "iron_head_item_id": "iron_arrowheads", "bronze_arrow_item_id": "bronze_arrows", "iron_arrow_item_id": "iron_arrows", "shafts": {"level": 1, "xp_tenths": 50, "duration_ms": 3000}, "headless": {"level": 1, "xp_tenths": 10, "duration_ms": 3000}, "bronze": {"level": 1, "xp_tenths": 13, "duration_ms": 3000}, "iron": {"level": 15, "xp_tenths": 25, "duration_ms": 3000}}
const ARROW_FIELDS := [["knife_item_id", "Knife Item Id"], ["shaft_item_id", "Shaft Item Id"], ["headless_item_id", "Headless Item Id"], ["feather_item_id", "Feather Item Id"], ["bronze_head_item_id", "Bronze Head Item Id"], ["iron_head_item_id", "Iron Head Item Id"], ["bronze_arrow_item_id", "Bronze Arrow Item Id"], ["iron_arrow_item_id", "Iron Arrow Item Id"], ["shafts.level", "Shafts level"], ["shafts.xp_tenths", "Shafts xp tenths"], ["shafts.duration_ms", "Shafts duration ms"], ["headless.level", "Headless level"], ["headless.xp_tenths", "Headless xp tenths"], ["headless.duration_ms", "Headless duration ms"], ["bronze.level", "Bronze level"], ["bronze.xp_tenths", "Bronze xp tenths"], ["bronze.duration_ms", "Bronze duration ms"], ["iron.level", "Iron level"], ["iron.xp_tenths", "Iron xp tenths"], ["iron.duration_ms", "Iron duration ms"]]
const FURNITURE_FIELDS := ["recipe_id", "display_name", "item_id", "level", "xp_tenths", "duration_ms", "inputs", "required_tool_ids", "placement_layer", "south_texture_path", "placement_level", "footprint_width_tiles", "footprint_height_tiles", "occupies_furniture_space", "blocks_movement", "east_texture_path", "west_texture_path"]
const SAWING_FIELDS := ["recipe_id", "display_name", "log_item_id", "plank_item_id", "level", "xp_tenths", "duration_ms"]
var _sawing_rows: VBoxContainer
var _add_sawing_button: Button
var _furniture_rows: VBoxContainer
var _add_furniture_button: Button
var _arrow_fields: Dictionary = {}
const SPINNING_DEFAULTS := {"wool_item_id": "raw_wool", "yarn_item_id": "wool_yarn", "station_definition_id": "spinning_wheel", "level": 1, "xp_tenths": 50, "duration_ms": 3000}
var _spinning_fields := {}
var _spinning_enabled: CheckBox
var _arrows_enabled: CheckBox
var _fields: Dictionary = {}
var _current: Dictionary = {}
var _request: Dictionary = {}
var _operation: OptionButton
var _apply: Button
var _status: RichTextLabel
var _busy := false

func _ready() -> void:
	var title := Label.new()
	title.text = "Crafting — planks, furniture, arrows and wool"
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
	_sawing_rows = VBoxContainer.new()
	form.add_child(_sawing_rows)
	_add_sawing_button = Button.new()
	_add_sawing_button.text = "Add sawing recipe"
	_add_sawing_button.pressed.connect(func():
		var facts: Dictionary = DEFAULTS.sawing[0].duplicate(true)
		for key in ["recipe_id", "display_name", "log_item_id", "plank_item_id"]: facts[key] = ""
		_add_sawing(facts)
		_apply.disabled = true)
	form.add_child(_add_sawing_button)
	_furniture_rows = VBoxContainer.new()
	form.add_child(_furniture_rows)
	_add_furniture_button = Button.new()
	_add_furniture_button.text = "Add furniture recipe"
	_add_furniture_button.pressed.connect(func():
		var facts: Dictionary = DEFAULTS.furniture[0].duplicate(true)
		facts.recipe_id = ""
		facts.item_id = ""
		facts.display_name = ""
		_add_furniture(facts)
		_apply.disabled = true)
	form.add_child(_add_furniture_button)
	_arrows_enabled = CheckBox.new()
	_arrows_enabled.text = "Enable inventory arrow recipes"
	_arrows_enabled.toggled.connect(func(_enabled: bool): _apply.disabled = true)
	form.add_child(_arrows_enabled)
	for spec: Array in ARROW_FIELDS:
		var label := Label.new()
		label.text = str(spec[1])
		form.add_child(label)
		var field := LineEdit.new()
		field.text_changed.connect(func(_value: String): _apply.disabled = true)
		form.add_child(field)
		_arrow_fields[spec[0]] = field
	_spinning_enabled = CheckBox.new()
	_spinning_enabled.text = "Enable wool spinning"
	_spinning_enabled.toggled.connect(func(_enabled: bool): _apply.disabled = true)
	form.add_child(_spinning_enabled)
	for key: String in SPINNING_DEFAULTS:
		var label := Label.new()
		label.text = "Spinning " + key.replace("_", " ")
		form.add_child(label)
		var field := LineEdit.new()
		field.text_changed.connect(func(_value: String): _apply.disabled = true)
		form.add_child(field)
		_spinning_fields[key] = field
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

func _add_sawing(facts: Dictionary) -> void:
	var row := VBoxContainer.new()
	_sawing_rows.add_child(row)
	for key: String in SAWING_FIELDS:
		var label := Label.new()
		label.text = "Sawing " + key.replace("_", " ")
		row.add_child(label)
		var field := LineEdit.new()
		field.name = key
		field.text = str(facts.get(key, ""))
		field.text_changed.connect(func(_value: String): _apply.disabled = true)
		row.add_child(field)
	var remove := Button.new()
	remove.text = "Remove sawing recipe"
	remove.pressed.connect(func():
		_sawing_rows.remove_child(row)
		row.queue_free()
		_apply.disabled = true)
	row.add_child(remove)

func _add_furniture(facts: Dictionary) -> void:
	var row := VBoxContainer.new()
	_furniture_rows.add_child(row)
	for key: String in FURNITURE_FIELDS:
		var label := Label.new()
		label.text = key.replace("_", " ")
		row.add_child(label)
		var field := LineEdit.new()
		field.name = key
		field.text = str(facts.get(key, ""))
		if key == "inputs":
			var entries: PackedStringArray = []
			for entry: Dictionary in facts.get(key, []): entries.append(str(entry.item_id) + ":" + str(entry.quantity))
			field.text = ", ".join(entries)
			label.text = "Materials (item_id:quantity, item_id:quantity)"
		elif key == "required_tool_ids":
			field.text = ", ".join(facts.get(key, []))
			label.text = "Retained tools (comma separated; empty for none)"
		field.text_changed.connect(func(_value: String): _apply.disabled = true)
		row.add_child(field)
	var remove := Button.new()
	remove.text = "Remove recipe"
	remove.pressed.connect(func():
		_furniture_rows.remove_child(row)
		row.queue_free()
		_apply.disabled = true)
	row.add_child(remove)

func _loaded(data: Dictionary) -> void:
	_set_busy(false)
	_current = data
	for row in _sawing_rows.get_children():
		_sawing_rows.remove_child(row)
		row.queue_free()
	for facts: Dictionary in data.get("draft", {}).get("sawing", []): _add_sawing(facts)
	for row in _furniture_rows.get_children():
		_furniture_rows.remove_child(row)
		row.queue_free()
	for facts: Dictionary in data.get("draft", {}).get("furniture", []): _add_furniture(facts)
	var spinning: Variant = data.get("draft", {}).get("spinning")
	_spinning_enabled.button_pressed = spinning is Dictionary
	if not spinning is Dictionary: spinning = SPINNING_DEFAULTS
	for key: String in _spinning_fields: _spinning_fields[key].text = str(spinning.get(key, ""))
	var arrows: Variant = data.get("draft", {}).get("arrows")
	_arrows_enabled.button_pressed = arrows is Dictionary
	if not arrows is Dictionary: arrows = ARROW_DEFAULTS
	for key: String in _arrow_fields:
		var parts := key.split(".")
		_arrow_fields[key].text = str(arrows.get(key, "") if parts.size() == 1 else arrows.get(parts[0], {}).get(parts[1], ""))
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
	draft.sawing = []
	for row in _sawing_rows.get_children():
		var facts := {}
		for key: String in SAWING_FIELDS:
			var value: String = row.get_node(key).text.strip_edges()
			if key in ["level", "xp_tenths", "duration_ms"]:
				if not value.is_valid_int():
					_status.text = "Enter a whole number for sawing " + key
					return
				facts[key] = int(value)
			else: facts[key] = value
		draft.sawing.append(facts)
	draft.furniture = []
	for row in _furniture_rows.get_children():
		var facts := {}
		for key: String in FURNITURE_FIELDS:
			var value: String = row.get_node(key).text.strip_edges()
			if key in ["inputs", "required_tool_ids"]:
				facts[key] = []
				for entry in value.split(",", false):
					if key == "required_tool_ids": facts[key].append(entry.strip_edges())
					else:
						var parts := entry.strip_edges().split(":")
						if parts.size() != 2 or not parts[1].is_valid_int():
							_status.text = "Use item_id:quantity for materials"
							return
						facts[key].append({"item_id":parts[0], "quantity":int(parts[1])})
			elif key in ["occupies_furniture_space", "blocks_movement"]:
				if value.to_lower() not in ["true", "false"]:
					_status.text = "Enter true or false for " + key
					return
				facts[key] = value.to_lower() == "true"
			elif key in ["level", "xp_tenths", "duration_ms", "planks", "nails", "placement_level", "footprint_width_tiles", "footprint_height_tiles"]:
				if not value.is_valid_int():
					_status.text = "Enter a whole number for " + key
					return
				facts[key] = int(value)
			else: facts[key] = value
		draft.furniture.append(facts)
	draft["arrows"] = null
	if _arrows_enabled.button_pressed:
		var arrows := ARROW_DEFAULTS.duplicate(true)
		for key: String in _arrow_fields:
			var value: String = _arrow_fields[key].text.strip_edges()
			if not key.ends_with("_item_id") and not value.is_valid_int():
				_status.text = "Enter a whole number for " + key
				return
			var parts := key.split(".")
			if parts.size() == 1: arrows[key] = value
			else: arrows[parts[0]][parts[1]] = int(value)
		draft["arrows"] = arrows
	draft["spinning"] = null
	if _spinning_enabled.button_pressed:
		var spinning := {}
		for key: String in _spinning_fields:
			var value: String = _spinning_fields[key].text.strip_edges()
			if key.ends_with("_id"): spinning[key] = value
			elif value.is_valid_int(): spinning[key] = int(value)
			else:
				_status.text = "Enter a whole number for " + key
				return
		draft["spinning"] = spinning
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
	_add_sawing_button.disabled = value
	for row in _sawing_rows.get_children():
		for field in row.get_children():
			if field is LineEdit: field.editable = not value
			elif field is Button: field.disabled = value
	_add_furniture_button.disabled = value
	for row in _furniture_rows.get_children():
		for field in row.get_children():
			if field is LineEdit: field.editable = not value
			elif field is Button: field.disabled = value
	for field: LineEdit in _fields.values():
		field.editable = not value
	for field: LineEdit in _arrow_fields.values(): field.editable = not value
	_spinning_enabled.disabled = value
	for field: LineEdit in _spinning_fields.values(): field.editable = not value
	_arrows_enabled.disabled = value
	_operation.disabled = value
