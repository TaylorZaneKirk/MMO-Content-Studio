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
section, and NPCs use a planned empty provider until their real feature slice
replaces that registration.

A new workspace therefore does not modify `ContentCatalogService`. It adds its
own provider and registers it from the feature module. This keeps catalog growth
additive and prevents the central service from gaining feature-specific service
dependencies or filtering rules.
