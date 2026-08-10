# Local Authoring API v1

The Godot Content Studio communicates with the local .NET Authoring Host over
loopback HTTP. The default endpoint is:

```text
http://127.0.0.1:5187
```

The host is intentionally bound to loopback. It is not a remote administration
API and must not be exposed publicly.

## Shared envelope

Every JSON response uses one envelope:

```json
{
  "api_version": "1",
  "request_id": "caller-or-server-generated-id",
  "success": true,
  "data": {},
  "errors": []
}
```

Errors include a stable code, user-facing message, severity, optional field, and
optional remediation. Validation severities are `Info`, `Warning`, and `Error`.

## System routes

### `GET /api/v1/system/handshake`

Confirms that the GUI and host support the same local API version before any
content operation is attempted.

### `GET /api/v1/system/health`

Reports the active database profile, PostgreSQL connectivity, feature-owned
schema contracts, and configured asset-root status. Mob schema requirements are
included in the health surface, but Content Studio still does not apply the MMO
Project migration automatically.

### `GET /api/v1/catalog`

Returns the top-level content catalog. Items is implemented as one unified
authoring section, Mobs is implemented when the configured database has the
mob-authoring schema, NPCs are implemented when the configured database has the
T5 NPC authoring schema, and Dialogue is implemented when the configured
database has the D2 dialogue authoring schema.

## Actor appearance routes

R4D.1A exposes file-backed actor-specific rig calibration independently from
item, NPC, and mob aggregates. The canonical catalog remains owned by MMO
Project; the local host reads and atomically writes it beneath the configured
`game_client_assets` root.

### `GET /api/v1/actor-appearance/calibrations/{calibrationId}`

Returns `exists`, the current SHA-256 `catalog_hash`, and the calibration when
present. A missing calibration is a successful response with `exists = false`.

### `PUT /api/v1/actor-appearance/calibrations/{calibrationId}`

Replaces the complete socket override set after validating an
`expected_catalog_hash`, immutable `rig_id`, canonical socket/direction/frame
IDs, and signed integer source-pixel coordinates. Stale hashes return
`actor_calibration_catalog_conflict`. Foreground-overlay overrides are
preserved rather than owned by this endpoint.

### `POST /api/v1/actor-appearance/calibration-frames`

Returns exact source-art availability for every `N/E/S/W` and `F1-F4` pose of
an NPC Chars family or normalized mob actor family. Missing exact art is
reported unavailable; runtime preview fallbacks are intentionally not used.

## Item asset routes

### `GET /api/v1/assets/items`

Lists canonical PNG files beneath the configured `game_client_assets/items`
directory. Each result includes its Godot `res://assets/...` resource path and
absolute local preview path.

### `POST /api/v1/assets/items/import`

Imports one local PNG into the canonical item directory.

```json
{
  "source_file_path": "/home/user/art/iron_ore.png",
  "target_file_name": "iron_ore.png"
}
```

The host verifies the PNG signature, limits file size, sanitizes the target
name, prevents directory traversal, and refuses to overwrite a different file.
An identical existing file is returned as a successful no-op.

## Unified item routes

U2 made the Items host boundary authoritative for complete item aggregates.
U3 consolidated the Godot item workflow into one contextual Items workspace.
U4 removed the old Basic payload branch and the retired `/api/v1/consumables`,
`/api/v1/equipment`, and `/api/v1/hand-equipment` route families. `/api/v1/items`
is now the only public item-authoring route family.

Tool capabilities are item-level metadata and do not require equipability or a
hand slot.

### `GET /api/v1/items/options`

Returns unified item-authoring options for equipment slots, weapon-capable
slots, skills, combat bonus fields, attack families/styles, tool capability
IDs, consumable action/effect/requirement types, published item references,
equipped-visual binding types, the canonical actor-rig catalog loaded from MMO
Project, and shared limits.

### `GET /api/v1/items?search=ore`

Lists item definitions with derived classification labels such as `Basic`,
`Consumable`, `Equipment`, `Weapon`, `Tool`, and combinations such as
`Consumable + Tool` or `Weapon + Tool`.

### `GET /api/v1/items/{itemId}`

Loads the complete aggregate: identity, icon, publication state, optional
`consumable_behavior`, optional `equipment`, optional equipment
`weapon_profile`, optional equipment `equipped_visual`, independent
`tool_capabilities`, and one `updated_at_utc` concurrency token.

### `POST /api/v1/items/{itemId}/preview`

Validates the complete normalized draft for `save_draft`, `publish`, `disable`,
or `delete`, returns exact logical changes, and returns one
`preview_signature`.

### `PUT /api/v1/items/{itemId}/draft`

Applies the same complete normalized draft that was previewed. The repository
locks the root `item_definitions` row, replaces each submitted specialization
collection, advances the root timestamp, reloads inside the transaction,
commits, then reloads and verifies the aggregate.

### `POST /api/v1/items/{itemId}/publish`

Publishes the complete saved aggregate after strict validation of every visible
and hidden specialization.

### `POST /api/v1/items/{itemId}/disable`

Disables the complete saved aggregate after live-reference checks.

### `POST /api/v1/items/{itemId}/delete`

Deletes a disabled item aggregate after preview/signature and concurrency
verification.

## Mob routes

T4B adds host-side authoring routes for reusable mob definitions. These routes do
not export to MMO Project runtime static content yet and do not author Tiled
`EnemySpawn` placement.

### `GET /api/v1/mobs/options`

Returns static and database-backed mob authoring options:

- publication states: `Draft`, `Published`, `Disabled`
- attack type: `melee`
- accuracy styles: `thrust`, `slash`, `crush`
- faction dispositions: `hostile`, `neutral`
- attack speed: `attack_speed_units`, where one unit is 600 milliseconds
- range: logical tiles
- supported limits and defaults
- database-backed faction options
- published item options for guaranteed drops
- mob visual preview capability metadata using `res://assets/maps/objects/mobs/`

### `GET /api/v1/mobs?search=slime`

Searches mob definition ID and display name. Results are ordered by display name
and stable ID, and include publication state, visual path, max health, faction
summary, combat-profile presence, guaranteed-drop count, editable status, and
`updated_at_utc`.

### `GET /api/v1/mobs/{mobDefinitionId}`

Loads one complete mob aggregate:

- identity and `publication_state`
- visual texture path, source dimensions, anchor offsets, and render scale
- footprint, max health, and movement speed
- reusable movement behavior, wander radius, aggression mode, aggression
  radius, leash radius, and return-home behavior
- optional combat faction and proactive hostile-mob targeting settings
- optional primary combat profile
- optional shared 13-field combat bonuses
- ordered guaranteed drops
- aggregate `updated_at_utc`
- local sprite preview file path when the asset resolves

### `POST /api/v1/mobs/{mobDefinitionId}/preview`

Validates `save_draft`, `publish`, or `disable`, normalizes the complete draft
payload, calculates exact logical changes, and returns a deterministic
`preview_signature`.

`publish` and `disable` previews use the currently saved aggregate as the
effective payload. If the request body differs from the saved aggregate, the
preview reports `unsaved_mob_changes`.

### `PUT /api/v1/mobs/{mobDefinitionId}/draft`

Creates a new draft when the ID does not exist, or updates an existing aggregate.
The request must include the same complete draft that was previewed plus
`preview_signature`. Existing definitions also require `expected_updated_at_utc`.

Saving replaces optional child rows as complete sets:

- `mob_combat_profiles`
- `mob_combat_bonuses`
- `mob_drops`

Saving always sets `publication_state = Draft`.

### `POST /api/v1/mobs/{mobDefinitionId}/publish`

Publishes the already saved aggregate after strict validation. The request body
contains only `expected_updated_at_utc` and `preview_signature`; it does not
carry unsaved form data. Publication requires a valid visual asset, positive
stats/movement, a primary melee combat profile, valid faction/aggression
consistency, valid bonuses, and published guaranteed-drop items.

### `POST /api/v1/mobs/{mobDefinitionId}/disable`

Sets `publication_state = Disabled` without deleting the aggregate. T4B does not
yet have an authoritative generated/published `EnemySpawn` reference guard, so
disable previews and mutations report that limitation as a warning. Runtime
reference enforcement remains T4E/runtime-integration work.

Mob contracts intentionally exclude Tiled placement fields such as spawn id,
map/region, home tile, patrol paths, and spawn count.

The T4C Godot **Mobs** workspace consumes these routes through
`AuthoringHostClient` and `AuthoringHttpTransport`. It sends complete draft and
preview aggregates for save/preview, sends only `expected_updated_at_utc` plus
`preview_signature` for publish/disable, clears the apply gate after every form
edit, and disables mob editing when the mob-authoring schema is unavailable.

## NPC routes

T5C adds host-side authoring routes for reusable NPC definitions. T5D adds the
Godot NPCs workspace over those routes. T5E/T5F complete the runtime catalog
handoff and harden reference safety, but the routes still do not modify Tiled
`NpcSpawn` placement.

### `GET /api/v1/npcs/options`

Returns NPC authoring options:

- publication states: `Draft`, `Published`, `Disabled`
- movement behaviors: `static`, `random_wander`
- interaction types: `talk`
- numeric defaults and limits
- known dialogue references when the configured file-backed MMO Project
  dialogue catalog can be resolved
- dialogue-reference validation capability
- visual asset-root metadata using `res://assets/actors/npcs/`
- capability flags: `supports_runtime_npc_catalog = true`,
  `supports_quest_authoring = false`, `supports_multiple_interactions = false`

### `GET /api/v1/npcs?search=test`

Searches NPC definition ID and display name. Results are ordered by display name
and stable ID, and include publication state, visual path, movement behavior,
interaction enabled state, default dialogue ID, editable status, and
`updated_at_utc`.

### `GET /api/v1/npcs/{npcDefinitionId}`

Loads one complete NPC aggregate:

- identity and `publication_state`
- visual texture path, source dimensions, anchor offsets, and render scale
- initial runtime-compatible `1x1` footprint
- default movement behavior, wander radius, tick interval, and idle chance
- `talk` interaction enablement, range, and optional default dialogue ID
- authoring-only notes
- created/updated timestamps
- local sprite preview file path when the asset resolves

### `POST /api/v1/npcs/{npcDefinitionId}/preview`

Validates `save_draft`, `publish`, `disable`, or `delete`, normalizes the
complete draft payload, calculates exact logical changes, returns reference
diagnostics, and returns a deterministic `preview_signature`.

`publish`, `disable`, and `delete` previews use the currently saved aggregate as
the effective payload. If the request body differs from the saved aggregate,
the preview reports `unsaved_npc_changes`.

Reference diagnostics use known database, generated chunk, and Tiled source
references when available. Known references block disable/delete; incomplete
reference visibility is surfaced with `reference_check_complete = false`.

### `PUT /api/v1/npcs/{npcDefinitionId}/draft`

Creates a new draft when the ID does not exist, or updates an existing
aggregate. The request must include the same complete draft that was previewed
plus `preview_signature`. Existing definitions also require
`expected_updated_at_utc`.

Saving writes only the `npc_definitions` root row, advances `updated_at_utc`,
reloads inside the transaction, commits, reloads after commit, and verifies the
saved aggregate.

### `POST /api/v1/npcs/{npcDefinitionId}/publish`

Publishes the already saved aggregate after strict validation. The request body
contains only `expected_updated_at_utc` and `preview_signature`; it does not
carry unsaved form data. Publication requires a valid visual PNG, matching
source dimensions when readable, current `1x1` footprint support, valid
movement, `talk` interaction, and a dialogue reference. Dialogue references are
checked against the file-backed MMO Project dialogue catalog when available;
otherwise validation is syntax-only and reports that limitation.

### `POST /api/v1/npcs/{npcDefinitionId}/disable`

Sets `publication_state = Disabled` without replacing the saved draft. Known
database, generated chunk, or Tiled source spawn references block disable. Incomplete reference
visibility is reported as a warning with `reference_check_complete = false`.

### `POST /api/v1/npcs/{npcDefinitionId}/delete`

Deletes a disabled NPC definition after preview/signature and concurrency
verification. Known database, generated chunk, or Tiled source spawn references
block delete.

The T5D Godot **NPCs** workspace consumes these routes through
`AuthoringHostClient` and `AuthoringHttpTransport`. It sends complete draft
and preview aggregates for save/preview, sends only `expected_updated_at_utc`
plus `preview_signature` for publish/disable/delete, clears the apply gate after
every form edit, and displays quest-authoring, multiple-action, and
reference-completeness capability states.

## Dialogue routes

D2 adds host-side authoring routes for reusable dialogue definitions. D3 Godot
Dialogue Studio implemented the top-level Dialogue workspace after NPCs and
before Environment over this same route family. D4 exports Published dialogue
definitions to MMO Project runtime JSON through `MapPublisher`.

### `GET /api/v1/dialogues/options`

Returns publication states, node types `speaker_text`, `player_choice`, and
`end`, ID rules/limits, default entry/start IDs, and capability flags. The
condition and effect registries are empty. Quest conditions/effects,
localization, portraits, hot reload, and cutscenes all report unsupported. The
Godot workspace shows those capabilities as unsupported and provides no quest,
condition, or effect authoring.

### `GET /api/v1/dialogues?search=greeting`

Searches dialogue definition ID and display name. Results are ordered by display
name and stable ID and include publication state, schema version, entry-point
count, node count, choice count, and `updated_at_utc`.

### `GET /api/v1/dialogues/{dialogueDefinitionId}`

Loads one complete aggregate: root metadata, publication state, schema version,
ordered entry points, ordered nodes, ordered choices, authoring-only metadata,
and root concurrency timestamps.

Runtime-relevant fields are definition ID, entry points, node IDs, node types,
speaker, text, `next_node_id`, dismissible, ordered choice IDs/text/targets.
Display name, metadata description, notes, canvas coordinates, editor notes,
publication state, and timestamps are authoring-only.

### `POST /api/v1/dialogues/{dialogueDefinitionId}/preview`

Validates a complete normalized draft for `save_draft`, `publish`, `disable`,
or `delete`, returns graph analysis, exact logical changes, NPC reference
diagnostics, and a deterministic `preview_signature`. Save Draft previews the
submitted graph. Publish, Disable, and Delete operate on the saved graph and
report `dialogue_unsaved_changes` when the submitted body differs from what is
already persisted.

### `POST /api/v1/dialogues/{dialogueDefinitionId}/playthrough`

Runs a noncommitting playthrough preview over the submitted draft or saved
definition. It can start/restart from an entry point, continue from
`speaker_text`, show ordered choices for `player_choice`, select a choice,
acknowledge `end`, detect stale node or choice IDs, and report loop-protection
warnings. It executes no effects because D2 has no effect vocabulary.

### `PUT /api/v1/dialogues/{dialogueDefinitionId}/draft`

Creates a new Draft or replaces an existing saved aggregate. The request must
include the same complete graph that was previewed plus `preview_signature`.
Existing definitions also require `expected_updated_at_utc`.

Saving locks the root row, verifies the expected timestamp, writes root
metadata, replaces entry points/nodes/choices transactionally, advances the root
timestamp for child-only edits, reloads inside the transaction, commits, reloads
again, and verifies semantic equality.

### `POST /api/v1/dialogues/{dialogueDefinitionId}/publish`

Publishes the already saved aggregate after strict validation against current
runtime semantics. Publication requires at least one entry point, resolving
entry and transition targets, supported node types, empty condition/effect data,
valid node-specific fields, a reachable end path, and all published nodes
reachable from entry points.

### `POST /api/v1/dialogues/{dialogueDefinitionId}/disable`

Sets `publication_state = Disabled` without replacing the saved graph. Published
NPC definitions that reference the dialogue through
`npc_definitions.default_dialogue_id` block disable.

### `POST /api/v1/dialogues/{dialogueDefinitionId}/delete`

Deletes a disabled dialogue definition after preview/signature and concurrency
verification. Any known NPC definition reference blocks delete, including Draft
and Disabled NPC references.

The D3 Godot **Dialogue** workspace consumes these routes through
`AuthoringHostClient` and `AuthoringHttpTransport`. It sends complete draft and
preview aggregates for save/preview, sends only `expected_updated_at_utc` plus
`preview_signature` for publish/disable/delete, clears the apply gate after
every form edit, uses GraphEdit for current node types, runs playthrough preview
through the host, and supports NPC cross-navigation through shell-level routing.

## Request correlation

The GUI sends `X-Request-Id`. The host preserves a non-empty supplied value or
generates one when absent. The value is returned in the response envelope.

## Compatibility policy

Breaking request or response changes require a new API route prefix. Additive
fields may be introduced within v1 when existing clients can safely ignore them.

## Runtime-consumption boundary

These routes author and validate persistence. They do not imply that the current
MMO server executes every authored item specialization. U5 must integrate
server-authoritative tool-capability resolution across equipped and inventory
items before authored tool capabilities affect gameplay.
