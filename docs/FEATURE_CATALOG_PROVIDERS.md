# Feature Catalog Providers

Each authoring feature owns the catalog section it exposes to the Godot shell.
A feature registers one `IAuthoringCatalogSectionProvider` beside its endpoint,
schema, validation, and persistence registrations.

The central `ContentCatalogService` only:

- orders providers by `SortOrder`
- rejects duplicate `ContentType` registrations
- invokes each provider
- returns the combined versioned catalog response

Items now projects one unified item catalog section. Mobs provides its own
section. T5B NPC schema and contract foundation implemented
`host/Features/Npcs/NpcCatalogSectionProvider.cs`, which registers the `NPCs`
section as unavailable without fake entries until NPC repository/API support
exists.

A new workspace therefore does not modify `ContentCatalogService`. It adds its
own provider and registers it from the feature module. This keeps catalog growth
additive and prevents the central service from gaining feature-specific service
dependencies or filtering rules.

The NPC catalog provider is intentionally only a shell-level placeholder today:
repository/API, Godot workspace, runtime handoff, and verification remain
pending.
