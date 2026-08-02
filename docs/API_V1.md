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

Reports the active database profile, PostgreSQL connectivity, the T2 item and
consumable schema contract, and configured asset-root status.

### `GET /api/v1/catalog`

Returns the top-level content catalog. Items and Consumables are implemented;
Mobs and NPCs remain planned workspaces.

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

## Basic-item routes

### `GET /api/v1/items?search=ore`

Lists current `item_definitions` records with publication and authoring-kind
metadata.

### `GET /api/v1/items/{itemId}`

Loads one complete T1 basic-item aggregate. Equipment definitions are visible
but marked read-only for this workspace.

### `POST /api/v1/items/{itemId}/preview`

Validates an intended operation and calculates its exact logical change summary.
Supported target operations are exactly `save_draft`, `publish`, and `disable`;
unknown values are rejected rather than silently defaulted.

```json
{
  "display_name": "Iron Ore",
  "icon_texture_path": "res://assets/items/iron_ore.png",
  "expected_updated_at_utc": null,
  "target_operation": "save_draft"
}
```

The Godot GUI requires a successful preview matching the current form before it
enables the apply action.

### `PUT /api/v1/items/{itemId}/draft`

Creates or updates one non-equippable item as `runtime_enabled = false`.
The operation locks the current row, executes in one transaction, commits, then
reloads and verifies the persisted aggregate.

### `POST /api/v1/items/{itemId}/publish`

Runs strict publication validation and sets `runtime_enabled = true`. The icon
must exist in the canonical asset root.

### `POST /api/v1/items/{itemId}/disable`

Sets `runtime_enabled = false` transactionally. Disable—and saving a currently
published item back as a draft—is rejected while live inventory, equipment, or
ground-item state references the item. T1 also warns that static mob-drop
references are still checked by the MMO server startup validator rather than by
this initial database-only workspace.

Publish and disable requests include:

```json
{
  "expected_updated_at_utc": "2026-08-01T23:00:00Z"
}
```

This optimistic-concurrency token is required for mutations of existing items
and prevents stale authoring sessions from silently overwriting newer changes.

## Request correlation

The GUI sends `X-Request-Id`. The host preserves a non-empty supplied value or
generates one when absent. The value is returned in the response envelope.

## Compatibility policy

Breaking request or response changes require a new API route prefix. Additive
fields may be introduced within v1 when existing clients can safely ignore them.

## Consumable routes

T2 requires the integration migration at
`integrations/mmo-project/prototype/sql/017_item_consumable_profiles.sql`.

### `GET /api/v1/consumables/options`

Returns the exact declarative values supported by this host version:

- use actions: `eat`, `drink`, `use`
- requirement type: `skill_minimum`
- effect type: `restore_resource`
- resource targets: `health`, `concentration`, `special`
- current skills loaded from `skill_definitions`

The response explicitly reports that true per-instance charges are unsupported
by the current inventory schema.

### `GET /api/v1/consumables?search=potion`

Lists all item definitions so an eligible Basic item can be converted. Each row
reports whether a consumable profile exists and whether the Consumables
workspace may edit it. Equipment definitions remain visible but read-only.

### `GET /api/v1/consumables/{itemId}`

Loads one aggregate containing:

- base item definition and publication state
- consumable profile
- ordered requirements
- ordered effects
- aggregate concurrency timestamp
- local icon preview path

An eligible Basic item with no profile loads with safe form defaults and can be
converted by saving a consumable draft.

### `POST /api/v1/consumables/{itemId}/preview`

Validates and previews `save_draft`, `publish`, or `disable`. The request contains
the complete intended definition:

```json
{
  "display_name": "Minor health potion",
  "icon_texture_path": "res://assets/items/minor_health_potion.png",
  "use_action": "drink",
  "consume_quantity": 1,
  "result_item_id": "empty_vial",
  "success_message": "You drink the potion.",
  "usable_in_combat": true,
  "cooldown_ms": 1200,
  "use_animation_id": "drink_potion",
  "use_sound_resource_path": "res://assets/audio/items/drink_potion.ogg",
  "requirements": [
    {
      "requirement_index": 0,
      "requirement_type": "skill_minimum",
      "target_id": "vitality",
      "minimum_value": 5
    }
  ],
  "effects": [
    {
      "effect_index": 0,
      "effect_type": "restore_resource",
      "target_id": "health",
      "minimum_amount": 8,
      "maximum_amount": 10
    }
  ],
  "expected_updated_at_utc": null,
  "target_operation": "save_draft"
}
```

The host normalizes child indexes from list order and never accepts arbitrary
code or expressions.

### `PUT /api/v1/consumables/{itemId}/draft`

Synchronizes `item_definitions`, `item_consumable_profiles`, requirements, and
effects in one transaction. Requirements and effects are replacement sets:
removing a row from the logical definition deletes the obsolete persisted row.
Saving always produces a runtime-disabled draft.

### `POST /api/v1/consumables/{itemId}/publish`

Requires a canonical icon, at least one valid effect, known requirement skills,
and a published result item when one is configured. It then sets the base item
`runtime_enabled = true`.

### `POST /api/v1/consumables/{itemId}/disable`

Returns the base item to Draft while preserving its consumable profile. Existing
MMO database guards continue to reject disabling an item referenced by live
gameplay state.

## Runtime-consumption boundary

These routes author and validate persistence. They do not imply that the current
MMO server executes the new tables. The server-side item-use resolver must be
integrated separately before authored profiles affect gameplay.
