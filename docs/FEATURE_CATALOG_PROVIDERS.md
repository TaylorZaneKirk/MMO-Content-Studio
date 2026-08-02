# Feature Catalog Providers

Each authoring feature owns the catalog section it exposes to the Godot shell.
A feature registers one `IAuthoringCatalogSectionProvider` beside its endpoint,
schema, validation, and persistence registrations.

The central `ContentCatalogService` only:

- orders providers by `SortOrder`
- rejects duplicate `ContentType` registrations
- invokes each provider
- returns the combined versioned catalog response

Items, Consumables, and Equipment project their own entries. Mobs and NPCs use
planned empty providers until their real feature slices replace those
registrations.

A new workspace therefore does not modify `ContentCatalogService`. It adds its
own provider and registers it from the feature module. This keeps catalog growth
additive and prevents the central service from gaining feature-specific service
dependencies or filtering rules.
