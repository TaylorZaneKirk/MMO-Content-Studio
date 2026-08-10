# MMO Content Studio

A Godot-based content authoring application for the
[MMO Project](https://github.com/TaylorZaneKirk/MMO-Project).

MMO Content Studio turns game-design inputs into validated, transactional
content updates without requiring contributors or maintainers to hand-author
SQL across multiple related tables.

## Architecture

```text
Godot Content Studio GUI
        ↓ local versioned HTTP/JSON
.NET Content Authoring Host
        ↓
Authoring services, validation, and transactions
        ↓
PostgreSQL + canonical asset directories
```

Godot owns forms and rendering previews so authored items, equipment, mobs, and
NPCs can use the same visual rules as the game client. The .NET host owns
database access, validation, publication, and filesystem mutations. Godot does
not issue arbitrary SQL or connect directly to PostgreSQL.

## Current state: D5 Dialogue Runtime Verification

The repository now contains:

- a Godot 4 desktop Content Studio shell
- a loopback-only .NET 10 authoring host
- environment, schema, and asset-root health checks
- a searchable unified Items workspace backed by complete `/api/v1/items` aggregates
- canonical item-PNG selection and import
- declarative consumable profiles, ordered requirements, and ordered effects
- editable wearable slots, requirements, skill modifiers, and combat bonuses
- backend/API and Godot UI support for unified item aggregates, weapon profiles, and tool capabilities
- optional equipped-visual metadata inside unified equipment aggregates, including canonical actor-rig selectors, socket/grip authoring, and per-pose grip anchors
- deterministic publication of runtime-enabled equipped visuals to the MMO Project item-ID catalog, with the current file-backed catalog retained as migration fallback
- a T4B mob-authoring host boundary with schema handoff, repository, validation,
  options, catalog, load, preview, draft, publish, disable, concurrency, and
  preview-signature support
- a top-level Godot Mobs workspace for reusable mob identity, visuals, footprint,
  stats, faction/aggression, one primary combat profile, combat bonuses, and
  ordered guaranteed drops
- a shared directional paper-doll preview that now reads canonical MMO Project rig metadata for layer depth, sockets, attachment math, and optional foreground hand/grip overlays
- a shared NPC/Mob actor socket calibration editor with exact-pose art, sparse inherited/override socket editing, drag and integer coordinates, zoom/grid/scroll inspection, conflict-safe file-backed save, and explicit descriptor assignment
- an explicit Equippable / Not equippable control that removes stale equipment metadata atomically
- portions/empty-container transformations through result items
- exact validation and logical-change previews
- transactional draft creation and editing
- strict publication and disable operations
- optimistic aggregate concurrency and reload-after-commit verification
- source and optional runtime contract tests
- D2 Dialogue Studio schema, contracts, repository, validation, playthrough preview, and host API
- D3 Godot Dialogue Studio implemented as a top-level Dialogue workspace after NPCs and before Environment

T4 Phase 0 audited the current MMO Project mob/enemy runtime path and locked the
implementation plan for the Mobs workspace. T4B implements the host-side API for
reusable mob definitions. T4C adds the Godot workspace over that API. T4D mirrors
the mob schema into MMO Project, includes the post-regen lifecycle timing
columns, seeds the current runtime mobs for local authoring catalogs, and adds
deterministic export of `Published` mob definitions into the existing runtime
`mob_definition_catalog`. Authoring generated-spawn reference guards remain
deferred to a later T4 hardening slice. Spawn placement stays in Tiled/generated
static content.

T5A audits the current MMO Project NPC runtime, Tiled `NpcSpawn` format,
dialogue handoff, visual conventions, and current manual authoring guide. It
locks the next NPC authoring boundary: Content Studio will own reusable NPC
definitions, while Tiled remains responsible for placement coordinates, stable
spawn names, initial facing, and the `npc_definition_id` link. T5B adds
`integrations/mmo-project/prototype/sql/024_npc_authoring_schema.sql`,
compile-time NPC contracts, domain normalization rules, a registry/options seam,
feature-owned schema-health requirements, and a placeholder NPC catalog section.
T5D Godot NPC workspace implemented. T5E MMO Project runtime NPC catalog
handoff implemented via mirrored migrations, `export-npc-catalog`, generated
and database static-content catalogs, and runtime composition from
`NpcSpawn.npc_definition_id`. T5F hardens the handoff with startup validation,
byte-stable export tests, placement-only Tiled source checks, and Content Studio
disable/delete guards across database, generated, and Tiled spawn references.
Dialogue-reference validation uses the configured file-backed MMO Project
dialogue catalog when it is available.
The NPCs workspace authors reusable definitions only; placement remains in Tiled.

D2 implements the Content Studio host-side Dialogue authoring boundary over
`integrations/mmo-project/prototype/sql/026_dialogue_authoring_schema.sql` and
`/api/v1/dialogues`. D3 Godot Dialogue Studio implemented the integrated
Dialogue workspace after NPCs and before Environment with a GraphEdit canvas,
node inspector, entry-point editing, validation/change previews, playthrough
preview, and NPC cross-navigation. D4 MMO Project runtime catalog handoff implemented
through deterministic Published dialogue export to
`prototype/shared/dialogues/catalog.json`. D5 hardens validator/runtime
equivalence, playthrough/session behavior, reference safety, and export
reproducibility. D1-D5 author only the current non-quest
dialogue runtime model: definitions, entry points, `speaker_text`,
`player_choice`, and `end` nodes, node-owned transitions, server-filtered
choices, and no quest, condition, or effect authoring. Quest predicates, quest
effects, objective progress, rewards, content gates, hot reload, and Quest
Studio remain deferred until later phases.

T0 through T4D still require runtime verification on a machine with .NET 10, Godot 4, the MMO Project development database, and the game asset directory available.

## Repository layout

```text
content-studio/   Godot GUI and visual-preview application
host/             .NET Content Authoring Host
contracts/        reserved for generated/shared contract artifacts
content-workspace/ ignored local authoring workspace
checks/           reserved for CI definitions
scripts/          reserved for packaging/orchestration scripts
tests/            cross-process contract tests
tools/            local run and test commands
integrations/     MMO Project migrations and integration handoff artifacts
docs/             architecture, API, roadmap, and acceptance documents
```

## Local setup

Requirements:

- .NET 10 SDK
- Godot 4
- Python 3
- Access to the MMO Project development PostgreSQL database for full health
  verification

Create local host configuration:

```bash
cp host/appsettings.Local.example.json host/appsettings.Local.json
```

Edit the copied file with the local PostgreSQL connection string and absolute
asset paths. The local file is ignored by Git.

Run all available checks:

```bash
./tools/test.sh
```

Launch the full local Studio:

```bash
./mmo-content-studio
```

That executable starts or reuses the local .NET authoring host, waits for its
health endpoint, and then launches the Godot Studio client. Use
`./mmo-content-studio --check` when you want the full repository checks before
launch.

Run the host separately for debugging:

```bash
./tools/run-host.sh
```

Run only the Godot Studio client in another terminal:

```bash
./tools/run-studio.sh
```

The default API address is `http://127.0.0.1:5187`.

## Roadmap

1. **T0 — Authoring foundation** — implemented; runtime verification pending
2. **T1 — Basic items** — implemented; runtime verification pending
3. **T2 — Consumable items** — implemented; migration/runtime verification pending
4. **T3A — Wearable equipment** — implemented; runtime verification pending
5. **T3B — Weapons and tools workspace** — implemented; runtime verification pending
6. **T4 — Mobs** — T4D runtime catalog export implemented; reference hardening pending
7. **T5 — Minimal NPC authoring** — T5F runtime/reference hardening implemented
8. **D1-D5 — Dialogue Studio** — non-quest authoring, runtime export, equivalence, and reference safety implemented
9. **R4D.1B — Draggable actor socket calibration editor** — implemented
10. **T6 — Interactable world objects foundation**
11. **T7 — Gathering resources and processing stations**
12. **MMO Project quest foundations**
13. **Dialogue Studio quest integration**
14. **Quest Studio**

The current vertical slices author ordinary items, declarative consumables,
wearable equipment, and hand-held weapons/tools.
Equipment synchronizes the base item, slot, requirements, modifiers, combat
bonuses, optional weapon profile, and tool capabilities in one transaction.
Turning **Equippable** off deliberately clears dependent equipment, combat, and
weapon rows while preserving item-level tool capabilities. Tool capabilities
work from inventory or equipment; equipability is optional.
U1 tool-capability independence and metadata safety is implemented.

The T3B Godot workspace was recovered selectively from prior attempts and
reconciled with the hardened `AuthoringHttpTransport`,
`AuthoringHostClient`, `AuthoringWorkspaceSupport`, and feature-owned host
architecture. Obsolete bootstrap payloads and recovery workflows were removed
rather than restored. MMO Project runtime execution of tool capabilities remains
deferred to a future server-authoritative gameplay slice.

T4 planning confirms that Content Studio should own reusable mob definitions:
identity, visuals, footprint, stats, primary melee attacks, movement/aggression,
factions, and guaranteed drops. Tiled remains responsible for `EnemySpawn`
placement, home position, facing, and the `mob_definition_id` link. Reusable
movement, aggression, leash, and return-home behavior belongs to the mob
definition.

T4B contributes the local `/api/v1/mobs` host API and transactional persistence
boundary. T4C contributes the Godot Mobs workspace. T4D mirrors the schema into
MMO Project and adds the runtime export handoff while keeping generated-spawn
reference guards deferred.

T5 planning confirmed that NPCs were authored as Tiled `NpcSpawn` placements
whose `npc_definition_id` was resolved by hard-coded runtime mapping before
T5E.
The locked T5 direction is a reusable NPC definition aggregate with explicit
visuals, movement defaults, talk/dialogue references, publication state, and no
Content Studio ownership of map placement. T5B NPC schema and contract
foundation implemented the additive schema handoff and host-side compile-time
contracts; T5C NPC repository, validation, and API implemented; T5D Godot NPC
workspace implemented; T5E MMO Project runtime NPC catalog handoff implemented;
T5F runtime/reference hardening implemented.
Notes are
authoring-only and dialogue-reference validation uses the configured
file-backed MMO Project dialogue catalog when it is available, otherwise it is
syntax-only. `supports_runtime_npc_catalog = true` and
`supports_quest_authoring = false`. See
[`docs/T5_NPC_DOMAIN_AUDIT.md`](docs/T5_NPC_DOMAIN_AUDIT.md),
[`docs/T5_NPC_AUTHORING_PLAN.md`](docs/T5_NPC_AUTHORING_PLAN.md), and
[`docs/T5_NPC_ACCEPTANCE.md`](docs/T5_NPC_ACCEPTANCE.md).

Unified item-authoring now has one public item route family, one host-side
complete item aggregate, one mutation authority, and one contextual Godot Items
workspace. U2 delivered the host aggregate and temporary compatibility
adapters, U3 made that aggregate the normal Godot workflow, and U4 removed the
obsolete Basic payload branch, specialization route groups, duplicate services,
duplicate repositories, duplicate catalog/schema providers, and legacy Godot
editor scripts. `/api/v1/items` is the only public item-authoring route family.
A3 attachment authoring was intentionally implemented before A2 overlay art so
item grip calibration can happen in Content Studio against the canonical MMO
Project rig contract instead of by hand-editing JSON catalogs.
See
[`docs/UNIFIED_ITEM_AUTHORING_AUDIT.md`](docs/UNIFIED_ITEM_AUTHORING_AUDIT.md)
and [`docs/UNIFIED_ITEM_AUTHORING_PLAN.md`](docs/UNIFIED_ITEM_AUTHORING_PLAN.md).
U4 unified route/tab retirement is implemented. U5 runtime tool resolution is
implemented in MMO Project as a server-authoritative selection seam across
equipped and inventory items; gathering and processing consumers remain
deferred.

D2 Dialogue Studio host work adds additive authoring schema, complete graph
contracts, repository-backed `/api/v1/dialogues`, draft/publish/disable/delete
preview signatures, optimistic concurrency, pure playthrough preview, graph
validation, schema-health, and catalog registration. D3 Godot Dialogue Studio
implemented the Dialogue workspace after NPCs and before Environment using
GraphEdit for graph editing, complete draft payloads, preview-before-apply
mutation gates, playthrough preview, and NPC cross-navigation. D4 MMO Project runtime catalog handoff implemented, and D5 hardening and playthrough
verification are complete for the non-quest runtime-compatible dialogue slice.
See
[`docs/DIALOGUE_STUDIO_RUNTIME_AUDIT.md`](docs/DIALOGUE_STUDIO_RUNTIME_AUDIT.md),
[`docs/DIALOGUE_STUDIO_DOMAIN_MODEL.md`](docs/DIALOGUE_STUDIO_DOMAIN_MODEL.md),
[`docs/DIALOGUE_STUDIO_IMPLEMENTATION_PLAN.md`](docs/DIALOGUE_STUDIO_IMPLEMENTATION_PLAN.md),
and [`docs/DIALOGUE_STUDIO_ACCEPTANCE.md`](docs/DIALOGUE_STUDIO_ACCEPTANCE.md).

Future planned work includes reusable interactable world-object authoring for
typed-capability objects such as levers, searchable containers, gathering
resources, and processing stations while preserving Tiled placement and
server-authoritative runtime execution. See
[`docs/INTERACTABLE_WORLD_OBJECTS_DESIGN.md`](docs/INTERACTABLE_WORLD_OBJECTS_DESIGN.md).

## Documentation

- [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md)
- [`docs/API_V1.md`](docs/API_V1.md)
- [`docs/T0_ACCEPTANCE.md`](docs/T0_ACCEPTANCE.md)
- [`docs/T1_ACCEPTANCE.md`](docs/T1_ACCEPTANCE.md)
- [`docs/T2_ACCEPTANCE.md`](docs/T2_ACCEPTANCE.md)
- [`docs/T3A_ACCEPTANCE.md`](docs/T3A_ACCEPTANCE.md)
- [`docs/T3B_ACCEPTANCE.md`](docs/T3B_ACCEPTANCE.md)
- [`docs/T4_ACCEPTANCE.md`](docs/T4_ACCEPTANCE.md)
- [`docs/T4_MOB_DOMAIN_AUDIT.md`](docs/T4_MOB_DOMAIN_AUDIT.md)
- [`docs/T4_IMPLEMENTATION_PLAN.md`](docs/T4_IMPLEMENTATION_PLAN.md)
- [`docs/T5_NPC_ACCEPTANCE.md`](docs/T5_NPC_ACCEPTANCE.md)
- [`docs/T5_NPC_DOMAIN_AUDIT.md`](docs/T5_NPC_DOMAIN_AUDIT.md)
- [`docs/T5_NPC_AUTHORING_PLAN.md`](docs/T5_NPC_AUTHORING_PLAN.md)
- [`docs/UNIFIED_ITEM_AUTHORING_AUDIT.md`](docs/UNIFIED_ITEM_AUTHORING_AUDIT.md)
- [`docs/UNIFIED_ITEM_AUTHORING_PLAN.md`](docs/UNIFIED_ITEM_AUTHORING_PLAN.md)
- [`docs/UNIFIED_ITEM_AUTHORING_ACCEPTANCE.md`](docs/UNIFIED_ITEM_AUTHORING_ACCEPTANCE.md)
- [`docs/DIALOGUE_STUDIO_RUNTIME_AUDIT.md`](docs/DIALOGUE_STUDIO_RUNTIME_AUDIT.md)
- [`docs/DIALOGUE_STUDIO_DOMAIN_MODEL.md`](docs/DIALOGUE_STUDIO_DOMAIN_MODEL.md)
- [`docs/DIALOGUE_STUDIO_IMPLEMENTATION_PLAN.md`](docs/DIALOGUE_STUDIO_IMPLEMENTATION_PLAN.md)
- [`docs/DIALOGUE_STUDIO_ACCEPTANCE.md`](docs/DIALOGUE_STUDIO_ACCEPTANCE.md)
- [`docs/INTERACTABLE_WORLD_OBJECTS_DESIGN.md`](docs/INTERACTABLE_WORLD_OBJECTS_DESIGN.md)
- [`integrations/mmo-project/README.md`](integrations/mmo-project/README.md)
- [`docs/ROADMAP.md`](docs/ROADMAP.md)
