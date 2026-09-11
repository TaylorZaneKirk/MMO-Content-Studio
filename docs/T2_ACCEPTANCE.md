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
- Existing databases also require `050_item_consumable_deterministic_amount.sql`;
  unified item saves require child timestamps from `048_item_consumable_child_timestamps.sql`
- `game_client_assets` configured to the MMO Project
  `prototype/client/assets` directory

## Acceptance flow

1. Run `./tools/test.sh`.
2. Apply corrected `017` for a fresh installation, followed by the normal migration
   chain including `048` and `050`. For an existing installation, apply `050` only
   after deliberately reauthoring any historical true ranges it reports. See the
   [integration notes](../integrations/mmo-project/README.md#t2-consumable-migration).
3. Start the host and confirm the Environment tab reports
   unified item schema as healthy, including the consumable `amount` column.
4. Open **Items** and confirm existing definitions are searchable. Fresh
   installations do not seed food values; consumable content must be authored intentionally.
5. Select an ordinary basic item or create a new stable item ID.
6. Enable **Consumable Behavior** and configure its use action, consumed quantity, optional result item, combat
   availability, cooldown, message, animation, and sound references.
7. Add zero or more `skill_minimum` requirements.
8. Add at least one `restore_resource` effect targeting health, concentration,
   or Special. Each row has one `amount` control, defaulting to 1. Author an integer
   from 1 through 1,000,000; preview/save/reload must preserve that exact amount.
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
- The unified Items aggregate preserves equipment and tool metadata while editing
  consumable behavior. There is no separate Consumables workspace.
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

## Deterministic amount correction verification

The correction remains pending exact-delta review and human acceptance. It does
not authorize game-server consumption or choose food balance values. See the
[correction evidence](DETERMINISTIC_CONSUMABLE_AMOUNT_CORRECTION_2026-09-11.md)
for validation limits and existing content requiring reauthoring.

Migration checks in `tests/contract/test_consumable_amount_migration.py` use
`psql` and `CONTENT_STUDIO_MIGRATION_DSN` against a disposable PostgreSQL database.
Each case creates an isolated schema inside a rolled-back transaction. Without
that environment, the database cases explicitly skip.

## Runtime boundary

T2 completes authoring and persistence. The current MMO game server still needs
an explicit integration slice to load and execute `item_consumable_profiles`,
requirements, and effects. Until that work lands, the editor surfaces
`runtime_consumption_integration_pending` as informational validation for drafts and a publication warning.
