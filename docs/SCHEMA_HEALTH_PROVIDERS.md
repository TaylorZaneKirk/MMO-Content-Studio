# Feature-Provided Schema Health

Database compatibility requirements belong to the authoring feature that uses
them. Each host feature registers an `IAuthoringSchemaRequirementProvider`
alongside its repository, validator, and authoring service.

```text
host/
  Health/
    AuthoringSchemaRequirement.cs
    SchemaHealthInspector.cs
  Features/
    Items/ItemSchemaRequirements.cs
    Consumables/ConsumableSchemaRequirements.cs
    Equipment/EquipmentSchemaRequirements.cs
```

A requirement describes one PostgreSQL object:

- table
- column
- constraint
- trigger

`AuthoringHealthService` gathers every registered feature manifest, deduplicates
requirements by a stable key, and delegates inspection to
`SchemaHealthInspector`. The health service no longer needs to be edited when a
workspace adds schema objects.

## Adding a workspace

A new feature should:

1. Implement `IAuthoringSchemaRequirementProvider` in its feature directory.
2. Declare only the schema objects needed by that authored aggregate.
3. Register the provider in the feature's `Add...Authoring` extension.
4. Add source contracts for important migration objects.
5. Leave PostgreSQL metadata-query details in `SchemaHealthInspector`.

Shared dependencies may appear in more than one manifest. The aggregator removes
duplicates while preserving first-registration order, allowing each workspace
to describe its own requirements without coordinating a global hardcoded list.
