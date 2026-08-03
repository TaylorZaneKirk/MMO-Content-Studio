# T4 Acceptance

## Scope

T4 moves reusable mob definitions into the Content Studio authoring boundary
while preserving the MMO Project split between reusable definitions and Tiled
`EnemySpawn` placement.

## T4A - Schema and Contracts Foundation

Status: complete in Content Studio.

Acceptance checks:

- `integrations/mmo-project/prototype/sql/019_mob_authoring_schema.sql`,
  `020_mob_lifecycle_authoring.sql`, and
  `021_seed_existing_mob_definitions.sql` exist
  as an additive migration handoff artifact.
- The migration declares `mob_factions`, `mob_faction_dispositions`,
  `mob_definitions`, `mob_combat_profiles`, `mob_combat_bonuses`, and
  `mob_drops`.
- The migration stores only reusable definition data and ordered guaranteed
  drops; it does not store spawn placement, respawn timing, patrols, weighted
  roll tables, dialogue, shops, quests, or scripts.
- `host/Contracts/MobContracts.cs` defines catalog, aggregate, draft, preview,
  publication, mutation, validation, and options contracts.
- Mob contracts reuse `EquipmentCombatBonusDefinition` for the established
  13-field combat-bonus shape.
- `host/Features/Mobs/MobSchemaRequirements.cs` owns the mob schema-health
  manifest under feature id `prototype-mob-authoring-v1`.
- `host/Features/Mobs/MobAuthoringFeature.cs` registers schema health, catalog,
  and registry seams without mapping `/api/v1/mobs` routes.
- The top-level catalog includes a feature-owned Mobs section; T4A introduced it
  as `implemented = false` until the T4B API made it repository-backed.
- `docs/CONTENT_AUTHORING_GUIDE.md` is a reference page to the authoritative MMO
  Project guide rather than a copied runtime guide.

## T4B - Repository, Validation, Preview, and Mutation API

Status: complete in Content Studio.

Acceptance checks:

- `host/Persistence/MobRepository.cs` owns mob persistence and transaction
  behavior.
- `host/Services/MobDefinitionValidator.cs` owns draft and publication
  validation.
- `host/Services/MobAuthoringService.cs` owns options, catalog, aggregate load,
  preview, save draft, publish, disable, preview signatures, optimistic
  concurrency, and reload verification.
- `host/Features/Mobs/MobAuthoringFeature.cs` maps the `/api/v1/mobs` route
  family and keeps route ownership inside the feature.
- `MobCatalogSectionProvider` is repository-backed and reports Mobs as
  implemented in the host catalog.
- Draft saves create or replace complete aggregates and set
  `publication_state = Draft`.
- Publish and disable operate only on the currently saved aggregate and require
  matching preview signatures.
- Existing-definition mutations require `expected_updated_at_utc`.
- Child rows for combat profile, combat bonuses, and guaranteed drops are
  replaced as complete sets.
- Disable preserves the authored aggregate and documents that authoritative
  generated/published `EnemySpawn` reference guards remain deferred.

## T4C - Godot Mobs Workspace

Status: complete in Content Studio.

Acceptance checks:

- The Godot Mobs workspace is implemented with a top-level tab, catalog,
  aggregate form, visual/footprint preview, preview-signature apply gate, draft,
  publish, and disable actions over `/api/v1/mobs`.
- The workspace does not author Tiled placement.

## T4D - Runtime Integration Design and Handoff

Status: complete as a runtime handoff slice.

Acceptance checks:

- `prototype/sql/019_mob_authoring_schema.sql`,
  `prototype/sql/020_mob_lifecycle_authoring.sql`, and
  `prototype/sql/021_seed_existing_mob_definitions.sql` are mirrored into the
  MMO Project runtime repository.
- The current runtime catalog mobs `slime`, `training_goblin`, and
  `training_guard` are idempotently seeded for development authoring catalogs.
- MMO Project `prototype/tools/MapPublisher` exposes `export-mob-catalog`.
- The exporter reads only `Published` mob definitions and writes deterministic
  `mob_definition_catalog` JSON.
- Draft and Disabled mobs are excluded from the runtime catalog.
- Runtime-enabled item references, faction references, primary combat profiles,
  combat bonuses, and ordered guaranteed drops are validated before writing.
- Current MMO Project health-regeneration runtime fields are emitted with zero
  defaults because nonzero mob regen authoring is not part of the T4 aggregate.
- Generated-file workflows continue through the importer/publisher, where
  `EnemySpawn.mob_definition_id` is validated against the exported catalog.
- Database-published region workflows continue through `world_regions.manifest`,
  which carries the same catalog read by `DatabaseWorldStaticContentSource`.
- Runtime enemies still consume `WorldStaticContentSnapshot`; simulation code
  does not query Content Studio authoring tables directly.

## Deferred

- T4E hardening around live references, generated maps, and multiplayer runtime
  validation.
