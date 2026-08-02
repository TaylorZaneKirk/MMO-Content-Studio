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
