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
    Mobs/MobSchemaRequirements.cs
    Npcs/NpcSchemaRequirements.cs
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

Shared dependencies may appear in more than one manifest. The unified Items
manifest owns the complete item aggregate schema after U4, including consumable,
equipment, weapon, combat-bonus, and tool-capability tables. The aggregator
removes duplicates while preserving first-registration order, allowing each
workspace to describe its own requirements without coordinating a global
hardcoded list.

T5B NPC schema and contract foundation implemented the NPC manifest in
`host/Features/Npcs/NpcSchemaRequirements.cs`. It checks the additive
`npc_definitions` table from
`integrations/mmo-project/prototype/sql/024_npc_authoring_schema.sql`,
including the root `updated_at_utc` concurrency token and the constraints for
visuals, initial `1x1` footprint support, movement, interaction, and dialogue
reference consistency. T5C implements the repository/API boundary; T5D adds the
Godot workspace; T5E adds the MMO Project runtime NPC catalog handoff.

D2 Dialogue schema/API implemented the Dialogue manifest in
`host/Features/Dialogues/DialogueSchemaRequirements.cs`. It checks
`dialogue_definitions`, `dialogue_entry_points`, `dialogue_nodes`, and
`dialogue_choices` from
`integrations/mmo-project/prototype/sql/026_dialogue_authoring_schema.sql`,
including stable ID constraints, supported node types, finite layout
coordinates, root timestamp columns, and child-table triggers that advance the
root concurrency token. D3 Godot Dialogue Studio consumes that health surface
for the Dialogue workspace; D4 MMO Project runtime catalog handoff is now
implemented.
