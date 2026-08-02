# Godot Authoring Workspace Support

`AuthoringWorkspaceSupport` centralizes the UI behavior shared by every current
content workspace:

- preview signature, operation, and applicability state
- invalidation when a form changes
- preview-before-apply matching
- apply-button enablement and labeling
- exact persisted-change rendering
- validation-message rendering
- publication-operation display names

Items, Consumables, and Equipment keep ownership of their forms, payloads,
preview requests, mutations, status wording, and visual previews. They delegate
only the lifecycle and feedback behavior that must remain consistent across all
workspaces.

A future workspace should create one support instance and call:

1. `clear_preview` whenever authored form state changes.
2. `accept_preview` when a matching host preview arrives.
3. `can_apply` immediately before sending a mutation.
4. `render_changes` and `render_validation` for host feedback.

This preserves the existing safety invariant: a mutation can only be submitted
when the latest valid preview matches both the selected operation and the exact
current form signature.
