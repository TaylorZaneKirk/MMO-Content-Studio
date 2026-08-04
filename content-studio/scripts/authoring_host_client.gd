extends Node
class_name AuthoringHostClient

signal connection_state_changed(state: String, message: String)
signal handshake_received(payload: Dictionary)
signal health_received(payload: Dictionary)
signal catalog_received(payload: Dictionary)
signal item_assets_received(payload: Dictionary)
signal item_asset_imported(payload: Dictionary)
signal item_options_received(payload: Dictionary)
signal item_catalog_received(payload: Dictionary)
signal item_definition_received(payload: Dictionary)
signal items_received(payload: Dictionary)
signal item_received(payload: Dictionary)
signal item_preview_received(payload: Dictionary)
signal item_mutation_completed(payload: Dictionary)
signal item_delete_completed(payload: Dictionary)
signal mob_options_received(payload: Dictionary)
signal mob_catalog_received(payload: Dictionary)
signal mob_item_received(payload: Dictionary)
signal mob_preview_received(payload: Dictionary)
signal mob_mutation_completed(payload: Dictionary)
signal npc_options_received(payload: Dictionary)
signal npc_catalog_received(payload: Dictionary)
signal npc_definition_received(payload: Dictionary)
signal npc_preview_received(payload: Dictionary)
signal npc_mutation_completed(payload: Dictionary)
signal npc_delete_completed(payload: Dictionary)
signal dialogue_options_received(payload: Dictionary)
signal dialogue_catalog_received(payload: Dictionary)
signal dialogue_definition_received(payload: Dictionary)
signal dialogue_preview_received(payload: Dictionary)
signal dialogue_playthrough_received(payload: Dictionary)
signal dialogue_mutation_completed(payload: Dictionary)
signal dialogue_delete_completed(payload: Dictionary)
signal request_failed(operation: String, message: String, errors: Array)

const TRANSPORT_SCRIPT := preload("res://scripts/http_json_client.gd")
const DEFAULT_BASE_URL := "http://127.0.0.1:5187"

const OP_HANDSHAKE := "handshake"
const OP_HEALTH := "health"
const OP_CATALOG := "catalog"
const OP_ITEM_ASSETS := "item_assets"
const OP_ITEM_ASSET_IMPORT := "item_asset_import"
const OP_ITEM_OPTIONS := "item_options"
const OP_ITEMS := "items"
const OP_ITEM := "item"
const OP_ITEM_PREVIEW := "item_preview"
const OP_ITEM_SAVE_DRAFT := "item_save_draft"
const OP_ITEM_PUBLISH := "item_publish"
const OP_ITEM_DISABLE := "item_disable"
const OP_ITEM_DELETE := "item_delete"
const OP_MOB_OPTIONS := "mob_options"
const OP_MOBS := "mobs"
const OP_MOB_ITEM := "mob_item"
const OP_MOB_PREVIEW := "mob_preview"
const OP_MOB_SAVE_DRAFT := "mob_save_draft"
const OP_MOB_PUBLISH := "mob_publish"
const OP_MOB_DISABLE := "mob_disable"
const OP_MOB_DELETE := "mob_delete"
const OP_NPC_OPTIONS := "npc_options"
const OP_NPCS := "npcs"
const OP_NPC_DEFINITION := "npc_definition"
const OP_NPC_PREVIEW := "npc_preview"
const OP_NPC_SAVE_DRAFT := "npc_save_draft"
const OP_NPC_PUBLISH := "npc_publish"
const OP_NPC_DISABLE := "npc_disable"
const OP_NPC_DELETE := "npc_delete"
const OP_DIALOGUE_OPTIONS := "dialogue_options"
const OP_DIALOGUES := "dialogues"
const OP_DIALOGUE_DEFINITION := "dialogue_definition"
const OP_DIALOGUE_PREVIEW := "dialogue_preview"
const OP_DIALOGUE_PLAYTHROUGH := "dialogue_playthrough"
const OP_DIALOGUE_SAVE_DRAFT := "dialogue_save_draft"
const OP_DIALOGUE_PUBLISH := "dialogue_publish"
const OP_DIALOGUE_DISABLE := "dialogue_disable"
const OP_DIALOGUE_DELETE := "dialogue_delete"

const CONNECTION_OPERATIONS := [
	OP_HANDSHAKE,
	OP_HEALTH,
	OP_CATALOG,
	OP_ITEM_ASSETS,
	OP_ITEM_OPTIONS,
	OP_ITEMS,
]

@export var base_url := DEFAULT_BASE_URL

var _transport: AuthoringHttpTransport
var _startup_operations: Array = []


func _ready() -> void:
	_transport = TRANSPORT_SCRIPT.new() as AuthoringHttpTransport
	_transport.base_url = base_url
	_transport.request_succeeded.connect(_on_request_succeeded)
	_transport.request_failed.connect(_on_request_failed)
	add_child(_transport)


func connect_and_load() -> void:
	if _transport.is_busy():
		return

	connection_state_changed.emit("connecting", "Connecting to the local authoring host…")
	_request(OP_HANDSHAKE, "/api/v1/system/handshake")


func retry() -> void:
	_transport.reset()
	connect_and_load()


func import_item_asset(source_file_path: String, target_file_name: String = "") -> void:
	_request(OP_ITEM_ASSET_IMPORT, "/api/v1/assets/items/import", HTTPClient.METHOD_POST, {
		"source_file_path": source_file_path,
		"target_file_name": target_file_name,
	})


func load_items(search: String = "") -> void:
	search_item_catalog(search)


func load_item_options() -> void:
	_request(OP_ITEM_OPTIONS, "/api/v1/items/options")


func search_item_catalog(search: String = "") -> void:
	var suffix := ""
	if not search.strip_edges().is_empty():
		suffix = "?search=%s" % search.strip_edges().uri_encode()
	_request(OP_ITEMS, "/api/v1/items%s" % suffix)


func load_item(item_id: String) -> void:
	load_item_definition(item_id)


func load_item_definition(item_id: String) -> void:
	_request(OP_ITEM, "/api/v1/items/%s" % item_id.uri_encode())


func preview_item(item_id: String, payload: Dictionary) -> void:
	preview_item_operation(item_id, payload)


func preview_item_operation(item_id: String, payload: Dictionary) -> void:
	_request(OP_ITEM_PREVIEW, "/api/v1/items/%s/preview" % item_id.uri_encode(), HTTPClient.METHOD_POST, payload)


func save_item_draft(item_id: String, payload: Dictionary) -> void:
	save_complete_item_draft(item_id, payload)


func save_complete_item_draft(item_id: String, payload: Dictionary) -> void:
	_request(OP_ITEM_SAVE_DRAFT, "/api/v1/items/%s/draft" % item_id.uri_encode(), HTTPClient.METHOD_PUT, payload)


func publish_item(item_id: String, expected_updated_at_utc: Variant, preview_signature: String = "") -> void:
	_request(OP_ITEM_PUBLISH, "/api/v1/items/%s/publish" % item_id.uri_encode(), HTTPClient.METHOD_POST, {
		"expected_updated_at_utc": expected_updated_at_utc,
		"preview_signature": preview_signature,
	})


func disable_item(item_id: String, expected_updated_at_utc: Variant, preview_signature: String = "") -> void:
	_request(OP_ITEM_DISABLE, "/api/v1/items/%s/disable" % item_id.uri_encode(), HTTPClient.METHOD_POST, {
		"expected_updated_at_utc": expected_updated_at_utc,
		"preview_signature": preview_signature,
	})


func delete_item(item_id: String, expected_updated_at_utc: Variant, preview_signature: String = "") -> void:
	_request(OP_ITEM_DELETE, "/api/v1/items/%s/delete" % item_id.uri_encode(), HTTPClient.METHOD_POST, {
		"expected_updated_at_utc": expected_updated_at_utc,
		"preview_signature": preview_signature,
	})


func load_mob_options() -> void:
	_request(OP_MOB_OPTIONS, "/api/v1/mobs/options")


func load_mobs(search: String = "") -> void:
	var suffix := ""
	if not search.strip_edges().is_empty():
		suffix = "?search=%s" % search.strip_edges().uri_encode()
	_request(OP_MOBS, "/api/v1/mobs%s" % suffix)


func load_mob(mob_definition_id: String) -> void:
	_request(OP_MOB_ITEM, "/api/v1/mobs/%s" % mob_definition_id.uri_encode())


func preview_mob(mob_definition_id: String, payload: Dictionary) -> void:
	_request(OP_MOB_PREVIEW, "/api/v1/mobs/%s/preview" % mob_definition_id.uri_encode(), HTTPClient.METHOD_POST, payload)


func save_mob_draft(mob_definition_id: String, payload: Dictionary) -> void:
	_request(OP_MOB_SAVE_DRAFT, "/api/v1/mobs/%s/draft" % mob_definition_id.uri_encode(), HTTPClient.METHOD_PUT, payload)


func publish_mob(mob_definition_id: String, expected_updated_at_utc: Variant, preview_signature: String) -> void:
	_request(OP_MOB_PUBLISH, "/api/v1/mobs/%s/publish" % mob_definition_id.uri_encode(), HTTPClient.METHOD_POST, {
		"expected_updated_at_utc": expected_updated_at_utc,
		"preview_signature": preview_signature,
	})


func disable_mob(mob_definition_id: String, expected_updated_at_utc: Variant, preview_signature: String) -> void:
	_request(OP_MOB_DISABLE, "/api/v1/mobs/%s/disable" % mob_definition_id.uri_encode(), HTTPClient.METHOD_POST, {
		"expected_updated_at_utc": expected_updated_at_utc,
		"preview_signature": preview_signature,
	})


func delete_mob(mob_definition_id: String, expected_updated_at_utc: Variant, preview_signature: String) -> void:
	_request(OP_MOB_DELETE, "/api/v1/mobs/%s/delete" % mob_definition_id.uri_encode(), HTTPClient.METHOD_POST, {
		"expected_updated_at_utc": expected_updated_at_utc,
		"preview_signature": preview_signature,
	})


func load_npc_options() -> void:
	_request(OP_NPC_OPTIONS, "/api/v1/npcs/options")


func load_npcs(search: String = "") -> void:
	var suffix := ""
	if not search.strip_edges().is_empty():
		suffix = "?search=%s" % search.strip_edges().uri_encode()
	_request(OP_NPCS, "/api/v1/npcs%s" % suffix)


func load_npc(npc_definition_id: String) -> void:
	_request(OP_NPC_DEFINITION, "/api/v1/npcs/%s" % npc_definition_id.uri_encode())


func preview_npc(npc_definition_id: String, payload: Dictionary) -> void:
	_request(OP_NPC_PREVIEW, "/api/v1/npcs/%s/preview" % npc_definition_id.uri_encode(), HTTPClient.METHOD_POST, payload)


func save_npc_draft(npc_definition_id: String, payload: Dictionary) -> void:
	_request(OP_NPC_SAVE_DRAFT, "/api/v1/npcs/%s/draft" % npc_definition_id.uri_encode(), HTTPClient.METHOD_PUT, payload)


func publish_npc(npc_definition_id: String, expected_updated_at_utc: Variant, preview_signature: String) -> void:
	_request(OP_NPC_PUBLISH, "/api/v1/npcs/%s/publish" % npc_definition_id.uri_encode(), HTTPClient.METHOD_POST, {
		"expected_updated_at_utc": expected_updated_at_utc,
		"preview_signature": preview_signature,
	})


func disable_npc(npc_definition_id: String, expected_updated_at_utc: Variant, preview_signature: String) -> void:
	_request(OP_NPC_DISABLE, "/api/v1/npcs/%s/disable" % npc_definition_id.uri_encode(), HTTPClient.METHOD_POST, {
		"expected_updated_at_utc": expected_updated_at_utc,
		"preview_signature": preview_signature,
	})


func delete_npc(npc_definition_id: String, expected_updated_at_utc: Variant, preview_signature: String) -> void:
	_request(OP_NPC_DELETE, "/api/v1/npcs/%s/delete" % npc_definition_id.uri_encode(), HTTPClient.METHOD_POST, {
		"expected_updated_at_utc": expected_updated_at_utc,
		"preview_signature": preview_signature,
	})


func load_dialogue_options() -> void:
	_request(OP_DIALOGUE_OPTIONS, "/api/v1/dialogues/options")


func load_dialogues(search: String = "") -> void:
	var suffix := ""
	if not search.strip_edges().is_empty():
		suffix = "?search=%s" % search.strip_edges().uri_encode()
	_request(OP_DIALOGUES, "/api/v1/dialogues%s" % suffix)


func load_dialogue(dialogue_definition_id: String) -> void:
	_request(OP_DIALOGUE_DEFINITION, "/api/v1/dialogues/%s" % dialogue_definition_id.uri_encode())


func preview_dialogue(dialogue_definition_id: String, payload: Dictionary) -> void:
	_request(OP_DIALOGUE_PREVIEW, "/api/v1/dialogues/%s/preview" % dialogue_definition_id.uri_encode(), HTTPClient.METHOD_POST, payload)


func preview_dialogue_playthrough(dialogue_definition_id: String, payload: Dictionary) -> void:
	_request(OP_DIALOGUE_PLAYTHROUGH, "/api/v1/dialogues/%s/playthrough" % dialogue_definition_id.uri_encode(), HTTPClient.METHOD_POST, payload)


func save_dialogue_draft(dialogue_definition_id: String, payload: Dictionary) -> void:
	_request(OP_DIALOGUE_SAVE_DRAFT, "/api/v1/dialogues/%s/draft" % dialogue_definition_id.uri_encode(), HTTPClient.METHOD_PUT, payload)


func publish_dialogue(dialogue_definition_id: String, expected_updated_at_utc: Variant, preview_signature: String) -> void:
	_request(OP_DIALOGUE_PUBLISH, "/api/v1/dialogues/%s/publish" % dialogue_definition_id.uri_encode(), HTTPClient.METHOD_POST, {
		"expected_updated_at_utc": expected_updated_at_utc,
		"preview_signature": preview_signature,
	})


func disable_dialogue(dialogue_definition_id: String, expected_updated_at_utc: Variant, preview_signature: String) -> void:
	_request(OP_DIALOGUE_DISABLE, "/api/v1/dialogues/%s/disable" % dialogue_definition_id.uri_encode(), HTTPClient.METHOD_POST, {
		"expected_updated_at_utc": expected_updated_at_utc,
		"preview_signature": preview_signature,
	})


func delete_dialogue(dialogue_definition_id: String, expected_updated_at_utc: Variant, preview_signature: String) -> void:
	_request(OP_DIALOGUE_DELETE, "/api/v1/dialogues/%s/delete" % dialogue_definition_id.uri_encode(), HTTPClient.METHOD_POST, {
		"expected_updated_at_utc": expected_updated_at_utc,
		"preview_signature": preview_signature,
	})


func _request(
	operation: String,
	path: String,
	method: int = HTTPClient.METHOD_GET,
	payload: Dictionary = {}
) -> void:
	_transport.request(operation, path, method, payload)


func _on_request_succeeded(operation: String, data: Dictionary) -> void:
	match operation:
		OP_HANDSHAKE:
			if str(data.get("api_version", "")) != AuthoringHttpTransport.API_VERSION:
				_on_request_failed(
					operation,
					"The host does not support Content Studio API v%s." % AuthoringHttpTransport.API_VERSION,
					[]
				)
				return
			handshake_received.emit(data)
			_request(OP_HEALTH, "/api/v1/system/health")
		OP_HEALTH:
			health_received.emit(data)
			_request(OP_CATALOG, "/api/v1/catalog")
		OP_CATALOG:
			catalog_received.emit(data)
			_request(OP_ITEM_ASSETS, "/api/v1/assets/items")
		OP_ITEM_ASSETS:
			item_assets_received.emit(data)
			_request(OP_ITEM_OPTIONS, "/api/v1/items/options")
		OP_ITEM_ASSET_IMPORT:
			item_asset_imported.emit(data)
		OP_ITEM_OPTIONS:
			item_options_received.emit(data)
			_request(OP_ITEMS, "/api/v1/items")
		OP_ITEMS:
			item_catalog_received.emit(data)
			items_received.emit(data)
			connection_state_changed.emit("connected", "Connected to the local authoring host.")
			_start_workspace_initialization()
		OP_MOB_OPTIONS:
			mob_options_received.emit(data)
			_request(OP_MOBS, "/api/v1/mobs")
		OP_MOBS:
			mob_catalog_received.emit(data)
			_request_next_startup_operation()
		OP_NPC_OPTIONS:
			npc_options_received.emit(data)
			_request(OP_NPCS, "/api/v1/npcs")
		OP_NPCS:
			npc_catalog_received.emit(data)
			_request_next_startup_operation()
		OP_DIALOGUE_OPTIONS:
			dialogue_options_received.emit(data)
			_request(OP_DIALOGUES, "/api/v1/dialogues")
		OP_DIALOGUES:
			dialogue_catalog_received.emit(data)
			_request_next_startup_operation()
		OP_ITEM:
			item_definition_received.emit(data)
			item_received.emit(data)
		OP_ITEM_PREVIEW:
			item_preview_received.emit(data)
		OP_ITEM_DELETE:
			item_delete_completed.emit(data)
		OP_ITEM_SAVE_DRAFT, OP_ITEM_PUBLISH, OP_ITEM_DISABLE:
			item_mutation_completed.emit(data)
		OP_MOB_ITEM:
			mob_item_received.emit(data)
		OP_MOB_PREVIEW:
			mob_preview_received.emit(data)
		OP_MOB_SAVE_DRAFT, OP_MOB_PUBLISH, OP_MOB_DISABLE, OP_MOB_DELETE:
			mob_mutation_completed.emit(data)
		OP_NPC_DEFINITION:
			npc_definition_received.emit(data)
		OP_NPC_PREVIEW:
			npc_preview_received.emit(data)
		OP_NPC_DELETE:
			npc_delete_completed.emit(data)
		OP_NPC_SAVE_DRAFT, OP_NPC_PUBLISH, OP_NPC_DISABLE:
			npc_mutation_completed.emit(data)
		OP_DIALOGUE_DEFINITION:
			dialogue_definition_received.emit(data)
		OP_DIALOGUE_PREVIEW:
			dialogue_preview_received.emit(data)
		OP_DIALOGUE_PLAYTHROUGH:
			dialogue_playthrough_received.emit(data)
		OP_DIALOGUE_DELETE:
			dialogue_delete_completed.emit(data)
		OP_DIALOGUE_SAVE_DRAFT, OP_DIALOGUE_PUBLISH, OP_DIALOGUE_DISABLE:
			dialogue_mutation_completed.emit(data)
		_:
			_on_request_failed(operation, "Unexpected request completion.", [])


func _on_request_failed(operation: String, message: String, errors: Array) -> void:
	if operation in CONNECTION_OPERATIONS:
		connection_state_changed.emit("disconnected", message)
	request_failed.emit(operation, message, errors)
	if operation in [OP_MOB_OPTIONS, OP_MOBS, OP_NPC_OPTIONS, OP_NPCS, OP_DIALOGUE_OPTIONS, OP_DIALOGUES]:
		_request_next_startup_operation()


func _start_workspace_initialization() -> void:
	_startup_operations = [OP_MOB_OPTIONS, OP_NPC_OPTIONS, OP_DIALOGUE_OPTIONS]
	_request_next_startup_operation()


func _request_next_startup_operation() -> void:
	if _transport.is_busy() or _startup_operations.is_empty():
		return
	var operation := str(_startup_operations.pop_front())
	match operation:
		OP_MOB_OPTIONS:
			_request(OP_MOB_OPTIONS, "/api/v1/mobs/options")
		OP_NPC_OPTIONS:
			_request(OP_NPC_OPTIONS, "/api/v1/npcs/options")
		OP_DIALOGUE_OPTIONS:
			_request(OP_DIALOGUE_OPTIONS, "/api/v1/dialogues/options")
