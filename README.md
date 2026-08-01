# MMO Content Studio

A Godot-based content authoring application for the [MMO Project](https://github.com/TaylorZaneKirk/MMO-Project).

MMO Content Studio turns game-design inputs into validated, transactional content updates without requiring contributors or maintainers to hand-author SQL across multiple related tables.

## Core architecture

```text
Godot Content Studio GUI
        ↓ local HTTP/JSON
.NET Content Authoring Host
        ↓
Authoring services, validation, and transactions
        ↓
PostgreSQL + canonical asset directories
```

Godot owns forms and rendering previews so authored items, equipment, mobs, and NPCs use the same visual rules as the game client. The .NET host owns database access, validation, publication, and filesystem mutations. Godot does not issue arbitrary SQL or connect directly to PostgreSQL.

## Initial roadmap

1. **T0 — Authoring foundation**
2. **T1 — Basic items**
3. **T2 — Consumable items**
4. **T3A — Wearable equipment**
5. **T3B — Weapons and tools**
6. **T4 — Mobs**
7. **T5 — Minimal NPC authoring**
8. **Dialogue workspace**
9. **Quest Studio evaluation**

The first vertical slice will create, edit, validate, disable, and publish a basic non-consumable, non-equippable item through the GUI, including inventory and ground-sprite selection and transactional PostgreSQL synchronization.

## Repository status

Architecture scaffold only. Implementation begins with T0.

See:

- [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md)
- [`docs/ROADMAP.md`](docs/ROADMAP.md)
