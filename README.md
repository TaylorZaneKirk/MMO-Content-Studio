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

## Current state: T3B weapon/tool foundation implemented

The repository now contains:

- a Godot 4 desktop Content Studio shell
- a loopback-only .NET 10 authoring host
- environment, schema, and asset-root health checks
- searchable Basic Items, Consumables, and Equipment workspaces
- canonical item-PNG selection and import
- declarative consumable profiles, ordered requirements, and ordered effects
- editable wearable slots, requirements, skill modifiers, and combat bonuses
- backend/API support for hand-equipment aggregates, weapon profiles, and tool capabilities
- a directional paper-doll preview that follows the game client's current asset-key, frame-fallback, and layer-order rules
- an explicit Equippable / Not equippable control that removes stale equipment metadata atomically
- portions/empty-container transformations through result items
- exact validation and logical-change previews
- transactional draft creation and editing
- strict publication and disable operations
- optimistic aggregate concurrency and reload-after-commit verification
- source and optional runtime contract tests

T0 through T3B still require runtime verification on a machine with .NET 10, Godot 4, the MMO Project development database, and the game asset directory available.

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

Run the host:

```bash
./tools/run-host.sh
```

Run the Godot Studio in another terminal:

```bash
./tools/run-studio.sh
```

The default API address is `http://127.0.0.1:5187`.

## Roadmap

1. **T0 — Authoring foundation** — implemented; runtime verification pending
2. **T1 — Basic items** — implemented; runtime verification pending
3. **T2 — Consumable items** — implemented; migration/runtime verification pending
4. **T3A — Wearable equipment** — implemented; runtime verification pending
5. **T3B — Weapons and tools foundation** — implemented; runtime verification pending
6. **T4 — Mobs**
7. **T5 — Minimal NPC authoring**
8. **Dialogue workspace**
9. **Quest Studio evaluation**

The current vertical slices author ordinary items, declarative consumables,
wearable equipment, and the backend hand-equipment foundation for weapons/tools.
Equipment synchronizes the base item, slot, requirements, modifiers, combat
bonuses, optional weapon profile, and tool capabilities in one transaction.
Turning **Equippable** off deliberately clears every dependent equipment and
combat/tool row, which provides a safe correction path for legacy
misclassifications such as Chunk of Iron.

## Documentation

- [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md)
- [`docs/API_V1.md`](docs/API_V1.md)
- [`docs/T0_ACCEPTANCE.md`](docs/T0_ACCEPTANCE.md)
- [`docs/T1_ACCEPTANCE.md`](docs/T1_ACCEPTANCE.md)
- [`docs/T2_ACCEPTANCE.md`](docs/T2_ACCEPTANCE.md)
- [`docs/T3A_ACCEPTANCE.md`](docs/T3A_ACCEPTANCE.md)
- [`docs/T3B_ACCEPTANCE.md`](docs/T3B_ACCEPTANCE.md)
- [`integrations/mmo-project/README.md`](integrations/mmo-project/README.md)
- [`docs/ROADMAP.md`](docs/ROADMAP.md)
