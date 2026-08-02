# Architecture

## Locked decisions

- The desktop GUI and visual-preview environment use **Godot**.
- A separate local **.NET Content Authoring Host** owns PostgreSQL access.
- The GUI communicates with the host through a local, versioned HTTP/JSON API.
- The GUI never issues arbitrary SQL and never connects directly to PostgreSQL.
- Authoring operations work with complete logical content aggregates, not individual table rows.
- Every database mutation is transactional.
- Draft content remains unavailable to the active game runtime.
- Publication requires complete aggregate validation.
- Existing content can be loaded, edited, disabled, and republished.
- Tiled remains responsible for map placement. Content Studio defines reusable items, mobs, and NPCs.

## Primary components

### Godot Content Studio

Responsibilities:

- Catalog navigation and search
- Guided data-entry forms
- Inventory, ground-item, paper-doll, mob, and NPC previews
- Asset selection and import requests
- Validation and change-summary presentation
- Draft, publish, disable, and edit workflows

### .NET Content Authoring Host

Responsibilities:

- Database connectivity and schema compatibility checks
- Loading complete authored aggregates
- Transactional create/update/disable/publication operations
- Validation and cross-reference resolution
- Canonical asset-path and filesystem mutation orchestration
- Reload-and-verify after writes
- Future contribution export and audit support

## Authoring operation model

The host accepts a logical definition such as an `ItemAuthoringDefinition` and makes all applicable persisted records match it in one transaction.

An operation may synchronize multiple tables, including:

- Base definitions
- Inventory behavior
- Equipment metadata
- Combat profiles
- Combat bonuses
- Skill modifiers
- Asset references
- Publication state

Deleting a capability from the logical definition removes obsolete dependent records rather than leaving stale metadata behind.

## Initial publication model

The current MMO Project uses `item_definitions.runtime_enabled` as the active item-publication seam.

The current boolean persists only two states:

- `Published` maps to `runtime_enabled = true`
- `Draft` maps to `runtime_enabled = false`

The **Disable** operation therefore returns an item to the persisted `Draft`
state in T1. Validation readiness is calculated rather than stored. A future
publication-lifecycle migration may distinguish Draft, Ready for Review,
Disabled, and Deprecated without changing the GUI/host boundary.

## Process model

Initially, developers may run Godot and the .NET host separately. The intended final experience is one launcher flow:

1. Launch Content Studio.
2. Start or connect to the local authoring host.
3. Verify host API version, database schema, and asset roots.
4. Load the content catalog.
5. Shut down the child host when the application exits.


## T1 basic-item boundary

The first authoring aggregate maps to the MMO Project's current
`item_definitions` contract:

- stable `item_id`
- `item_name`
- canonical `icon_texture_path`
- `runtime_enabled`
- `updated_at` optimistic-concurrency token

T1 intentionally refuses to edit definitions carrying equipment metadata. A
basic-item draft always has `equipment_slot_id = null`, `required_strength = 1`,
and `runtime_enabled = false`.

The current game schema uses one icon path for both inventory and ground-item
presentation, so T1 exposes one shared icon. Separate ground art requires a
future game-schema and runtime change rather than a Content Studio-only field.

PNG import is a host-owned filesystem mutation. Godot chooses a local file, but
the host validates and copies it into the canonical `game_client_assets/items`
directory without overwriting a different existing file.

Published definitions cannot be returned to draft/disabled state while live
character inventory, character equipment, or ground-item rows reference them.
The host performs a friendly preflight check and the MMO Project database trigger
remains the final race-safe authority. Existing-item mutations require the
`updated_at` concurrency token.

Static mob-drop references are not yet database-authored, so T1 surfaces a
warning rather than pretending to validate them. The MMO server startup
validator remains authoritative for those references until the T4 mob migration.

## T2 consumable aggregate

T2 expands one authored item into a four-part aggregate:

```text
item_definitions
  └─ item_consumable_profiles
       ├─ item_consumable_requirements (ordered)
       └─ item_consumable_effects (ordered)
```

The host owns complete replacement semantics for both child collections. A save
locks the base item, validates the full logical definition, upserts the base and
profile, deletes/reinserts ordered children, reloads the aggregate, and verifies
semantic equality before reporting success.

The first declarative vocabulary is deliberately narrow:

- `skill_minimum` requirement
- `restore_resource` effect with an inclusive minimum/maximum range
- health, concentration, and Special resources
- eat, drink, and use actions

This is a registry-shaped boundary, not a scripting language. Contributors
cannot store C#, GDScript, SQL, or arbitrary expressions in effect rows. New
behavior requires a reviewed runtime handler and a corresponding schema/contract
extension.

`result_item_id` supports portions and container transformations without adding
per-instance state. True charges remain deferred until inventory instances can
store authoritative metadata beyond `item_id` and `stack_count`.

T2 includes a migration artifact and an idempotent translation of the current hard-coded food dictionary into equivalent inclusive restore ranges, but intentionally does not mutate the separate
MMO Project repository. Applying the migration enables authoring; the game
server still requires an explicit consumer that executes the declarative profile
through its authoritative inventory/runtime-state mutation boundary.

## T3A equipment read aggregate

The first wearable-equipment slice is intentionally read-only. It exposes the
current game schema as one aggregate:

```text
item_definitions
  ├─ equipment_slot_definitions
  ├─ item_skill_requirements
  ├─ item_skill_modifiers
  ├─ item_combat_profiles
  └─ item_combat_bonuses
```

T3A separates wearable slots from hand-held weapon/tool slots. Head, cape, body,
legs, boots, gloves, and ring definitions are the wearable boundary for this
workspace. Left-hand and right-hand definitions remain visible for context but
are deferred to T3B.

The current runtime derives player-layer visual asset keys from item name and
slot. The Content Studio read model reports that derived key, but T3A does not
add a persisted paper-doll asset override or any mutation routes yet.
