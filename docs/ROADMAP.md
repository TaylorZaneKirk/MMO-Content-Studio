# Implementation Roadmap

## T0 — Authoring Foundation

**Status:** Source implementation complete; development-machine runtime verification pending.

Godot shell, loopback .NET host, versioned API, environment health, shared
envelopes, catalog seams, asset roots, and contract tests.

## T1 — Basic Items

**Status:** Source implementation complete; development-machine runtime verification pending.

Search, load, create, edit, validate, preview, draft, publish, disable, import
item PNGs, enforce concurrency, and reload/verify base item definitions.

## T2 — Consumable Items

**Status:** Source implementation complete; database migration and development-machine runtime verification pending.

Capabilities:

- Search all item definitions and identify Basic, Consumable, and Equipment kinds
- Convert an eligible basic item into a consumable aggregate
- Author `eat`, `drink`, or `use` actions
- Configure consumed stack quantity and optional result-item transformation
- Configure combat availability, cooldown, messages, animation, and sound references
- Author ordered `skill_minimum` requirements
- Author ordered `restore_resource` effects with inclusive minimum/maximum ranges for health, concentration, and Special
- Preview exact base/profile/requirement/effect replacements
- Save, publish, and disable transactionally with aggregate concurrency
- Preserve live-reference publication guards
- Explicitly defer per-instance charge counters and arbitrary executable effects

Exit condition:

> A maintainer can create or modify a declarative consumable without manual
> content SQL, and the complete persisted aggregate survives reload and strict
> publication validation.

A separate MMO server integration remains required to execute the authored
profiles at runtime.

## T3A — Wearable Equipment

**Status:** Source implementation complete; development-machine runtime verification pending.

Capabilities:

- Search every item definition so Basic items can be promoted into wearable equipment
- Explicitly author **Equippable** or **Not equippable** rather than relying on legacy name-derived metadata
- Configure wearable slots, required Strength, additional skill requirements, skill modifiers, and combat bonuses
- Preview the selected wearable on default player layers in N/S/E/W directions and frames 1-4 using the configured game-client assets
- Preview exact aggregate replacements before applying them
- Save, publish, and disable transactionally with optimistic concurrency and reload verification
- Atomically remove slot, requirements, modifiers, combat profile, and combat bonuses when equipability is removed
- Correct legacy misclassifications such as Chunk of Iron without hand-written SQL
- Keep left-hand/right-hand weapon and tool editing deferred to T3B while still permitting intentional declassification
- Continue deriving player-layer visual keys from display name and slot until the runtime supports explicit visual overrides

Exit condition:

> A maintainer can create or modify wearable equipment—or convert a mistakenly
> equippable item back into an ordinary item—without manually updating any of
> the related PostgreSQL tables.

## T3B — Weapons and Tools

**Status:** Backend/domain/API and Godot workspace implementation complete; development-machine runtime verification pending.

Capabilities:

- Search hand-equipment candidates and derive `Weapon`, `Tool`, and `Weapon + Tool` classification labels without adding a new top-level item kind
- Load the complete base/equipment/specialization aggregate
- Use a dedicated top-level **Weapons & Tools** workspace rather than folding hand specialization editing into Equipment
- Author `right_hand` and `left_hand` equipment while preserving wearable declassification paths
- Author optional `weapon_profile` rows using runtime-supported melee family/style, logical tile range, and `attack_speed_units`
- Display attack interval timing from `attack_speed_units` without persisting milliseconds
- Preserve combat-bonus ownership in `item_combat_bonuses`
- Author and reorder zero or more declarative `tool_capabilities`
- Require preview signatures before save, publish, or disable apply calls
- Share paper-doll asset resolution and rendering with T3A Equipment
- Replace child collections transactionally and clear stale hand specialization rows when equipability or slot changes
- Reject publication states that the current runtime cannot load, including left-hand weapon profiles and right-hand items without a weapon profile
- Defer durability, ammo, charges, item instance state, two-handed rules, and MMO Project runtime execution of tool capabilities

Exit condition:

> A maintainer can use the Godot Content Studio to safely author the
> hand-equipment domain aggregate and tool-capability persistence without
> manually editing linked SQL tables, while runtime-unsupported combat/tool
> semantics remain blocked from publication.

## T4 — Mobs

**Status:** T4B mob repository, validation, preview, and mutation API implemented; Godot workspace, migration application, runtime integration, and runtime verification pending.

Move reusable mob definitions into the database-backed authoring boundary while
keeping spawn placement in Tiled. Add identity, visuals, footprint, stats,
primary melee attack profiles, movement/aggression settings, factions,
guaranteed drops, preview/apply workflows, and runtime publication guards.

Locked boundaries:

- Content Studio owns reusable mob definitions.
- Tiled/generated static content owns `EnemySpawn` placement, home position,
  facing, spawn behavior, and leash radius.
- The current runtime consumes a `mob_definition_catalog`; T4 should preserve
  that shape during integration.
- Mob respawn, weighted drop tables, patrols, dialogue, shops, quests, arbitrary
  behavior scripts, and hot reload remain deferred.

Phase 0 references:

- [`T4_MOB_DOMAIN_AUDIT.md`](T4_MOB_DOMAIN_AUDIT.md)
- [`T4_IMPLEMENTATION_PLAN.md`](T4_IMPLEMENTATION_PLAN.md)
- [`T4_ACCEPTANCE.md`](T4_ACCEPTANCE.md)

T4A added the additive handoff migration
`integrations/mmo-project/prototype/sql/019_mob_authoring_schema.sql`,
compile-time host contracts, feature-owned schema-health requirements, a narrow
mob registry/domain-rules seam, and a feature-owned Mobs catalog section.

T4B adds database-backed repository behavior, validation, options, catalog
listing, aggregate loading, preview signatures, draft save, publish, disable,
optimistic concurrency, transaction-scoped child replacement, and reload
verification. It does not add the Godot Mobs workspace, apply the migration to
MMO Project, export definitions to runtime static content, or author Tiled
placement.

## T5 — Minimal NPC Authoring

Add reusable NPC identity, visuals, movement profiles, interaction capabilities, service references, and dialogue-reference placeholders before the NPC interaction slice.

## Later Workspaces

- Dialogue graph authoring
- Dialogue preview and playthrough validation
- Quest Studio scope evaluation
- Quest state-graph authoring
- Contribution bundle export and maintainer publication
- Validated candidate-snapshot hot reload
