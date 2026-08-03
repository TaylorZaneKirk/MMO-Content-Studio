# T4 Implementation Plan

## Goal

Add a Mobs authoring workspace that safely creates, edits, validates, previews,
publishes, and disables reusable mob definitions without moving spawn placement
out of Tiled.

This plan is based first on the existing MMO Project authoring guide,
`docs/development/CONTENT_AUTHORING_GUIDE.md`, especially `Adding a New Mob`.
The guide confirms that Content Studio should replace manual JSON mob-definition
editing while preserving Tiled `EnemySpawn` placement.

## Non-goals

- Do not author individual `EnemySpawn` placements.
- Do not edit MMO Project runtime code in the first Content Studio slice.
- Do not add mob respawn, random drop tables, patrols, dialogue, shops, quests,
  or arbitrary behavior scripts.
- Do not hot-reload mob definitions into a running game server.
- Do not rename existing runtime `enemy_*` protocol fields.

## Authoritative Aggregate

The first T4 aggregate should map one mob definition and its child collections:

```text
mob_definitions
  ├─ mob_combat_profiles
  ├─ mob_combat_bonuses
  ├─ mob_drops
  └─ faction reference fields

mob_factions
mob_faction_dispositions
```

`mob_definitions` owns:

- `mob_definition_id`
- `display_name`
- `publication_state`
- `visual_texture_path`
- `source_width`
- `source_height`
- `visual_anchor_offset_x`
- `visual_anchor_offset_y`
- `visual_render_scale`
- `footprint_width_tiles`
- `footprint_height_tiles`
- `max_health`
- `movement_speed_tiles_per_second`
- nullable `combat_faction_id`
- `can_proactively_target_hostile_mobs`
- `mob_detection_radius_tiles`
- `mob_target_scan_interval_ms`
- `mob_target_scan_candidate_limit`
- `updated_at_utc`

`mob_combat_profiles` owns one primary profile per mob:

- `mob_definition_id`
- `attack_type`
- `accuracy_style`
- `minimum_range_tiles`
- `maximum_range_tiles`
- `attack_speed_units`
- `attack_level`
- `strength_level`
- `defence_level`

`mob_combat_bonuses` mirrors the runtime aggregate:

- `attack_thrust`
- `attack_slash`
- `attack_crush`
- `attack_ranged`
- `attack_magic`
- `strength_melee`
- `strength_ranged`
- `strength_magic`
- `defence_thrust`
- `defence_slash`
- `defence_crush`
- `defence_ranged`
- `defence_magic`

`mob_drops` starts with guaranteed drops only:

- `mob_definition_id`
- `drop_order`
- `item_id`
- `stack_count`

`mob_factions` and `mob_faction_dispositions` provide a small typed source for
definition faction fields and hostile/neutral relationships.

`publication_state` is locked as the mob authoring lifecycle field for the new
tables. Runtime integration exports only `Published` definitions into the
existing `mob_definition_catalog` shape. It does not reuse
`item_definitions.runtime_enabled`, which is item-specific.

## API Shape

Add these v1 routes under a feature-owned module:

```text
GET  /api/v1/mobs/options
GET  /api/v1/mobs?search=slime
GET  /api/v1/mobs/{mobDefinitionId}
POST /api/v1/mobs/{mobDefinitionId}/preview
PUT  /api/v1/mobs/{mobDefinitionId}/draft
POST /api/v1/mobs/{mobDefinitionId}/publish
POST /api/v1/mobs/{mobDefinitionId}/disable
```

Preview returns:

- normalized target operation
- validation messages
- `valid_for_publication`
- exact logical changes
- local sprite preview file path when available
- `preview_signature`

Mutations require:

- `expected_updated_at_utc` for existing definitions
- matching `preview_signature`
- complete aggregate payload for draft saves

## Host Implementation Slices

### T4A - Schema and Contracts

Status: complete in Content Studio as a foundation slice.

- Add integration migrations under
  `integrations/mmo-project/prototype/sql/019_mob_authoring_schema.sql`,
  `020_mob_lifecycle_authoring.sql`, and
  `021_seed_existing_mob_definitions.sql`.
- Add `MobContracts.cs`.
- Add `MobSchemaRequirements.cs`.
- Add `MobAuthoringRegistry` and `MobDomainRules` for stable vocabulary,
  defaults, and source-only normalization helpers.
- Add contract tests that assert the schema, route, and source boundaries.
- Replace the planned Mobs catalog provider with a feature-owned provider that
  was initially `implemented = false` until the T4B API existed.
- Keep `/api/v1/mobs` routes, repositories, validation service, Godot UI, runtime
  export, and MMO Project application of the migration out of T4A.

Exit condition:

> The host can report missing/present mob schema health and exposes compile-time
> contracts for the Mobs workspace without adding runtime behavior.

### T4B - Repository, Validation, and API

Status: complete in Content Studio as the host-side authoring boundary.

- Add `MobRepository`.
- Add `MobDefinitionValidator`.
- Add `MobAuthoringService`.
- Extend `MobAuthoringFeature` with `/api/v1/mobs` route mappings.
- Implement list, load, preview, draft, publish, and disable.
- Implement transaction semantics:
  - lock the aggregate root row
  - replace child collections as complete sets
  - keep draft content runtime-disabled
  - reload inside the transaction
  - commit
  - reload and verify semantic equality
- Add source and .NET unit tests for validator/domain rules.

Exit condition:

> The API can safely author a full mob-definition aggregate in PostgreSQL with
> deterministic preview/apply behavior.

Implementation notes:

- Draft save creates new definitions and replaces existing aggregates as
  complete logical payloads.
- Publish and disable use the saved aggregate rather than arbitrary request body
  content.
- Preview signatures include mob definition ID, normalized operation,
  normalized draft, and expected concurrency token.
- Disable reference checks for generated/published `EnemySpawn` rows remain
  deferred until runtime integration exposes an authoritative reference seam.

### T4C - Godot Mobs Workspace

Status: implemented in the Godot workspace.

- Add a top-level **Mobs** tab.
- Add `mob_editor.gd`.
- Add `AuthoringHostClient` mob methods and signals.
- Use `AuthoringWorkspaceSupport` for preview/apply lifecycle.
- Sections:
  - Identity
  - Visuals and footprint
  - Stats
  - Primary attack
  - Movement and aggression
  - Drops
  - Validation and exact changes
- Add a sprite preview that applies source dimensions, anchor offsets, render
  scale, and footprint.
- Keep spawn/leash/home-position fields out of the editor. Disable warns that
  generated-spawn reference guards are not integrated yet.
- Keep mob API calls behind `AuthoringHostClient` and
  `AuthoringHttpTransport`; the editor does not parse envelopes, call SQL, or
  talk to PostgreSQL directly.
- Load mob options and catalog after the core studio connection completes so a
  missing mob-authoring schema disables only the Mobs workspace.

Exit condition:

> A maintainer can create, edit, preview, publish, and disable mob definitions
> from Godot without hand-written SQL.

### T4D - Runtime Integration Design and Handoff

Status: complete as an MMO Project runtime handoff slice.

T4D mirrors the schema into MMO Project and implements deterministic export of
`Published` mob definitions into the existing `mob_definition_catalog` shape.
The export supports both current static content paths:

1. generated-file workflows, where the importer/publisher validates
   `EnemySpawn.mob_definition_id` against the exported catalog;
2. database-published region workflows, where `world_regions.manifest` carries
   the same catalog read by `DatabaseWorldStaticContentSource`.

Do not make runtime enemies query Content Studio tables directly. Keep
`WorldStaticContentSnapshot` as the runtime consumption boundary.

After MMO Project enemy health regeneration, the runtime catalog also carries
`health_regeneration_amount` and `health_regeneration_interval_ms`. T4D exports
those fields as deterministic `0` defaults; authoring nonzero mob regeneration
requires a later schema/API/workspace slice.

Exit condition:

> Published Content Studio mob definitions feed the same runtime
> `MobDefinitionCatalog` consumed today, and `EnemySpawn.mob_definition_id`
> references remain authoritative placement links.

### T4E - Hardening

- Add known-spawn reference reporting.
- Block disable for published mobs still referenced by active generated/published
  spawns.
- Add migration/runtime smoke documentation.
- Add import/export reconciliation tests around current JSON catalog fixtures.
- Revisit respawn/drop-table expansion only after runtime support exists.

## Validation Rules

Publication should require:

- nonempty stable `mob_definition_id`
- nonempty display name
- valid canonical visual texture path
- positive source dimensions
- finite visual render scale greater than zero
- positive footprint dimensions
- positive max health
- supported attack type/style
- `minimum_range_tiles >= 0`
- `maximum_range_tiles >= minimum_range_tiles`
- `attack_speed_units` between 1 and 60
- nonnegative combat levels and bonuses
- positive finite movement speed
- faction present when proactive hostile targeting is enabled
- nonnegative detection radius, scan interval, and candidate limit
- positive scan interval/candidate limit when proactive targeting is enabled
- drop rows with published item ids and positive stack counts
- no duplicate drop order
- no duplicate faction disposition pairs

Draft validation may allow missing visual/drop references as warnings, but publish
must fail on runtime-invalid references.

## Suggested Defaults

- `attack_type`: `melee`
- `accuracy_style`: `crush`
- `minimum_range_tiles`: `1`
- `maximum_range_tiles`: `1`
- `attack_speed_units`: `4`
- `movement_speed_tiles_per_second`: `1.25`
- `footprint_width_tiles`: `1`
- `footprint_height_tiles`: `1`
- `visual_render_scale`: `0.25`
- proactive hostile targeting: disabled
- detection radius/scan interval/candidate limit: `0` while disabled
- drops: empty

## File Map for First Implementation

Host:

- `host/Contracts/MobContracts.cs`
- `host/Features/Mobs/MobAuthoringFeature.cs`
- `host/Features/Mobs/MobCatalogSectionProvider.cs`
- `host/Features/Mobs/MobSchemaRequirements.cs`
- `host/Persistence/MobRepository.cs`
- `host/Services/MobAuthoringService.cs`
- `host/Services/MobDefinitionValidator.cs`
- `host/Services/MobAuthoringRegistry.cs`
- `host/Services/MobDomainRules.cs`
- `host/Features/AuthoringFeatureExtensions.cs`

Deferred to T4C:

- `content-studio/scenes/Main.tscn`
- `content-studio/scripts/authoring_host_client.gd`
- `content-studio/scripts/mob_editor.gd`
- `content-studio/tests/contract_fixture_test.gd`

Tests:

- `tests/contract/test_t4_source_contract.py`
- `tests/contract/test_feature_catalog_providers.py`
- `tests/host/MMO.ContentStudio.AuthoringHost.Tests/MobDomainRulesTests.cs`

Docs:

- `docs/API_V1.md`
- `docs/ROADMAP.md`
- `docs/T4_ACCEPTANCE.md`
- `integrations/mmo-project/README.md`

## Open Decisions

No maintainer decision is required before T4 Phase 1.

Implementation notes:

- Seed/import current JSON catalog definitions into the new authoring tables as
  part of the runtime integration handoff, not as hidden startup behavior.
- Known spawn references should be read from generated files and database-published
  region chunks first; Tiled source scanning can be an additional diagnostic.
- Respawn, weighted drop tables, patrols, and animation states remain deferred
  until runtime contracts exist.
