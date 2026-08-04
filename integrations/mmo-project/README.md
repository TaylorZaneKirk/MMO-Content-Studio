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
