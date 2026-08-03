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

**Status:** T4D runtime catalog export implemented; generated-spawn reference
hardening and full runtime verification pending.

Move reusable mob definitions into the database-backed authoring boundary while
keeping spawn placement in Tiled. Add identity, visuals, footprint, stats,
primary melee attack profiles, movement/aggression settings, factions,
guaranteed drops, preview/apply workflows, and runtime publication guards.

Locked boundaries:

- Content Studio owns reusable mob definitions.
- Tiled/generated static content owns `EnemySpawn` placement, home position,
  facing, and the `mob_definition_id` link; reusable behavior and leash radius
  belong to the mob definition.
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
verification.

T4C adds the top-level Godot **Mobs** workspace over the existing `/api/v1/mobs`
routes. Maintainers can search, load, create, edit, validate, preview, save
drafts, publish, and disable reusable mob definitions with identity, visuals,
footprint, stats, faction/aggression, one primary combat profile, combat
bonuses, and ordered guaranteed drops. It does not author Tiled placement.

T4D mirrors the mob authoring migrations into MMO Project and adds the
`MapPublisher export-mob-catalog` handoff from `Published` authoring rows to the
existing `prototype/shared/maps/mobs/catalog.json` runtime catalog. Generated
and database-published region manifests continue to embed that catalog, and
`EnemySpawn.mob_definition_id` remains the authoritative placement link.
The handoff now includes lifecycle timing columns and an idempotent seed for
the current runtime mobs: `slime`, `training_goblin`, and `training_guard`.
The exporter emits current MMO Project health-regeneration fields as zero
defaults until a later authoring slice adds explicit controls. Generated-spawn
reference guards remain deferred.

## T5 — Minimal NPC Authoring

Add reusable NPC identity, visuals, movement profiles, interaction capabilities, service references, and dialogue-reference placeholders before the NPC interaction slice.

## T6 — Interactable World Objects Foundation

**Status:** Planned; design direction documented.

Establish reusable interactable world-object definitions while preserving Tiled
placement and server-authoritative execution.

Capabilities:

- Audit the current MMO Project static world-object implementation before
  production schema or runtime work
- Establish reusable object definitions with stable IDs, visuals, footprint,
  collision policy, interaction distance, interaction options, and typed
  capability configuration
- Preserve Tiled ownership of placement, coordinates, facing/rotation,
  map-specific configuration, and linked placement IDs
- Add stable definition-to-placement linkage from `WorldObjectSpawn` to
  `WorldObjectDefinition`
- Add runtime instance identity and mutable state owned by MMO Project
- Add click-to-approach-and-interact through server-authoritative reachability
  and action resolution
- Add typed capability dispatch without arbitrary executable scripting
- Add visual states, footprint, and collision behavior tied to runtime state
- Add basic inspect, toggle, and search examples
- Add linked placement actions such as lever-to-gate interactions
- Add multiplayer state publication and revisioned updates
- Establish explicit state-scope rules for shared, per-player, temporary, and
  instance-scoped object state

Exit condition:

> A maintainer can author a reusable interactable object definition, place an
> instance in Tiled, and interact with the authoritative runtime instance
> through a typed capability without custom executable scripting.

## T7 — Gathering Resources and Processing Stations

**Status:** Planned; depends on the T6 interactable world-object foundation.

Build resource-node and processing-station gameplay on the shared world-object
definition, placement, and runtime-instance model.

Capabilities:

- Mining rocks and other resource nodes
- Tool-capability requirements using T3B typed tool capabilities
- Action timing and interruption
- Skill and XP integration
- Item production through authoritative inventory services
- Depletion and regeneration
- Cooking ranges, furnaces, anvils, looms, and workbenches
- Recipe/crafting-domain references without embedding recipes into each station
- Contention and reservation behavior
- Runtime persistence and recovery for state that must survive restart

Exit condition:

> Gathering nodes and processing stations use the shared world-object foundation
> and existing authoritative skill, tool, inventory, and activity systems
> without duplicating recipes or hard-coding specific tools.

## Ordering Note

T5 establishes reusable noncombat actors and interaction foundations. T6
establishes non-actor world interactions. T7 adds skill-specific gathering and
production behavior. Dialogue and Quest Studio then gain both NPC and
world-object interaction targets; this ordering provides infrastructure they can
consume and does not reduce their importance.

Design reference:

- [`INTERACTABLE_WORLD_OBJECTS_DESIGN.md`](INTERACTABLE_WORLD_OBJECTS_DESIGN.md)

## Later Workspaces

- Dialogue graph authoring
- Dialogue preview and playthrough validation
- Quest Studio scope evaluation
- Quest state-graph authoring
- Contribution bundle export and maintainer publication
- Validated candidate-snapshot hot reload
