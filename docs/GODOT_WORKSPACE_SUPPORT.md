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

The U3/U4 Items workspace and the Mobs workspace established the current
pattern: each editor keeps ownership of its form, payloads, preview requests,
mutations, status wording, and visual preview while delegating the shared
lifecycle and feedback behavior to one support instance. The T5D NPCs workspace
follows the same pattern. The legacy Consumables, Equipment, and Weapons & Tools editor scripts were removed in U4 after the unified Items workspace became the only normal item authoring surface.

Every existing editor follows the same sequence:

1. `clear_preview` whenever authored form state changes.
2. `accept_preview` when a matching host preview arrives.
3. `can_apply` immediately before sending a mutation.
4. `render_changes` and `render_validation` for host feedback.
5. `operation_name` for consistent publication-operation labels.

New workspaces should use the same support
object instead of declaring their own preview state or feedback renderers. This
preserves the safety invariant: a mutation can only be submitted when the latest
valid preview matches both the selected operation and the exact current form
signature or host-issued preview signature.

The NPCs workspace follows the same rule over `/api/v1/npcs`: list/search,
create, load, preview, save draft, publish, disable, and delete all flow through
`AuthoringHostClient`; every meaningful form edit clears the accepted preview;
and publish/disable/delete use the saved aggregate concurrency token plus the
server preview signature.
