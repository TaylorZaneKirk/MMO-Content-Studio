# T1 Basic Items Acceptance

T1 is the first database-backed content-authoring vertical slice.

## Required environment

- .NET 10 SDK
- Godot 4
- MMO Project PostgreSQL development database through migration `015_item_runtime_publication.sql`
- `game_client_assets` configured to the MMO Project `prototype/client/assets` directory

## Acceptance flow

1. Run `./tools/test.sh`.
2. Start the host with `./tools/run-host.sh`.
3. Start the Studio with `./tools/run-studio.sh`.
4. Confirm the Environment tab reports the T1 schema contract as healthy.
5. Open the Items tab and confirm existing item definitions load.
6. Create a new item using a stable lowercase ID, display name, and existing PNG from the item-asset catalog.
7. Select **Save as Draft**, then choose **Validate and Preview Changes**.
8. Confirm the exact logical changes are shown before the apply button becomes available.
9. Apply the draft operation and confirm the item reloads with state `Draft`.
10. Select **Publish**, preview again, and apply. Confirm the item reloads with state `Published`.
11. Select **Disable**, preview again, and apply. Confirm the item reloads with state `Draft`.
12. Restart the MMO Project server after publishing and confirm the item is admitted to the active runtime catalog.

## Safety checks

- A malformed stable ID must be rejected.
- A missing icon may be saved as a draft only with a warning; it must block publication.
- An item with `equipment_slot_id` must be read-only in Basic Items.
- Editing stale data, or omitting the concurrency token for an existing item, must return `item_version_conflict` rather than overwriting a newer save.
- Saving a published item as a draft or disabling it must be blocked while inventory, equipment, or ground-item rows reference it.
- Disable previews must warn that static mob-drop references remain enforced by MMO server startup validation until T4 moves mob definitions into the authoring database boundary.
- Every mutation must lock the row, execute transactionally, commit, reload, and verify.
- Godot must never connect directly to PostgreSQL or issue SQL.

## Current schema boundary

The MMO Project currently stores the same `icon_texture_path` for inventory and ground-item rendering. T1 therefore exposes one **Inventory / ground icon** field. Separate ground presentation becomes a schema and runtime change when the game requires it.

The current schema also does not carry declarative consumable metadata. Non-equippable definitions are editable in this initial workspace; T2 will introduce explicit consumable profiles and refine content-kind classification.
