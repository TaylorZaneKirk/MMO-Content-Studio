# Owns this focused editor's fields and preview/apply decision. Host owns durable validation.
extends VBoxContainer
@onready var _client: AuthoringHostClient = %AuthoringHostClient
const FIELDS := [["crystal_item_id", "Crystal Item Id"], ["hammer_item_id", "Hammer Item Id"], ["shard_item_id", "Shard Item Id"], ["dust_item_id", "Dust Item Id"], ["bottle_item_id", "Bottle Item Id"], ["station_definition_id", "Station Definition Id"], ["crystal_low_percent", "Crystal Low Percent"], ["crystal_high_percent", "Crystal High Percent"], ["crystal_cap_level", "Crystal Cap Level"], ["crush_level", "Crush Level"], ["crush_duration_ms", "Crush Duration Ms"], ["crush_xp_tenths", "Crush Xp Tenths"], ["shard_min", "Shard Min"], ["shard_max", "Shard Max"], ["dust_min", "Dust Min"], ["dust_max", "Dust Max"], ["decay_interval_ms", "Decay Interval Ms"], ["sip_cooldown_ms", "Sip Cooldown Ms"], ["recipes", "Recipes (JSON rows)"], ["potions", "Potions (JSON rows)"]]
var _fields: Dictionary = {}
var _current: Dictionary = {}
var _request: Dictionary = {}
var _operation: OptionButton
var _apply: Button
var _status: Label
var _busy := false

func _ready() -> void:
	var title := Label.new()
	title.text = "Alchemy — recipes and combat potions"
	add_child(title)
	var reload_button := Button.new()
	reload_button.text = "Load / reload saved settings"
	reload_button.pressed.connect(func():
		if not _busy:
			_apply.disabled = true
			_set_busy(true)
			_client.load_alchemy("v1"))
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
		var field: Variant = TextEdit.new() if spec[0] in ["recipes", "potions"] else LineEdit.new()
		if field is TextEdit:
			field.custom_minimum_size = Vector2(600, 180)
		if field is TextEdit:
			field.text_changed.connect(func(): _apply.disabled = true)
		else:
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
	_client.alchemy_definition_received.connect(_loaded)
	_client.alchemy_preview_received.connect(_previewed)
	_client.alchemy_mutation_completed.connect(_completed)
	_client.request_failed.connect(_failed)

func open_resource(_id: String) -> void:
	if _busy:
		return
	_set_busy(true)
	_apply.disabled = true
	_client.load_alchemy("v1")

func _loaded(data: Dictionary) -> void:
	_set_busy(false)
	_current = data
	for key: String in _fields:
		_fields[key].text = JSON.stringify(data.get("draft", {}).get(key, []), "  ") if key in ["recipes", "potions"] else str(data.get("draft", {}).get(key, ""))
	_apply.disabled = true
	_status.text = "Loaded %s. Draft edits retain the previous published rules." % str(data.get("publication_state", ""))

func _preview() -> void:
	if _busy or _current.is_empty():
		return
	var draft: Dictionary = {}
	for key: String in _fields:
		var value: String = _fields[key].text.strip_edges()
		if key in ["recipes", "potions"]:
			var parsed: Variant = JSON.parse_string(value)
			if not parsed is Array:
				_status.text = "Enter a JSON array for " + key
				return
			draft[key] = parsed
		elif key.ends_with("_id"):
			draft[key] = value
		elif not value.is_valid_float():
			_status.text = "Enter a number for " + key
			return
		else:
			draft[key] = float(value) if key in ["crystal_low_percent", "crystal_high_percent"] else int(value)
	_request = {"draft": draft, "expected_updated_at_utc": _current.get("updated_at_utc"), "target_operation": "save_draft" if _operation.selected == 0 else "publish"}
	_apply.disabled = true
	_set_busy(true)
	_client.preview_alchemy("v1", _request)

func _previewed(data: Dictionary) -> void:
	_set_busy(false)
	_request["preview_signature"] = data.get("preview_signature", "")
	_apply.disabled = not bool(data.get("applicable", false))
	_status.text = JSON.stringify(data.get("changes", []), "  ") + "\n" + JSON.stringify(data.get("messages", []), "  ")

func _mutate() -> void:
	_set_busy(true)
	_apply.disabled = true
	_client.mutate_alchemy("v1", str(_request["target_operation"]), _request)

func _completed(data: Dictionary) -> void:
	_set_busy(false)
	_loaded(data.get("definition", {}))

func _failed(_operation_name: String, _message: String, _errors: Array) -> void:
	if not _operation_name.begins_with("alchemy_"):
		return
	_set_busy(false)
	_apply.disabled = true
	_status.text = "Request failed. Reload saved settings before trying again."


func _set_busy(value: bool) -> void:
	_busy = value
	for field: Variant in _fields.values():
		field.editable = not value
	_operation.disabled = value
