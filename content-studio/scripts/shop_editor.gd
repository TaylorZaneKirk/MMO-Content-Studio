# Authors one reusable Shop through the existing host client. This workspace
# owns form/preview state; the host validates and commits. NPCs reference this content.
extends HBoxContainer
class_name ShopEditor

const STUDIO_THEME := preload("res://scripts/studio_theme.gd")

const WORKSPACE_SUPPORT := preload("res://scripts/authoring_workspace_support.gd")
@onready var _client: AuthoringHostClient = %AuthoringHostClient
var _support := WORKSPACE_SUPPORT.new()
var _options: Dictionary = {}
var _current: Dictionary = {}
var _fields: Dictionary = {}
var _list: ItemList
var _search: LineEdit
var _definition_id: LineEdit
var _state: Label
var _stock: VBoxContainer
var _changes: VBoxContainer
var _validation: VBoxContainer
var _operation: OptionButton
var _preview: Button
var _apply: Button
var _status: Label
var _form: VBoxContainer
var _loading := false
var _preview_request: Dictionary = {}
var _pending_definition_id := ""


func _ready() -> void:
	_build_ui()
	_client.shop_options_received.connect(_on_options)
	_client.shop_catalog_received.connect(_on_catalog)
	_client.shop_definition_received.connect(_on_definition)
	_client.shop_preview_received.connect(_on_preview)
	_client.shop_mutation_completed.connect(_on_mutation)
	_client.request_failed.connect(_on_failed)
	_form.visible = false


func _build_ui() -> void:
	theme = STUDIO_THEME.item_theme()
	add_theme_constant_override("separation", 14)
	var catalog := _panel(240)
	_heading(catalog, "Shop library", 20)
	_search = LineEdit.new()
	_search.placeholder_text = "Search shops…"
	catalog.add_child(_search)
	_search.text_submitted.connect(func(value: String): _client.load_shops(value))
	_button(catalog, "+ New shop", _new_definition)
	_button(catalog, "Search / reload library", func(): _client.load_shops(_search.text))
	_list = ItemList.new()
	_list.size_flags_vertical = Control.SIZE_EXPAND_FILL
	_list.add_theme_color_override("font_color", Color("e5edf5"))
	_list.add_theme_stylebox_override("panel", STUDIO_THEME.box("101923", "354355"))
	_list.add_theme_stylebox_override("selected", STUDIO_THEME.box("29443f", "66d9c1"))
	_list.add_theme_stylebox_override("selected_focus", STUDIO_THEME.box("29443f", "66d9c1"))
	_list.add_theme_constant_override("v_separation", 12)
	catalog.add_child(_list)
	_list.item_selected.connect(func(index: int):
		_invalidate()
		_client.load_shop(str(_list.get_item_metadata(index))))

	var editor := _panel()
	editor.get_parent().size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_heading(editor, "Shop details", 22)
	_note(editor, "Author reusable Shop content. NPCs reference this Shop; runtime Trade comes later.")
	_form = VBoxContainer.new()
	_form.size_flags_vertical = Control.SIZE_EXPAND_FILL
	editor.add_child(_form)
	var pages := TabContainer.new()
	pages.size_flags_vertical = Control.SIZE_EXPAND_FILL
	pages.use_hidden_tabs_for_min_size = false
	_form.add_child(pages)
	var basics := _page(pages, "Basics", "Shop identity", "Item definitions own base prices. This Shop owns the stock-price change rate.")
	var stock := _page(pages, "Stock", "Ordered regular stock", "Default stock is equilibrium. Restock ticks are per item (600 ms each); future runtime moves one unit toward equilibrium.")
	_definition_id = _text_field("Shop ID", basics)
	_definition_id.placeholder_text = "lowercase_shop_id"
	_state = _label(basics, "Draft")
	_state.modulate = Color("91ecd7")
	_fields["display_name"] = _text_field("Display name", basics)
	var buys := CheckBox.new()
	buys.text = "Buys unstocked items (eligible Item policy still required)"
	basics.add_child(buys)
	buys.toggled.connect(_invalidate)
	_fields["buys_unstocked_items"] = buys
	_fields["price_change_per_stock_percent"] = _number_field(basics, "Price change per stock (%)", 0, 100, 0)
	_label(basics, "Notes (authoring only)")
	var notes := TextEdit.new()
	notes.custom_minimum_size.y = 100
	basics.add_child(notes)
	notes.text_changed.connect(_invalidate)
	_fields["notes"] = notes
	_stock = VBoxContainer.new()
	_stock.add_theme_constant_override("separation", 12)
	stock.add_child(_stock)
	_button(stock, "+ Add stock item", func(): _add_stock({}); _invalidate())

	var review := _panel(264)
	_heading(review, "Review & apply", 20)
	_label(review, "Operation")
	_operation = OptionButton.new()
	for operation: String in ["save_draft", "publish", "disable", "delete"]:
		_operation.add_item(_support.operation_name(operation))
		_operation.set_item_metadata(_operation.item_count - 1, operation)
	_operation.item_selected.connect(_invalidate)
	review.add_child(_operation)
	_preview = _button(review, "1. Preview changes", func(): _request_preview(str(_operation.get_selected_metadata())))
	_preview.theme_type_variation = "PrimaryButton"
	_preview.disabled = true
	_apply = _button(review, "2. Apply changes", _apply_preview)
	_apply.disabled = true
	_status = _label(review, "Select a Shop or create a draft to begin.")
	_status.modulate = Color("a9b8c9")
	var scroll := ScrollContainer.new()
	scroll.size_flags_vertical = Control.SIZE_EXPAND_FILL
	scroll.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	review.add_child(scroll)
	var feedback := VBoxContainer.new()
	feedback.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	feedback.add_theme_constant_override("separation", 12)
	scroll.add_child(feedback)
	_heading(feedback, "Validation", 16)
	_validation = VBoxContainer.new()
	feedback.add_child(_validation)
	_heading(feedback, "Changes", 16)
	_changes = VBoxContainer.new()
	feedback.add_child(_changes)


func _panel(width: float = 0) -> VBoxContainer:
	var panel := PanelContainer.new()
	panel.custom_minimum_size.x = width
	var style := STUDIO_THEME.box("17212e", "354355")
	style.content_margin_left = 16
	style.content_margin_right = 16
	style.content_margin_top = 14
	style.content_margin_bottom = 14
	panel.add_theme_stylebox_override("panel", style)
	add_child(panel)
	var content := VBoxContainer.new()
	content.add_theme_constant_override("separation", 12)
	panel.add_child(content)
	return content


func _heading(parent: Node, value: String, size: int) -> void:
	_label(parent, value).add_theme_font_size_override("font_size", size)


func _note(parent: Node, value: String) -> void:
	_label(parent, value).modulate = Color("a9b8c9")


func _page(pages: TabContainer, title: String, heading: String, description: String) -> VBoxContainer:
	var scroll := ScrollContainer.new()
	scroll.name = title
	scroll.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	pages.add_child(scroll)
	var page := VBoxContainer.new()
	page.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	page.add_theme_constant_override("separation", 12)
	scroll.add_child(page)
	_heading(page, heading, 20)
	_note(page, description)
	return page


func _label(parent: Node, value: String) -> Label:
	var label := Label.new()
	label.text = value
	label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	parent.add_child(label)
	return label


func _button(parent: Node, caption: String, action: Callable) -> Button:
	var button := Button.new()
	button.text = caption
	button.pressed.connect(action)
	parent.add_child(button)
	return button


func _text_field(caption: String, parent: Node) -> LineEdit:
	_label(parent, caption)
	var field := LineEdit.new()
	field.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	parent.add_child(field)
	field.text_changed.connect(_invalidate)
	return field


func _number_field(parent: Node, caption: String, minimum: int, maximum: int, value: int) -> SpinBox:
	var field := VBoxContainer.new()
	field.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	field.add_theme_constant_override("separation", 6)
	parent.add_child(field)
	_label(field, caption).autowrap_mode = TextServer.AUTOWRAP_OFF
	var number := SpinBox.new()
	number.custom_minimum_size = Vector2(160, 40)
	number.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	number.min_value = minimum
	number.max_value = maximum
	number.step = 1
	number.value = value
	field.add_child(number)
	number.value_changed.connect(_invalidate)
	return number


# Each row owns its controls; its position is the authored stock order.
func _add_stock(value: Dictionary) -> void:
	var row := VBoxContainer.new()
	row.add_theme_constant_override("separation", 10)
	_stock.add_child(row)
	var item := OptionButton.new()
	item.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	item.fit_to_longest_item = false
	row.add_child(item)
	item.add_item("Select an Item")
	item.set_item_metadata(0, "")
	for option: Dictionary in _options.get("items", []):
		item.add_item("%s — %s" % [option["display_name"], option["item_id"]])
		var index := item.item_count - 1
		item.set_item_metadata(index, option["item_id"])
		if option["item_id"] == value.get("item_id", ""): item.select(index)
	var context := _label(row, "")
	context.modulate = Color("a9b8c9")
	var refresh_context := func():
		context.text = "Choose an item to see its publication and economic policy."
		for option: Dictionary in _options.get("items", []):
			if option["item_id"] == item.get_selected_metadata():
				context.text = "%s | %s | NPC buys: %s | NPC sells: %s" % ["Published" if option["runtime_enabled"] else "Not published", option["shop_policy"], str(option["npc_buy_price"]), str(option["npc_sell_price"])]
	item.item_selected.connect(func(_index: int): refresh_context.call(); _invalidate())
	refresh_context.call()
	var quantities := HBoxContainer.new()
	quantities.add_theme_constant_override("separation", 16)
	row.add_child(quantities)
	var default_stock := _number_field(quantities, "Default stock", 0, 2147483647, int(value.get("default_stock", 0)))
	var restock_ticks := _number_field(quantities, "Restock ticks", 1, 2147483647, int(value.get("restock_ticks", 1)))
	row.set_meta("controls", [item, default_stock, restock_ticks])
	var buttons := HBoxContainer.new()
	buttons.alignment = BoxContainer.ALIGNMENT_END
	buttons.add_theme_constant_override("separation", 6)
	row.add_child(buttons)
	_button(buttons, "↑", func(): _stock.move_child(row, maxi(0, row.get_index() - 1)); _invalidate())
	_button(buttons, "↓", func(): _stock.move_child(row, mini(_stock.get_child_count() - 1, row.get_index() + 1)); _invalidate())
	_button(buttons, "Remove", func(): _stock.remove_child(row); row.queue_free(); _invalidate())
	row.add_child(HSeparator.new())


func open_resource(shop_id: String) -> void:
	_invalidate()
	_client.load_shop(shop_id)


func _on_options(payload: Dictionary) -> void:
	_options = payload


func _on_catalog(payload: Dictionary) -> void:
	_list.clear()
	for item: Dictionary in payload.get("items", []):
		var index := _list.add_item(str(item["display_name"]))
		_list.set_item_metadata(index, item["shop_definition_id"])
		_list.set_item_tooltip(index, "%s\n%s" % [item["shop_definition_id"], item["publication_state"]])
		if item["shop_definition_id"] == _current.get("shop_definition_id", ""): _list.select(index)


func _new_definition() -> void:
	if _options.is_empty(): return
	_on_definition({"shop_definition_id": "", "publication_state": "Draft", "draft": {"display_name": "", "buys_unstocked_items": false, "price_change_per_stock_percent": 0, "notes": null, "stock": []}})


func _on_definition(payload: Dictionary) -> void:
	_loading = true
	_current = payload
	_list.deselect_all()
	for index in _list.item_count:
		if _list.get_item_metadata(index) == payload.get("shop_definition_id", ""): _list.select(index)
	_definition_id.text = str(payload.get("shop_definition_id", ""))
	_definition_id.editable = payload.get("updated_at_utc") == null
	_state.text = str(payload.get("publication_state", "Draft"))
	var draft: Dictionary = payload.get("draft", {})
	for key: String in _fields:
		var control: Control = _fields[key]
		if control is LineEdit or control is TextEdit: control.text = str(draft.get(key, "")) if draft.get(key) != null else ""
		elif control is CheckBox: control.button_pressed = bool(draft.get(key, false))
		elif control is SpinBox: control.value = float(draft.get(key, 0.0)) if draft.get(key) != null else 0.0
	_support.clear_container(_stock)
	for row: Dictionary in draft.get("stock", []): _add_stock(row)
	_loading = false
	_form.visible = true
	_preview.disabled = false
	_status.text = "Editing %s." % payload.get("shop_definition_id", "") if not str(payload.get("shop_definition_id", "")).is_empty() else "New draft. Choose a stable definition ID."
	_invalidate()


func _draft() -> Dictionary:
	var stock: Array = []
	for row: Node in _stock.get_children():
		var controls: Array = row.get_meta("controls")
		stock.append({"item_id": str(controls[0].get_selected_metadata()), "default_stock": int(controls[1].value), "restock_ticks": int(controls[2].value)})
	var notes: String = _fields["notes"].text.strip_edges()
	return {"display_name": _fields["display_name"].text.strip_edges(), "buys_unstocked_items": _fields["buys_unstocked_items"].button_pressed,
		"price_change_per_stock_percent": int(_fields["price_change_per_stock_percent"].value), "notes": null if notes.is_empty() else notes, "stock": stock}


func _request_preview(operation: String) -> void:
	_invalidate()
	_pending_definition_id = _definition_id.text.strip_edges()
	_preview_request = {"draft": _draft(), "expected_updated_at_utc": _current.get("updated_at_utc"), "target_operation": operation}
	_client.preview_shop(_pending_definition_id, _preview_request)


func _on_preview(payload: Dictionary) -> void:
	# Ignore responses for a form edited or replaced while the request was in flight.
	if _preview_request.is_empty(): return
	_support.render_changes(_changes, payload.get("changes", []))
	_support.render_validation(_validation, payload.get("messages", []))
	var operation := str(payload.get("target_operation", ""))
	_support.accept_preview(operation, str(payload.get("preview_signature", "")), bool(payload.get("applicable", false)), _apply, "2. Apply " + _support.operation_name(operation))


func _apply_preview() -> void:
	if not _support.can_apply(_support.preview_operation, _support.preview_signature): return
	var request := _preview_request.duplicate(true)
	request["preview_signature"] = _support.preview_signature
	var operation: String = _support.preview_operation
	_invalidate()
	_client.mutate_shop(_pending_definition_id, operation, request)


func _on_mutation(payload: Dictionary) -> void:
	if payload.get("definition") is Dictionary:
		_on_definition(payload["definition"])
	else:
		_new_definition()
	_support.render_validation(_validation, payload.get("messages", []))
	_status.text = "Operation committed and reloaded. Publication refreshes the Shop catalog; the game does not consume Shops yet."
	_client.load_shop_options()


func _on_failed(operation: String, message: String, errors: Array) -> void:
	if not operation.begins_with("shop_"): return
	_invalidate()
	_status.text = message
	_support.render_validation(_validation, errors)


func _invalidate(_value: Variant = null) -> void:
	if _loading or _apply == null: return
	_preview_request = {}
	_support.clear_preview(_apply, _changes, _validation, "2. Apply changes")
