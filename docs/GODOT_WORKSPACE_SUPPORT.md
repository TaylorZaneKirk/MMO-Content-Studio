# Godot Authoring Workspace Support

`AuthoringWorkspaceSupport` defines the reusable UI behavior that every content
workspace needs:

- preview signature, operation, and applicability state
- invalidation when a form changes
- preview-before-apply matching
- apply-button enablement and labeling
- exact persisted-change rendering
- validation-message rendering
- publication-operation display names

The support object is intentionally UI-only. It does not own HTTP requests,
feature payloads, mutation routing, database access, or visual-preview logic.

A future workspace should create one support instance and call:

1. `clear_preview` whenever authored form state changes.
2. `accept_preview` when a matching host preview arrives.
3. `can_apply` immediately before sending a mutation.
4. `render_changes` and `render_validation` for host feedback.

This preserves the safety invariant that a mutation can only be submitted when
the latest valid preview matches both the selected operation and the exact
current form signature.

## Adoption sequence

This PR adds the reusable foundation without changing the existing editors.
Items, Consumables, and Equipment will migrate to it after PR #6's Godot HTTP
transport passes its development-machine smoke test. Keeping those changes
separate avoids stacking two unverified runtime refactors in the same UI path.

The next new workspace may use this support object immediately, even before the
existing editor migration is complete.
