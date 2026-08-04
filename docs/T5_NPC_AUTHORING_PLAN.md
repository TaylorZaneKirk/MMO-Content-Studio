# T5 NPC Authoring Plan

Status: T5C NPC repository, validation, and API implemented; Godot workspace,
runtime handoff, and verification remain pending. T5B added the additive schema
handoff, host contracts, domain rules, registry/options seam, and schema-health
provider. T5C adds repository persistence, validation, options, catalog/list/load,
preview, draft save, publish, disable, delete, preview signatures, optimistic
concurrency, reload verification, and reference diagnostics.

## Locked Domain Model

T5 introduces reusable NPC definitions owned by Content Studio. Tiled continues
to own placed NPC spawn instances.

Canonical linkage:

```text
NpcSpawn.object_name       -> stable placement identity
NpcSpawn.npc_definition_id -> reusable NPC definition ID
Published NPC definition   -> exported runtime NPC definition catalog
```

Locked definition ID format:

- `npc_definition_id`
- lowercase snake-case text
- starts with a lowercase letter
- contains lowercase letters, digits, and underscores
- stable after publication
- examples: `test_npc`, `bank_clerk`, `fisherman_001`

## Proposed Aggregate

`NpcDefinition`:

- `npc_definition_id`
- `display_name`
- `publication_state`: `Draft`, `Published`, or `Disabled`
- `visual_texture_path`
- `source_width`
- `source_height`
- `visual_anchor_offset_x`
- `visual_anchor_offset_y`
- `visual_render_scale`
- `footprint_width_tiles`
- `footprint_height_tiles`
- `movement_behavior`: `static` or `random_wander`
- `wander_radius_tiles`
- `tick_interval_ms`
- `idle_chance`
- `interaction_enabled`
- `interaction_range_tiles`
- `default_interaction`: initially `talk`
- `default_dialogue_id`
- `notes`
- `created_at_utc`
- `updated_at_utc`

The aggregate deliberately excludes:

- map ID, region ID, chunk ID, spawn ID, source coordinates, runtime mount
  coordinates, and patrol origin
- shops, banking, training services, quest starts, schedules, emotes, cutscenes,
  portraits, arbitrary scripts, NPC combat stats, and faction hostility

## Proposed Schema

T5B added the additive MMO Project handoff migration at
`integrations/mmo-project/prototype/sql/024_npc_authoring_schema.sql`.
T5C implements Content Studio repository persistence against that one root table.

Proposed tables:

```sql
CREATE TABLE npc_definitions (
    npc_definition_id TEXT PRIMARY KEY,
    display_name TEXT NOT NULL,
    publication_state TEXT NOT NULL,
    visual_texture_path TEXT NOT NULL,
    source_width INTEGER NOT NULL,
    source_height INTEGER NOT NULL,
    visual_anchor_offset_x REAL NOT NULL,
    visual_anchor_offset_y REAL NOT NULL,
    visual_render_scale REAL NOT NULL,
    footprint_width_tiles INTEGER NOT NULL,
    footprint_height_tiles INTEGER NOT NULL,
    movement_behavior TEXT NOT NULL,
    wander_radius_tiles INTEGER NOT NULL,
    tick_interval_ms INTEGER NOT NULL,
    idle_chance REAL NOT NULL,
    interaction_enabled BOOLEAN NOT NULL,
    interaction_range_tiles INTEGER NOT NULL,
    default_interaction TEXT NOT NULL,
    default_dialogue_id TEXT NULL,
    notes TEXT NULL,
    created_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
```

No child collection is required for the first publishable NPC model. A future
`npc_interaction_capabilities` child table may be added when runtime supports
more than one action per NPC. Starting with one row-free `talk` capability keeps
T5 aligned with the current client and server.

Validation constraints:

- `publication_state IN ('Draft', 'Published', 'Disabled')`
- ID format check
- nonblank display name and visual path
- source dimensions positive
- render scale positive and finite
- footprint is `1x1` for initial publishable runtime compatibility
- movement behavior is `static` or `random_wander`
- static movement uses zero `wander_radius_tiles`
- random-wander movement requires positive `wander_radius_tiles`
- `tick_interval_ms >= 600`
- `idle_chance BETWEEN 0 AND 1`
- `interaction_range_tiles >= 1`
- `default_interaction = 'talk'`
- `default_dialogue_id` required when `interaction_enabled = true`

The rule is simple: notes is authoring-only metadata. It should be preserved by
Content Studio
repository/API round trips, but it is omitted from the future runtime NPC
catalog export.

Dialogue-reference validation uses the configured file-backed MMO Project
dialogue catalog when it can be resolved from `game_client_assets`; otherwise
validation is syntax-only and reports that limitation. There is still no
dialogue database table.

## API Routes

T5C adds a feature-owned `/api/v1/npcs` route family:

- `GET /api/v1/npcs/options`
- `GET /api/v1/npcs`
- `GET /api/v1/npcs/{npcDefinitionId}`
- `POST /api/v1/npcs/{npcDefinitionId}/preview`
- `PUT /api/v1/npcs/{npcDefinitionId}/draft`
- `POST /api/v1/npcs/{npcDefinitionId}/publish`
- `POST /api/v1/npcs/{npcDefinitionId}/disable`
- `POST /api/v1/npcs/{npcDefinitionId}/delete`

Mutations must use:

- complete aggregate payloads
- `expected_version` from root `updated_at`
- server-generated `preview_signature`
- reload-and-verify after commit
- friendly validation envelopes consistent with existing item and mob routes

## Validation Rules

Draft validation:

- allow incomplete definitions to save as Draft
- still report missing fields and invalid references as warnings/errors in the
  preview response
- block only malformed IDs, unsupported enum values, unsafe paths, and
  impossible numeric values

Publish validation:

- require a valid visual PNG that resolves under configured game assets
- require the current runtime-supported directional actor sprite convention
- require `interaction_enabled` plus a valid `default_dialogue_id` for
  talk-capable NPCs
- reject service references, quest references, shop/bank/trainer roles,
  arbitrary scripts, combat stats, schedules, and unsupported movement modes
- require definitions referenced by checked-in Tiled/generated spawns to remain
  published unless the operation is explicitly disabling future export and the
  host can prove no live reference risk

## Feature Layout

Content Studio host files introduced in T5B:

- `host/Contracts/NpcContracts.cs`
- `host/Features/Npcs/NpcAuthoringFeature.cs`
- `host/Features/Npcs/NpcSchemaRequirements.cs`
- `host/Features/Npcs/NpcCatalogSectionProvider.cs`
- `host/Services/NpcAuthoringRegistry.cs`
- `host/Services/NpcDomainRules.cs`
- tests under `tests/host/MMO.ContentStudio.AuthoringHost.Tests/`

Content Studio host files introduced in T5C:

- `host/Persistence/NpcRepository.cs`
- `host/Services/NpcAuthoringService.cs`
- `host/Services/NpcDefinitionValidator.cs`
- `host/Services/NpcDialogueReferenceProvider.cs`

The implementation should follow the existing Mobs and unified Items patterns,
but must not copy mob combat/drop concerns into NPCs.

## Godot Workspace Scope

Add a top-level **NPCs** workspace after Mobs or before Environment.

Recommended layout:

- catalog/search/create
- identity and publication state
- visuals and footprint
- movement
- interaction and dialogue reference
- preview and validation/apply panel

One screen is manageable for T5 because the aggregate is small. Use scrollable
content for the form and preview/validation panel from the start.

## Preview Scope

T5 preview should include:

- runtime-resolved directional sprite preview
- facing selector and frame selector
- footprint tile overlay
- movement summary
- interaction range summary
- dialogue-reference status
- exact logical changes and validation messages

Do not build a dialogue graph editor, quest simulator, shop preview, combat
simulator, or cutscene preview in T5.

## Runtime Handoff

The runtime handoff should be a later phase, not part of T5C.

Target shape:

- Content Studio writes `npc_definitions`.
- `MapPublisher export-npc-catalog` exports only `Published` rows to a checked
  in or generated `npc_definition_catalog` artifact.
- The Tiled importer validates `NpcSpawn.npc_definition_id` against the catalog.
- Generated region manifests embed the NPC definition catalog like the mob and
  world-object catalogs.
- `GeneratedFileWorldStaticContentSource` and
  `DatabaseWorldStaticContentSource` read the catalog.
- `NpcRuntimeService` composes each `NpcSpawn` with its definition instead of
  calling `ResolveGeneratedNpcTexturePath`.
- Snapshot payloads remain backward-compatible.

## Quest-Foundation Handoff

T5 should prepare for but not implement Quest Studio.

Locked rule:

- NPC definitions may reference existing dialogue IDs.
- Dialogue graphs may later reference quest state and effects.
- NPC definitions must not store quest scripts, quest objectives, rewards,
  quest-state transitions, or executable logic.

Future Quest Studio should consume:

- stable `npc_definition_id`
- stable runtime actor source kind `Npc`
- `default_dialogue_id`
- dialogue source references from `DialogueSourceRef`

## Publication And Live References

Draft:

- stored for authoring only
- included in Content Studio catalog
- never exported to runtime static content

Publish:

- validates the complete aggregate
- marks the definition exportable
- export/update reaches runtime through the static-content publication pipeline

Disable:

- prevents future export
- should be blocked or strongly warned when checked-in Tiled/generated content
  still references the definition and no replacement is supplied

Delete:

- allowed only for Disabled definitions
- requires no known spawn references in configured source/generated content
- must use saved complete aggregate and concurrency token

Hot reload:

- deferred
- runtime changes require export/import/publication and normal server reload or
  restart behavior

## Concurrency

Use one root `updated_at` aggregate token. Since T5 starts without child
collections, this is sufficient. If later interaction-capability child rows are
added, child-only edits must still advance the root timestamp and participate in
the same aggregate preview signature.

## Testing Strategy

Phase tests:

- source-contract tests for docs, route ownership, and no placement ownership
- compiled domain-rules tests for validation edge cases
- repository round-trip and concurrency tests
- API route tests for options/list/load/preview/save/publish/disable/delete
- Godot source tests for payload shape and independent workspace startup
- runtime importer/export tests when MMO Project integration begins

Manual verification:

- load draft and published NPCs in Content Studio
- preview current `test_npc`
- publish an NPC definition
- export catalog
- import/publish starter region
- connect game client and verify the NPC appears, can be clicked, approaches,
  and opens the existing `test_npc_greeting` dialogue

## Phased Implementation Order

### T5 Phase 1 - NPC schema, contracts, and validation foundation

- additive migration artifact
- contracts
- domain rules
- schema-health provider
- catalog section provider
- options provider
- validation
- source and compiled tests

### T5 Phase 2 - NPC repository and mutation API

Implemented in T5C:

- options
- list/load
- preview
- save draft
- publish
- disable
- delete
- preview signatures
- optimistic concurrency
- reload verification
- transaction tests
- reference diagnostics

Current limitations:

- `supports_runtime_npc_catalog = false`
- `supports_quest_authoring = false`
- default reference diagnostics report `reference_check_complete = false`
  unless a known generated/database reference provider can prove otherwise
- No Godot NPC workspace is implemented yet

### T5 Phase 3 - Godot NPC workspace

- catalog
- identity
- visuals
- movement
- interaction/dialogue reference
- preview
- validation/apply lifecycle

### T5 Phase 4 - MMO Project runtime catalog handoff

- mirror migration
- seed existing `test_npc`
- add `MapPublisher export-npc-catalog`
- importer validation against published NPC catalog
- generated/database static-content source catalog loading
- replace hard-coded texture mapping
- preserve snapshot payload compatibility

### T5 Phase 5 - Runtime verification and hardening

- real database
- real generated map
- connected Godot client
- click-to-approach
- dialogue start/continue/choice/close
- moving NPC smoke if random wander is enabled
- spawn reference guards

## Deferred Work

- dialogue graph authoring
- quest authoring and quest state mutation
- shops, banking, trainers, vendors, and service menus
- NPC combat stats, attackability, hostility, and factions
- schedules, patrol paths, scripted movement, emotes, camera focus, portraits,
  cutscenes, localization, and voice
- arbitrary executable scripts or encoded behavior payloads

## Open Decisions

None for T5C. The implementation should use the locked narrow model above.
