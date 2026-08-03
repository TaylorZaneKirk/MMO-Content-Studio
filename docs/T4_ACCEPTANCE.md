# T4 Acceptance

## Scope

T4 moves reusable mob definitions into the Content Studio authoring boundary
while preserving the MMO Project split between reusable definitions and Tiled
`EnemySpawn` placement.

## T4A - Schema and Contracts Foundation

Status: complete in Content Studio.

Acceptance checks:

- `integrations/mmo-project/prototype/sql/019_mob_authoring_schema.sql` exists
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
- The top-level catalog includes a feature-owned Mobs section that remains
  `implemented = false` until T4B/T4C.
- `docs/CONTENT_AUTHORING_GUIDE.md` is a reference page to the authoritative MMO
  Project guide rather than a copied runtime guide.

## Deferred

- T4B repository, validator, service, routes, transactional preview/apply, and
  database-backed catalog rows.
- T4C Godot Mobs workspace.
- T4D MMO Project runtime integration and migration application.
- T4E hardening around live references, generated maps, and multiplayer runtime
  validation.
