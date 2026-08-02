# T3A Wearable Equipment Acceptance

T3A begins wearable-equipment authoring by exposing the current MMO Project
equipment schema as a read-only Content Studio workspace. Mutation support is
deferred until the full aggregate validation and publication rules are pinned
down.

## Required environment

- .NET 10 SDK
- Godot 4
- MMO Project PostgreSQL development database through migrations
  `014_item_combat_bonuses.sql`, `015_item_runtime_publication.sql`, and
  `017_item_consumable_profiles.sql`
- `game_client_assets` configured to the MMO Project
  `prototype/client/assets` directory

## Acceptance flow

1. Run `./tools/test.sh`.
2. Start the host and confirm the Environment tab reports
   `prototype-equipment-authoring-v1` as healthy.
3. Call `GET /api/v1/equipment/options` and confirm it returns wearable slots,
   deferred hand slots, skills, and combat bonus fields.
4. Call `GET /api/v1/equipment` and confirm existing equipment-shaped item
   definitions are searchable.
5. Call `GET /api/v1/equipment/{itemId}` for a wearable item and confirm it
   returns the base item, slot, required strength, skill requirements, skill
   modifiers, combat bonuses, optional combat profile, derived visual key,
   concurrency timestamp, and icon preview path.
6. Confirm right-hand and left-hand definitions are visible but reported as
   `WeaponOrTool` and not editable in the T3A wearable workspace.

## Safety checks

- The T3A API contains no draft, publish, disable, or save route yet.
- Godot still never receives database credentials and contains no SQL.
- Basic Items and Consumables continue to route equipment definitions away from
  those workspaces.
- The read model uses the game database schema directly:
  `equipment_slot_definitions`, `item_skill_requirements`,
  `item_skill_modifiers`, `item_combat_profiles`, and `item_combat_bonuses`.
- Hand-held weapons and tools remain deferred to T3B.
- Player-layer visual keys are reported as derived metadata because the current
  runtime derives them from item name and slot. T3A does not introduce a
  separate persisted paper-doll asset override.
