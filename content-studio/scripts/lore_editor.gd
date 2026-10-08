# Owns this focused editor's fields and preview/apply decision. Host owns durable validation.
extends VBoxContainer
@onready var _client: AuthoringHostClient = %AuthoringHostClient
const FIELDS := [["lectern_definition_id", "Lectern definition id"], ["station_xp_percent", "Station xp percent"], ["station_auto_ms", "Station auto ms"], ["station_manual_ms", "Station manual ms"], ["focus_drain_ms", "Focus drain ms"], ["focus_reduction_percent", "Focus reduction percent"], ["fishing_focus_level", "Fishing focus level"], ["cooking_focus_level", "Cooking focus level"], ["mining_focus_level", "Mining focus level"], ["blacksmithing_focus_level", "Blacksmithing focus level"], ["woodcutting_focus_level", "Woodcutting focus level"], ["crafting_focus_level", "Crafting focus level"], ["farming_focus_level", "Farming focus level"], ["alchemy_focus_level", "Alchemy focus level"], ["goo_item_id", "Goo item ID"], ["mob_definition_id", "Mob definition ID"], ["study_level", "Study level"], ["study_duration_ms", "Study duration (milliseconds)"], ["study_xp", "Insight XP per study"], ["mastery_studies", "Studies required"], ["mastery_level", "Permanent Insight level required"], ["accuracy_basis_points", "Accuracy bonus (basis points; 50 = 0.5%)"]]
var _fields: Dictionary = {}
var _current: Dictionary = {}
var _request: Dictionary = {}
var _operation: OptionButton
var _apply: Button
var _status: Label
var _busy := false

func _ready() -> void:
	(get_parent() as TabContainer).set_tab_title(get_index(), "Insight")
	var title := Label.new()
	title.text = "Insight — Goo study and Slime mastery"
	add_child(title)
	var reload_button := Button.new()
	reload_button.text = "Load / reload saved settings"
	reload_button.pressed.connect(func():
		if not _busy:
			_apply.disabled = true
			_set_busy(true)
			_client.load_lore("v1"))
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
	_client.lore_definition_received.connect(_loaded)
	_client.lore_preview_received.connect(_previewed)
	_client.lore_mutation_completed.connect(_completed)
	_client.request_failed.connect(_failed)

func open_resource(_id: String) -> void:
	if _busy:
		return
	_set_busy(true)
	_apply.disabled = true
	_client.load_lore("v1")

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
		if key.ends_with("_id"):
			draft[key] = value
		elif not value.is_valid_float():
			_status.text = "Enter a number for " + key
			return
		else:
			draft[key] = int(value)
	_request = {"draft": draft, "expected_updated_at_utc": _current.get("updated_at_utc"), "target_operation": "save_draft" if _operation.selected == 0 else "publish"}
	_apply.disabled = true
	_set_busy(true)
	_client.preview_lore("v1", _request)

func _previewed(data: Dictionary) -> void:
	_set_busy(false)
	_request["preview_signature"] = data.get("preview_signature", "")
	_apply.disabled = not bool(data.get("applicable", false))
	_status.text = JSON.stringify(data.get("changes", []), "  ") + "\n" + JSON.stringify(data.get("messages", []), "  ")

func _mutate() -> void:
	_set_busy(true)
	_apply.disabled = true
	_client.mutate_lore("v1", str(_request["target_operation"]), _request)

func _completed(data: Dictionary) -> void:
	_set_busy(false)
	_loaded(data.get("definition", {}))
	for message: Dictionary in data.get("messages", []):
		_status.text += "\n" + str(message.get("message", ""))

func _failed(_operation_name: String, _message: String, _errors: Array) -> void:
	if not _operation_name.begins_with("lore_"):
		return
	_set_busy(false)
	_apply.disabled = true
	_status.text = "Request failed. Reload saved settings before trying again."


func _set_busy(value: bool) -> void:
	_busy = value
	for field: LineEdit in _fields.values():
		field.editable = not value
	_operation.disabled = value
