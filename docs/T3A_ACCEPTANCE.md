# T3A Wearable Equipment Acceptance

T3A provides complete wearable-equipment authoring over the MMO Project's
existing item, equipment, skill, and combat-bonus schema. Hand-held weapon/tool
profiles remain T3B, but T3A may deliberately remove their equipability when
legacy metadata is wrong.

## Required environment

- .NET 10 SDK
- Godot 4
- MMO Project PostgreSQL development database through the equipment, skill,
  combat-profile, combat-bonus, runtime-publication, and T2 consumable migrations
- `game_client_assets` configured to the MMO Project
  `prototype/client/assets` directory

## Acceptance flow

1. Run `./tools/test.sh`.
2. Start the host and confirm the Environment tab reports
   `prototype-equipment-authoring-v1` as healthy.
3. Open **Equipment** and confirm every item definition is searchable, including
   ordinary Basic items that are not currently equippable.
4. Select an ordinary item, enable **Equippable**, choose a wearable slot, add
   requirements/modifiers/bonuses, preview the exact changes, and save a draft.
5. Change direction and frame in the paper-doll preview and confirm the selected wearable layer uses the same derived key and fallback rules as the game client.
6. Reload the item and confirm the complete aggregate survived persistence.
7. Publish a valid wearable item and confirm the runtime-publication state is
   updated only after strict validation.
8. Select a legacy hand-slot item such as Chunk of Iron, turn **Equippable** off,
   and preview the cleanup. The preview must list removal of the slot, strength
   gate, requirements, modifiers, combat profile, and combat bonuses.
9. Apply the cleanup and reload the item. It must now be classified as Basic,
   have no equipment metadata, and be available to the Basic Items workspace.

## Safety checks

- The GUI contains no SQL or PostgreSQL driver.
- Every mutation requires a matching successful preview before apply.
- Saving a published item produces a draft and obeys the live-reference guard.
- Existing-item writes require the `updated_at` concurrency token.
- Save operations lock the base row, update the base definition, replace child
  collections, reload inside the transaction, commit, then reload and verify.
- **Not equippable** clears `equipment_slot_id`, resets `required_strength` to 1,
  and deletes rows from `item_skill_requirements`, `item_skill_modifiers`,
  `item_combat_profiles`, and `item_combat_bonuses` atomically.
- Consumables remain owned by the Consumables workspace.
- Left-hand/right-hand weapons and tools cannot be edited or published as
  wearables in T3A. They can only be declassified; full editing remains T3B.
- The directional preview reads the configured MMO client asset root and mirrors current default layers, N-frame fallback, legacy-file matching, and layer ordering.
- Player-layer visual keys remain derived metadata. T3A does not add a separate persisted paper-doll asset override.
