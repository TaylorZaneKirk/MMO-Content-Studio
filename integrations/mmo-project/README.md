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
- a hand-slot guard requiring the owning item to be `right_hand` or `left_hand`

Tool capabilities are declarative metadata. The migration does not introduce
durability, ammo, charges, item-instance state, or executable behavior. A later
runtime gathering/tool-use slice must decide how to consume these rows
server-authoritatively.

The Content Studio host intentionally publishes only runtime-supported weapon
profiles today: active weapons are `right_hand`, attack family is `melee`,
styles are `thrust`, `slash`, or `crush`, range is logical tiles, and timing is
stored as `attack_speed_units`.

## T4A mob-authoring schema handoff

`prototype/sql/019_mob_authoring_schema.sql` is an additive handoff migration for
future reusable mob-definition authoring. It is present only in this integration
directory until a later, explicitly approved MMO Project runtime slice applies
or mirrors it.

The migration introduces:

- `mob_factions`
- `mob_faction_dispositions`
- `mob_definitions`
- `mob_combat_profiles`
- `mob_combat_bonuses`
- `mob_drops`

The schema follows the T4 audit boundary: Content Studio owns reusable mob
definitions, while Tiled/generated static content continues to own `EnemySpawn`
placement, home position, facing, spawn behavior, and leash radius.

T4A intentionally supports only primary melee combat profiles, hostile/neutral
faction dispositions, 600 ms attack-speed units, logical-tile ranges, normalized
13-field combat bonuses, and ordered guaranteed drops. It does not add random or
weighted drops, respawn settings, patrols, placement rows, dialogue, shops,
quests, arbitrary scripts, or runtime hot reload.
