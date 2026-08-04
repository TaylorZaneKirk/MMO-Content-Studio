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
mob-authoring schema, and NPCs are implemented when the configured database has
the T5 NPC authoring schema.

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
IDs, consumable action/effect/requirement types, published item references, and
shared limits.

### `GET /api/v1/items?search=ore`

Lists item definitions with derived classification labels such as `Basic`,
`Consumable`, `Equipment`, `Weapon`, `Tool`, and combinations such as
`Consumable + Tool` or `Weapon + Tool`.

### `GET /api/v1/items/{itemId}`

Loads the complete aggregate: identity, icon, publication state, optional
`consumable_behavior`, optional `equipment`, optional equipment
`weapon_profile`, independent `tool_capabilities`, and one `updated_at_utc`
concurrency token.

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

T5C adds host-side authoring routes for reusable NPC definitions. These routes
do not export a runtime NPC catalog yet, do not modify Tiled `NpcSpawn`
placement, and do not add a Godot NPC workspace.

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
- capability flags: `supports_runtime_npc_catalog = false`,
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

Reference diagnostics are deliberately narrow in T5C. Known generated/database
references block disable/delete, but the default repository seam reports
`reference_check_complete = false` because Tiled source validation and runtime
catalog handoff remain T5E/T5F work.

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
generated/database spawn references block disable. Incomplete reference
visibility is reported as a warning with `reference_check_complete = false`.

### `POST /api/v1/npcs/{npcDefinitionId}/delete`

Deletes a disabled NPC definition after preview/signature and concurrency
verification. Known generated/database spawn references block delete.

No Godot NPC workspace is implemented yet.

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
