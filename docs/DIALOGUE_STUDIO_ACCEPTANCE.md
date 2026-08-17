# Dialogue Studio Acceptance

Status: D1-D5 Dialogue Studio authoring, graph editing, runtime catalog export,
validator/runtime equivalence, reference safety, and end-to-end verification are
complete. QV3 typed read-only quest/item predicates and QV4 typed choice
effects are implemented; Quest Studio and authored first-quest content remain
deferred.

## D1 - Runtime Audit And Domain Lock

D1 is complete when:

- `docs/DIALOGUE_STUDIO_RUNTIME_AUDIT.md` identifies the current source of
  truth, aggregate shape, node types, speaker/text model, transition model,
  choice model, condition model, effect model, session behavior, NPC linkage,
  runtime validation, client protocol, and existing content inventory.
- `docs/DIALOGUE_STUDIO_DOMAIN_MODEL.md` locks the initial Content Studio
  aggregate, schema direction, identity rules, publication/reference policy,
  graph editor requirements, playthrough-preview strategy, runtime export
  strategy, and future typed extension seam.
- `docs/DIALOGUE_STUDIO_IMPLEMENTATION_PLAN.md` documents D2-D5 and places
  Dialogue Studio before quest foundations.
- `docs/DIALOGUE_STUDIO_ACCEPTANCE.md` defines acceptance criteria without
  claiming implementation.
- README, roadmap, architecture, Godot workspace support, and MMO Project
  integration docs name Dialogue Studio as an integrated Content Studio
  workspace.
- Quest Studio and authored first-quest content are explicitly deferred.
- Source-contract tests preserve the historical D1 boundary and now guard that
  the Godot Dialogue editor stays within current runtime-compatible semantics.
- MMO Project remains read-only.

## D2 - Schema, Contracts, Repository, Validation, And API

Status: complete for host-side authoring.

- Additive dialogue authoring schema exists.
- Feature-owned schema-health provider reports required dialogue tables and
  constraints.
- Compile-time contracts expose a complete graph aggregate.
- `/api/v1/dialogues` supports options, list, load, preview, playthrough
  preview, save draft, publish, disable, and delete.
- Draft save can preserve incomplete graph work while rejecting malformed IDs,
  unsupported node types, unsupported condition/effect types, and broken graph
  references.
- Publish requires current runtime-compatible validation.
- Mutations require root concurrency and server preview signatures.
- Repository writes replace child collections transactionally and advance the
  root timestamp for child-only edits.
- Reference guards block disable when Published NPC definitions reference a
  dialogue and block delete when any NPC definition references it.
- QV3 condition registries expose `quest_status`, `quest_step`, and `has_item`.
- Quest fields are accepted only through typed read-only conditions and typed
  QV4 quest-transition effects.
- Effect registries expose only `start_quest`, `advance_quest`,
  `complete_quest`, `grant_item`, `remove_item`, and `grant_experience`.

## D3 - Godot Dialogue Studio

Status: complete for the Content Studio Godot workspace.

- A top-level **Dialogue** workspace appears after NPCs and before Environment.
- Dialogue is not a separate application.
- The NPC workspace can route to a referenced dialogue without embedding the
  graph editor.
- The workspace lists Draft, Published, and Disabled definitions.
- Users can create, load, edit, validate, preview, save draft, publish, disable,
  and delete dialogue definitions.
- The graph canvas supports the current runtime graph shapes:
  `speaker_text`, `player_choice`, and `end`.
- Node inspector fields are contextual to the selected node type.
- Playthrough preview follows current runtime semantics and does not commit
  effects.
- Validation and exact logical changes remain visible and scrollable.
- Preview/apply lifecycle uses `AuthoringWorkspaceSupport`.
- Dialogue references can route back to NPC definitions through shell-level
  NPC cross-navigation.
- D3 provided no quest, condition, or effect authoring; QV3/QV4 add the typed
  condition and choice-effect controls.

## D4 - MMO Project Runtime Handoff

Status: complete for the generated runtime catalog handoff.

- Existing `test_npc_greeting` is migrated or seeded into Dialogue Studio
  authoring data.
- `MapPublisher export-dialogue-catalog` or equivalent deterministic handoff
  exports only Published definitions.
- The exported JSON remains compatible with `DialogueDefinitionCatalog`.
- NPC `default_dialogue_id` validation resolves against the exported catalog.
- Runtime client protocol payloads remain backward-compatible.
- Active runtime sessions are not hot-reloaded.
- Typed QV3 predicates and QV4 choice effects may be exported. Objective
  progress, story flags, arbitrary scripts, and content locks are not exported.

## D5 - Hardening And Verification

Status: complete for the non-quest runtime-compatible dialogue slice.

- Byte-stable export tests pass.
- Content Studio graph validation and runtime catalog validation agree on
  supported graphs.
- Playthrough preview and `DialogueSessionService` agree on current
  continuation, choice, end-node, close, stale-node, stale-session, and
  duplicate-command semantics.
- A connected Godot client can interact with `test_npc`, open
  `test_npc_greeting`, choose every branch, acknowledge the end node, and close
  manually.
- Recoverable session replacement republishes the current node.
- Manual movement, outgoing combat, world-object interaction, newer NPC
  interaction, incoming hostile attack, player defeat, and terminal cleanup
  close or cancel dialogue as documented.
- Disable/delete guards prevent breaking published NPC references.

## Non-Goals Through D5

- No quest authoring.
- No additional quest condition types beyond QV3 `quest_status`, `quest_step`,
  and `has_item`.
- No quest effects beyond QV4 `start_quest`, `advance_quest`, and
  `complete_quest`.
- No objective progress.
- No quest rewards beyond QV4 item and XP choice effects.
- No quest journal.
- No content-access lock authoring.
- No arbitrary scripting or executable payloads.
- No separate Dialogue application.
- No runtime hot reload.
- No localization, portraits, mini-scenes, emotes, camera focus, or cutscenes.
