# Architecture

## Locked decisions

- The desktop GUI and visual-preview environment use **Godot**.
- A separate local **.NET Content Authoring Host** owns PostgreSQL access.
- The GUI communicates with the host through a local, versioned HTTP/JSON API.
- The GUI never issues arbitrary SQL and never connects directly to PostgreSQL.
- Authoring operations work with complete logical content aggregates, not individual table rows.
- Every database mutation is transactional.
- Draft content remains unavailable to the active game runtime.
- Publication requires complete aggregate validation.
- Existing content can be loaded, edited, disabled, and republished.
- Tiled remains responsible for map placement. Content Studio defines reusable items, mobs, and NPCs.

## Primary components

### Godot Content Studio

Responsibilities:

- Catalog navigation and search
- Guided data-entry forms
- Inventory, ground-item, paper-doll, mob, and NPC previews
- Asset selection and import requests
- Validation and change-summary presentation
- Draft, publish, disable, and edit workflows

### .NET Content Authoring Host

Responsibilities:

- Database connectivity and schema compatibility checks
- Loading complete authored aggregates
- Transactional create/update/disable/publication operations
- Validation and cross-reference resolution
- Canonical asset-path and filesystem mutation orchestration
- Reload-and-verify after writes
- Future contribution export and audit support

## Authoring operation model

The host accepts a logical definition such as an `ItemAuthoringDefinition` and makes all applicable persisted records match it in one transaction.

An operation may synchronize multiple tables, including:

- Base definitions
- Inventory behavior
- Equipment metadata
- Combat profiles
- Combat bonuses
- Skill modifiers
- Asset references
- Publication state

Deleting a capability from the logical definition removes obsolete dependent records rather than leaving stale metadata behind.

## Initial publication model

The current MMO Project uses `item_definitions.runtime_enabled` as the active item-publication seam.

Content Studio initially exposes friendly states:

- Draft
- Valid
- Published
- Disabled

Until the game adopts a richer lifecycle, `Published` maps to `runtime_enabled = true`; all other states remain runtime-disabled.

## Process model

Initially, developers may run Godot and the .NET host separately. The intended final experience is one launcher flow:

1. Launch Content Studio.
2. Start or connect to the local authoring host.
3. Verify host API version, database schema, and asset roots.
4. Load the content catalog.
5. Shut down the child host when the application exits.
