# Dialogue Studio Runtime Audit

Status: D1 runtime audit and domain lock.

Source repositories inspected:

- Content Studio: `/home/taylor/MMO Project/tools/MMO-Content-Studio` on `main`
- MMO Project: `/home/taylor/MMO Project` on `master`, read-only

Primary MMO Project dialogue documents:

- `docs/design/OSRS_STYLE_NPC_CONVERSATIONS_AND_QUEST_GATES.md`
- `docs/design/DIALOGUE_FOUNDATION_V1.md`
- `docs/modernization/DIALOGUE_QUEST_AND_CUTSCENE_ROADMAP.md`
- `docs/development/CONTENT_AUTHORING_GUIDE.md`

`OSRS_STYLE_NPC_CONVERSATIONS_AND_QUEST_GATES.md` is the current primary
direction-setting document. It supersedes older "implementation not started"
wording for the dialogue foundation. The implemented baseline is Dialogue
Foundation V1; quest state, authored effects, and content-access gates remain
planned.

## Executive Summary

MMO Project currently stores dialogue definitions in
`prototype/shared/dialogues/catalog.json`. `DialogueDefinitionCatalog` loads
that file during server startup through `Program.cs`, validates graph shape, and
keeps immutable in-memory definitions for the running process. There is no
dialogue database schema, Content Studio dialogue API, graph editor, hot reload,
or runtime query against authoring tables.

The current implemented model is intentionally narrow:

- definition catalog schema version `1`
- `dialogue_id`
- prioritized entry points
- node-owned transitions
- node types `speaker_text`, `player_choice`, and `end`
- speaker metadata per node
- visible choice lists filtered server-side
- empty condition arrays only
- no implemented effects

Dialogue Studio should therefore begin as a top-level Content Studio workspace
that authors the existing non-quest dialogue graph model. It should not create
quest fields, quest predicates, quest effects, rewards, objective progress, or a
quest journal. Future quest-aware conditions and effects must be added only
after MMO Project implements quest foundations and exposes typed runtime
contracts for them.

## Current Source Of Truth

Current dialogue definitions live in:

```text
prototype/shared/dialogues/catalog.json
```

Runtime load path:

```text
Program.cs
  -> AddSingleton(new DialogueDefinitionCatalog(IHostEnvironment))
  -> DialogueDefinitionCatalog.GetDefaultCatalogPath()
  -> ../shared/dialogues/catalog.json
  -> DialogueCatalogDocument
  -> immutable definitions dictionary
```

The server forces startup construction with:

```text
_ = app.Services.GetRequiredService<DialogueDefinitionCatalog>();
```

Current properties:

- File-backed JSON is the only production dialogue definition source.
- Definitions are startup-loaded and immutable during a running server.
- No hot reload exists.
- No generated static-content dialogue catalog is embedded in region manifests.
- No PostgreSQL dialogue authoring or runtime tables exist.
- `NpcInteractionService.CreateDefaultDialogueSessionService` contains a
  fallback in-memory `test_npc_greeting` graph only for legacy/test construction
  when dependency injection does not supply `DialogueSessionService`; production
  composition supplies the file-backed catalog.

## Current Dialogue Aggregate

Implemented `DialogueCatalogDocument`:

- `schema_version`
- `dialogues`

Implemented `DialogueDefinition`:

- `dialogue_id`
- `schema_version`
- `entry_points`
- `nodes`
- optional `metadata`

Implemented `DialogueEntryPoint`:

- `entry_id`
- `node_id`
- `priority`
- `conditions`

Implemented `DialogueNode`:

- `node_id`
- `node_type`
- `speaker`
- `text`
- `next_node_id`
- `dismissible`
- `choices`

Implemented `DialogueChoiceDefinition`:

- `choice_id`
- `text`
- `target_node_id`
- `conditions`

Implemented `DialogueSpeakerDefinition`:

- `speaker_kind`
- `display_name`
- optional `actor_id`

Not implemented in the current aggregate:

- display/editor name separate from metadata
- publication state
- authoring notes
- node canvas coordinates
- localization keys
- text interpolation
- portraits, scene portraits, emotes, camera cues, or audio
- effects
- quest state
- database concurrency token

## Current Node Types

Current node constants are in `DialogueNodeTypes`:

```text
speaker_text
player_choice
end
```

### `speaker_text`

Required for useful runtime behavior:

- `node_id`
- `node_type = "speaker_text"`
- `text`
- usually `speaker`
- usually `next_node_id` unless intentionally terminal by validation gap

Runtime behavior:

- `DialogueSessionService.BuildPresentation` sets `can_continue = true`.
- Continue requests advance to `next_node_id`.
- A missing `next_node_id` produces `missing_transition` at command time.
- Choices are ignored because choices are presented only on `player_choice`.
- It can be an entry-point node.
- It does not close automatically.

### `player_choice`

Required:

- `node_id`
- `node_type = "player_choice"`
- at least one choice
- each choice has `choice_id`, `text`, and `target_node_id`

Runtime behavior:

- `BuildPresentation` filters choices to those with empty condition arrays.
- `can_continue = false`.
- Continue requests fail with `choice_required`.
- Choice requests must match an eligible `choice_id`.
- Selected choices advance to `target_node_id`.
- It can be an entry-point node, though current content does not use that.
- It does not close automatically.

### `end`

Required:

- `node_id`
- `node_type = "end"`
- optional text/speaker

Runtime behavior:

- `next_node_id` is rejected by validation.
- `BuildPresentation` sets `can_continue = true`.
- Continue requests close the dialogue with `acknowledged`.
- End nodes remain visible until acknowledged; they do not disappear on entry.
- It can be an entry-point node by current validation, but that should be a
  warning in Dialogue Studio because it creates an immediate final panel.

Unsupported names include `npc_text`, `player_text`, `choice`, `branch`,
`continue`, and `end_node`.

## Speaker Model

The runtime stores speaker metadata on each node. `DialogueSourceRef` separately
tracks the interaction source for the session.

Supported source kinds:

```text
Npc
Mob
BossEncounter
WorldObject
Tutorial
Quest
ServerScript
```

Only NPC initiation is wired today. Other source kinds are reserved boundaries.

Supported node speaker data:

- `speaker_kind`
- `display_name`
- optional `actor_id`

Current content uses:

```json
{
  "speaker_kind": "source",
  "display_name": "Test NPC"
}
```

The runtime does not derive display name from the active NPC interaction when a
node speaker is present. It maps the node's speaker data into the client
payload. The session source still includes `source_kind`, `source_id`, and
`actor_id`.

There is no portrait, visual-key, mini-scene, emote, or camera-focus contract in
the current runtime payload.

## Text Model

Current text model:

- one optional raw `text` string on each node
- no localization key support
- no interpolation or variable substitution
- no player-name substitution
- no markup parser
- no explicit max length in runtime validation
- JSON strings may contain escaped line breaks, but no special formatting
  semantics are implemented

Dialogue Studio should initially preserve plain text only. Localization and
formatting should remain deferred.

## Transition Model

Current transitions are node-owned:

- entry points target a node by `node_id`
- `speaker_text` continues through one `next_node_id`
- `player_choice` choices each target one `target_node_id`
- `end` has no outgoing transition

Ordering:

- entry-point selection orders eligible entry points by descending `priority`,
  then ascending `entry_id`
- choices are presented in authored array order after eligibility filtering

Validation:

- missing entry-point targets are rejected
- missing `next_node_id` targets are rejected when nonblank
- missing choice targets are rejected
- duplicate node IDs are rejected
- duplicate choice IDs are rejected per node
- cycles and self-loops are not rejected
- unreachable nodes are currently tolerated
- `speaker_text` without `next_node_id` is tolerated at startup but fails on
  Continue with `missing_transition`

Dialogue Studio should add warnings for cycles, self-loops, unreachable nodes,
and non-end terminal speaker nodes, while preserving runtime-compatible export.

## Choice Model

Current choice fields:

- stable `choice_id`
- display `text`
- `target_node_id`
- `conditions`

Runtime semantics:

- choices are ordered by authored array order
- only choices with empty `conditions` are visible
- hidden/ineligible choices are not sent to the client
- the client submits only `choice_id`
- `DialogueSessionService.Choose` revalidates current dialogue instance,
  session generation, expected node, command sequence, node type, choice ID, and
  choice eligibility at command time
- invalid or hidden choices fail with `invalid_choice`

Current runtime has no disabled-choice presentation. It hides ineligible
choices.

## Condition Model

Implemented production condition types:

```text
none
```

The data shape `DialogueCondition(condition_type, value)` exists on entry
points and choices, but `DialogueDefinitionCatalog.IsEligible` and
`DialogueSessionService.IsChoiceEligible` currently return true only when the
condition list is empty.

Categories:

- Implemented general dialogue condition: none.
- Temporary test-only condition: `future_flag` appears in
  `DialogueSessionServiceTests` only to prove non-empty choice conditions are
  hidden/rejected.
- Future quest-related conditions: documentation-only and explicitly deferred.

There is no negation, composition, payload validation, or data access for
conditions today. Any non-empty condition list makes the entry point or choice
ineligible.

## Effect Model

Implemented committed effect types:

```text
none
```

There are no effect fields in `DialogueDefinition`, `DialogueNode`, or
`DialogueChoiceDefinition`. No runtime code executes node-entry, continue, or
choice effects. No persistence, idempotency, or transaction boundary exists for
dialogue effects.

Dialogue Studio must not allow arbitrary C#, GDScript, SQL, expression
languages, encoded payloads, or quest-effect placeholders.

## Dialogue End Behavior

Current locked behavior:

- End nodes stay visible until acknowledged.
- Continue/Space on an end node sends `dialogue_continue_request`.
- The server closes with `DialogueCloseReason.Acknowledged`.
- Current nodes expose `dismissible`; current content sets it true.
- Manual close is allowed where `can_close` is true and currently all content is
  dismissible.
- There are no committed dialogue effects to rewind.
- Starting a fresh interaction creates a new `dialogue_instance_id`, selects an
  entry point, and begins from that entry-point node rather than resuming the
  previous tree position.
- Recoverable transport interruption suspends the dialogue session and can
  rebind to a replacement session.
- Terminal logout/character cleanup clears dialogue state.

Evidence:

- `DialogueSessionService.Continue`
- `DialogueSessionService.Close`
- `DialogueSessionService.SuspendRecoverableTransport`
- `DialogueSessionService.RebindSession`
- `DialogueSessionService.ClearCharacterState`
- `DialogueSessionServiceTests`

## Cancellation And Activity Behavior

`ForegroundActivityService` coordinates foreground transitions. Dialogue closes
when superseded by:

- ordinary manual movement: `manual_movement`
- outgoing combat: `combat_started`
- world-object interaction: `world_object_interaction_started`
- newer NPC interaction: `superseded`
- committed incoming hostile attack: `incoming_hostile_attack`
- player defeat: `player_defeated`
- terminal session cleanup: `session_terminated`

NPC interaction start cancels conflicting world-object interaction and outgoing
combat through `CancelConflictingForNpcInteractionAsync`. It does not clear
external enemy aggression merely because a conversation starts. Incoming
committed hostile attacks can close dialogue through
`ZoneSimulationService.CancelNpcInteractionForIncomingHostileAttacksAsync`.

Failed NPC approach semantics:

- unknown, non-interactable, dead-player, map mismatch, or route-unavailable
  cases fail without opening dialogue
- if an NPC moves away before completion and no active approach remains, the
  interaction fails with `target_moved_out_of_range`
- stale session completion after rebind is ignored

Dialogue Studio should document and preview graph semantics only. It should not
redesign activity cancellation.

## NPC Linkage

Current runtime linkage:

```text
Published NPC definition default_dialogue_id
  -> embedded/generated npc_definition_catalog
  -> NpcRuntimeService.NpcRuntimeState.DialogueId
  -> NpcInteractionService.CreateDialogue
  -> DialogueSessionService.StartSession
```

Current NPC facts:

- One NPC definition has one `default_dialogue_id`.
- A dialogue definition may have multiple entry points.
- Entry-point selection can redirect to a different beginning node once
  conditions exist, but today only unconditional entry points are eligible.
- `DialogueSourceRef` passes NPC source data to dialogue sessions:
  `Kind = Npc`, `SourceId = target.NpcId`, `ActorId = target.ActorId`.
- The same dialogue ID can be referenced by multiple NPC definitions.
- Static-content startup validates `default_dialogue_id` against
  `DialogueDefinitionCatalog` when NPC definitions are loaded from generated or
  database static content.

Initial cross-workspace linkage:

- NPC workspace keeps `default_dialogue_id`.
- Dialogue workspace lists references from published/draft NPC definitions.
- NPC workspace should eventually expose `Open Dialogue`, which routes to the
  top-level Dialogue workspace without embedding a graph editor in the NPC form.

## Runtime Validation

Current `DialogueDefinitionCatalog` validation:

- catalog `schema_version` must be `1`
- definition `dialogue_id` must be nonblank
- definition `schema_version` must be `1`
- at least one entry point
- at least one node
- entry IDs nonblank and unique per definition
- entry target node IDs nonblank and existing
- node IDs nonblank and unique per definition
- node type is one of `speaker_text`, `player_choice`, or `end`
- speaker display name is nonblank when speaker is present
- `player_choice` nodes have at least one choice
- `end` nodes cannot define `next_node_id`
- nonblank `next_node_id` targets exist
- choice IDs are unique per node
- choice target node IDs exist
- at least one unconditional entry point exists

Validation gaps Dialogue Studio should add:

- stable ID format checks for `dialogue_id`, `node_id`, `choice_id`
- nonblank choice text
- nonblank node text where node type expects visible text
- `speaker_text` must have `next_node_id` unless explicitly allowed as a draft
  warning
- unreachable-node warnings
- cycle/self-loop diagnostics
- entry-point priority conflicts as warnings
- condition/effect registry validation once runtime types exist
- publication reference guards against published NPC definitions
- deterministic export ordering

## Client Protocol

Client intents:

- `npc_interaction_request`
- `dialogue_continue_request`
- `dialogue_choice_request`
- `dialogue_close_request`

Dialogue command payloads include:

- `dialogue_instance_id`
- `expected_node_id`
- `command_sequence`
- `choice_id` for choice requests

Server messages:

- `dialogue_opened`
- `dialogue_node_presented`
- `dialogue_closed`
- `dialogue_command_failed`

Renderable node payloads include:

- dialogue instance and definition IDs
- typed source metadata
- current node ID and node type
- speaker metadata
- text
- server-filtered choices with only `choice_id` and text
- `can_continue`
- `can_close`
- activity generation

The client does not receive the whole tree, hidden choices, target node IDs,
conditions, or effects.

## Existing Content Inventory

Current definitions:

```text
test_npc_greeting
entry_points: default -> welcome, fallback -> welcome
nodes: 5
conditions: none
effects: none
referenced by NPC definition: test_npc
```

Edges:

```text
welcome -> question [continue]
question -> where_answer [choice: where_am_i]
question -> goodbye [choice: goodbye]
where_answer -> end [continue]
goodbye -> end [continue]
```

The existing definition can migrate losslessly into the proposed authored model
because it uses only supported node types, node-owned transitions, empty
conditions, and no effects.

## Evidence Index

MMO Project runtime:

- `prototype/shared/dialogues/catalog.json`
- `prototype/server/Program.cs`
- `prototype/server/features/dialogue/application/DialogueDefinitionCatalog.cs`
- `prototype/server/features/dialogue/application/DialogueSessionService.cs`
- `prototype/server/features/dialogue/protocol/DialoguePayloads.cs`
- `prototype/server/features/dialogue/host/DialogueCommandHandlers.cs`
- `prototype/server/features/dialogue/host/DialogueProtocolMapper.cs`
- `prototype/server/features/npcs/application/NpcInteractionService.cs`
- `prototype/server/features/npcs/application/NpcRuntimeService.cs`
- `prototype/server/features/activities/application/ForegroundActivityService.cs`
- `prototype/server/features/runtime/application/ZoneSimulationService.cs`
- `prototype/server/features/static_content/application/GeneratedFileWorldStaticContentSource.cs`
- `prototype/server/features/static_content/application/DatabaseWorldStaticContentSource.cs`
- `prototype/tools/MapPublisher/NpcCatalogExporter.cs`
- `prototype/importer/import_tiled_region.py`
- `prototype/shared/protocol-v1.json`
- `prototype/shared/maps/npcs/catalog.json`
- `prototype/client/screens/game/controllers/dialogue_panel_controller.gd`
- `prototype/client/screens/game/game_screen.gd`
- `prototype/client/network/session_client.gd`

MMO Project tests:

- `prototype/tests/MMO.Project.Prototype.Server.Tests/DialogueDefinitionCatalogTests.cs`
- `prototype/tests/MMO.Project.Prototype.Server.Tests/DialogueSessionServiceTests.cs`
- `prototype/tests/MMO.Project.Prototype.Server.Tests/NpcInteractionServiceTests.cs`
- `prototype/client/tools/dialogue_panel_controller_tests.gd`

Content Studio seams:

- `docs/T5_NPC_DOMAIN_AUDIT.md`
- `docs/T5_NPC_AUTHORING_PLAN.md`
- `host/Services/NpcDialogueReferenceProvider.cs`
- `content-studio/scripts/npc_editor.gd`
- `content-studio/scripts/authoring_host_client.gd`
- `content-studio/scripts/authoring_workspace_support.gd`
