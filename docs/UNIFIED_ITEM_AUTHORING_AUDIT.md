# Unified Item Authoring Audit

## Scope

This audit covers the current MMO Content Studio item-authoring slices and the
read-only MMO Project runtime item consumers as of the T4 mob workspace state.
It does not implement the unified editor, remove routes, add migrations, or
modify MMO Project runtime behavior.

Repositories inspected:

- Content Studio: `TaylorZaneKirk/MMO-Content-Studio` on `main`
- Runtime reference: `TaylorZaneKirk/MMO-Project` on `master`

## Executive Finding

Items, consumables, equipment, weapons, and tools already share one physical
runtime root: `item_definitions`. The current Content Studio implementation,
however, exposes that root through four separate feature routes and Godot tabs
whose repositories mutate overlapping columns and child tables.

That split is now the main correctness risk. Tool capabilities are currently
constrained as hand-equipment metadata, while the target design requires tool
capabilities to be independent item capabilities. A unified item aggregate is
the correct next architecture: keep internal specialization modules, but expose
one public `ItemDefinition` contract, one preview signature, one concurrency
token, and one contextual Items workspace.

## Current Schemas

| Table or column | Current purpose | Current owner in Content Studio | Runtime consumer |
| --- | --- | --- | --- |
| `item_definitions.item_id` | Stable item identity | Items, Consumables, Equipment, HandEquipment | Inventory, equipment, ground items, combat startup validation |
| `item_definitions.item_name` | Display/runtime name | Items, Consumables, Equipment, HandEquipment | Inventory snapshots, food-name fallback, visual key derivation |
| `item_definitions.icon_texture_path` | Inventory/ground icon | Items, Consumables, Equipment, HandEquipment | Inventory/equipment/ground-item snapshots and Godot icon loading |
| `item_definitions.equipment_slot_id` | Equipability and slot | Equipment, HandEquipment; reset by Items and Consumables | Equip action, equipment snapshots, combat profile validation |
| `item_definitions.required_strength` | Legacy equipment gate | Equipment, HandEquipment; reset by Items and Consumables | Authoring compatibility, superseded by generic skill requirements for runtime |
| `item_definitions.runtime_enabled` | Runtime publication switch | All item routes | Runtime-enabled item catalogs, live inventory/equipment/ground-item guards |
| `item_skill_requirements` | Equip requirements | Equipment, HandEquipment | Equip validation and snapshot tooltips |
| `item_skill_modifiers` | Equipped skill modifiers | Equipment, HandEquipment | Character effective skill resolution |
| `item_combat_bonuses` | Equipped combat bonuses | Equipment, HandEquipment | `CharacterCombatBonusResolver` |
| `item_combat_profiles` | Weapon attack profile | HandEquipment; read by Equipment | `ItemCombatProfileRepository`, `CharacterCombatProfileResolver` |
| `item_consumable_profiles` | Consumable behavior profile | Consumables | Migration exists; runtime still uses hard-coded food-name behavior |
| `item_consumable_requirements` | Consumable-use requirements | Consumables | No runtime consumer yet |
| `item_consumable_effects` | Declarative consumable effects | Consumables | No runtime consumer yet |
| `item_tool_capabilities` | Declarative tool capability metadata | HandEquipment | No runtime consumer yet |

Primary migration evidence:

- `integrations/mmo-project/prototype/sql/017_item_consumable_profiles.sql`
- `integrations/mmo-project/prototype/sql/018_item_tool_capabilities.sql`
- MMO Project copies under `prototype/sql/017_item_consumable_profiles.sql` and
  `prototype/sql/018_item_tool_capabilities.sql`
- MMO Project ownership note: `prototype/sql/MODULE_OWNERSHIP.md`

## Current Feature Ownership

### Items

Files and symbols:

- `host/Contracts/ItemContracts.cs`
- `host/Features/Items/ItemAuthoringFeature.cs`
- `host/Features/Items/ItemSchemaRequirements.cs`
- `host/Persistence/BasicItemRepository.cs`
- `host/Services/BasicItemAuthoringService.cs`
- `host/Services/BasicItemValidator.cs`
- `content-studio/scripts/main.gd`

The Items feature owns a base-only view over `item_definitions`. It treats any
row with `equipment_slot_id`, `required_strength != 1`, or a consumable profile
as not basic through `EnsureBasicEditable` and `IsBasicRecord`.

Risk: `BasicItemRepository.SaveDraftAsync` writes `equipment_slot_id = null` and
`required_strength = 1` on save. It does not explicitly delete equipment child
rows, but it refuses known non-basic rows before saving. Under the current split,
this is safe only because the tab conflict checks are correct.

### Consumables

Files and symbols:

- `host/Contracts/ConsumableContracts.cs`
- `host/Features/Consumables/ConsumableAuthoringFeature.cs`
- `host/Features/Consumables/ConsumableSchemaRequirements.cs`
- `host/Persistence/ConsumableItemRepository.cs`
- `host/Services/ConsumableItemAuthoringService.cs`
- `host/Services/ConsumableItemValidator.cs`
- `content-studio/scripts/consumable_editor.gd`

The Consumables feature owns `item_consumable_profiles`,
`item_consumable_requirements`, and `item_consumable_effects`. It also writes
base item columns and currently treats consumability as mutually exclusive with
equipment by requiring `equipment_slot_id is null` and `required_strength == 1`
in `EnsureConsumableEditable` / `IsConsumableEditable`.

Risk: `ConsumableItemRepository.SaveDraftAsync` upserts the base item with
`equipment_slot_id = null` and `required_strength = 1`. That is incompatible
with the target aggregate, where consumable behavior is an optional
specialization and should not imply "not equipment forever."

### Equipment

Files and symbols:

- `host/Contracts/EquipmentContracts.cs`
- `host/Features/Equipment/EquipmentAuthoringFeature.cs`
- `host/Features/Equipment/EquipmentSchemaRequirements.cs`
- `host/Persistence/EquipmentItemRepository.cs`
- `host/Services/EquipmentItemAuthoringService.cs`
- `host/Services/EquipmentItemValidator.cs`
- `content-studio/scripts/equipment_editor.gd`

The Equipment feature owns wearable equipability, requirements, modifiers, and
combat bonuses. It loads `item_combat_profiles` but blocks hand-held
weapons/tools from publication and delete with `weapon_or_tool_requires_t3b`.

Risk: `EquipmentItemRepository.SaveDraftAsync` calls
`DeleteEquipmentMetadataAsync` when `draft.Equippable` is false. That deletes
`item_skill_requirements`, `item_skill_modifiers`, `item_combat_profiles`, and
`item_combat_bonuses`. It does not know about `item_tool_capabilities`, because
T3A predates that table. The current T3A cleanup is good for equipment-only
metadata but would need to preserve independent tool capability rows.

### Weapons and Tools

Files and symbols:

- `host/Contracts/HandEquipmentContracts.cs`
- `host/Features/HandEquipment/HandEquipmentAuthoringFeature.cs`
- `host/Features/HandEquipment/HandEquipmentSchemaRequirements.cs`
- `host/Persistence/HandEquipmentRepository.cs`
- `host/Services/HandEquipmentAuthoringService.cs`
- `host/Services/HandEquipmentItemValidator.cs`
- `host/Services/HandEquipmentDomainRules.cs`
- `content-studio/scripts/hand_equipment_editor.gd`

The HandEquipment feature owns hand slots, weapon profiles, combat bonuses, and
tool capabilities. It includes preview-signature protection, but its public
model still says tools are hand equipment.

Risk: `HandEquipmentRepository.DeleteAllEquipmentMetadataAsync` deletes
`item_tool_capabilities`. `Normalize(... equippable=false ...)` clears tool
capabilities, `ValidateNotEquippable` rejects them, and
`hand_equipment_editor.gd` sends `"tool_capabilities": []` unless the item is
both equippable and assigned to a hand slot. This is the strongest current
disagreement with the target model.

U1 correction: this risk is addressed by splitting equipment-metadata cleanup
from tool-capability replacement, preserving tool rows when equipability is
removed, and dropping the obsolete hand-slot-only schema guards.

## Current Routes

Current item routes are separate public aggregate routes:

- `/api/v1/items`
- `/api/v1/consumables`
- `/api/v1/equipment`
- `/api/v1/hand-equipment`

All four route groups ultimately mutate the same `item_definitions` row. The
routes use the same coarse optimistic concurrency value,
`expected_updated_at_utc`, but only Weapons and Tools has a `preview_signature`.
Basic, Consumables, and Equipment protect against changed form state in Godot
with local form signatures only.

The split means a publish, disable, delete, or draft save can validate only the
subset visible to that tab. That is now insufficient for a composable item
aggregate.

## Current Godot Tabs

Files:

- `content-studio/scripts/main.gd`
- `content-studio/scripts/consumable_editor.gd`
- `content-studio/scripts/equipment_editor.gd`
- `content-studio/scripts/hand_equipment_editor.gd`
- `content-studio/scripts/authoring_host_client.gd`
- `content-studio/scripts/authoring_workspace_support.gd`
- `content-studio/scenes/Main.tscn`

The tabs duplicate identity, icon, publication, operation, validation, change
list, delete, and apply flows. They also force maintainers to understand
implementation boundaries:

- Basic Items cannot edit rows classified as equipment or consumable.
- Consumables treats equipment metadata as a blocking foreign specialization.
- Equipment can declassify some hand-held items but cannot fully edit hand
  weapon/tool metadata.
- Weapons and Tools requires equipability and a hand slot before tool
  capabilities can appear in the payload.

This UI shape no longer matches the desired model that consumable, equipment,
weapon, and tool behavior are optional sections of one item.

## Runtime Assumptions

### Runtime-enabled items

`prototype/server/features/inventory/persistence/RuntimeItemDefinitionRepository.cs`
loads only `runtime_enabled = true` item rows into the active runtime item
definition catalog. `prototype/sql/015_item_runtime_publication.sql` also guards
live inventory, equipment, and ground-item references so they point only at
runtime-enabled items and prevents disabling rows that are still live.

### Inventory and equipment use

`InventoryItemUseService.UseInventoryItemCoreAsync` first handles a hard-coded
food-name dictionary via `FoodRestoreDefinitions`. Otherwise it calls
`CharacterInventoryRepository.TryEquipInventoryItemAsync`.

`TryEquipInventoryItemAsync` treats a null or blank `equipment_slot_id` as
`unimplemented_non_equipment_use`. It checks `item_skill_requirements`, requires
stack count 1 to equip, moves the item from inventory to
`character_equipment`, and returns the equipped slot.

### Consumables

Despite the authoring schema, current MMO Project runtime food use is still
name-driven in `InventoryItemUseService.FoodRestoreDefinitions`. The T2
declarative consumable tables are seeded to match the legacy food ranges, but
the active runtime consumer has not yet switched to `item_consumable_profiles`.

### Weapon profiles

`CharacterCombatProfileResolver.WeaponSlotId` is `right_hand`.
`ItemCombatProfileRepository.LoadAllProfilesAsync` rejects runtime-enabled item
combat profiles unless the item is configured for `right_hand`.
`CombatContentStartupValidator.ValidateItemProfiles` emits
`combat.item.profile_slot_mismatch` for non-right-hand profiles and
`combat.item.right_hand_profile_missing` for every runtime-enabled `right_hand`
item without a valid profile.

Current authoritative rule: published weapon profiles are valid only for
`equipment_slot_id = right_hand`. Left-hand weapon profiles remain unsupported.
Future `two_handed` support requires a new runtime slot/capability contract.

### Combat bonuses

`CharacterCombatBonusResolver` loads currently equipped items from
`CharacterEquipmentRepository`, then aggregates rows from
`ItemCombatBonusRepository.LoadByItemIdsAsync`. Combat bonuses are therefore
runtime equipment bonuses. They do not apply from inventory-only items.

### Tool capabilities

`prototype/sql/MODULE_OWNERSHIP.md` says `item_tool_capabilities` is declarative
authoring metadata for future gathering/tool-use features. Repository search
found no runtime consumer outside migrations and Content Studio handoff files.

Current authoritative runtime fact: no existing game runtime path requires a
tool-capable item to be equipped, because no runtime path consumes
`item_tool_capabilities` at all. The hand/equipability restriction is an
authoring schema/UI restriction, not a runtime gameplay requirement.

### Client visuals

Inventory icons load `icon_texture_path` directly in
`InventoryPanelController._load_icon`. Player equipment visuals are derived from
equipment slot and item display-name derived asset keys in
`PlayerComposite.SLOT_TO_DIRECTORY`, `SLOT_TO_NODE_PATH`, and the layer asset
resolution code. There is no explicit persisted equipped visual override yet.

## Required Audit Questions

### 1. Current Aggregate Ownership

Current ownership is split by feature, but all features share
`item_definitions`. Items owns base-only rows. Consumables owns consumable child
tables plus base columns. Equipment owns wearable metadata, requirements,
modifiers, and combat bonuses. HandEquipment owns hand slots, weapon profile,
combat bonuses, and tool capabilities.

Overlapping ownership exists for `item_definitions.item_name`,
`icon_texture_path`, `equipment_slot_id`, `required_strength`, `runtime_enabled`,
`updated_at`, `item_skill_requirements`, `item_skill_modifiers`, and
`item_combat_bonuses`.

### 2. Tool-Capability Dependency

Current dependencies:

- Schema: `item_tool_capabilities_hand_slot_guard` rejects tool capabilities
  unless `item_definitions.equipment_slot_id` is `right_hand` or `left_hand`.
- Schema: `item_definitions_tool_capability_slot_guard` rejects moving a
  tool-capable item to a non-hand slot or to no slot.
- Host validator: `HandEquipmentItemValidator.ValidateNotEquippable` rejects
  tool capabilities on non-equippable drafts.
- Host validator: `ValidateEquippableAsync` rejects weapon/tool specialization
  on non-hand slots.
- Host repository: `HandEquipmentRepository.SaveDraftAsync` deletes tool
  capabilities when equipability is false or the slot is not hand-held.
- Godot UI: `hand_equipment_editor.gd` disables tool rows outside hand slots
  and sends no tool capabilities unless `equippable && hand_slot`.
- Runtime: no consumer currently requires a tool-capable item to be equipped.

No current code requires tool capabilities to be specifically `right_hand`, but
the current schema requires either `right_hand` or `left_hand`.

### 3. Metadata Deletion Behavior

Current behavior when equipability is removed:

- Basic save resets `equipment_slot_id` and `required_strength`, after refusing
  known non-basic rows.
- Consumable save resets `equipment_slot_id` and `required_strength`, after
  refusing equipment rows.
- Equipment save with `equippable = false` deletes requirements, modifiers,
  combat profile, and combat bonuses.
- HandEquipment save with `equippable = false` deletes requirements, modifiers,
  combat profile, combat bonuses, and tool capabilities.
- HandEquipment save to a non-hand slot deletes combat profile and tool
  capabilities.
- No persisted equipped visual override exists to delete.

Corrected rule to lock: removing equipability clears equipment-only metadata
and weapon-only metadata, but preserves independent tool capabilities. Tool
capability removal must be an explicit tool-capability edit.

### 4. Weapon Eligibility

The current runtime locks active weapon profile eligibility to
`equipment_slot_id = right_hand`. Left-hand weapon profiles are invalid for
publication. The unified authoring model should expose weapon profile controls
only when the selected slot is weapon-capable. Initially, the only
weapon-capable slot is `right_hand`; future `two_handed` is deferred until MMO
Project adds runtime support.

### 5. Combat Bonuses

Combat bonuses should remain equipment metadata in the first unified model.
They are available for equipable items in any supported equipment slot, including
armour, shields, and weapons. They should remain absent for non-equipable items
because the runtime aggregates bonuses from equipped items only.

### 6. Tool Discovery At Runtime

No runtime tool resolver exists yet. The future resolver should read published
tool capabilities from both equipped items and inventory items. It should match
the requested capability, rank by effectiveness, and select deterministically.
Equipability should provide equipment-slot convenience, not exclusive access to
tool behavior.

Locked policy for later runtime work: rank higher `power_tier` first; for equal
power, prefer equipped items over inventory items; then use equipment slot sort
or inventory slot index; then stable `item_id`.

### 7. Consumable Interaction

The schema for consumables is structurally attached only to `item_definitions`,
so it can become an independent optional specialization. The current host and UI
do not allow that independence: `ConsumableItemRepository` and
`ConsumableItemAuthoringService` require consumables to have no equipment
metadata, while Equipment and HandEquipment reject rows with
`HasConsumableProfile`.

### 8. Publication Lifecycle

Current publication is per route. Each route sets `runtime_enabled`, but
validation sees only that route's specialization. Live-reference checks are
shared through `BasicItemRepository.HasLiveReferencesAsync` and published
consumable result checks. HandEquipment and Mob authoring use server-issued
preview signatures; earlier item routes do not.

Unified item authoring should use one saved aggregate token
(`item_definitions.updated_at`) and one preview signature over the complete
normalized draft. Publishing validates the complete aggregate. Disable and save
draft still respect live-reference guards for published rows.

### 9. Unified API

The preferred target route structure fits the current host style:

```text
GET  /api/v1/items/options
GET  /api/v1/items
GET  /api/v1/items/{itemId}
POST /api/v1/items/{itemId}/preview
PUT  /api/v1/items/{itemId}/draft
POST /api/v1/items/{itemId}/publish
POST /api/v1/items/{itemId}/disable
POST /api/v1/items/{itemId}/delete
```

Existing `/api/v1/consumables`, `/api/v1/equipment`, and
`/api/v1/hand-equipment` routes should become compatibility adapters during the
migration. They must load the full aggregate, apply only the legacy subset, and
preserve hidden specializations.

### 10. Unified Godot Workspace

One contextual Items workspace should replace the conceptual separation between
Basic Items, Consumables, Equipment, and Weapons and Tools. Contextual sections:

- Identity and Inventory
- Consumable Behavior
- Equipability
- Equipped Appearance
- Requirements and Modifiers
- Combat Bonuses
- Weapon Profile
- Tool Capabilities
- Validation and Changes

Visibility rules:

- Consumable behavior enabled shows consumable profile/effects/requirements.
- Equipability enabled shows slot, equip requirements, modifiers, visual
  preview, and combat bonuses.
- Weapon-capable slot selected shows the weapon-profile section.
- Tool capabilities show whenever present or explicitly enabled, regardless of
  equipability.

### 11. Migration Safety

The plan should not recreate tables. Existing `item_id` values, child rows,
timestamps, drafts, and published rows must survive. The migration should remove
or replace the hand-slot guard triggers for `item_tool_capabilities`, keep
`item_tool_capabilities` rows, and update host behavior before exposing the new
UI path.

### 12. Modular Implementation

The public API and Godot workflow should unify. Internal implementation should
remain modular:

- base identity/inventory metadata helper
- consumable specialization validator/persistence helper
- equipment specialization validator/persistence helper
- weapon-profile validator/persistence helper
- tool-capability validator/persistence helper
- publication/live-reference helper

This avoids one oversized service while still making one aggregate the public
authoring contract.

## Gaps Between Current Behavior And Target Model

- Tool capabilities cannot currently exist on non-equipable items.
- Tool capabilities cannot currently exist on wearable non-hand equipment.
- Removing equipability in the HandEquipment flow deletes tool capabilities.
- Consumables are treated as a mutually exclusive authoring kind.
- Publication can validate only one tab's subset.
- Basic/Consumable/Equipment routes do not use server-issued preview signatures.
- Multiple Godot tabs duplicate identity, publication, change, validation, and
  delete flows.
- Runtime uses hard-coded food-name behavior instead of authored consumable
  profiles.
- Runtime has no `item_tool_capabilities` consumer yet.
- Equipped visuals remain derived from display name and slot.

## Evidence Index

Content Studio:

- `host/Persistence/BasicItemRepository.cs`: `SaveDraftAsync`,
  `EnsureBasicEditable`, `HasLiveReferencesAsync`
- `host/Persistence/ConsumableItemRepository.cs`: `SaveDraftAsync`,
  `EnsureConsumableEditable`, `DeleteAsync`
- `host/Persistence/EquipmentItemRepository.cs`: `SaveDraftAsync`,
  `DeleteEquipmentMetadataAsync`, `HasEquipmentMetadata`
- `host/Persistence/HandEquipmentRepository.cs`: `SaveDraftAsync`,
  `DeleteAllEquipmentMetadataAsync`, `ReplaceToolCapabilitiesAsync`
- `host/Services/HandEquipmentItemValidator.cs`: `ValidateNotEquippable`,
  `ValidateEquippableAsync`, `ValidatePublication`
- `host/Services/EquipmentItemAuthoringService.cs`: `weapon_or_tool_requires_t3b`
- `content-studio/scripts/hand_equipment_editor.gd`: `_payload`,
  `_belongs_in_hand_equipment_catalog`, `_update_guidance`
- `content-studio/scripts/authoring_host_client.gd`: split route methods and
  startup loading chain

MMO Project:

- `prototype/sql/018_item_tool_capabilities.sql`:
  `item_tool_capabilities_hand_slot_guard`,
  `item_definitions_tool_capability_slot_guard`
- `prototype/sql/MODULE_OWNERSHIP.md`: future runtime ownership note for tools
- `prototype/server/features/inventory/application/InventoryItemUseService.cs`:
  `FoodRestoreDefinitions`, `UseInventoryItemCoreAsync`
- `prototype/server/features/inventory/persistence/CharacterInventoryRepository.cs`:
  `TryEquipInventoryItemAsync`, `TryConsumeFoodItemAsync`
- `prototype/server/features/combat/application/CharacterCombatProfileResolver.cs`:
  `WeaponSlotId`
- `prototype/server/features/combat/persistence/ItemCombatProfileRepository.cs`:
  `LoadAllProfilesAsync`
- `prototype/server/features/combat/application/CombatContentStartupValidator.cs`:
  `ValidateItemProfiles`
- `prototype/server/features/combat/application/CharacterCombatBonusResolver.cs`:
  `ResolveAsync`, `Aggregate`
- `prototype/client/screens/game/controllers/inventory_panel_controller.gd`:
  `_load_icon`
- `prototype/client/actors/player/player_composite.gd`: slot-to-layer visual
  conventions
