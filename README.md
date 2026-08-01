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

## Current state: T1 basic-item authoring implemented

The repository now contains:

- a Godot 4 desktop Content Studio shell
- a loopback-only .NET 10 authoring host
- environment, schema, and asset-root health checks
- searchable item catalog and complete item loading
- canonical item-PNG selection and import
- exact validation and logical-change previews
- transactional draft creation and editing
- strict publication and disable operations
- optimistic concurrency and reload-after-commit verification
- source and optional runtime contract tests

T0 and T1 still require runtime verification on a machine with .NET 10, Godot 4, the MMO Project development database, and the game asset directory available.

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
3. **T2 — Consumable items**
4. **T3A — Wearable equipment**
5. **T3B — Weapons and tools**
6. **T4 — Mobs**
7. **T5 — Minimal NPC authoring**
8. **Dialogue workspace**
9. **Quest Studio evaluation**

The first content vertical slice creates, edits, validates, disables, and
publishes a basic non-consumable, non-equippable item through the GUI, including
shared inventory/ground-icon selection and transactional PostgreSQL
synchronization.

## Documentation

- [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md)
- [`docs/API_V1.md`](docs/API_V1.md)
- [`docs/T0_ACCEPTANCE.md`](docs/T0_ACCEPTANCE.md)
- [`docs/T1_ACCEPTANCE.md`](docs/T1_ACCEPTANCE.md)
- [`docs/ROADMAP.md`](docs/ROADMAP.md)
