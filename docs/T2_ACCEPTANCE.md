# T2 Consumable Items Acceptance

T2 adds database-backed declarative consumable authoring on top of the T1 item
foundation.

## Required environment

- .NET 10 SDK
- Godot 4
- MMO Project PostgreSQL development database through migration
  `016_ground_item_ownership_kind.sql`
- T2 integration migration applied from
  `integrations/mmo-project/prototype/sql/017_item_consumable_profiles.sql`
- `game_client_assets` configured to the MMO Project
  `prototype/client/assets` directory

## Acceptance flow

1. Run `./tools/test.sh`.
2. Apply migration `017_item_consumable_profiles.sql` to the development database.
3. Start the host and confirm the Environment tab reports
   `prototype-consumable-authoring-v1` as healthy.
4. Open **Consumables** and confirm existing item definitions are searchable and the current hard-coded food set appears as seeded Consumable definitions with equivalent inclusive restore ranges.
5. Select an ordinary basic item or create a new stable item ID.
6. Configure its use action, consumed quantity, optional result item, combat
   availability, cooldown, message, animation, and sound references.
7. Add zero or more `skill_minimum` requirements.
8. Add at least one `restore_resource` effect with an inclusive minimum/maximum range targeting health,
   concentration, or Special.
9. Preview **Save as Draft**, review every base/profile/requirement/effect change,
   and apply it.
10. Reload the consumable and confirm the complete aggregate matches the form.
11. Preview and apply **Publish**. Confirm missing effects, missing icons,
    unknown skills, missing result items, and unpublished result items block
    publication.
12. Preview and apply **Disable** after removing live gameplay references.

## Safety checks

- Godot never receives database credentials and contains no SQL.
- Saving replaces requirements and effects transactionally; stale child rows do
  not survive removal from the logical definition.
- Existing-item mutations require the aggregate `updated_at` token.
- Equipment definitions remain read-only in Consumables.
- Basic Items marks definitions carrying a consumable profile as Consumable and
  refuses to edit them.
- A result item cannot reference the consumable itself.
- Duplicate skill requirements and duplicate resource effects are rejected.
- Published items cannot return to draft while live inventory, equipment, or
  ground-item rows reference them.
- Every mutation commits, reloads, and semantically verifies the full aggregate.

## Charge and portion boundary

The current character inventory stores only `item_id` and `stack_count`; it has
no per-instance metadata container. T2 therefore does not pretend to support
true per-instance charges.

Supported now:

- consume one or more stack units with `consume_quantity`
- transform the consumed item into another definition with `result_item_id`
- model portions, doses as separate item definitions, or empty containers

Deferred:

- one item instance carrying a mutable charge count
- arbitrary effect scripts or contributor-supplied executable expressions

## Runtime boundary

T2 completes authoring and persistence. The current MMO game server still needs
an explicit integration slice to load and execute `item_consumable_profiles`,
requirements, and effects. Until that work lands, the editor surfaces
`runtime_consumption_integration_pending` as informational validation for drafts and a publication warning.
