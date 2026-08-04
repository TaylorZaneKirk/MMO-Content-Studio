# T5 NPC Domain Audit

Status: T5A domain audit and lock.

Source repositories inspected:

- Content Studio: `/home/taylor/MMO Project/tools/MMO-Content-Studio` on `main`
- MMO Project: `/home/taylor/MMO Project` on `master`, read-only

## Executive Summary

The current MMO Project NPC implementation is placement-first. NPCs are authored
as `NpcSpawn` point objects in Tiled, compiled into generated chunk JSON under
`npc_spawns`, and mounted by `NpcRuntimeService` from immutable static content.
There is no shared reusable NPC definition catalog, database-backed NPC
definition table, or Content Studio NPC API yet.

T5 should introduce reusable NPC definitions in Content Studio while preserving
Tiled ownership of placement. The future runtime linkage should mirror the mob
handoff shape:

```text
Tiled NpcSpawn
    -> npc_definition_id
    -> published database-backed NPC definition
    -> immutable runtime NPC definition catalog embedded in static content
```

The current runtime already uses `npc_definition_id`, but only as a hard-coded
texture lookup key in `NpcRuntimeService.ResolveGeneratedNpcTexturePath`.
T5 should replace that hard-coded mapping with an exported catalog in a later
runtime integration slice.

## Source Documents

Primary source guide:

- MMO Project `docs/development/CONTENT_AUTHORING_GUIDE.md`
- Relevant section: `## Adding a New NPC`

Related design/runtime documents:

- MMO Project `docs/design/FOREGROUND_ACTIVITY_AND_INTERACTION_SPATIAL_BOUNDARY.md`
- MMO Project `docs/design/DIALOGUE_FOUNDATION_V1.md`
- MMO Project `docs/modernization/GAMEPLAY_SYSTEM_ROADMAP.md`
- MMO Project `prototype/importer/README.md`

The guide is authoritative about the current manual workflow and limitations:
there is not yet a shared NPC definition catalog, Tiled owns NPC placement, and
new NPC definition IDs must currently be wired into server code.

## Existing Manual NPC-Authoring Workflow

Source guide: MMO Project `docs/development/CONTENT_AUTHORING_GUIDE.md`,
section `## Adding a New NPC`.

Current documented steps:

1. Add or verify a visual asset under
   `prototype/client/assets/maps/objects/npcs/`.
2. Add the visual to `prototype/shared/maps/tiled/tilesets/NPCs.tsx` if it
   should be placeable visually in Tiled.
3. Extend
   `prototype/server/features/npcs/application/NpcRuntimeService.cs`
   `ResolveGeneratedNpcTexturePath` for the new `npc_definition_id`.
4. Open `prototype/shared/maps/tiled/regions/starter_region.tmj`.
5. Add a point object to the `NPC Spawns` layer.
6. Set the object type/class to `NpcSpawn`.
7. Give the object a stable spawn name such as `npc_bank_clerk_001`.
8. Set object properties:
   `npc_definition_id`, `facing`, and `movement_behavior`.
9. Optionally set current interaction properties such as
   `interaction_enabled`, `interaction_range_tiles`, `dialogue_id`, and
   `dialogue_greeting`.
10. Keep coordinates in Tiled; the importer maps point pixels to source tiles
    with `floor(pixel / 128)`.
11. Validate or import the region with `tools/maps/publish_region.py`.
12. Publish generated region content to PostgreSQL when the database should
    serve the map.
13. Run importer/server/Godot validation.
14. Manually verify the NPC in the connected Godot client.

Current files and assets involved:

- Source map: `prototype/shared/maps/tiled/regions/starter_region.tmj`
- Tiled tileset: `prototype/shared/maps/tiled/tilesets/NPCs.tsx`
- Generated chunks: `prototype/shared/maps/generated/starter_region/chunks/*.json`
- Dialogue catalog: `prototype/shared/dialogues/catalog.json`
- Importer: `prototype/importer/import_tiled_region.py`
- Runtime loader: `NpcRuntimeService`
- Client renderer: `StaticNpc` and `NpcController`

Required IDs and naming conventions:

- `NpcSpawn.object_name`: stable placement identity.
- `npc_definition_id`: currently stable reusable-ish identifier, but resolved
  by hard-coded server mapping instead of a catalog.
- Runtime actor ID: `mount_id:object_name`, for example
  `starter_region:npc_test_001`.
- `dialogue_id`: reference into `prototype/shared/dialogues/catalog.json`.

Documented validation status:

- Authoritative: Tiled placement remains the source for spawn coordinates and
  stable spawn names.
- Authoritative: `NpcSpawn` must live on the `NPC Spawns` object layer.
- Authoritative: `npc_definition_id` is the stable linkage candidate.
- Partially outdated: visual assets live in both map-object and actor-sprite
  conventions. Runtime rendering uses the `res://assets/actors/npcs/...` path
  emitted by `ResolveGeneratedNpcTexturePath`.
- Partially outdated: `dialogue_greeting` is preserved and loaded but the
  current dialogue flow uses `dialogue_id` and `DialogueSessionService`
  presentations.
- Obsolete after T5 runtime integration: hand-editing
  `ResolveGeneratedNpcTexturePath`.
- Undocumented in runtime: service references, shops, quest starters,
  schedules, portraits, emotes, and NPC combat.

Manual steps T5 should replace or automate:

- Writing hard-coded `npc_definition_id` to texture mappings in
  `NpcRuntimeService`.
- Repeating reusable identity, display, visual, movement, and interaction data
  on each Tiled spawn.
- Manually checking that `dialogue_id` exists in the dialogue catalog.
- Manually checking asset paths and directional sprite conventions.
- Manually validating publish readiness before map publication.

Workflow boundaries T5 should preserve:

- Tiled continues to own spawn placement, source coordinates, stable spawn
  object names, initial facing, and map-local placement decisions.
- Content Studio owns reusable NPC identity, display name, visual references,
  default movement profile, default interaction/dialogue capability, and
  publication state.
- MMO Project runtime composes published reusable definitions with Tiled
  placements during static-content publication or load.

## Current Runtime Flow

Current NPC flow from authored map to gameplay:

1. A maintainer places `NpcSpawn` point objects in the Tiled `NPC Spawns` layer.
2. `prototype/importer/import_tiled_region.py` `_read_npc_spawns` validates the
   object layer, requires `type == "NpcSpawn"`, requires a nonblank object
   name, converts Tiled pixel coordinates to source tile coordinates, preserves
   all custom properties, and writes each spawn into the owning generated
   chunk's `npc_spawns` array.
3. The generated chunk JSON is consumed by either
   `GeneratedFileWorldStaticContentSource` or `DatabaseWorldStaticContentSource`
   as raw chunk JSON within `WorldRegionChunkContent`.
4. `NpcRuntimeService.EnsureInitialized` iterates mounted generated chunks from
   `WorldChunkCatalog.Snapshot.Regions`.
5. `NpcRuntimeService.LoadNpcStatesFromRoot` reads `npc_spawns`, pulls
   `npc_definition_id`, `facing`, `movement_behavior`, interaction fields, and
   `dialogue_id` from the nested `properties` object, and creates
   `NpcRuntimeState`.
6. If `texture_path` is not present, `NpcRuntimeService` calls
   `ResolveGeneratedNpcTexturePath(npc_definition_id)`. Today only `test_npc`
   resolves. Missing texture path causes the NPC to be skipped.
7. Runtime actor ID is built from mounted static content:
   `mount_id:object_name`.
8. `WorldSnapshotService` includes NPC snapshots from
   `NpcRuntimeService.GetNpcsForMap`.
9. `WorldSnapshotPayloadMapper` sends each NPC as `WorldSnapshotNpcPayload`.
10. Godot `NpcController` instantiates `StaticNpc`, loads the texture path, and
    indexes interactable NPCs by world tile.
11. Godot `PlayerInteractionController` offers a `talk` context action when
    `interaction_enabled` is true and `npc_actor_id` is present.
12. Godot sends `npc_interaction_request` with the runtime NPC actor ID and
    command sequence.
13. `NpcInteractionHandler` delegates to `NpcInteractionService`.
14. `NpcInteractionService` validates the authoritative NPC snapshot, computes
    adjacent approach candidates, requests server-owned movement through
    `IInteractionSpatialPort`, and starts dialogue through
    `DialogueSessionService` once range is authoritative.
15. `DialogueSessionService` selects and presents nodes from
    `DialogueDefinitionCatalog`; the client never receives the full graph.

## Current Schema And Persistence

There are no NPC authoring tables in the current MMO Project schema.

Current relevant persistence:

- `world_regions`, `world_region_chunks`, and `world_region_mounts` store
  published static-content region manifests and raw chunk JSON.
- Generated chunks store `npc_spawns` in JSON.
- `prototype/shared/dialogues/catalog.json` stores dialogue definitions outside
  PostgreSQL.
- `item_*`, `mob_*`, and world-object tables do not own NPC definitions.

## Actor Taxonomy

Player:

- Actor ID is the character ID.
- Owns mutable movement, health, inventory, equipment, skills, combat state,
  and session authority.
- Can initiate foreground NPC interaction and dialogue.

NPC:

- Runtime actor ID is `mount_id:object_name`.
- `NpcRuntimeService` owns mounted spatial and presentation state.
- Exposes spatial snapshots through `NpcRuntimeSnapshot` and
  `NpcCombatActorRuntimeProvider`.
- Can be targeted for spatial approach and dialogue.
- Does not currently own combat stats, attack profiles, damage mutation,
  loot, rewards, factions, shops, schedules, or quest state.

Mob:

- Runtime identity is mounted enemy/spawn driven and linked to a reusable
  `mob_definition_id`.
- Has combat stats, health, attacks, aggression, leash, drops, defeat, and
  respawn behavior.
- Uses `MobDefinitionCatalog` from immutable static content.

Do not collapse NPCs and mobs. They share some spatial interfaces, but their
runtime contracts and gameplay ownership are different.

## Spawn Linkage

Current placement-to-definition linkage:

- Tiled object type/class: `NpcSpawn`
- Tiled object name: stable spawn identity
- Tiled property: `npc_definition_id`
- Runtime mapping: hard-coded
  `NpcRuntimeService.ResolveGeneratedNpcTexturePath`

Recommended future canonical linkage:

```text
NpcSpawn.properties.npc_definition_id
    -> published NPC definition
    -> exported npc_definition_catalog
    -> runtime composition
```

Tiled should retain only placement identity, coordinates, initial facing, and
placement-local overrides that already have an authoritative runtime seam.

## Movement

Current movement fields consumed by runtime:

- `movement_behavior`: nested property, supports `static` and `random_wander`.
- `home_tile_x` and `home_tile_y`: read only from top-level generated fields,
  not nested Tiled properties, and default to the spawn tile.
- `wander_radius`: read only from a top-level generated field, default `0`.
- `tick_interval_ms`: read only from top-level generated field, clamped to at
  least `600`; effective speed is `1000 / tick_interval_ms` tiles per second.
- `idle_chance`: read only from top-level generated field, clamped to `[0, 1]`.

Importer reality:

- `_read_npc_spawns` preserves custom properties but does not promote wander
  tuning fields into top-level runtime fields.
- The current guide correctly warns not to rely on those knobs until the
  importer/runtime boundary is expanded.

T5 ownership recommendation:

- Reusable definition owns default movement behavior and movement tuning.
- Tiled spawn owns home coordinate through placement.
- Initial facing remains placement-owned.
- Importer/runtime integration must explicitly compose defaults with placement.

## Interaction And Dialogue

Current interaction fields:

- `interaction_enabled`: nested Tiled property, default false.
- `interaction_range_tiles`: nested Tiled property, minimum runtime value 1.
- `dialogue_id`: nested Tiled property, reference to
  `DialogueDefinitionCatalog`.
- `dialogue_greeting`: nested Tiled property, loaded into runtime snapshot but
  no longer the primary dialogue flow.

Current interaction behavior:

- Client context action is always `talk`.
- Server message is `npc_interaction_request`.
- Server validates actor ID, life state, map membership, route availability,
  and interaction range.
- Dialogue starts through `DialogueSessionService.StartSession`.
- Dialogue source is `DialogueSourceRef(DialogueSourceKind.Npc, npc_id,
  actor_id)`.

Current dialogue model:

- `DialogueDefinitionCatalog` loads
  `prototype/shared/dialogues/catalog.json`.
- Nodes support `speaker_text`, `player_choice`, and `end`.
- Conditions exist as data, but only unconditional/default entry selection is
  currently eligible.
- The client sends only continue, choice, and close intent commands.

T5 boundary:

- T5 may author a default `dialogue_id` reference for a reusable NPC.
- T5 must not author dialogue graphs, quest effects, shops, or service menus.
- Dialogue authoring remains a future Dialogue/Quest Studio slice.

## Services, Shops, Banking, And Quests

The current runtime has no implemented NPC service dispatch in this path.
`DialogueSourceKind.Quest` and service-like concepts are reserved in design
documents, but NPC initiation is the only wired dialogue source.

T5 should not add publishable service references beyond the current `talk`
capability and optional `default_dialogue_id`. Any future shop, bank, trainer,
quest, or service reference needs a concrete runtime consumer and validation
contract before becoming publishable.

## Visual Model

Current runtime rendering:

- `WorldSnapshotNpcPayload.TexturePath` is a Godot resource path.
- `StaticNpc` loads the texture through `ResourceLoader`.
- Directional NPC sprite filenames follow the `Chars_*_<group>-F<frame>-<NSEW>.png`
  convention under `res://assets/actors/npcs/`.
- `StaticNpc` extracts the sprite group from a filename such as
  `Chars_139_200-F2-S.png`.
- Direction changes resolve sibling frame/direction textures. Missing frames
  fall back to frame 1, then to the supplied texture path.
- `VISUAL_DENSITY_SCRIPT.ART_RENDER_SCALE` applies client-side density scaling.
- Current `StaticNpc.anchor_offset` defaults to `Vector2(14, -3)`.

Tiled visual placement:

- `NPCs.tsx` references large map-object NPC source images under
  `prototype/client/assets/maps/objects/npcs/`.
- These are placement aids, not the runtime actor sprite paths consumed by
  `StaticNpc`.

T5 should persist explicit visual references instead of inferring from display
name. Minimum persisted model: `visual_texture_path`, source dimensions,
anchor offsets, render scale, and optional stable visual key if the runtime
keeps deriving directional sibling frames from the path.

## Networking

Snapshot payload:

- `WorldSnapshotNpcPayload`: `npc_id`, `display_name`, `texture_path`, `tile_x`,
  `tile_y`, `facing`, `current_state`, `world_x`, `world_y`,
  `interaction_enabled`, `interaction_range_tiles`, and `npc_actor_id`.

Motion payload:

- `NpcMotionUpdatePayload`: `npc_id`, `world_x`, `world_y`, `facing`, and
  `current_state`.

Interaction/dialogue messages:

- Client: `npc_interaction_request`
- Server: `npc_interaction_accepted`, `npc_interaction_failed`,
  `npc_interaction_canceled`, `dialogue_opened`, `dialogue_node_presented`,
  `dialogue_closed`, `dialogue_command_failed`

The current client expects the snapshot surface to remain stable. T5 runtime
integration should preserve payload compatibility while changing how the server
resolves reusable NPC data.

## Publication Concerns

Publication should mean an NPC definition is eligible for export into the
runtime NPC definition catalog. Draft definitions must stay available in Content
Studio catalogs but unavailable to MMO Project runtime static-content imports.

Recommended T5 publication behavior:

- Draft save: persist incomplete reusable definitions for authoring.
- Publish: require complete identity, valid visual asset, supported movement
  defaults, supported interaction capability, and valid dialogue reference when
  dialogue is enabled.
- Disable: prevent export and prevent future spawn composition, while leaving
  existing saved data intact.
- Delete: allow only for disabled definitions with no known checked-in
  generated/Tiled references that the host can validate.
- Rename: display name may change; `npc_definition_id` must not.
- Active runtime instances: no hot reload in T5; changes reach runtime after
  export, map publication/import, and server restart or existing static-content
  reload mechanism.

## Identified Gaps

- No reusable NPC definition catalog exists.
- No NPC authoring tables exist.
- `npc_definition_id` is hard-coded to texture mapping in server code.
- Tiled spawn properties currently duplicate definition-like data.
- Dialogue references are manually validated.
- NPC visual paths span two conventions: Tiled placement images and runtime
  actor-direction sprites.
- Random-wander tuning fields are read by runtime but not promoted by importer.
- No service, shop, bank, trainer, quest, schedule, emote, portrait, or NPC
  combat runtime consumer is present.
- No Content Studio NPC workspace, routes, schema-health provider, repository,
  options provider, or preview/apply lifecycle exists yet.

## Evidence Index

MMO Project:

- `docs/development/CONTENT_AUTHORING_GUIDE.md`: current manual NPC workflow.
- `prototype/importer/import_tiled_region.py`: `_read_npc_spawns`.
- `prototype/shared/maps/tiled/regions/starter_region.tmj`: `NPC Spawns`
  layer and `npc_test_001`.
- `prototype/shared/maps/generated/starter_region/chunks/chunk_1_1.json`:
  generated `npc_spawns` payload.
- `prototype/server/features/npcs/application/NpcRuntimeService.cs`:
  `EnsureInitialized`, `LoadNpcStatesFromRoot`,
  `ResolveGeneratedNpcTexturePath`, `NpcRuntimeSnapshot`.
- `prototype/server/features/npcs/application/NpcInteractionService.cs`:
  `StartAsync`, `TryCompletePendingAsync`, `CreateDialogue`.
- `prototype/server/features/dialogue/application/DialogueDefinitionCatalog.cs`:
  `LoadDefinitions`, `ValidateDefinition`, `DialogueSourceKind`.
- `prototype/server/features/dialogue/application/DialogueSessionService.cs`:
  `StartSession`, `Continue`, `Choose`, `Close`.
- `prototype/server/features/world/application/WorldSnapshotResult.cs`:
  `WorldSnapshotNpcResult`.
- `prototype/server/features/world/protocol/WorldSnapshotPayload.cs`:
  `WorldSnapshotNpcPayload`.
- `prototype/server/features/world/host/WorldSnapshotPayloadMapper.cs`:
  NPC payload mapping.
- `prototype/server/features/activities/application/InteractionSpatialPort.cs`:
  NPC spatial adapter.
- `prototype/server/features/combat/application/CombatActorIdentity.cs`:
  `CombatActorType.Npc`.
- `prototype/server/features/combat/application/CombatActorRuntimeProvider.cs`:
  `NpcCombatActorRuntimeProvider`.
- `prototype/client/screens/game/controllers/npc_controller.gd`:
  snapshot rendering and tile indexing.
- `prototype/client/actors/npc/static_npc.gd`: directional sprite resolution.
- `prototype/client/screens/game/controllers/player_interaction_controller.gd`:
  `talk` action selection.
- `prototype/client/network/session_client.gd`: `send_npc_interaction_request`.
- `prototype/client/screens/game/controllers/dialogue_panel_controller.gd`:
  modal dialogue presentation.

Content Studio:

- `docs/ARCHITECTURE.md`: reusable NPC ownership already named as the intended
  Content Studio boundary.
- `docs/ROADMAP.md`: T5 is the planned minimal NPC authoring slice.
- `README.md`: current architecture lists NPC previews as planned Godot-owned
  surfaces but has no implemented NPC route/workspace.
