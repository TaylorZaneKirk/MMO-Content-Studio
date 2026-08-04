# MMO Project integration

This directory contains reviewed integration artifacts that must ultimately be
applied or copied into the main `MMO-Project` repository. The Content Studio
repository does not silently modify the game repository.

## T2 consumable migration

`prototype/sql/017_item_consumable_profiles.sql` adds the declarative consumable
schema used by the Content Studio's Consumables workspace:

- one profile per item
- ordered requirements
- ordered effects
- optional result-item transformation
- use action, combat availability, cooldown, message, animation, and sound metadata
- publication guards that prevent a published consumable from producing a disabled result item and prevent disabling result items still used by published consumables
- an idempotent seed that translates the current hard-coded food restore ranges and messages into declarative profiles without overwriting existing authored profiles

The first schema version intentionally supports only:

- requirement: `skill_minimum`
- effect: `restore_resource`
- resources: `health`, `concentration`, `special`
- use actions: `eat`, `drink`, `use`

True per-instance charge counters remain deferred because the current inventory
schema stores only `item_id` and `stack_count`. Portions and empty containers can
be modeled safely with `consume_quantity` plus `result_item_id`.

Applying this migration enables authoring. The MMO game server still needs a
separate runtime-consumption integration slice before it will execute these rows
instead of its current item-use behavior.

## T3A wearable-equipment integration

T3A does not require a new game-schema migration. It authors the existing
`item_definitions`, `equipment_slot_definitions`, `item_skill_requirements`,
`item_skill_modifiers`, `item_combat_profiles`, and `item_combat_bonuses`
contracts already established by the MMO Project.

The Content Studio adds an explicit **Equippable / Not equippable** workflow over
those tables. Removing equipability clears every dependent equipment/combat row
transactionally, which is the intended correction path for historical
name-derived slot assignments such as Chunk of Iron being mapped to a hand slot.
The MMO database's runtime-publication and live-reference triggers remain the
final authority during that operation.

## T3B weapon/tool foundation integration

T3B reuses the existing MMO Project equipment and combat tables for base item
metadata, slots, requirements, modifiers, `item_combat_profiles`, and
`item_combat_bonuses`.

`prototype/sql/018_item_tool_capabilities.sql` adds the only new table required
by this foundation slice. This integration artifact is mirrored into the local
MMO Project runtime checkout for development verification:

- `item_tool_capabilities`
- one row per item/capability ID
- deterministic `capability_order`
- optional declarative animation/effect resource IDs
- no durability, charges, ammo, item-instance state, or executable behavior

`prototype/sql/023_item_tool_capability_independence.sql` removes the obsolete
hand-slot guard triggers/functions from the T3B schema. Tool capabilities now
belong to the base item definition and may be authored for inventory tools,
wearable equipment, or hand equipment. Removing equipability clears equipment
and weapon metadata but preserves tool rows unless the submitted capability
collection is explicitly empty.

Tool capabilities are declarative metadata. The migration does not introduce
durability, ammo, charges, item-instance state, or executable behavior. A later
runtime gathering/tool-use slice must decide how to consume these rows
server-authoritatively.

The Content Studio host intentionally publishes only runtime-supported weapon
profiles today: active weapons are `right_hand`, attack family is `melee`,
styles are `thrust`, `slash`, or `crush`, range is logical tiles, and timing is
stored as `attack_speed_units`.

## T4 mob-authoring schema and runtime handoff

`prototype/sql/019_mob_authoring_schema.sql` is an additive handoff migration for
reusable mob-definition authoring. T4D mirrors the same file into the MMO Project
runtime repository as `prototype/sql/019_mob_authoring_schema.sql`.
`prototype/sql/020_mob_lifecycle_authoring.sql` carries the post-regen runtime
lifecycle timing columns, and `prototype/sql/021_seed_existing_mob_definitions.sql`
idempotently backfills the current runtime catalog mobs into authoring tables.

The migration introduces:

- `mob_factions`
- `mob_faction_dispositions`
- `mob_definitions`
- `mob_combat_profiles`
- `mob_combat_bonuses`
- `mob_drops`

The seed migration preserves existing authored rows and adds the starter
runtime definitions `slime`, `training_goblin`, and `training_guard` only when
those stable IDs are absent. This keeps the Mobs workspace from appearing empty
on development databases that have the new T4 schema but have not yet authored
their own mob drafts.

The schema follows the T4 audit boundary: Content Studio owns reusable mob
definitions, while Tiled/generated static content continues to own `EnemySpawn`
placement, home position, facing, and the `mob_definition_id` link.
`prototype/sql/022_mob_behavior_ownership.sql` moves reusable movement,
aggression, leash, and return-home behavior onto `mob_definitions`.

The T4B Content Studio host API reads and writes this schema when it exists in
the configured development database. T4D adds the MMO Project
`MapPublisher export-mob-catalog` handoff, which exports only `Published` rows
into `prototype/shared/maps/mobs/catalog.json` for the existing Tiled importer,
publisher, generated-file static-content source, and database-published
static-content source.

Runtime simulation continues to consume `MobDefinitionCatalog` from immutable
static content. It does not query these authoring tables directly.

The current T4 schema/API scope intentionally supports only primary melee combat
profiles, hostile/neutral faction dispositions, 600 ms attack-speed units,
logical-tile ranges, normalized 13-field combat bonuses, ordered guaranteed
drops, and runtime-defaulted defeated-hold/respawn timings. The MMO Project
runtime catalog now includes health-regeneration fields; T4D exports
`health_regeneration_amount = 0` and `health_regeneration_interval_ms = 0`
until a later slice adds explicit authoring support. It does not add random or
weighted drops, patrols, placement rows, dialogue, shops, quests, arbitrary
scripts, or runtime hot reload.

## T5 NPC-authoring runtime handoff

T5F runtime handoff hardening and reference safety implemented. T5A audited the current
MMO Project NPC runtime and locked the integration boundary; T5B added the
additive handoff migration `prototype/sql/024_npc_authoring_schema.sql`, host
contract shapes, normalization rules, registry/options, and schema-health
requirements. T5C adds Content Studio repository persistence, validator
behavior, options, catalog/list/load, preview, draft save, publish, disable,
delete, preview signatures, optimistic concurrency, reload verification, and
reference diagnostics. T5D adds the Godot NPCs workspace over `/api/v1/npcs`.
T5E mirrors the migration into MMO Project, seeds `test_npc`, exports
`prototype/shared/maps/npcs/catalog.json`, validates `NpcSpawn.npc_definition_id`
against that catalog, and composes runtime NPCs from placement plus reusable
definition data. T5F hardens startup validation, byte-stable catalog export,
placement-only Tiled source checks, and Content Studio disable/delete guards
across database, generated, and Tiled spawn references.

The active handoff is:

- Content Studio owns reusable `npc_definitions`.
- Tiled continues to own `NpcSpawn` placement, stable object names,
  coordinates, initial facing, and the `npc_definition_id` link.
- `MapPublisher export-npc-catalog` writes the published runtime catalog.
- Generated and database static-content sources expose `npc_definition_catalog`
  at the region level.
- `NpcRuntimeService` no longer requires hard-coded texture mapping.
- Content Studio blocks disable/delete when the definition is still referenced by
  known database, generated chunk, or Tiled source spawn data.
- The existing `WorldSnapshotNpcPayload`, `npc_interaction_request`, and
  `DialogueSessionService` payloads remain compatible.

`notes` is authoring-only metadata and is not exported to the runtime NPC
catalog. `default_dialogue_id` remains a stable string reference;
dialogue-reference validation uses the configured file-backed MMO Project
dialogue catalog when available and otherwise reports syntax-only validation.
`supports_runtime_npc_catalog = true` and `supports_quest_authoring = false`.

T5 does not add shops, banks, trainers, quest state, dialogue graph authoring,
NPC combat, schedules, portraits, emotes, cutscenes, arbitrary scripts, or
runtime hot reload. The Godot NPCs workspace is reusable-definition authoring
only; it does not export a runtime NPC catalog or author Tiled placement.

## D Dialogue Studio runtime handoff plan

D2 adds the Content Studio host-side dialogue authoring schema/API without
modifying the MMO Project runtime repository.

Current MMO Project dialogue definitions live in
`prototype/shared/dialogues/catalog.json`, are loaded by
`DialogueDefinitionCatalog`, and are executed through `DialogueSessionService`.
D2 adds database-backed reusable dialogue authoring inside MMO Content Studio
through `prototype/sql/026_dialogue_authoring_schema.sql`. The migration
introduces:

- `dialogue_definitions`
- `dialogue_entry_points`
- `dialogue_nodes`
- `dialogue_choices`

The D2 schema stores authoring-only display metadata, notes, canvas positions,
editor notes, publication state, and root concurrency timestamps. Runtime export
remains D4: a deterministic handoff should write only `Published` dialogue
definitions back into the runtime catalog shape already consumed by MMO
Project.

The initial handoff must preserve current runtime semantics: prioritized entry
points, `speaker_text`, `player_choice`, and `end` nodes, node-owned
transitions, server-filtered choices, end acknowledgement, close/cancellation
behavior, and the existing dialogue protocol payloads. D1-D5 do not add quest
predicates, quest effects, objective progress, rewards, content gates, arbitrary
scripting, or runtime hot reload.

D2 reference safety reads Content Studio `npc_definitions.default_dialogue_id`.
Published NPC references block dialogue disable, and any NPC reference blocks
dialogue delete. Conditions and effects have no authorable registry entries in
D2, and no condition/effect tables are created.
