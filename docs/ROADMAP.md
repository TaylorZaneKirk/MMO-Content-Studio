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

## U - Unified Item Authoring

**Status:** U5 runtime tool resolution implemented in MMO Project; gathering and processing consumers remain deferred.

Collapse Basic Items, Consumables, Equipment, and Weapons and Tools into one
public item aggregate and one contextual Godot Items workspace.

Locked boundaries:

- Consumable behavior, equipability, weapon profile, combat bonuses, and tool
  capabilities are optional specializations of `ItemDefinition`.
- Tool capability does not require equipability, a hand slot, or the item being
  currently equipped.
- Removing equipability clears equipment-owned and weapon-owned metadata but
  preserves independent tool capabilities.
- Weapon profiles are contextual to weapon-capable slots; initially only
  `right_hand` is runtime-publishable.
- Combat bonuses remain equipment metadata and apply while equipped.
- One preview signature and one `updated_at` concurrency token cover the
  complete item aggregate.
- U4 retired the old T1-T3B public route families and kept `/api/v1/items` as
  the only public item-authoring route family.

References:

- [`UNIFIED_ITEM_AUTHORING_AUDIT.md`](UNIFIED_ITEM_AUTHORING_AUDIT.md)
- [`UNIFIED_ITEM_AUTHORING_PLAN.md`](UNIFIED_ITEM_AUTHORING_PLAN.md)

Phased plan:

- U1 decouples tool capabilities from hand equipment and preserves them when
  equipability is removed.
- U2 added the unified item host aggregate and temporary compatibility adapters.
- U3 replaced specialization tabs with one contextual Items workspace.
- U4 retired obsolete routes, adapters, duplicate providers, and legacy editor scripts.
- U5 added MMO Project runtime tool resolution across equipped and inventory
  items. It selects only; gathering, processing, durability, charges, resource
  depletion, and world-object execution remain deferred.

## T5 — Minimal NPC Authoring

**Status:** T5A runtime audit and domain lock documented. T5B NPC schema and
contract foundation implemented. T5C NPC repository, validation, and API
implemented. T5D Godot NPC workspace implemented. T5E MMO Project runtime NPC
catalog handoff implemented. T5F runtime/reference hardening implemented.

Move reusable NPC identity and presentation data into the database-backed
authoring boundary while keeping spawn placement in Tiled. The current MMO
Project path authors `NpcSpawn` objects in Tiled, validates their
`npc_definition_id` against a published runtime NPC catalog, embeds that catalog
in generated/database static content, and composes runtime NPCs from placement
plus reusable definitions.

Locked boundaries:

- Content Studio owns reusable NPC definitions: stable id, display name,
  visuals, footprint, default movement behavior, default talk/dialogue
  interaction, publication state, and aggregate concurrency.
- Tiled/generated static content owns `NpcSpawn` placement, stable spawn object
  name, source coordinates, runtime mount composition, initial facing, and the
  `npc_definition_id` link.
- The current runtime supports only the `talk` interaction through
  server-authoritative click-to-approach and `DialogueSessionService`.
- Dialogue graph authoring, quests, shops, banking, trainers, service menus,
  NPC combat, factions, schedules, portraits, emotes, cutscenes, arbitrary
  scripts, and hot reload remain deferred.

References:

- [`T5_NPC_DOMAIN_AUDIT.md`](T5_NPC_DOMAIN_AUDIT.md)
- [`T5_NPC_AUTHORING_PLAN.md`](T5_NPC_AUTHORING_PLAN.md)
- [`T5_NPC_ACCEPTANCE.md`](T5_NPC_ACCEPTANCE.md)

T5B added the additive handoff migration
`integrations/mmo-project/prototype/sql/024_npc_authoring_schema.sql`,
compile-time host contracts, feature-owned schema-health requirements, NPC
domain normalization rules, a registry/options seam, and a feature-owned NPC
catalog section. T5C adds repository persistence, validator behavior, options,
catalog/list/load, preview, save draft, publish, disable, delete, preview
signatures, optimistic concurrency, transactional root writes, reload
verification, and a narrow reference diagnostic seam. Notes are authoring-only
metadata and are omitted from the runtime NPC catalog export.
Dialogue-reference validation uses the configured file-backed MMO Project
dialogue catalog when available; otherwise it reports syntax-only validation.
`supports_runtime_npc_catalog = true` and `supports_quest_authoring = false`.
T5D adds the Godot NPCs workspace for list/search/create/load/edit/preview/save,
publish, disable, and delete over `/api/v1/npcs`. T5E adds the catalog-export,
static-content, and runtime handoff. T5F adds startup validation for exported NPC
definitions and spawn references, byte-stable exporter coverage, placement-only
Tiled source checks, and Content Studio reference guards across database,
generated, and Tiled spawn references; it does not add placement authoring or
quest editing.

## D - Dialogue Studio

**Status:** D1-D5 non-quest Dialogue Studio authoring, graph editing, runtime
catalog export, validator/runtime equivalence, reference safety, and end-to-end
verification are complete. Quest predicates/effects remain deferred.

Move current non-quest dialogue graph authoring into MMO Content Studio as a
first-class workspace after NPCs and before Environment. Dialogue Studio is not
a separate application, and the NPC workspace links to referenced dialogues
without embedding the full graph editor.

Current MMO Project dialogue definitions are file-backed JSON at
`prototype/shared/dialogues/catalog.json`, loaded during server startup by
`DialogueDefinitionCatalog`, and executed through `DialogueSessionService`.
The current runtime model supports dialogue definitions, prioritized entry
points, `speaker_text`, `player_choice`, and `end` nodes, node-owned
transitions, server-filtered choices, manual close, end acknowledgement, stale
command protection, and activity cancellation.

Locked D1-D5 boundaries:

- Content Studio owns reusable dialogue definitions in the D2 host schema.
- NPC definitions reference dialogues by stable `default_dialogue_id`; NPC
  placement remains in Tiled.
- D1-D5 author only current runtime-compatible dialogue semantics.
- Initial condition and effect registries expose no production authorable types.
- Quest predicates, quest effects, objective progress, rewards, content gates,
  arbitrary scripting, portraits, cutscenes, localization, and hot reload remain
  deferred.
- D3 provides no quest, condition, or effect authoring.

References:

- [`DIALOGUE_STUDIO_RUNTIME_AUDIT.md`](DIALOGUE_STUDIO_RUNTIME_AUDIT.md)
- [`DIALOGUE_STUDIO_DOMAIN_MODEL.md`](DIALOGUE_STUDIO_DOMAIN_MODEL.md)
- [`DIALOGUE_STUDIO_IMPLEMENTATION_PLAN.md`](DIALOGUE_STUDIO_IMPLEMENTATION_PLAN.md)
- [`DIALOGUE_STUDIO_ACCEPTANCE.md`](DIALOGUE_STUDIO_ACCEPTANCE.md)

Phased plan:

- D1 audits the runtime and locks the non-quest Dialogue Studio model.
- D2 adds additive schema, contracts, repository, validation, playthrough
  preview, schema-health/catalog registration, and `/api/v1/dialogues`.
- D3 adds the Godot Dialogue workspace with GraphEdit graph editing,
  playthrough preview, validation/change previews, and NPC cross-navigation.
- D4 adds the MMO Project runtime catalog handoff while preserving the existing
  dialogue protocol. This handoff is implemented.
- D5 hardens runtime verification, reference safety, and connected playthroughs.
  This non-quest hardening slice is complete.

D2 adds `integrations/mmo-project/prototype/sql/026_dialogue_authoring_schema.sql`
with `dialogue_definitions`, `dialogue_entry_points`, `dialogue_nodes`, and
`dialogue_choices`. Target-node resolution is validated by the service rather
than PostgreSQL target FKs so Draft graphs can preserve incomplete work.
Child-table triggers advance the root `updated_at_utc`; the repository still
locks the root, checks the expected timestamp, replaces children
transactionally, reloads inside the transaction, commits, and reloads again.
Published NPC references through `npc_definitions.default_dialogue_id` block
disable; any NPC reference blocks delete. Current capabilities report
`supports_runtime_dialogue_catalog = true`, empty condition/effect registries,
and no quest, localization, portrait, cutscene, or hot-reload support.

D3 Godot Dialogue Studio is implemented over the D2 `/api/v1/dialogues` routes.
The Dialogue workspace after NPCs and before Environment lists definitions,
loads complete aggregates, edits `speaker_text`, `player_choice`, and `end`
nodes on a GraphEdit canvas, keeps condition/effect controls read-only as
unsupported, previews exact logical changes, gates mutations with
preview-signatures, runs host playthrough preview, and routes NPC references
back to the NPCs workspace. D4 MMO Project runtime catalog handoff now exports
Published authoring rows to runtime JSON.

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

T5 establishes reusable noncombat actors and interaction foundations. Dialogue
Studio now proceeds before T6/T7 because the current MMO Project dialogue
runtime already exists and can be authored without quest semantics. D1-D5 first
cover the non-quest dialogue model. MMO Project quest foundations come later,
followed by Dialogue Studio quest integration and Quest Studio. T6/T7 remain
planned world-object and gathering/processing slices, but they are not blockers
for D1-D5.

Remote and mobile Content Studio support is not a prerequisite for any of those
features. It is deliberately placed after all currently known authoring and
operational work. It may move earlier only when MMO Project deployment work,
remote collaboration, or another concrete dependency needs the same hosted and
authenticated boundary.

Design references:

- [`INTERACTABLE_WORLD_OBJECTS_DESIGN.md`](INTERACTABLE_WORLD_OBJECTS_DESIGN.md)
- [`REMOTE_MOBILE_CONTENT_STUDIO_PLAN.md`](REMOTE_MOBILE_CONTENT_STUDIO_PLAN.md)

## Later Workspaces

Current order:

1. Quest Studio scope evaluation
2. Quest state-graph authoring
3. Contribution bundle export and maintainer publication
4. Validated candidate-snapshot hot reload
5. Other higher-value authoring work scheduled later
6. Remote and Mobile Content Studio

Future features may be inserted before or after this list as priorities become
clear. Remote and Mobile Content Studio remains last among the currently known
items; this does not mean it must remain the final feature forever.

## RM — Remote and Mobile Content Studio

**Status:** Planned and deliberately deferred; last among currently known
roadmap work.

Establish a secure remote-authoring mode and responsive Godot Web/PWA client as
a lower-risk proof of concept that MMO Project can later reuse for hosted
services, browser/mobile exports, authentication, asset delivery, reconnect,
deployment, and touch-interface behavior.

Locked boundaries:

- Preserve the existing loopback-only local desktop workflow.
- Add remote access only through an explicitly configured authenticated HTTPS
  boundary.
- Keep PostgreSQL, validation, publication, runtime exports, and canonical asset
  mutations host-owned.
- Replace client-local absolute-path imports and direct asset-tree reads with
  network-safe upload and retrieval contracts.
- Prefer a responsive Godot Web/PWA proof before optional Android-native
  packaging.
- Do not treat authoring-host security as a substitute for MMO Project player,
  realtime, scaling, or anti-cheat requirements.

Planned phases:

- RM0 audits export compatibility, local-only assumptions, and the threat model.
- RM1 adds a secure remote authoring-host mode while preserving local mode.
- RM2 adds network-safe asset upload, retrieval, and preview contracts.
- RM3 adds responsive phone/tablet layouts and touch-capable graph workflows.
- RM4 delivers and verifies the Godot Web/PWA proof.
- RM5 optionally packages the same responsive client for native Android.

Exit condition:

> From a supported mobile browser, an authenticated maintainer can remotely
> load, create, edit, validate, preview, save, and publish representative item,
> NPC, dialogue, and quest content; upload an asset; recover safely from a
> connection interruption; and receive a deterministic concurrency conflict if
> another client changed the same aggregate.

Detailed scope, scheduling triggers, architecture guardrails, non-goals, and
acceptance criteria are documented in
[`REMOTE_MOBILE_CONTENT_STUDIO_PLAN.md`](REMOTE_MOBILE_CONTENT_STUDIO_PLAN.md).
