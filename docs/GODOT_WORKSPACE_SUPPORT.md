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

Items, Consumables, Equipment, and Weapons & Tools now keep ownership of their
forms, payloads, preview requests, mutations, status wording, and visual
previews while delegating the shared lifecycle and feedback behavior to one
support instance.

Every existing editor follows the same sequence:

1. `clear_preview` whenever authored form state changes.
2. `accept_preview` when a matching host preview arrives.
3. `can_apply` immediately before sending a mutation.
4. `render_changes` and `render_validation` for host feedback.
5. `operation_name` for consistent publication-operation labels.

New workspaces, including any future workspace, should use the same support
object instead of declaring their own preview state or feedback renderers. This
preserves the safety invariant: a mutation can only be submitted when the latest
valid preview matches both the selected operation and the exact current form
signature or host-issued preview signature.
