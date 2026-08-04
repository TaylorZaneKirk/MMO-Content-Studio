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

## Current state: U4 unified item authoring

The repository now contains:

- a Godot 4 desktop Content Studio shell
- a loopback-only .NET 10 authoring host
- environment, schema, and asset-root health checks
- a searchable unified Items workspace backed by complete `/api/v1/items` aggregates
- canonical item-PNG selection and import
- declarative consumable profiles, ordered requirements, and ordered effects
- editable wearable slots, requirements, skill modifiers, and combat bonuses
- backend/API and Godot UI support for unified item aggregates, weapon profiles, and tool capabilities
- a T4B mob-authoring host boundary with schema handoff, repository, validation,
  options, catalog, load, preview, draft, publish, disable, concurrency, and
  preview-signature support
- a top-level Godot Mobs workspace for reusable mob identity, visuals, footprint,
  stats, faction/aggression, one primary combat profile, combat bonuses, and
  ordered guaranteed drops
- a shared directional paper-doll preview that follows the game client's current asset-key, frame-fallback, and layer-order rules
- an explicit Equippable / Not equippable control that removes stale equipment metadata atomically
- portions/empty-container transformations through result items
- exact validation and logical-change previews
- transactional draft creation and editing
- strict publication and disable operations
- optimistic aggregate concurrency and reload-after-commit verification
- source and optional runtime contract tests

T4 Phase 0 audited the current MMO Project mob/enemy runtime path and locked the
implementation plan for the Mobs workspace. T4B implements the host-side API for
reusable mob definitions. T4C adds the Godot workspace over that API. T4D mirrors
the mob schema into MMO Project, includes the post-regen lifecycle timing
columns, seeds the current runtime mobs for local authoring catalogs, and adds
deterministic export of `Published` mob definitions into the existing runtime
`mob_definition_catalog`. Authoring generated-spawn reference guards remain
deferred to a later T4 hardening slice. Spawn placement stays in Tiled/generated
static content.

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
7. **T5 — Minimal NPC authoring**
8. **T6 — Interactable world objects foundation**
9. **T7 — Gathering resources and processing stations**
10. **Dialogue workspace**
11. **Quest Studio evaluation**

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

Unified item-authoring now has one public item route family, one host-side
complete item aggregate, one mutation authority, and one contextual Godot Items
workspace. U2 delivered the host aggregate and temporary compatibility
adapters, U3 made that aggregate the normal Godot workflow, and U4 removed the
obsolete Basic payload branch, specialization route groups, duplicate services,
duplicate repositories, duplicate catalog/schema providers, and legacy Godot
editor scripts. `/api/v1/items` is the only public item-authoring route family.
See
[`docs/UNIFIED_ITEM_AUTHORING_AUDIT.md`](docs/UNIFIED_ITEM_AUTHORING_AUDIT.md)
and [`docs/UNIFIED_ITEM_AUTHORING_PLAN.md`](docs/UNIFIED_ITEM_AUTHORING_PLAN.md).
U4 unified route/tab retirement is implemented. U5 runtime tool resolution is
implemented in MMO Project as a server-authoritative selection seam across
equipped and inventory items; gathering and processing consumers remain
deferred.

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
- [`docs/UNIFIED_ITEM_AUTHORING_AUDIT.md`](docs/UNIFIED_ITEM_AUTHORING_AUDIT.md)
- [`docs/UNIFIED_ITEM_AUTHORING_PLAN.md`](docs/UNIFIED_ITEM_AUTHORING_PLAN.md)
- [`docs/UNIFIED_ITEM_AUTHORING_ACCEPTANCE.md`](docs/UNIFIED_ITEM_AUTHORING_ACCEPTANCE.md)
- [`docs/INTERACTABLE_WORLD_OBJECTS_DESIGN.md`](docs/INTERACTABLE_WORLD_OBJECTS_DESIGN.md)
- [`integrations/mmo-project/README.md`](integrations/mmo-project/README.md)
- [`docs/ROADMAP.md`](docs/ROADMAP.md)
