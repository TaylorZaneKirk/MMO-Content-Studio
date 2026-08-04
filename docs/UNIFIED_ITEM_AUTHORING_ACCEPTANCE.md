# Unified Item Authoring Acceptance

## U1

U1 is complete. Tool capabilities are item-level metadata, not hand-equipment
metadata. Removing equipability clears equipment and weapon metadata while
preserving tool capabilities unless the submitted tool-capability collection is
explicitly empty.

## U2

U2 is complete when the host exposes one complete item aggregate and routes all
item specialization mutations through one unified item service and repository.

Accepted U2 behavior:

- `/api/v1/items/options` returns all option data needed by the future unified
  Godot Items workspace.
- `/api/v1/items` lists complete item summaries with derived classification
  labels, not persisted exclusive item kinds.
- `/api/v1/items/{itemId}` loads identity, publication state, optional
  consumable behavior, optional equipment metadata, optional weapon profile,
  independent tool capabilities, and one aggregate `updated_at_utc`.
- `/api/v1/items/{itemId}/preview` validates the complete normalized draft and
  returns one preview signature.
- `/api/v1/items/{itemId}/draft`, `publish`, `disable`, and `delete` mutate
  through `UnifiedItemAuthoringService` and `UnifiedItemRepository`.
- U2 temporarily kept legacy Basic Items payloads and the Consumables,
  Equipment, and Weapons & Tools mutation routes as compatibility adapters over
  the unified service.
- Those adapters preserved hidden specializations while replacing only the
  subset represented by the legacy request.
- U4 removed the compatibility adapters after the consolidated Godot Items
  workspace became the normal workflow.
- Unified routes require server preview signatures for save, publish, disable,
  and delete.

## U4

U4 is complete when `/api/v1/items` is the only public item-authoring route
family, every item mutation flows through `UnifiedItemAuthoringService`, every
item persistence mutation flows through `UnifiedItemRepository`, duplicate
specialization catalog/schema providers are gone, and the legacy Godot item
editor scripts are removed.

Accepted U4 behavior:

- `/api/v1/consumables`, `/api/v1/equipment`, and `/api/v1/hand-equipment` are
  no longer registered.
- `/api/v1/items` accepts only complete unified request contracts.
- The Godot shell exposes one Items tab for all item specializations.
- Old item specialization editors are not referenced by scenes or the host
  client.
- The unified item schema-health provider covers consumable, equipment,
  weapon, combat-bonus, and tool-capability tables.

## Pending

- U5: runtime tool resolution across inventory and equipped items.

Runtime tool execution, gathering, crafting, two-handed equipment, and broad
item schema redesign remain deferred.
