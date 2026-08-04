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
section. T5C NPC repository, validation, and API implemented
`host/Features/Npcs/NpcCatalogSectionProvider.cs` as a repository-backed
section over `NpcAuthoringService.ListAsync`, and T5D adds the matching Godot
NPCs workspace.

A new workspace therefore does not modify `ContentCatalogService`. It adds its
own provider and registers it from the feature module. This keeps catalog growth
additive and prevents the central service from gaining feature-specific service
dependencies or filtering rules.

The NPC catalog provider never invents fake entries. It returns real NPC
definition entries when the configured database has the T5 schema, and returns
an empty implemented section when the host cannot list definitions. MMO Project
runtime NPC catalog handoff is implemented in T5E.

The Dialogue catalog provider follows the same rule. It is backed by
`DialogueAuthoringService.ListAsync`, reports real Dialogue definition entries
when the configured database has the D2 schema, and returns an empty implemented
section when listing is unavailable. It does not fabricate runtime graph entries
and does not claim the D4 runtime export handoff.
