# Implementation Roadmap

## T0 — Authoring Foundation

**Status:** Source implementation complete; development-machine runtime verification pending.

Godot shell, loopback .NET host, versioned API, environment health, shared
envelopes, catalog seams, asset roots, and contract tests.

## T1 — Basic Items

**Status:** Source implementation complete; development-machine runtime verification pending.

Search, load, create, edit, validate, preview, draft, publish, disable, import
item PNGs, enforce concurrency, and reload/verify base item definitions.

## T2 — Consumable Items

**Status:** Source implementation complete; database migration and development-machine runtime verification pending.

Capabilities:

- Search all item definitions and identify Basic, Consumable, and Equipment kinds
- Convert an eligible basic item into a consumable aggregate
- Author `eat`, `drink`, or `use` actions
- Configure consumed stack quantity and optional result-item transformation
- Configure combat availability, cooldown, messages, animation, and sound references
- Author ordered `skill_minimum` requirements
- Author ordered `restore_resource` effects with inclusive minimum/maximum ranges for health, concentration, and Special
- Preview exact base/profile/requirement/effect replacements
- Save, publish, and disable transactionally with aggregate concurrency
- Preserve live-reference publication guards
- Explicitly defer per-instance charge counters and arbitrary executable effects

Exit condition:

> A maintainer can create or modify a declarative consumable without manual
> content SQL, and the complete persisted aggregate survives reload and strict
> publication validation.

A separate MMO server integration remains required to execute the authored
profiles at runtime.

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
