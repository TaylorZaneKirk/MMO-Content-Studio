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
- Legacy Basic Items payloads and the Consumables, Equipment, and Weapons &
  Tools mutation routes are compatibility adapters over the unified service.
- Compatibility adapters preserve hidden specializations while replacing only
  the subset represented by the legacy request.
- Unified routes require server preview signatures. Legacy compatibility routes
  preserve their pre-U2 request shapes until U3/U4 and therefore use unified
  full-aggregate validation and concurrency, but not uniformly the new
  signature field.
- The old Godot tabs remain present for U2. The consolidated Godot Items
  workspace is U3.

## Pending

- U3: unified Godot Items workspace.
- U4: retire obsolete specialization routes and tabs.
- U5: runtime tool resolution across inventory and equipped items.

Runtime tool execution, gathering, crafting, two-handed equipment, and broad
item schema redesign remain deferred.
