# Host Feature Modules

The authoring host composes each content workspace through an isolated feature module.

```text
host/Features/
  ├─ Items/ItemAuthoringFeature.cs
  ├─ Consumables/ConsumableAuthoringFeature.cs
  ├─ Equipment/EquipmentAuthoringFeature.cs
  └─ AuthoringFeatureExtensions.cs
```

Each workspace feature owns:

- dependency-injection registration for its repositories, validators, and services
- its versioned HTTP route group
- request binding for preview, draft, publish, and disable operations
- delegation into the shared authoring-operation result boundary

`Program.cs` owns only process-level concerns:

- configuration and loopback binding
- shared infrastructure
- JSON configuration
- API middleware
- system handshake, health, and catalog endpoints
- feature composition
- fallback handling

Shared operation-to-HTTP mapping lives in `host/Http/AuthoringHttpResults.cs`. This keeps API envelopes, conflict responses, not-found behavior, and database-unavailable responses consistent across workspaces.

## Adding a workspace

A new workspace should:

1. Add one feature module under `host/Features/<Workspace>`.
2. Register the module in `AddAuthoringFeatures`.
3. Map the module in `MapAuthoringFeatures`.
4. Keep workspace-specific routes and dependencies out of `Program.cs`.
5. Add contracts that assert the routes in the owning feature module.

This boundary is intended to make T3B, T4, and later workspaces additive instead of requiring repeated edits to global host composition.
