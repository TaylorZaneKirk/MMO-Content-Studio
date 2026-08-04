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
- Tiled remains responsible for map placement. Content Studio defines reusable
  items, mobs, NPCs, and planned dialogue definitions.

## Primary components

### Godot Content Studio

Responsibilities:

- Catalog navigation and search
- Guided data-entry forms
- Inventory, ground-item, paper-doll, mob, NPC, and dialogue graph/playthrough previews
- Asset selection and import requests
- Validation and change-summary presentation
- Draft, publish, disable, and edit workflows

`AuthoringHttpTransport` owns HTTP and envelope parsing. `AuthoringHostClient`
owns feature-specific request methods and signals. Editors own their forms and
payloads, while `AuthoringWorkspaceSupport` owns preview/apply lifecycle state
and feedback rendering.

The D3 Godot Dialogue Studio workspace follows that same boundary over
`/api/v1/dialogues`: GraphEdit and the node/playthrough inspectors are
feature-owned in Godot, while the host owns validation, graph analysis,
preview-signatures, persistence, reference checks, and transactions. The
Dialogue workspace appears after NPCs and before Environment and uses shell
routing for NPC cross-navigation. D4 MMO Project runtime catalog handoff is
implemented, while D1-D5 still provide no quest, condition, or effect authoring.

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
- Tool capabilities
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

## T3A wearable-equipment aggregate

T3A uses the existing MMO Project schema rather than introducing a parallel
Content Studio-only model:

```text
item_definitions
  ├─ equipment_slot_id / required_strength
  ├─ item_skill_requirements
  ├─ item_skill_modifiers
  ├─ item_combat_bonuses
  └─ item_combat_profiles (read-only in wearable mode; editable later in T3B)
```

Equipability is an explicit authoring decision in the API and GUI. In current
persistence it is represented by a non-null `equipment_slot_id`; the author does
not need to know that implementation detail.

Turning **Equippable** off is a destructive aggregate synchronization, not a
visual toggle. The host locks the item row, clears the slot, resets the legacy
Strength gate, deletes requirements, modifiers, combat profile, and combat
bonuses, reloads the result inside the transaction, commits, then reloads and
verifies it again. This prevents stale rows from continuing to make an ordinary
material behave like equipment.

The cleanup operation intentionally works for hand-held items even though T3A
cannot edit their weapon/tool fields. This lets maintainers correct legacy
name-derived assignments such as `Chunk of Iron -> right_hand` without waiting
for T3B or writing SQL. Re-enabling or modifying a real hand-held weapon/tool
remains a T3B concern.

Wearable paper-doll asset keys continue to be derived from display name and slot
because that is the current client runtime contract. The Godot workspace uses
the configured `game_client_assets` root to preview default player layers plus
the selected wearable in four directions and four frames. It mirrors the current
north-frame fallback, legacy filename normalization, and layer z-order rules.
Explicit directional asset overrides require a later schema and runtime
integration rather than being invented solely in Content Studio.

## T3B hand-equipment aggregate

T3B keeps weapons and tools inside the equipment domain:

```text
item_definitions
  ├─ equipment_slot_id / required_strength
  ├─ item_skill_requirements
  ├─ item_skill_modifiers
  ├─ item_combat_profiles (optional weapon_profile)
  ├─ item_combat_bonuses
  └─ item_tool_capabilities (ordered declarative tool capabilities)
```

`right_hand` and `left_hand` are the only hand slot identifiers. The current
game server resolves active weapon combat profiles from `right_hand`, so
publication validation follows that runtime fact: published `right_hand` items
must have a valid `weapon_profile`, and `left_hand` weapon profiles are blocked
until the runtime supports them.

Weapon profile data remains narrow and declarative. Range is stored in logical
tiles, attack speed is stored as `attack_speed_units`, and combat bonuses stay
in `item_combat_bonuses` rather than being duplicated in the profile.

Tool behavior is also declarative. `item_tool_capabilities` stores ordered
capability identifiers such as `mining`; it does not store durability, ammo,
charges, arbitrary scripts, or item-instance state.

T3B API mutations require a matching `preview_signature` in addition to the
aggregate `updated_at` concurrency token. The host locks the base item row,
replaces child collections, clears stale hand specialization rows when
equipability or slot changes, reloads inside the transaction, commits, and then
reloads again to verify the persisted aggregate.

The T3B Godot implementation originally kept hand equipment in a dedicated
**Weapons & Tools** workspace. U3 replaced the visible item-specialization tabs
with one contextual **Items** workspace backed by the complete `/api/v1/items`
aggregate. U4 removed the legacy `consumable_editor.gd`,
`equipment_editor.gd`, and `hand_equipment_editor.gd` files and retired the old
specialization route groups.

## Unified item-authoring boundary

The T1-T3B slices intentionally shipped as separate vertical workspaces, but
they all mutate one runtime root: `item_definitions`. U2 exposes one public
`ItemDefinition` aggregate while keeping specialization internals modular:

```text
item_definitions
  ├─ item_consumable_profiles
  │    ├─ item_consumable_requirements
  │    └─ item_consumable_effects
  ├─ item_skill_requirements
  ├─ item_skill_modifiers
  ├─ item_combat_bonuses
  ├─ item_combat_profiles (optional weapon_profile)
  └─ item_tool_capabilities (independent item capabilities)
```

The host-side U2 boundary treats consumable behavior, equipability, weapon
profile, combat bonuses, and tool capabilities as contextual item
specializations rather than separate top-level item domains. Tool capabilities
are independent of equipability; removing equipability clears equipment and
weapon metadata but must preserve tool capability rows. Weapon profiles remain
contextual to runtime-supported weapon-capable slots, initially `right_hand`.

U3 made that aggregate the active Godot item workflow. U4 made the same
aggregate the only public item mutation surface by removing the legacy Basic
payload branch and retired `/api/v1/consumables`, `/api/v1/equipment`, and
`/api/v1/hand-equipment` route groups. All item mutations now flow through
`UnifiedItemAuthoringService` and `UnifiedItemRepository`.

Detailed evidence and the phased migration plan live in
[`UNIFIED_ITEM_AUTHORING_AUDIT.md`](UNIFIED_ITEM_AUTHORING_AUDIT.md) and
[`UNIFIED_ITEM_AUTHORING_PLAN.md`](UNIFIED_ITEM_AUTHORING_PLAN.md), and
[`UNIFIED_ITEM_AUTHORING_ACCEPTANCE.md`](UNIFIED_ITEM_AUTHORING_ACCEPTANCE.md).

## T4 mob-definition boundary

T4 keeps mob authoring aligned with the current MMO Project static-content
contract:

```text
mob_definitions
  ├─ mob_combat_profiles
  ├─ mob_combat_bonuses
  ├─ mob_drops
  └─ faction/aggression fields

Tiled EnemySpawn placement
  └─ mob_definition_id reference
```

Content Studio owns reusable mob definitions: stable id, display name, visuals,
footprint, max health, primary melee attack profile, combat levels, combat
bonuses, movement speed, optional faction/aggression settings, guaranteed drops,
publication state, and aggregate concurrency.

Tiled and generated/static-content publication continue to own placement facts:
spawn id, source position, home position, facing, and `mob_definition_id`
linkage. Reusable movement, aggression, leash, and return-home behavior belongs
to the mob definition. The game runtime composes those placement facts with a
`mob_definition_catalog` into runtime enemies during static-content startup.

Initial T4 authoring deliberately excludes mob respawn timers, random/weighted
drop tables, patrol routes, dialogue, shops, quests, arbitrary scripts, and hot
reload until matching runtime contracts exist.

T4B adds the Content Studio host implementation for that boundary: an additive
MMO Project migration handoff artifact, host-side mob contracts, a feature-owned
schema-health manifest, repository-backed catalog/load/options, validation,
preview signatures, draft save, publish, disable, transaction-scoped child
replacement, and reload verification. The Godot editor and runtime repository
integration remain outside this slice.

## T5 NPC-definition boundary

T5 keeps NPC authoring separate from Tiled placement and separate from future
Dialogue/Quest Studio work:

```text
npc_definitions
  └─ default talk/dialogue reference

Tiled NpcSpawn placement
  └─ npc_definition_id reference
```

Content Studio should own reusable NPC definitions: stable id, display name,
runtime actor visual path, source dimensions, anchor/render settings, footprint,
default movement behavior, interaction range, default `talk` capability,
optional default `dialogue_id`, publication state, and aggregate concurrency.

Tiled and generated/static-content publication continue to own placement facts:
stable spawn object name, source coordinates, runtime mount composition, initial
facing, and the `npc_definition_id` linkage. Placement IDs and coordinates must
not become Content Studio fields.

The current MMO Project runtime supports NPC click-to-approach and modal
dialogue through `NpcInteractionService`, `IInteractionSpatialPort`,
`DialogueDefinitionCatalog`, and `DialogueSessionService`. Initial T5 authoring
therefore persists only the current `talk`/dialogue capability. Dialogue graphs,
quests, shops, banking, trainers, service menus, NPC combat, schedules, emotes,
portraits, cutscenes, arbitrary scripts, and runtime hot reload remain deferred
until matching runtime consumers exist.

T5B implements the schema/contract foundation for this boundary. The handoff
migration is
`integrations/mmo-project/prototype/sql/024_npc_authoring_schema.sql`; the host
has compile-time NPC contracts, domain normalization rules, static registry
options, and feature-owned schema-health requirements.

T5C NPC repository, validation, and API implemented. T5D Godot NPC workspace
implemented. T5E MMO Project runtime NPC catalog handoff implemented. T5F
runtime/reference hardening implemented. The host
now owns repository-backed
options, catalog/list/load, preview, save draft, publish, disable, delete,
preview signatures, optimistic concurrency, transactional root writes, reload
verification, and reference diagnostics for known database, generated chunk, and
Tiled source spawn references. `notes` is authoring-only metadata and is omitted from the
runtime NPC catalog export. `default_dialogue_id` remains a stable string
reference. Dialogue-reference validation uses the configured file-backed MMO
Project dialogue catalog when it can be resolved from `game_client_assets`; when
that catalog is unavailable, validation is syntax-only and reports that
limitation. `supports_runtime_npc_catalog = true` and
`supports_quest_authoring = false`. The Godot NPCs workspace owns the complete
NPC aggregate form and preview/apply lifecycle, but does not author placement,
dialogue graphs, or quests.

## D Dialogue Studio boundary

D2 implements Dialogue Studio's host-side authoring boundary. The later Godot
workspace belongs in the same Godot shell and .NET authoring host as Items,
Mobs, and NPCs. It is not a separate application, and the NPC workspace should
provide navigation to referenced dialogue definitions without embedding the full
graph editor.

The current MMO Project runtime source of truth is
`prototype/shared/dialogues/catalog.json`, loaded by
`DialogueDefinitionCatalog` and executed through `DialogueSessionService`.
D2 authors that current model through `dialogue_definitions`,
`dialogue_entry_points`, `dialogue_nodes`, and `dialogue_choices`: reusable
dialogue definitions, prioritized entry points, `speaker_text`,
`player_choice`, and `end` nodes, node-owned transitions, plain speaker/text
fields, visible choices, manual close, end acknowledgement, and activity
cancellation semantics.

The D2 repository performs complete aggregate replacement transactionally.
Target-node consistency is service validation rather than a database target FK,
which allows incomplete Draft graphs to persist while publish validation stays
strict. Child-table mutations advance the root `updated_at_utc`, and mutations
require both optimistic concurrency and preview signatures. The pure
playthrough-preview service emulates current session flow without executing
effects. NPC reference guards read `npc_definitions.default_dialogue_id`:
Published NPC references block disable, and any NPC reference blocks delete.

D1-D5 intentionally do not author quest predicates, quest effects, objective
progress, rewards, content gates, arbitrary scripts, portraits, localization,
cutscenes, or hot reload. Those capabilities require MMO Project quest
foundations and typed runtime contracts before Dialogue Studio exposes them.
