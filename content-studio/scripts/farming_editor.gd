# Owns this focused editor's fields and preview/apply decision. Host owns durable validation.
extends VBoxContainer
@onready var _client: AuthoringHostClient = %AuthoringHostClient
const FIELDS := [["hoe_item_id", "Hoe / rake item"], ["bucket_item_id", "Watering bucket item"], ["seed_item_id", "Flower seed item"], ["flower_item_id", "Flower output item"], ["till_seconds", "Tilling attempt seconds"], ["growth_seconds", "Seconds per growth stage"], ["prepared_seconds", "Unused prepared soil seconds"], ["till_low_percent", "Till chance at level 1 (%)"], ["till_high_percent", "Till chance at cap (%)"], ["growth_low_percent", "Growth chance at level 1 (%)"], ["growth_high_percent", "Growth chance at cap (%)"], ["chance_cap_level", "Chance cap level"], ["water_bonus_percent", "Water bonus (percentage points)"], ["till_xp", "Successful till XP"], ["plant_xp", "Planting XP"], ["harvest_xp", "Flower pickup XP"]]
var _fields: Dictionary = {}
var _current: Dictionary = {}
var _request: Dictionary = {}
var _operation: OptionButton
var _apply: Button
var _status: Label
var _busy := false

func _ready() -> void:
	var title := Label.new()
	title.text = "Flower farming — private patches"
	add_child(title)
	var reload_button := Button.new()
	reload_button.text = "Load / reload saved settings"
	reload_button.pressed.connect(func():
		if not _busy:
			_apply.disabled = true
			_set_busy(true)
			_client.load_farming("flowers"))
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
	_status = Label.new()
	_status.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	add_child(_status)
	_client.farming_definition_received.connect(_loaded)
	_client.farming_preview_received.connect(_previewed)
	_client.farming_mutation_completed.connect(_completed)
	_client.request_failed.connect(_failed)

func open_resource(_id: String) -> void:
	if _busy:
		return
	_set_busy(true)
	_apply.disabled = true
	_client.load_farming("flowers")

func _loaded(data: Dictionary) -> void:
	_set_busy(false)
	_current = data
	for key: String in _fields:
		_fields[key].text = str(data.get("draft", {}).get(key, ""))
	_apply.disabled = true
	_status.text = "Loaded %s. Draft edits retain the previous published rules. Growing crops retain their planted rules." % str(data.get("publication_state", ""))

func _preview() -> void:
	if _busy or _current.is_empty():
		return
	var draft: Dictionary = {}
	for key: String in _fields:
		var value: String = _fields[key].text.strip_edges()
		if key.ends_with("_id"):
			draft[key] = value
		elif not value.is_valid_float():
			_status.text = "Enter a number for " + key
			return
		else:
			draft[key] = float(value) if key.ends_with("_percent") else int(value)
	_request = {"draft": draft, "expected_updated_at_utc": _current.get("updated_at_utc"), "target_operation": "save_draft" if _operation.selected == 0 else "publish"}
	_apply.disabled = true
	_set_busy(true)
	_client.preview_farming("flowers", _request)

func _previewed(data: Dictionary) -> void:
	_set_busy(false)
	_request["preview_signature"] = data.get("preview_signature", "")
	_apply.disabled = not bool(data.get("applicable", false))
	_status.text = JSON.stringify(data.get("changes", []), "  ") + "\n" + JSON.stringify(data.get("messages", []), "  ")

func _mutate() -> void:
	_set_busy(true)
	_apply.disabled = true
	_client.mutate_farming("flowers", str(_request["target_operation"]), _request)

func _completed(data: Dictionary) -> void:
	_set_busy(false)
	_loaded(data.get("definition", {}))

func _failed(_operation_name: String, _message: String, _errors: Array) -> void:
	if not _operation_name.begins_with("farming_"):
		return
	_set_busy(false)
	_apply.disabled = true
	_status.text = "Request failed. Reload saved settings before trying again."


func _set_busy(value: bool) -> void:
	_busy = value
	for field: LineEdit in _fields.values():
		field.editable = not value
	_operation.disabled = value
