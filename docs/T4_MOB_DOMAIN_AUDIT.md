# T4 Mob Domain Audit

## Scope

This audit locks the current MMO Project mob boundary before implementation work
begins in Content Studio. It is intentionally read-only for the runtime repo: no
runtime files, generated map data, migrations, or database state were changed.

The target authoring domain is reusable **mob definitions**. Placement-level
spawn data remains owned by Tiled and the generated/static-content pipeline.

## Repositories and Branches

- Content Studio repository: `TaylorZaneKirk/MMO-Content-Studio`
- Runtime repository: `TaylorZaneKirk/MMO-Project`
- Content Studio branch inspected: `main`
- Runtime branch inspected: `master`

The runtime repo had unrelated local changes at audit time. They were treated as
existing user work and left untouched.

## Current Runtime Model

The current runtime already has a reusable mob-definition catalog:

- `prototype/shared/maps/mobs/catalog.json`
- `prototype/importer/import_tiled_region.py`
- `prototype/shared/maps/generated/starter_region/region.json`
- `prototype/server/features/static_content/application/WorldStaticContentSnapshot.cs`
- `prototype/server/features/static_content/application/GeneratedFileWorldStaticContentSource.cs`
- `prototype/server/features/static_content/application/DatabaseWorldStaticContentSource.cs`
- `prototype/server/features/enemies/application/EnemyRuntimeService.cs`

The catalog is embedded into each generated region manifest as
`mob_definition_catalog`. Tiled `EnemySpawn` objects refer to entries by
`mob_definition_id`. Runtime enemies are constructed from:

```text
EnemySpawn placement + MobDefinition catalog entry = EnemyRuntimeState
```

The runtime wire/client path still uses the word `enemy` for interest and
snapshot messages, while combat taxonomy uses `CombatActorType.Mob`. Content
Studio should use **Mobs** for the authoring workspace and preserve `enemy_*`
terms only when referring to existing runtime protocol or placement artifacts.

## Existing Manual Mob-Authoring Workflow

Primary source guide:

- `docs/development/CONTENT_AUTHORING_GUIDE.md`
- Relevant sections:
  - `Important Paths`
  - `Adding a New Mob`
  - `Publication and Validation Checklist`
  - `Known Temporary Boundaries`

The guide is current enough to be authoritative for the maintainer workflow. It
states that mobs use a shared JSON definition catalog plus Tiled-authored
`EnemySpawn` objects, and that runtime enemies are server-owned and instantiated
from mounted authored spawns at server startup.

### Documented Process and Current Status

| Guide step | Current status | Verification |
| --- | --- | --- |
| Add or verify the mob visual asset under `prototype/client/assets/maps/objects/mobs/`; use PNG dimensions and 4x art with `visual_render_scale = 0.25`. | Still authoritative. | `prototype/shared/maps/mobs/catalog.json` stores `visual_texture_path`, source dimensions, anchor offsets, and render scale. `enemy_controller.gd` applies the texture, offsets, and scale directly. |
| Add the asset to `prototype/shared/maps/tiled/tilesets/mobs.tsx` if authors need visual placement in Tiled. | Still authoritative for map-authoring ergonomics. | The checked-in Tiled region uses an `Enemy Spawns` object layer, and the importer reads objects independent of tileset display art. |
| Add a reusable definition to `prototype/shared/maps/mobs/catalog.json`. | Still authoritative today; T4 should replace this manual JSON edit with database-backed definition authoring. | `import_tiled_region.py` loads this catalog, validates it, embeds it as `mob_definition_catalog`, and both static-content sources read that shape. |
| Include all required identity, visual, footprint, health, combat, movement, faction/aggression, drop, level, and bonus fields. | Still authoritative. | The importer and `GeneratedFileWorldStaticContentSource`/`DatabaseWorldStaticContentSource` read these fields into `MobDefinition`; `MobDefinitionValidation` and combat services consume them. |
| Restrict `attack_type` to `melee`; restrict melee `accuracy_style` to `thrust`, `slash`, or `crush`; store `attack_speed_units` as 600 ms units. | Still authoritative. | Importer validation and `CombatAttackProfileValidation` enforce the same narrow profile shape and speed-unit range. |
| Use passive aggression zeros unless proactive hostile mob targeting is enabled; proactive mobs require faction, detection radius, scan interval, and candidate cap. | Still authoritative. | `EnemyCombatEngagementService` only runs acquisition scans for alive idle mobs with proactive targeting enabled and valid faction/detection settings. |
| Drops must reference runtime-enabled item definitions; mob-caused defeats without eligible player contribution create public ownerless ground items. | Partially outdated. | The drop shape is authoritative, but current importer/startup validation only proves nonempty item ids and positive stacks. `CombatDefeatService` creates private drops for eligible player reward owners and public drops otherwise; database ground-item insertion requires runtime-enabled items. T4 should validate published drop references before export. |
| Add directed faction dispositions under `faction_dispositions`; missing directions are neutral. | Still authoritative. | The catalog reader builds `MobFactionDispositionKey` pairs, and `MobFactionCombatRelationshipProvider`/combat permission flow treats relationships directionally. |
| Open `prototype/shared/maps/tiled/regions/starter_region.tmj`. | Still authoritative for the starter region. | The `.tmj` file exists and contains the authored `training_slime_001` enemy spawn. A `.tmx` copy also exists, but the publish command requires `.tmj`. |
| Add a point object on the `Enemy Spawns` layer with object type/class `EnemySpawn` and a stable object Name as `spawn_id`. | Still authoritative. | Importer validation requires object layer `Enemy Spawns`, type `EnemySpawn`, point objects, unique nonempty names, and emits `spawn_id`. |
| Add `mob_definition_id`, `spawn_behavior`, optional `facing`, and `leash_radius_tiles`. | Still authoritative with a restriction. | `mob_definition_id` is required and must match the catalog; `spawn_behavior` defaults to and currently only supports `static`; `facing` is normalized; `leash_radius_tiles` is nonnegative and placement-owned. |
| Keep point-to-tile conversion as `floor(pixel / 128)` and keep footprint in bounds. | Still authoritative. | Importer constants use 128px Tiled source tiles and validate footprint bounds before assigning the spawn to a `17x9` generated chunk. |
| Run `python3 tools/maps/publish_region.py --source prototype/shared/maps/tiled/regions/starter_region.tmj --validate-only`. | Still authoritative and non-destructive. | `publish_region.py` supports `--validate-only`, compiles to a temp bundle, validates, and does not replace output or publish. |
| Run the same command with `--import-only` to replace generated output. | Still authoritative, but outside this T4 audit. | The publisher compares generated JSON and replaces checked-in output only when changed. T4 Phase 0 did not run import-only because it would modify runtime generated files. |
| Publish to PostgreSQL when the active database should serve the new region. | Still authoritative for database-backed static content, but optional per environment. | `DatabaseWorldStaticContentSource` reads active `world_regions`, `world_region_mounts`, and `world_region_chunks`; credentials are supplied through environment/local env files. |
| Restart the server after generated/static-content changes. | Still authoritative. | `Program.cs` initializes `EnemyRuntimeService` during startup, and `EnemyRuntimeService.Initialize` loads mounted spawns once from `WorldChunkCatalog.Snapshot`. |
| Validate importer tests, server build/tests, Godot validation, and manual client behavior. | Still authoritative. | These checks cover importer shape, server startup/runtime combat behavior, and client rendering/interaction. |

### Files, Tables, Assets, and IDs Involved

- Source guide: `docs/development/CONTENT_AUTHORING_GUIDE.md`
- Reusable definition catalog: `prototype/shared/maps/mobs/catalog.json`
- Mob art directory: `prototype/client/assets/maps/objects/mobs/`
- Tiled mob tileset: `prototype/shared/maps/tiled/tilesets/mobs.tsx`
- Tiled source region: `prototype/shared/maps/tiled/regions/starter_region.tmj`
- Generated region manifest: `prototype/shared/maps/generated/starter_region/region.json`
- Generated chunk spawns:
  `prototype/shared/maps/generated/starter_region/chunks/chunk_1_2.json`
- Publish command: `tools/maps/publish_region.py`
- Publish profiles: `tools/maps/region_publish_profiles.json`
- Database publication tables: `world_regions`, `world_region_mounts`,
  `world_region_chunks`
- Drop item table: `item_definitions`
- Ground-item output table: `ground_items`

Stable identifiers and naming:

- Reusable catalog id: `definition_id`
- Tiled spawn link property: `mob_definition_id`
- Tiled object class/type: `EnemySpawn`
- Tiled object Name: stable `spawn_id`, for example `training_slime_001`
- Runtime enemy id: `<mount_id>:<spawn_id>`
- Existing catalog examples: `training_goblin`, `slime`, `training_guard`
- Existing placement example: `training_slime_001`

### Manual Work T4 Should Replace or Preserve

Replace in Content Studio:

- direct JSON editing of `prototype/shared/maps/mobs/catalog.json`
- hand-maintaining required fields and value ranges
- hand-ordering guaranteed drops
- manually remembering proactive faction/aggression invariants
- manually checking visual source dimensions, render scale, and canonical paths
- manually checking item references in drops before publication
- manually constructing exact logical change summaries

Preserve outside Content Studio:

- placing `EnemySpawn` objects in Tiled
- choosing map coordinates and point-object placement
- keeping spawn id, facing, spawn behavior, and leash radius placement-owned
- running import/publish workflows when generated/static map content changes
- requiring server restart/static-content reload until a runtime hot-reload seam
  exists

Disagreements or gaps between guide and implementation:

- The guide says drops must reference runtime-enabled items. Current catalog
  importer validation does not check item publication; the ground-item database
  path enforces it later. T4 should close this at mob publish/export time.
- The guide's mob workflow is JSON-first. Current runtime also supports
  database-published region manifests, so T4 should target the shared
  `mob_definition_catalog` shape rather than assuming only generated files.
- The guide intentionally excludes broader aggro, respawn, owned player loot,
  rewards, faction systems, NPC combat, and PvP. The audit confirms those should
  remain out of the first T4 aggregate.

## Inspected Runtime Evidence

### Catalog and Importer

- `prototype/shared/maps/mobs/catalog.json`
  - Existing definitions: `training_goblin`, `slime`, `training_guard`.
  - Definition fields include identity, visuals, footprint, health, melee attack,
    combat levels, combat bonuses, movement speed, optional faction/aggression,
    guaranteed drops, and faction dispositions.
- `prototype/importer/import_tiled_region.py`
  - Requires `Enemy Spawns` and `NPC Spawns` layers.
  - Loads `prototype/shared/maps/mobs/catalog.json`.
  - Validates unique `definition_id`, required visual/combat/stat fields, melee
    attack type, melee accuracy styles, positive movement speed, nonnegative
    proactive targeting settings, drops, and faction dispositions.
  - Reads Tiled objects of type `EnemySpawn`.
  - Requires each spawn's `mob_definition_id` to exist in the catalog.
  - Writes generated `enemy_spawns` with position, facing, `spawn_behavior`, and
    `leash_radius_tiles`.
- `prototype/importer/README.md`
  - Documents the starter slime as an `EnemySpawn` using `mob_definition_id =
    slime`.
  - Confirms combat behavior is not implemented in the importer.
  - Confirms `attack_speed_units` are 600 ms combat units.

### Static Content and Startup

- `WorldStaticContentSnapshot.cs`
  - Defines `MobDefinitionCatalog`, `MobDefinition`, `MobDropDefinition`,
    `MobFactionDisposition`, and `EnemySpawn`.
  - Region content owns `MobDefinitionCatalog`; chunk content owns
    `EnemySpawns`.
- `GeneratedFileWorldStaticContentSource.cs`
  - Reads `mob_definition_catalog` from generated `region.json`.
  - Reads `enemy_spawns` from generated chunk JSON.
  - Validates every spawn against the loaded catalog.
- `DatabaseWorldStaticContentSource.cs`
  - Reads the same `mob_definition_catalog` from published `world_regions`
    manifests.
  - Reads the same `enemy_spawns` from published `world_region_chunks`.
  - This is the main future integration seam for published database-backed static
    content.
- `CombatContentStartupValidator.cs`
  - Includes mob definitions from `WorldStaticContentSnapshot` in combat startup
    validation.

### Runtime Enemy and Combat Behavior

- `EnemyRuntimeService.cs`
  - Initializes in-memory enemies from every region chunk `EnemySpawn`.
  - Runtime enemy id is `"{mountId}:{spawnId}"`.
  - Home position and leash radius come from placement.
  - Stats, visuals, movement speed, attack profile, faction, and drops come from
    definition.
  - `ApplyDamage` sets `Alive = false` and `CurrentState = defeated` at zero
    health, starts the defeated-hold timer, then schedules respawn.
  - Respawn restores the runtime instance from its spawn and definition data.
  - Mob movement occurs through pursuit/return-home routes. There is no idle
    wandering behavior for mobs.
- `MobDefinitionValidation.cs`
  - Validates ids, attack profile shape, skill levels, footprint, movement speed,
    faction/proactive targeting consistency, and drops.
- `EnemyCombatEngagementService.cs`
  - Handles retaliation, pursuit, leash checks, attack range checks, and proactive
    mob-vs-mob acquisition.
  - Proactive scans require the definition to enable hostile-mob targeting and
    have faction/detection settings.
- `CombatActorIdentity.cs`
  - Combat actor type includes `Mob`.
- `CombatProtocolActorTypeMapper.cs`
  - Maps runtime mobs to wire value `enemy`.
- `CombatDamageApplication.cs`
  - `MobCombatStateMutator` applies damage through `EnemyRuntimeService`.
- `CombatDefeatService.cs`
  - Resolves rewards and creates drop events from
    `EnemyRuntimeService.GetMobDrops`.
  - Current drops are guaranteed entries with `item_id` and `stack_count`.

### Client Presentation

- `WorldSnapshotPayload.cs`
  - `WorldSnapshotEnemyPayload` includes identity, visual texture/path dimensions,
    footprint, movement speed, leash, runtime location, health, alive state, and
    revision.
- `WorldSnapshotPayloadMapper.cs`
  - Maps `WorldSnapshotEnemyResult` into snapshot and state-update payloads.
- `enemy_controller.gd`
  - Renders enemy sprites from `texture_path`.
  - Applies `visual_anchor_offset_x`, `visual_anchor_offset_y`, and
    `visual_render_scale`.
  - Indexes clickable footprint tiles.
  - Uses existing wire payload field names.
- `combat_feedback_layer.gd`
  - Shows health bars and hit splats against resolved actor positions.

### Database Schema

Current runtime SQL does not define mob-authoring tables. Relevant existing
tables are:

- `item_definitions`
- `item_combat_profiles`
- `item_combat_bonuses`
- `ground_items`
- `world_regions`
- `world_region_mounts`
- `world_region_chunks`

`prototype/sql/README.md` still lists mobs as outside the durable database schema.
T4 should add additive mob-authoring tables in Content Studio integration
artifacts first, then runtime integration can consume published definitions.

## Current Content Studio Pattern

The current Content Studio host is feature-first:

- `host/Features/AuthoringFeatureExtensions.cs`
- `host/Features/Items/*`
- `host/Features/Consumables/*`
- `host/Features/Equipment/*`
- `host/Features/HandEquipment/*`
- `host/Contracts/*`
- `host/Persistence/*`
- `host/Services/*`
- `host/Health/*`

Every implemented workspace owns:

- contracts
- feature module registration and routes
- schema requirements
- catalog section provider
- repository
- service
- validator
- Godot editor
- `AuthoringHostClient` methods/signals
- source contract tests

T4 should follow that pattern instead of adding mob-specific logic to central
catalog or system-health services.

## Required Audit Answers

1. Current mob definition source: reusable data comes from
   `prototype/shared/maps/mobs/catalog.json`, is imported into generated
   `mob_definition_catalog`, and is read by generated-file or database static
   content sources before `EnemyRuntimeService` constructs runtime enemies.
2. Mob versus NPC distinction: players are durable account characters with skills,
   inventory, equipment, and mutable status; NPCs are non-combat map actors loaded
   from `npc_spawns` with static/random-wander behavior; mobs are combat actors
   using `EnemyRuntimeService`, runtime health/revision, combat profiles, drops,
   aggro/leash, and `enemy_*` protocol payloads.
3. Spawn-to-definition linkage: Tiled `EnemySpawn` point objects link to reusable
   definitions by `mob_definition_id`. This fits the future model exactly.
4. Combat stats: current mobs require `max_health`, `attack_type`,
   `accuracy_style`, min/max range tiles, `attack_speed_units`, attack/strength/
   defence levels, combat bonuses, movement speed, footprint, and optional faction
   aggression fields. No unconsumed stats should be added.
5. Attack model: one primary melee profile is currently consumed. There is no
   multiple attack, weight, special attack, projectile, cooldown, animation ref,
   condition, or status-effect model for mobs.
6. Movement and aggression: mobs do not idle-wander. They retaliate, pursue,
   scan for hostile mobs when configured, pathfind to attack range, respect
   placement-owned leash, return home, and can cross chunk membership while
   motion updates and interest deltas are projected.
7. Death and respawn: zero health sets `alive=false` and `current_state=defeated`.
   Defeated mobs remain briefly visible/non-attackable, do not move, leave
   interest after the defeated hold, and respawn from placement plus definition
   data after the configured delay.
8. Drops: current drops are embedded guaranteed per-mob entries of `item_id` and
   `stack_count`; `CombatDefeatService` writes private drops for eligible player
   reward owners and public drops otherwise through the ground-item system.
9. Factions and disposition: minimal directed mob faction dispositions exist for
   mob-vs-mob hostility. Player faction/reputation, NPC combat disposition, and
   broad group assistance are absent.
10. Visual model: mobs use explicit texture paths, source dimensions, anchor
   offsets, render scale, and footprint dimensions. Current client rendering uses
   a single sprite texture per mob snapshot, not directional animation states.
11. Publication and live-reference safety: current JSON has no draft/publish
   distinction. T4 should add publication state, export only published
   definitions, block disable when known active spawn references require the
   definition, and require server restart/static reload until hot reload exists.
12. Concurrency boundary: one aggregate `updated_at_utc` token on the mob
   definition is sufficient; child collections should be complete replacement
   sets under the root lock.
13. Schema ownership: reuse item tables only for drop references and static
   region tables only for publication/export integration. Add normalized mob
   tables for definitions, primary combat profile, combat bonuses, drops,
   factions, and dispositions.
14. Content Studio workspace: use a dedicated Mobs workspace with catalog,
   identity/visuals, stats/combat, behavior/faction, drops, preview, validation,
   and exact logical changes. One scrollable editor with compact sections is
   enough for Phase 1; tabs can be added later if form density warrants them.
15. Preview: render the configured sprite with source/anchor/scale/footprint,
   show attack timing/range, movement/aggression summary, faction relationships,
   and guaranteed drop summary. Do not build a combat simulator.

## Locked Domain Decisions

### Identity

- Author stable mob definition ids as explicit lower-snake-case ids.
- Persist ids as `mob_definition_id`.
- Export/runtime catalog fields continue to use `definition_id` inside
  `mob_definition_catalog` for compatibility.
- Do not allow renaming an existing stable id as an edit operation.

### Ownership Boundary

- Content Studio owns reusable mob definitions.
- Tiled owns `EnemySpawn` placement, including spawn id, position, facing,
  `spawn_behavior`, and `leash_radius_tiles`.
- Generated or database-published static content remains the bridge between the
  two.
- T4 does not author individual runtime enemy instances.

### Stats and Combat

- Initial T4 supports one primary melee attack profile per mob definition.
- Supported `attack_type`: `melee`.
- Supported melee accuracy styles: `thrust`, `slash`, `crush`.
- Store range in logical tiles as `minimum_range_tiles` and
  `maximum_range_tiles`.
- Store speed as `attack_speed_units`; one unit is 600 milliseconds.
- Store authored mob combat levels and combat bonuses on the mob aggregate.
- Do not store arbitrary formulas, scripts, or milliseconds.

### Movement and Aggression

- Store `movement_speed_tiles_per_second` on the mob definition.
- Store optional faction/proactive hostile-mob targeting on the definition.
- Keep leash radius and home position on spawn placement.
- Do not add idle wander or patrol routes in the first T4 authoring slice because
  current mobs do not consume those semantics. Preserve the runtime-defaulted
  defeated-hold and respawn timing columns until a later workspace slice exposes
  explicit controls.

### Death, Respawn, and Drops

- Current mob defeat enters a defeated hold, leaves interest, and respawns from
  spawn/definition data after the configured delay.
- Do not author respawn timers in T4 until the runtime has a mob life-generation
  and respawn model.
- Initial drops are guaranteed rows of `item_id` and `stack_count`.
- Weighted drop tables, chances, rare tables, and conditional drops are deferred.
- Published drop item ids must reference published runtime-enabled item
  definitions before runtime integration consumes them.

### Factions

- Store a small explicit mob faction catalog and pairwise dispositions.
- Supported dispositions: `Neutral`, `Hostile`.
- Avoid reputation, alignment, scriptable aggression, or player faction systems in
  T4.

### Visuals

- Store explicit `visual_texture_path`, source dimensions, anchor offsets, render
  scale, and footprint dimensions.
- Do not derive mob visuals from display name.
- Preview should render the configured sprite using the same offset/scale fields
  the game client consumes.
- Directional animation states are deferred unless a runtime contract is added.

### Publication and Runtime Consumption

- Draft mob definitions must not be exported to the active runtime catalog.
- Publish requires a complete valid aggregate.
- Disable should be blocked while known published/static spawn references still
  require the definition, unless a later explicit replacement workflow is added.
- The current server loads static content at startup. Hot reload is deferred.

## Risks and Gaps

- Current generated regions embed `mob_definition_catalog` in manifests. Runtime
  integration must decide whether the map publisher injects published database
  mob definitions at publish time or the server resolves them from a separate
  database table at startup.
- Tiled importer currently validates against the JSON catalog file. Replacing
  that source requires a deterministic exported catalog or importer/database
  integration.
- Disable/reference protection needs a source of known spawn references. Tiled
  source files are outside the authoring database today.
- Current mob drops are guaranteed only. Designers may ask for random drop
  tables, but that is not supported by runtime evidence yet.
- Current runtime has mob respawn scheduling. T4 keeps the lifecycle columns as
  runtime defaults for now and does not move spawn placement into Content Studio.
- The client snapshot path uses `enemy_*` field names. API docs and code should
  explain the naming split rather than trying to rename runtime protocol in T4.

## Conclusion

T4 should proceed with a database-backed mob-definition aggregate and a Godot
Mobs workspace. The first implementation slice should not move spawn placement
out of Tiled and should not introduce runtime-unsupported behavior. The safest
path is to model the current `mob_definition_catalog` exactly, add authoring
guards and preview/apply workflows, and defer runtime consumption until the
published catalog/export seam is implemented deliberately.
