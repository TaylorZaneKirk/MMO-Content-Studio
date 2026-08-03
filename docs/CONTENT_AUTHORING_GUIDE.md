# Content Authoring Guide

This guide records the current developer workflow for adding prototype content.
It describes the implementation that exists today, not the eventual content
tooling we may want later.

Keep these project constraints in mind while authoring content:

- Gameplay state is server-authoritative. Clients send intent, not trusted
  state.
- Logical tiles remain `32x32`; Tiled source maps use `128x128` tiles and 4x
  source art with `0.25` render compensation where applicable.
- Runtime mount coordinates belong in publishing profiles, not in Tiled maps or
  generated source chunks.
- Active item publication is controlled by
  `item_definitions.runtime_enabled`; do not infer activation from other rows.
- Draft, disabled, preview, and future items must remain
  `runtime_enabled = false` and must not be referenced by live inventory,
  equipment, ground items, or starter data.
- Every runtime-enabled item allowed in `right_hand` must have a valid combat
  profile. Occupied `right_hand` never falls back to unarmed combat.

## Important Paths

- Item schema and seed migrations: `prototype/sql/*.sql`
- Item use behavior: `prototype/server/features/inventory/application/InventoryItemUseService.cs`
- Item repositories and runtime guards:
  `prototype/server/features/inventory/persistence/`
- Skill requirements and modifiers: `prototype/sql/008_skill_equipment_schema.sql`
- Combat profiles and bonuses:
  `prototype/sql/013_item_combat_profiles.sql`,
  `prototype/sql/014_item_combat_bonuses.sql`
- Runtime item publication guards: `prototype/sql/015_item_runtime_publication.sql`
- Tiled source region:
  `prototype/shared/maps/tiled/regions/starter_region.tmj`
- Importer:
  `prototype/importer/import_tiled_region.py`
- Generated region output:
  `prototype/shared/maps/generated/starter_region/`
- Region publish command:
  `tools/maps/publish_region.py`
- Region publish profiles:
  `tools/maps/region_publish_profiles.json`
- Mob definitions:
  `prototype/shared/maps/mobs/catalog.json`
- NPC and mob tilesets:
  `prototype/shared/maps/tiled/tilesets/NPCs.tsx`,
  `prototype/shared/maps/tiled/tilesets/mobs.tsx`

## Adding a Consumable Item

Current consumables are food-like health restores. The item definition lives in
PostgreSQL seed data, but the actual consume behavior is currently routed by
item name in `InventoryItemUseService.FoodRestoreDefinitions`.

1. Pick a stable `item_id`.

   Use the existing convention when importing legacy inventory art, such as
   `inventory_532_berry_tart`. The id is the durable gameplay identity, so do
   not rename it once live data can reference it.

2. Add or verify the item icon.

   Place the icon under `prototype/client/assets/items/` and use a Godot path
   such as:

   ```text
   res://assets/items/Inventory_532_Berry Tart.png
   ```

3. Add a SQL migration that inserts the item into `item_definitions`.

   Use a new numbered migration after the latest file in `prototype/sql/`.
   Keep it idempotent:

   ```sql
   INSERT INTO item_definitions (
       item_id,
       item_name,
       icon_texture_path,
       equipment_slot_id,
       runtime_enabled
   )
   VALUES (
       'inventory_532_berry_tart',
       'Berry Tart',
       'res://assets/items/Inventory_532_Berry Tart.png',
       NULL,
       TRUE
   )
   ON CONFLICT (item_id) DO UPDATE
   SET
       item_name = EXCLUDED.item_name,
       icon_texture_path = EXCLUDED.icon_texture_path,
       equipment_slot_id = EXCLUDED.equipment_slot_id,
       runtime_enabled = EXCLUDED.runtime_enabled,
       updated_at = NOW();
   ```

4. Add the consume behavior to `FoodRestoreDefinitions`.

   Add the exact `item_name` with its base restore, random inclusive restore,
   and chat message. Today this is required; without it, primary use falls
   through to equip behavior and returns unsupported/non-equippable results.

5. Decide whether the item is active.

   Set `runtime_enabled = TRUE` only when the item is ready to enter live
   catalogs and be created in inventory, equipment, or ground-item state. Leave
   draft content false.

6. Seed starter/live references only after publication is enabled.

   New `character_inventory`, `character_equipment`, or `ground_items` rows must
   reference only runtime-enabled items. Database triggers reject disabled item
   references.

7. Validate.

   Run:

   ```bash
   dotnet build prototype/server/MMO.Project.Prototype.Server.csproj
   dotnet test prototype/tests/MMO.Project.Prototype.Server.Tests/MMO.Project.Prototype.Server.Tests.csproj --no-restore
   tools/validate-godot-client.sh
   ```

8. Update documentation.

   Add the new migration to `prototype/sql/README.md`. If the item establishes
   a new consumable category, document the new behavior near the inventory
   protocol or gameplay docs.

## Adding a Non-Consumable, Non-Equipable Item

This is an item that can exist in inventory or on the ground but has no current
primary-use behavior.

1. Pick a stable `item_id` and add or verify the icon asset.

2. Add an idempotent SQL migration inserting the row into `item_definitions`.

   Set:

   - `equipment_slot_id = NULL`
   - `runtime_enabled = TRUE` only if the item is ready for live runtime use

   Example:

   ```sql
   INSERT INTO item_definitions (
       item_id,
       item_name,
       icon_texture_path,
       equipment_slot_id,
       runtime_enabled
   )
   VALUES (
       'inventory_600_smooth_stone',
       'Smooth Stone',
       'res://assets/items/Inventory_600_Smooth Stone.png',
       NULL,
       TRUE
   )
   ON CONFLICT (item_id) DO UPDATE
   SET
       item_name = EXCLUDED.item_name,
       icon_texture_path = EXCLUDED.icon_texture_path,
       equipment_slot_id = EXCLUDED.equipment_slot_id,
       runtime_enabled = EXCLUDED.runtime_enabled,
       updated_at = NOW();
   ```

3. Do not add combat profiles, combat bonuses, skill requirements, or skill
   modifiers unless the item has gameplay that needs them.

4. Do not add the item to `FoodRestoreDefinitions` unless it should be consumed
   as food.

5. Validate that primary use fails cleanly.

   A non-consumable, non-equipable item may be present in inventory and ground
   items, but current primary use should not create server-side state changes.

6. Run the standard validation commands listed in the consumable section.

7. Update `prototype/sql/README.md`.

## Adding an Equipable Item

Equipable items are inventory items with `equipment_slot_id` set to one of the
rows in `equipment_slot_definitions`, such as `head`, `body`, `legs`, `boots`,
`right_hand`, `left_hand`, `gloves`, `cape`, or `ring`.

1. Pick a stable `item_id` and add or verify the inventory icon.

2. Decide the equipment slot.

   Use the canonical slot id, not display text. For right-hand items, remember
   that every runtime-enabled right-hand item is combat-capable and must have a
   valid combat profile.

3. Add the item definition in a SQL migration.

   Example body item:

   ```sql
   INSERT INTO item_definitions (
       item_id,
       item_name,
       icon_texture_path,
       equipment_slot_id,
       required_strength,
       runtime_enabled
   )
   VALUES (
       'inventory_601_padded_jacket',
       'Padded Jacket',
       'res://assets/items/Inventory_601_Padded Jacket.png',
       'body',
       1,
       TRUE
   )
   ON CONFLICT (item_id) DO UPDATE
   SET
       item_name = EXCLUDED.item_name,
       icon_texture_path = EXCLUDED.icon_texture_path,
       equipment_slot_id = EXCLUDED.equipment_slot_id,
       required_strength = EXCLUDED.required_strength,
       runtime_enabled = EXCLUDED.runtime_enabled,
       updated_at = NOW();
   ```

4. Add skill requirements when needed.

   Insert rows into `item_skill_requirements`. These are checked against base
   skill values when equipping.

   ```sql
   INSERT INTO item_skill_requirements (
       item_id,
       skill_id,
       required_value
   )
   VALUES
       ('inventory_601_padded_jacket', 'defence', 3)
   ON CONFLICT (item_id, skill_id) DO UPDATE
   SET
       required_value = EXCLUDED.required_value,
       updated_at = NOW();
   ```

5. Add skill modifiers when the equipped item changes effective levels.

   Insert rows into `item_skill_modifiers`. These affect snapshots and runtime
   derived state, but they do not satisfy equip requirements.

6. Add combat bonuses when the item affects combat formula inputs.

   Insert or update a row in `item_combat_bonuses`. Armour and non-weapon gear
   may use defensive bonuses without an `item_combat_profiles` row.

7. For `right_hand` items, add a combat profile.

   Insert a row into `item_combat_profiles` with:

   - `profile_id`
   - `attack_type` currently `melee`
   - `accuracy_style` one of `thrust`, `slash`, or `crush`
   - `minimum_range_tiles`
   - `maximum_range_tiles`
   - `attack_speed_units` as a count of 600 ms units, not milliseconds

   Example:

   ```sql
   INSERT INTO item_combat_profiles (
       item_id,
       profile_id,
       attack_type,
       accuracy_style,
       minimum_range_tiles,
       maximum_range_tiles,
       attack_speed_units
   )
   VALUES (
       'inventory_602_training_spear',
       'training_spear_thrust',
       'melee',
       'thrust',
       1,
       1,
       4
   )
   ON CONFLICT (item_id) DO UPDATE
   SET
       profile_id = EXCLUDED.profile_id,
       attack_type = EXCLUDED.attack_type,
       accuracy_style = EXCLUDED.accuracy_style,
       minimum_range_tiles = EXCLUDED.minimum_range_tiles,
       maximum_range_tiles = EXCLUDED.maximum_range_tiles,
       attack_speed_units = EXCLUDED.attack_speed_units,
       updated_at = NOW();
   ```

8. Add player visual assets if the equipped item should visibly render.

   The inventory/equipment state can exist with only an icon, but visual
   equipment display depends on Godot player actor assets under
   `prototype/client/assets/actors/player/`. Follow the existing slot/facing
   folder and filename conventions, and verify in the client.

9. Only set `runtime_enabled = TRUE` after all required metadata is present.

   Startup validation fails for active right-hand items with missing or invalid
   combat metadata.

10. Validate with build, server tests, Godot client validation, and a manual
    equip/unequip pass for visible gear.

11. Update `prototype/sql/README.md` and any relevant asset-gap notes.

## Adding a New NPC

NPC placement is Tiled-authored and importer-generated. There is not yet a
shared NPC definition catalog. Current generated NPCs carry
`npc_definition_id`, and the server resolves that id through
`NpcRuntimeService.ResolveGeneratedNpcTexturePath`.

1. Add or verify the NPC visual asset.

   Current NPC assets live under:

   ```text
   prototype/client/assets/maps/objects/npcs/
   ```

   Runtime rendering currently expects the resolved texture path from
   `NpcRuntimeService`, such as:

   ```text
   res://assets/actors/npcs/Chars_139_200-F2-S.png
   ```

2. Add the asset to the Tiled NPC tileset if authors need to place it visually.

   Update:

   ```text
   prototype/shared/maps/tiled/tilesets/NPCs.tsx
   ```

3. Add the runtime texture mapping.

   Extend `ResolveGeneratedNpcTexturePath` with the new `npc_definition_id`.
   Without this mapping, the importer can compile the spawn but the runtime NPC
   service skips it because it has no texture path.

4. Open the source region in Tiled.

   Use:

   ```text
   prototype/shared/maps/tiled/regions/starter_region.tmj
   ```

5. Add a point object on the `NPC Spawns` layer.

   Required Tiled object settings:

   - object type/class: `NpcSpawn`
   - object Name: stable spawn identity, for example `npc_bank_clerk_001`
   - point object: enabled

6. Add object properties.

   Current useful properties:

   - `npc_definition_id`: the id resolved by `NpcRuntimeService`
   - `facing`: `n`, `s`, `e`, `w`, or full direction text
   - `movement_behavior`: `static` or `random_wander`

   Current limitation: the importer preserves custom properties under the
   generated `properties` object. `NpcRuntimeService` reads `facing` and
   `movement_behavior` from that nested object, but random-wander tuning fields
   such as `home_tile_x`, `home_tile_y`, `wander_radius`, `tick_interval_ms`,
   and `idle_chance` are not yet promoted from Tiled properties into the runtime
   shape. Do not rely on those knobs until the importer/runtime boundary is
   expanded.

7. Keep coordinate rules unchanged.

   The importer uses the same point-to-tile rule as current NPC spawns:
   `floor(pixel / 128)`. Place the point on the intended source tile. Runtime
   mount coordinates stay in `tools/maps/region_publish_profiles.json`.

8. Compile and validate the region.

   ```bash
   python3 tools/maps/publish_region.py \
     --source prototype/shared/maps/tiled/regions/starter_region.tmj \
     --validate-only
   ```

9. Replace checked-in generated output when ready.

   ```bash
   python3 tools/maps/publish_region.py \
     --source prototype/shared/maps/tiled/regions/starter_region.tmj \
     --import-only
   ```

10. Publish to PostgreSQL when the database should serve the new content.

    ```bash
    MAP_PUBLISHER_CONNECTION_STRING='Host=localhost;Port=5432;Database=mmo_project_proto;Username=postgres;Password=...' \
    python3 tools/maps/publish_region.py \
      --source prototype/shared/maps/tiled/regions/starter_region.tmj
    ```

    Prefer `tools/maps/.publish-region.env` or shell environment for local
    credentials. Do not commit database passwords.

11. Validate.

    Run:

    ```bash
    python3 -m unittest prototype/importer/test_import_tiled_region.py
    dotnet test prototype/tests/MMO.Project.Prototype.Server.Tests/MMO.Project.Prototype.Server.Tests.csproj --no-restore
    tools/validate-godot-client.sh
    ```

12. Manually verify the NPC in the connected Godot client.

## Adding a New Mob

Mobs use a shared JSON definition catalog plus Tiled-authored `EnemySpawn`
objects. Runtime enemies are server-owned and instantiated from mounted authored
spawns at server startup.

1. Add or verify the mob visual asset.

   Current mob assets live under:

   ```text
   prototype/client/assets/maps/objects/mobs/
   ```

   Use source dimensions from the actual PNG. The current visual convention is
   4x source art with `visual_render_scale = 0.25`.

2. Add the asset to the Tiled mob tileset if authors need to place it visually.

   Update:

   ```text
   prototype/shared/maps/tiled/tilesets/mobs.tsx
   ```

3. Add a mob definition to `prototype/shared/maps/mobs/catalog.json`.

   Required fields today:

   - `definition_id`
   - `display_name`
   - `visual_texture_path`
   - `source_width`
   - `source_height`
   - `visual_anchor_offset_x`
   - `visual_anchor_offset_y`
   - `visual_render_scale`
   - `footprint_width_tiles`
   - `footprint_height_tiles`
   - `max_health`
   - `attack_type`
   - `accuracy_style`
   - `minimum_range_tiles`
   - `maximum_range_tiles`
   - `attack_speed_units`
   - `movement_speed_tiles_per_second`
   - `combat_faction_id` when the mob participates in authored faction
     relationships
   - `can_proactively_target_hostile_mobs`
   - `mob_detection_radius_tiles`
   - `mob_target_scan_interval_ms`
   - `mob_target_scan_candidate_limit`
   - `drops`
   - `attack_level`
   - `strength_level`
   - `defence_level`
   - combat bonus fields:
     `attack_thrust`, `attack_slash`, `attack_crush`, `attack_ranged`,
     `attack_magic`, `strength_melee`, `strength_ranged`, `strength_magic`,
     `defence_thrust`, `defence_slash`, `defence_crush`, `defence_ranged`,
     `defence_magic`

   Current restrictions:

   - `attack_type` must be `melee`
   - melee `accuracy_style` must be `thrust`, `slash`, or `crush`
   - `attack_speed_units` is a count of 600 ms combat units, not milliseconds
   - `attack_speed_units` must be in the accepted unit range
   - `maximum_range_tiles` must be greater than or equal to
     `minimum_range_tiles`
   - passive mobs should leave `can_proactively_target_hostile_mobs = false`
     and use zero detection/scan/candidate values
   - proactive mobs require a non-empty `combat_faction_id`, positive detection
     radius, positive scan interval, and positive candidate cap
   - `drops` must reference runtime-enabled item definitions; mob-caused
     defeats without eligible player contribution create public ownerless
     ground items

4. Add directed faction dispositions when this mob should proactively fight
   another mob faction.

   In `prototype/shared/maps/mobs/catalog.json`, add entries under
   `faction_dispositions`:

   ```json
   {
     "source_faction_id": "goblins",
     "target_faction_id": "town_guard",
     "disposition": "Hostile"
   }
   ```

   Dispositions are directional. Add the reverse direction separately if both
   factions should attack each other. Missing directions are neutral.

5. Open the source region in Tiled.

   Use:

   ```text
   prototype/shared/maps/tiled/regions/starter_region.tmj
   ```

6. Add a point object on the `Enemy Spawns` layer.

   Required Tiled object settings:

   - object type/class: `EnemySpawn`
   - object Name: stable `spawn_id`, for example `training_wolf_001`
   - point object: enabled

7. Add enemy spawn properties.

   Required or currently supported properties:

   - `mob_definition_id`: must match a definition in
     `prototype/shared/maps/mobs/catalog.json`
   - `spawn_behavior`: currently only `static` is supported
   - `facing`: optional direction shorthand or text
   - `leash_radius_tiles`: integer, nonnegative

8. Keep coordinate and footprint rules unchanged.

   The importer maps point position to source tile with `floor(pixel / 128)`.
   It validates that the mob footprint fits inside the finite source region and
   compiles the spawn into its owning `17x9` chunk while preserving source-global
   and chunk-local coordinates.

9. Compile and validate.

   ```bash
   python3 tools/maps/publish_region.py \
     --source prototype/shared/maps/tiled/regions/starter_region.tmj \
     --validate-only
   ```

10. Replace generated output when ready.

   ```bash
   python3 tools/maps/publish_region.py \
     --source prototype/shared/maps/tiled/regions/starter_region.tmj \
     --import-only
   ```

11. Publish to PostgreSQL when the active database should serve the new region.

    ```bash
    MAP_PUBLISHER_CONNECTION_STRING='Host=localhost;Port=5432;Database=mmo_project_proto;Username=postgres;Password=...' \
    python3 tools/maps/publish_region.py \
      --source prototype/shared/maps/tiled/regions/starter_region.tmj
    ```

12. Restart the server after changing generated/static content.

    `EnemyRuntimeService` initializes authored runtime enemies at server
    startup. The initial deterministic enemy id is:

    ```text
    <mount_id>:<spawn_id>
    ```

13. Validate.

    Run:

    ```bash
    python3 -m unittest prototype/importer/test_import_tiled_region.py
    dotnet build prototype/server/MMO.Project.Prototype.Server.csproj
    dotnet test prototype/tests/MMO.Project.Prototype.Server.Tests/MMO.Project.Prototype.Server.Tests.csproj --no-restore
    tools/validate-godot-client.sh
    ```

14. Manually verify in the connected Godot client.

    Confirm the mob enters initial snapshot interest, renders at the authored
    authoritative position, can be targeted by its logical footprint, and does
    not duplicate when the player crosses chunk boundaries while it remains in
    the `3x3` interest neighborhood.

## Publication and Validation Checklist

Before marking new content complete:

1. The source asset exists and loads in Godot.
2. SQL migrations are idempotent and listed in `prototype/sql/README.md`.
3. Active item rows set `runtime_enabled = TRUE` only when all active metadata
   is complete.
4. Runtime-disabled item rows are not referenced by starter inventory,
   equipment, ground items, or active content.
5. Right-hand active items have valid `item_combat_profiles` rows.
6. Tiled-authored NPCs and mobs use point objects and stable object names.
7. Importer output is deterministic and checked in only after validation.
8. Full region publication uses environment-provided database credentials.
9. Server startup validation passes.
10. The connected Godot client displays or rejects the content as expected.

## Known Temporary Boundaries

- Food consumables are still routed by item display name in code. A future
  data-backed consumable profile table would make adding food content cleaner.
- NPC definitions are not yet catalog-backed. Adding a new NPC currently
  requires updating `NpcRuntimeService.ResolveGeneratedNpcTexturePath`.
- Mob definitions are catalog-backed. The current proactive behavior is only the
  narrow authored hostile mob-versus-mob proof; broader aggro, respawn, owned
  player loot, rewards, faction systems, NPC combat, and PvP remain outside the
  current authoring process.
- Runtime item publication is a temporary seam, not a full approval workflow.
