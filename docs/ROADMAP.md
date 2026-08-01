# Implementation Roadmap

## T0 — Authoring Foundation

Deliver the shared architecture required by all later content workspaces.

Scope:

- Godot application shell
- .NET authoring-host skeleton
- Versioned local API contract
- Connection profile configuration
- Database and schema health checks
- Shared result/error envelope
- Transaction boundary conventions
- Validation severity model
- Catalog and aggregate-loading seams
- Asset-root configuration
- Automated startup and contract tests

Exit condition:

> Godot can connect to the local host, display database/schema health, and retrieve a versioned empty content catalog through tested contracts.

## T1 — Basic Items

Create the first complete vertical slice for non-consumable, non-equippable items.

Capabilities:

- List and search existing items
- Load one complete item definition
- Create and edit a basic item
- Select/import inventory and ground sprites
- Configure basic inventory and trade behavior
- Save as runtime-disabled draft
- Validate the persisted aggregate
- Publish or disable the item
- Show the exact logical change summary before commit
- Reload and verify after commit

Exit condition:

> A maintainer can add a valid basic item to the development game without writing SQL or manually coordinating related persistence records.

## T2 — Consumable Items

Add declarative consumable profiles, requirements, resource effects, messages, charges/portions, and presentation references.

## T3A — Wearable Equipment

Add equipment slots, requirements, paper-doll assets, skill modifiers, combat bonuses, and directional previews.

## T3B — Weapons and Tools

Add explicit combat profiles, attack family/style, range, attack speed, weapon animation references, and tool capability metadata.

## T4 — Mobs

Move reusable mob definitions into the database-backed authoring boundary while keeping spawn placement in Tiled. Add stats, attacks, movement/aggression profiles, factions, visuals, and drop-table authoring.

## T5 — Minimal NPC Authoring

Add reusable NPC identity, visuals, movement profiles, interaction capabilities, service references, and dialogue-reference placeholders before the NPC interaction slice.

## Later Workspaces

- Dialogue graph authoring
- Dialogue preview and playthrough validation
- Quest Studio scope evaluation
- Quest state-graph authoring
- Contribution bundle export and maintainer publication
- Validated candidate-snapshot hot reload
