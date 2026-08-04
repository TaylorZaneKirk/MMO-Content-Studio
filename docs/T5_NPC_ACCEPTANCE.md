# T5 NPC Authoring Acceptance

Status: acceptance criteria for the T5 implementation sequence. T5F runtime
handoff hardening and reference safety implemented.

## T5A - Audit And Domain Lock

- `docs/T5_NPC_DOMAIN_AUDIT.md` documents the current MMO Project NPC runtime,
  importer, Tiled spawn format, dialogue handoff, visuals, networking, and gaps.
- `docs/T5_NPC_AUTHORING_PLAN.md` locks the reusable-definition versus Tiled
  placement split.
- Roadmap, README, architecture, and MMO Project integration docs name T5 as
  planned work without claiming implemented NPC routes or workspace support.
- Source-contract tests prevent T5A from silently adding production NPC schema,
  API routes, or Godot editor code.
- MMO Project remains read-only.

## T5 Phase 1 - Schema And Contracts

- Additive migration artifact
  `integrations/mmo-project/prototype/sql/024_npc_authoring_schema.sql`
  introduces `npc_definitions`.
- Contracts expose the complete NPC aggregate, options, preview, validation,
  mutation, and catalog response shapes.
- Schema-health provider checks the NPC authoring table and required columns.
- Domain rules validate IDs, publication state, visuals, movement, interaction,
  and dialogue references.
- Registry/options expose only the supported T5B states, movement modes, and
  `talk` interaction. Dialogue-reference validation uses the configured
  file-backed MMO Project dialogue catalog when available.
- `notes` is authoring-only metadata and is omitted from the runtime NPC
  catalog export.
- No placement fields appear in the NPC definition contract.

## T5 Phase 2 - Repository And API

- `/api/v1/npcs` supports options, list, load, preview, save draft, publish,
  disable, and delete.
- Draft save accepts incomplete definitions while returning validation messages.
- Publish requires full runtime-compatible validation.
- Mutations require root concurrency and server preview signatures.
- Save/publish/disable/delete operate on the saved complete aggregate.
- Repository writes are transactional and verified by reload after commit.
- Disabled definitions can be deleted only when no known spawn references exist.
- Dialogue-reference validation uses the configured file-backed MMO Project dialogue catalog
  when available, and reports syntax-only validation when the catalog is unavailable.
- Options report `supports_runtime_npc_catalog = true` and
  `supports_quest_authoring = false`.

## T5 Phase 3 - Godot NPC Workspace

- Top-level NPCs workspace lists draft, published, and disabled NPC definitions.
- Users can create, edit, preview, save draft, publish, disable, and delete NPC
  definitions.
- The preview resolves the configured actor sprite path, applies authored scale
  and anchor offsets, and shows preview-only facing selection without persisting
  placement facing.
- Form and preview/validation panels are scrollable.
- Missing/invalid dialogue IDs are visible in validation.
- Quest authoring and multiple interactions are displayed as read-only
  capability states.
- Placement coordinates are not editable in Content Studio.

## T5 Phase 4 - MMO Project Runtime Handoff

- MMO Project receives the mirrored migration and seed for the current
  `test_npc` definition.
- `MapPublisher export-npc-catalog` exports only `Published` NPC definitions.
- Tiled importer validates `NpcSpawn.npc_definition_id` against the exported
  catalog.
- Generated/database static-content sources load the NPC definition catalog.
- `NpcRuntimeService` composes `NpcSpawn` placement with the reusable catalog
  definition and no longer requires hard-coded texture mapping.
- Static-content startup rejects malformed NPC definitions, duplicate definition
  IDs, missing catalog references, and `NpcSpawn.npc_definition_id` values that
  do not resolve to the embedded catalog.
- MapPublisher export coverage keeps the checked-in NPC catalog byte-stable.
- Checked-in Tiled source files keep ordinary `NpcSpawn` objects placement-only.
- Existing `WorldSnapshotNpcPayload` and NPC interaction messages remain
  backward-compatible.

## T5 Phase 5 - Verification

- A fresh development database shows current NPC seed data in Content Studio.
- A generated starter-region map containing `npc_test_001` loads the NPC in the
  Godot game client.
- Clicking the NPC sends `npc_interaction_request`, approaches if needed, and
  opens `test_npc_greeting`.
- Dialogue continue, choice, close, session replacement, and manual movement
  cancellation still behave as server-authoritative flows.
- Disabling or deleting an NPC definition referenced by checked-in spawn data is
  blocked through database, generated chunk, or Tiled source reference guards.

## Non-Goals

- No dialogue graph editor.
- No quest authoring.
- No shops, banking, vendors, trainers, services, schedules, emotes, portraits,
  cutscenes, or localization.
- No NPC combat stats, hostility, attacks, loot, XP, factions, or reputation.
- No arbitrary scripting or executable payloads.
- No Content Studio ownership of spawn placement.
