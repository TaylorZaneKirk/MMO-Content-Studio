# Unified Item Authoring Plan

## Goal

Consolidate Basic Items, Consumables, Equipment, and Weapons and Tools into one
public item aggregate and one contextual Godot Items workspace while preserving
existing authored data, routes, migrations, and runtime behavior during a staged
migration.

This plan is implementation-ready, but it is not an implementation claim.

## Locked Unified Aggregate

Target public aggregate:

```text
ItemDefinition
  identity
  inventory_icon
  publication_state
  optional consumable_behavior
  optional equipment_metadata
    equip_requirements
    skill_modifiers
    equipped_visual
    combat_bonuses
    optional weapon_profile
  tool_capabilities
  updated_at_utc
```

Locked rules:

- Every tool is an item.
- Tool capability does not require equipability.
- Tool capability does not require the item to be currently equipped.
- Equipable tools are useful because they can occupy a slot instead of only an
  inventory slot.
- A weapon is an equipable item with a weapon profile.
- Weapon profiles are contextual to weapon-capable slots.
- Initial weapon-capable slot set is `right_hand`.
- Left-hand weapon profiles remain publication-blocked.
- Future `two_handed` support is deferred until MMO Project has a slot and
  runtime loadout contract for it.
- Armour and other equipment may have combat bonuses without weapon profiles.
- Combat bonuses remain equipment metadata and apply only while equipped.
- Removing equipability clears equipment-only and weapon-only metadata.
- Removing equipability must preserve tool capabilities.
- Tool capability removal requires an explicit tool-capability edit.
- Consumable, equipment, weapon, and tool specializations are optional portions
  of one aggregate.
- One aggregate root concurrency token governs all item specializations.
- One preview signature covers the complete normalized item draft.
- One publication operation validates the complete aggregate.

## Specialization Ownership

The public boundary should be one `Items` feature. Internal modules should stay
small and focused:

| Internal module | Owns |
| --- | --- |
| Base item module | `item_id`, display name, icon path, publication state, aggregate token |
| Consumable module | `item_consumable_profiles`, requirements, effects |
| Equipment module | slot, equip requirements, skill modifiers, combat bonuses |
| Weapon module | `item_combat_profiles` and weapon-capable slot validation |
| Tool module | `item_tool_capabilities` independent of equipability |
| Publication module | full-aggregate validation, live references, preview signatures |
| Asset module | item icon imports and future equipped visual asset resolution |

The modules can share repositories or transactional helpers, but the public API
must load and save one complete item definition.

## Proposed Contract

Introduce unified contracts under the Items feature, for example:

```csharp
public sealed record ItemDefinition(
    string ItemId,
    string DisplayName,
    string IconTexturePath,
    string PublicationState,
    ConsumableBehaviorDefinition? ConsumableBehavior,
    EquipmentMetadataDefinition? Equipment,
    IReadOnlyList<ToolCapabilityDefinition> ToolCapabilities,
    DateTimeOffset UpdatedAtUtc,
    string? AssetPreviewFilePath);
```

Draft and preview requests should carry the same complete shape:

```csharp
public sealed record SaveItemDraftRequest(
    string DisplayName,
    string IconTexturePath,
    ConsumableBehaviorDraft? ConsumableBehavior,
    EquipmentMetadataDraft? Equipment,
    IReadOnlyList<ToolCapabilityDraft> ToolCapabilities,
    DateTimeOffset? ExpectedUpdatedAtUtc,
    string? PreviewSignature);
```

`EquipmentMetadataDraft` contains slot, requirements, modifiers, combat bonuses,
and optional weapon profile. Tool capabilities remain outside equipment so they
can exist on inventory-only items.

The catalog summary should show derived labels, not stored item kinds:

- `Basic`
- `Consumable`
- `Equipment`
- `Weapon`
- `Tool`
- combinations such as `Consumable + Tool` or `Weapon + Tool`

## Schema Changes

Use additive or compatibility-safe migrations. Do not recreate item tables.

Required schema correction:

1. Drop `item_tool_capabilities_hand_slot_guard`.
2. Drop `item_definitions_tool_capability_slot_guard`.
3. Drop or retire `ensure_item_tool_capabilities_hand_slot()`.
4. Drop or retire `prevent_non_hand_slot_with_tool_capabilities()`.
5. Keep `item_tool_capabilities` unchanged as a child table of
   `item_definitions`.

No data migration is required for existing tool rows; the migration removes an
invalid restriction around them. Existing `item_id`, publication state,
timestamps, weapon profiles, equipment metadata, consumable rows, and tool rows
must remain intact.

Optional hardening during U1:

- Add a schema-health provider for the unified Items boundary that requires the
  absence of the old tool hand-slot triggers after the correction migration is
  applied.
- Keep existing `item_tool_capabilities` primary key and order constraints.
- Keep the current `item_combat_profiles` table name and treat it as the weapon
  profile storage table. Do not rename it in this consolidation.

## Validation Rules

Draft validation should allow incomplete work unless a value would be impossible
to persist safely. Publication validation should be strict.

Base:

- `item_id` remains stable and nonempty.
- display name and icon path are required for publish.
- icon path may warn for draft and error for publish when unresolved.

Consumable behavior:

- Optional.
- If enabled for publish, require valid action, consume quantity, and at least
  one effect.
- Result item must exist; for publish, result item must be published.
- Consumable behavior does not imply non-equipable.

Equipment:

- Optional.
- If enabled, slot must exist.
- Requirements and modifiers must reference known skills.
- Combat bonuses are valid only when equipment metadata is enabled.
- Removing equipment metadata clears slot, required strength, requirements,
  modifiers, combat bonuses, equipped visual metadata, and weapon profile.
- Removing equipment metadata preserves tool capabilities.

Weapon profile:

- Optional inside equipment metadata.
- Visible and editable only when a weapon-capable slot is selected.
- Initial weapon-capable slot: `right_hand`.
- For publish, `right_hand` items must have a valid weapon profile until MMO
  Project relaxes `combat.item.right_hand_profile_missing`.
- Left-hand weapon profiles are blocked for publish.
- Attack speed remains `attack_speed_units`; each unit is 600 ms.
- Range remains logical tiles.

Tool capabilities:

- Optional and independent of equipment metadata.
- Capability ID must be known or explicitly accepted by the registry.
- Duplicate capability IDs are errors.
- `power_tier` remains 1-1000.
- Ordered rows remain 0-63.
- Tool capability rows persist for non-equipable items.

Publication:

- One publish validates the complete aggregate.
- One disable checks live inventory, equipment, ground-item, and published
  consumable result references.
- Delete requires disabled/unpublished state and must rely on foreign-key
  failures for remaining references.
- Publish/disable/delete require the latest `expected_updated_at_utc` and the
  server-issued preview signature.

## API Migration

Target final route structure:

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

Compatibility strategy:

- Keep existing `/api/v1/items`, `/api/v1/consumables`, `/api/v1/equipment`,
  and `/api/v1/hand-equipment` routes during U2 and U3.
- Convert legacy routes into adapters over the unified service.
- Each adapter must load the full aggregate, apply the legacy subset, preserve
  hidden specializations, preview the complete normalized result, and mutate
  only through the unified repository.
- Legacy route mutation responses should include enough existing fields to keep
  current Godot tabs working during transition.
- Mark legacy routes deprecated in docs after the unified workspace is usable.
- Remove legacy routes only in U4 after tests prove no in-repo callers depend
  on them.

## Godot Workspace Consolidation

Create one contextual Items workspace. It should replace the conceptual split
between Basic Items, Consumables, Equipment, and Weapons and Tools.

Recommended layout:

```text
Items catalog/filter
  |
  Identity and Inventory
  Consumable Behavior
  Equipability
  Equipped Appearance
  Requirements and Modifiers
  Combat Bonuses
  Weapon Profile
  Tool Capabilities
  Validation and Changes
```

Controls:

- Identity and Inventory is always visible.
- Consumable Behavior has an explicit enable control.
- Equipability has an explicit enable control.
- Equipped Appearance, Requirements and Modifiers, and Combat Bonuses show only
  when Equipability is enabled.
- Weapon Profile shows only when a weapon-capable slot is selected.
- Tool Capabilities shows whenever the item has tool rows or the author enables
  the section.
- Preview, Save Draft, Publish, Disable, Delete, change list, validation list,
  and apply controls are shared.

The existing paper-doll preview helper should remain shared. It should be shown
only for equipable items with supported player-layer slots until explicit visual
overrides exist.

## Runtime Tool-Resolution Contract

U5 should introduce a server-authoritative tool resolver without implementing
gathering or crafting itself.

Input:

- character id
- requested capability id
- optional activity context

Eligible sources:

- equipped items with matching published `item_tool_capabilities`
- inventory items with matching published `item_tool_capabilities`

Selection:

1. Filter to runtime-enabled item definitions.
2. Filter to matching `capability_id`.
3. Rank by higher `power_tier`.
4. For equal `power_tier`, prefer equipped items.
5. For equipped ties, use equipment slot sort order then slot id.
6. For inventory ties, use inventory slot index.
7. Final tie-breaker: stable `item_id`.

Equipability therefore provides slot convenience and equal-power preference, not
exclusive tool access.

## Migration Safety

Preserve existing data:

- no destructive table recreation
- no item ID renames
- no child-row wipe during migration
- no timestamp reset solely for migration
- no forced publication-state changes
- existing weapon profiles stay in `item_combat_profiles`
- existing combat bonuses stay in `item_combat_bonuses`
- existing tool capabilities stay in `item_tool_capabilities`
- existing consumable profiles/effects/requirements stay in their T2 tables

Repository mutation safety:

- Unified save must update the base row and replace only child collections that
  are present in the complete draft.
- Legacy adapters must preserve children that their old payloads cannot express.
- Turning off equipability deletes only equipment-owned and weapon-owned child
  rows.
- Turning off consumable behavior deletes only consumable child rows.
- Clearing tool capability rows happens only when the unified draft explicitly
  submits an empty tool-capability collection for an item that previously had
  tools.

## Testing Strategy

Source-contract tests:

- unified planning docs exist and contain the locked model
- audit names current destructive behavior and tool/equipability coupling
- plan names schema-trigger removal and compatibility adapters
- plan states tool capabilities remain independent of equipability
- plan states one preview signature over the complete item draft
- README, ROADMAP, and ARCHITECTURE link the unified plan without claiming it is
  implemented

Host tests during implementation:

- schema-health checks for corrected tool constraints
- repository save preserves tool capabilities when equipability is removed
- repository save preserves consumable/equipment/tool children across legacy
  adapter saves
- unified preview detects changes across every specialization
- stale `expected_updated_at_utc` blocks all mutations
- stale or mismatched preview signature blocks save/publish/disable/delete
- publish validates complete aggregate
- delete remains blocked by live/foreign references

Godot tests during implementation:

- one Items workspace loads complete item aggregates
- contextual sections appear and hide correctly
- non-equipable tool capability rows remain visible/editable
- apply is disabled when the form changes after preview
- validation and change lists scroll and remain reachable

Runtime tests during U5:

- non-equipable tool in inventory resolves by capability
- equipped and inventory tools both resolve
- higher `power_tier` wins
- equal-power equipped item wins over inventory item
- deterministic tie-breaks are stable

## Phased Implementation

### U1 - Domain And Schema Correction

Status: implemented.

- Add a compatibility-safe migration that removes the two tool-hand-slot guard
  triggers and retires their functions.
- Update Content Studio schema-health requirements for tool capabilities.
- Update HandEquipment normalization, validation, repository deletion, and Godot
  payload behavior so non-equipable drafts preserve tool capabilities.
- Add regression tests for preserving tool rows when equipability is removed.
- Do not merge tabs yet.

Exit condition:

> A non-equipable item can retain tool capabilities in storage and through the
> current compatibility routes.

### U2 - Unified Item Host Aggregate

Status: implemented in the host. The unified Godot workspace, route retirement,
and runtime tool resolution remain pending.

- Add unified item contracts and options.
- Add a unified repository orchestration layer that locks `item_definitions`,
  loads all child collections, saves the complete aggregate transactionally, and
  reloads/verifies after commit.
- Add specialization validators behind one unified validator.
- Add preview signatures to all unified mutations.
- Add `/api/v1/items/options` and make `/api/v1/items` return complete unified
  definitions.
- Convert old Consumables, Equipment, and HandEquipment routes into adapters
  over the unified service.
- Preserve existing response shapes for old routes.

Exit condition:

> All current T1-T3B flows still work, but no old route can silently delete a
> hidden specialization.

### U3 - Unified Godot Items Workspace

- Replace the current top-level item specialization tabs with one contextual
  Items workspace.
- Keep old tabs hidden or visibly deprecated behind a temporary developer flag
  if needed for comparison.
- Reuse dynamic-row helpers where practical.
- Reuse paper-doll preview only in the equipable visual section.
- Use one operation panel with scrollable validation and exact changes.
- Load and save complete item drafts only through the unified API.

Exit condition:

> Maintainers can author Basic, Consumable, Equipment, Weapon, Tool, and combined
> item definitions from one Items workspace.

### U4 - Route And Tab Retirement

- Verify no in-repo Godot or tests call the old routes.
- Remove compatibility adapters.
- Remove obsolete feature route registrations and duplicate contracts.
- Keep internal specialization modules and tests.
- Update API docs, roadmap, and acceptance docs to describe the final unified
  boundary.

Exit condition:

> The public API and GUI expose one item aggregate, while internals remain
> modular.

### U5 - Runtime Tool-Resolution Integration

- Add an MMO Project runtime tool-capability repository/read model.
- Add a server-authoritative tool resolver over equipment and inventory.
- Apply deterministic ranking.
- Expose the resolver as a dependency for future gathering, processing,
  interactable objects, traps, and similar activities.
- Do not implement full gathering/crafting gameplay in this phase unless a later
  slice explicitly requests it.

Exit condition:

> Runtime systems can ask for a capability and receive the best deterministic
> eligible item from equipment or inventory.

## Acceptance Criteria

The completed unified authoring work must satisfy:

1. A non-equipable pickaxe can be authored and published with mining capability.
2. An equipable pickaxe can have mining capability and an optional melee weapon
   profile.
3. Unequipping the pickaxe does not prevent inventory-based mining capability.
4. Making an equipable pickaxe non-equipable removes equipment and weapon
   metadata but preserves mining capability.
5. A sword can have a weapon profile without tool capabilities.
6. Armour can have combat bonuses without a weapon profile.
7. A chisel can have a tool capability without equipment metadata.
8. One complete preview shows changes across all item specializations.
9. Concurrent edits from obsolete separate tabs cannot overwrite another
   specialization silently.
10. Publishing validates the entire aggregate, not only the currently visible
    specialization.

## Deprecation And Removal Plan

- U2: legacy routes remain supported as adapters.
- U3: legacy tabs are hidden or marked deprecated after the unified Items
  workspace reaches parity.
- U4: old public routes and obsolete Godot tabs are removed after source tests
  prove no callers remain.
- Keep docs explicit that old routes are temporary compatibility surfaces, not
  separate item domains.

## Deferred Work

- `two_handed` slot and loadout semantics
- explicit equipped visual override schema
- declarative consumable runtime execution
- gathering and crafting activities
- durability, ammo, charges, and instance-scoped tool state
- recipes and production station behavior
- broad ability scripting
- reputation/faction effects on item use

No maintainer decision is required before U1.
