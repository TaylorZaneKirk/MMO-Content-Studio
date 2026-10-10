# Owns this focused editor's fields and preview/apply decision. Host owns durable validation.
extends VBoxContainer
@onready var _client: AuthoringHostClient = %AuthoringHostClient
const FIELDS := [["measured_force_level", "Measured Force: Insight level (fixed 4)"], ["measured_force_strength_percent", "Measured Force: melee Strength percent (fixed 5)"], ["measured_force_minimum_strength", "Measured Force: minimum Strength bonus (fixed 1)"], ["measured_force_drain_ms", "Measured Force: drain milliseconds (fixed 36000)"], ["measured_force_ready_ms", "Measured Force: readiness milliseconds (fixed 600)"], ["guarded_mind_level", "Guarded Mind: Insight level (fixed 1)"], ["guarded_mind_defence_percent", "Guarded Mind: Defence percent (fixed 5)"], ["guarded_mind_minimum_defence", "Guarded Mind: minimum Defence bonus (fixed 1)"], ["guarded_mind_drain_ms", "Guarded Mind: drain milliseconds (fixed 36000)"], ["guarded_mind_ready_ms", "Guarded Mind: readiness milliseconds (fixed 600)"], ["lectern_definition_id", "Lectern definition id"], ["station_xp_percent", "Station xp percent"], ["station_auto_ms", "Station auto ms"], ["station_manual_ms", "Station manual ms"], ["focus_drain_ms", "Focus drain ms"], ["focus_reduction_percent", "Focus reduction percent"], ["fishing_focus_level", "Fishing focus level"], ["cooking_focus_level", "Cooking focus level"], ["mining_focus_level", "Mining focus level"], ["blacksmithing_focus_level", "Blacksmithing focus level"], ["woodcutting_focus_level", "Woodcutting focus level"], ["crafting_focus_level", "Crafting focus level"], ["farming_focus_level", "Farming focus level"], ["alchemy_focus_level", "Alchemy focus level"], ["study_duration_ms", "Inventory study duration (milliseconds)"]]
var _fields: Dictionary = {}
var _current: Dictionary = {}
var _request: Dictionary = {}
var _operation: OptionButton
var _apply: Button
var _status: Label
var _busy := false
var _families: VBoxContainer
var _specimens: VBoxContainer
var _progression: Label

func _ready() -> void:
	(get_parent() as TabContainer).set_tab_title(get_index(), "Insight")
	var title := Label.new()
	title.text = "Insight — Subject family mastery"
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
		field.editable = not (str(spec[0]).begins_with("guarded_mind_") or str(spec[0]).begins_with("measured_force_"))
		field.text_changed.connect(func(_value: String): _apply.disabled = true)
		form.add_child(field)
		_fields[spec[0]] = field
	_families = VBoxContainer.new()
	form.add_child(_families)
	_specimens = VBoxContainer.new()
	form.add_child(_specimens)
	var add_specimen := Button.new()
	add_specimen.text = "Add study specimen"
	add_specimen.pressed.connect(func():
		if not _busy:
			_add_specimen({})
			_apply.disabled = true)
	form.add_child(add_specimen)
	_progression = Label.new()
	_progression.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	form.add_child(_progression)
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
	for row in _families.get_children():
		_families.remove_child(row)
		row.queue_free()
	for family: Dictionary in data.get("draft", {}).get("families", []):
		var row := VBoxContainer.new()
		row.set_meta("family", family.duplicate(true))
		_families.add_child(row)
		if str(family["family_id"]) == "flowers":
			_add_family_field(row, "required_level", "Flowers: base Insight for all five harvest milestones", str(family["required_level"]))
			var points: Array = family.get("milestone_points", [])
			for i in range(points.size()):
				_add_family_field(row, "milestone_%d" % i, "Milestone %d: total points (+%d%% extra flower chance)" % [i + 1, i + 1], str(points[i]))
		else:
			_add_family_field(row, "members", "%s — Insight %d, melee defence. Published Mob IDs, comma-separated" % [str(family["display_name"]), int(family["required_level"])], ", ".join(family.get("mob_definition_ids", [])))
	for row in _specimens.get_children():
		_specimens.remove_child(row)
		row.queue_free()
	for specimen: Dictionary in data.get("draft", {}).get("specimens", []):
		_add_specimen(specimen)
	var progression_lines: Array[String] = ["Approved milestones (read only):"]
	for milestone: Dictionary in data.get("mastery_preview", []):
		progression_lines.append("%s %d: %d points, Insight %d — %s +%s%%" % [str(milestone["family_id"]), int(milestone["milestone"]), int(milestone["points"]), int(milestone["required_level"]), str(milestone["category"]), str(float(milestone["total_basis_points"]) / 100.0)])
	_progression.text = "\n".join(progression_lines)
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
		elif not value.is_valid_int():
			_status.text = "Enter a whole number for " + key
			return
		else:
			draft[key] = int(value)
	var families: Array[Dictionary] = []
	for row in _families.get_children():
		var family: Dictionary = row.get_meta("family").duplicate(true)
		var points: Array[int] = []
		for field in row.get_children():
			if not field is LineEdit:
				continue
			var key := str(field.get_meta("field"))
			var value: String = field.text.strip_edges()
			if key == "members":
				var members: Array[String] = []
				for member: String in value.split(",", false):
					if not member.strip_edges().is_empty():
						members.append(member.strip_edges())
				family["mob_definition_ids"] = members
			elif not value.is_valid_int():
				_status.text = "Enter a whole number for " + key
				return
			elif key == "required_level":
				family[key] = int(value)
			else:
				points.append(int(value))
		if str(family["family_id"]) == "flowers":
			family["milestone_points"] = points
		families.append(family)
	draft["families"] = families
	var specimens: Array[Dictionary] = []
	for row in _specimens.get_children():
		var specimen: Dictionary = {}
		for field in row.get_children():
			if field is LineEdit:
				var key := str(field.get_meta("field"))
				var value: String = field.text.strip_edges()
				if not key.ends_with("_id") and not value.is_valid_int():
					_status.text = "Enter a whole number for " + key
					return
				specimen[key] = value if key.ends_with("_id") else int(value)
		specimens.append(specimen)
	draft["specimens"] = specimens
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
	for key in _fields:
		_fields[key].editable = not value and not (str(key).begins_with("guarded_mind_") or str(key).begins_with("measured_force_"))
	_operation.disabled = value
	for row in _families.get_children():
		for field in row.get_children():
			if field is LineEdit:
				field.editable = not value
	for row in _specimens.get_children():
		for field in row.get_children():
			if field is LineEdit:
				field.editable = not value

# A row owns only editable specimen values; this editor owns the complete draft.
func _add_specimen(specimen: Dictionary) -> void:
	var row := VBoxContainer.new()
	_specimens.add_child(row)
	for spec: Array in [["item_id", "Specimen item ID"], ["family_id", "Family ID (slime, beasts or flowers)"], ["required_level", "Minimum Insight"], ["base_xp", "Base XP"], ["mastery_points", "Mastery points"]]:
		var label := Label.new()
		label.text = str(spec[1])
		row.add_child(label)
		var field := LineEdit.new()
		field.set_meta("field", spec[0])
		field.text = str(specimen.get(spec[0], ""))
		field.text_changed.connect(func(_value: String): _apply.disabled = true)
		row.add_child(field)
	var remove := Button.new()
	remove.text = "Remove specimen"
	remove.pressed.connect(func():
		if not _busy:
			_specimens.remove_child(row)
			row.queue_free()
			_apply.disabled = true)
	row.add_child(remove)

# Only creates controls; this editor remains the owner of preview/apply.
func _add_family_field(row: VBoxContainer, key: String, caption: String, value: String) -> void:
	var label := Label.new()
	label.text = caption
	label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	row.add_child(label)
	var field := LineEdit.new()
	field.set_meta("field", key)
	field.text = value
	field.text_changed.connect(func(_value: String): _apply.disabled = true)
	row.add_child(field)
