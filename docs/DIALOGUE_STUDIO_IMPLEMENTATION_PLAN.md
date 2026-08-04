# Dialogue Studio Implementation Plan

Status: D1-D5 non-quest Dialogue Studio authoring, graph editing, runtime
catalog export, validator/runtime equivalence, reference safety, and end-to-end
verification are complete. Quest predicates/effects remain deferred.

## Sequence Lock

The revised sequence is:

```text
T5 reusable NPC authoring                  complete

D1 dialogue runtime/domain audit           complete
D2 dialogue schema and host API            complete
D3 Godot Dialogue Studio graph editor       complete
D4 MMO Project runtime catalog handoff     complete
D5 hardening and playthrough verification       complete

MMO Project quest foundations
Dialogue Studio quest integration
Quest Studio
```

Dialogue Studio will first author the current non-quest dialogue runtime model.
MMO Project quest foundations will then define authoritative quest predicates
and effects. Dialogue Studio will subsequently gain quest-aware
condition/effect types, followed by Quest Studio.

## D1 - Runtime Audit And Domain Lock

Deliverables:

- `docs/DIALOGUE_STUDIO_RUNTIME_AUDIT.md`
- `docs/DIALOGUE_STUDIO_DOMAIN_MODEL.md`
- `docs/DIALOGUE_STUDIO_IMPLEMENTATION_PLAN.md`
- `docs/DIALOGUE_STUDIO_ACCEPTANCE.md`
- roadmap/readme/architecture/workspace/integration updates
- source/document contract tests

D1 restrictions:

- no production dialogue schema
- no host dialogue contracts/routes/services/repository
- no Godot Dialogue workspace/editor
- no MMO Project modifications
- no quest semantics

## D2 - Schema, Contracts, Repository, Validation, And API

Implemented as a feature-owned Dialogue module in Content Studio.

Host files:

- `host/Contracts/DialogueContracts.cs`
- `host/Features/Dialogues/DialogueAuthoringFeature.cs`
- `host/Features/Dialogues/DialogueSchemaRequirements.cs`
- `host/Features/Dialogues/DialogueCatalogSectionProvider.cs`
- `host/Persistence/DialogueRepository.cs`
- `host/Services/DialogueAuthoringService.cs`
- `host/Services/DialogueDefinitionValidator.cs`
- `host/Services/DialogueAuthoringRegistry.cs`
- `host/Services/DialogueGraphAnalyzer.cs`
- `host/Services/DialoguePlaythroughService.cs`

Schema:

- `dialogue_definitions`
- `dialogue_entry_points`
- `dialogue_nodes`
- `dialogue_choices`
- no condition/effect tables; contracts expose empty condition arrays and
  registries report zero types

Routes:

- `GET /api/v1/dialogues/options`
- `GET /api/v1/dialogues?search=...`
- `GET /api/v1/dialogues/{dialogueDefinitionId}`
- `POST /api/v1/dialogues/{dialogueDefinitionId}/preview`
- `POST /api/v1/dialogues/{dialogueDefinitionId}/playthrough`
- `PUT /api/v1/dialogues/{dialogueDefinitionId}/draft`
- `POST /api/v1/dialogues/{dialogueDefinitionId}/publish`
- `POST /api/v1/dialogues/{dialogueDefinitionId}/disable`
- `POST /api/v1/dialogues/{dialogueDefinitionId}/delete`

Contract rules:

- use complete graph aggregates
- include one root `updated_at_utc`
- preview signatures cover the normalized graph
- save draft can preserve incomplete graphs but rejects malformed IDs,
  unsupported node types, and impossible references
- publish requires runtime-compatible graph validation
- publish/disable/delete operate on the saved aggregate
- reference guards block disabling dialogues referenced by Published NPC
  definitions and block deleting dialogues referenced by any NPC definition

Validation:

- current node types only: `speaker_text`, `player_choice`, `end`
- empty condition collections only
- no effects
- no quest fields
- no arbitrary scripts
- graph references verified
- unreachable/cycle/self-loop diagnostics surfaced

Options should expose:

- publication states
- supported node types
- current condition registry: empty
- current effect registry: empty
- capability flags such as `supports_quest_conditions = false` and
  `supports_effects = false`

D2 combines schema/repository/API in one slice so the route family is useful
with repository, preview, playthrough, schema-health, and catalog registration.

## D3 - Godot Dialogue Studio

Implemented a top-level **Dialogue** workspace after NPCs and before
Environment.

Godot files:

- `content-studio/scripts/dialogue_editor.gd`
- graph canvas remains inside `content-studio/scripts/dialogue_editor.gd`
- scene registration in `content-studio/scenes/Main.tscn`
- `AuthoringHostClient` dialogue route methods and signals

Required UI:

- catalog/search/new
- graph canvas
- node inspector
- validation and reference panel
- playthrough preview panel
- operation controls for save draft, publish, disable, delete

Graph canvas:

- use Godot `GraphEdit` and `GraphNode` where practical
- draggable nodes
- zoom/pan
- output port for `speaker_text.next_node_id`
- output ports or row handles for `player_choice` choices
- start-node marker based on entry points
- end-node marker
- invalid-edge indicators
- automatic layout command deferred unless the graph grows enough to justify it

Preview/apply lifecycle:

- use `AuthoringWorkspaceSupport`
- clear preview on every graph or inspector edit
- require server preview signature before mutation
- save draft sends the complete graph
- publish/disable/delete send saved aggregate concurrency plus preview signature
- reload selected definition after mutation

Cross-workspace navigation:

- introduce shell-level workspace routing in the main scene
- `NPC -> Open Dialogue` loads `default_dialogue_id`
- Dialogue references panel lists NPC definitions that reference the dialogue
- no direct dependency from `npc_editor.gd` to `dialogue_editor.gd`
- no quest, condition, or effect authoring in D3

## D4 - MMO Project Runtime Catalog Handoff

Work across Content Studio and MMO Project after D2/D3 are stable.

Target handoff:

```text
Content Studio dialogue authoring tables
  -> deterministic Published export
  -> prototype/shared/dialogues/catalog.json
  -> DialogueDefinitionCatalog
```

Implementation:

- mirror D2 schema into MMO Project only if the runtime repository needs local
  migration artifacts for developer database export
- seed/migrate `test_npc_greeting`
- add `MapPublisher export-dialogue-catalog`
- preserve current JSON shape for `DialogueDefinitionCatalog`
- export only Published definitions
- order output deterministically
- validate NPC `default_dialogue_id` references against the exported catalog
- keep generated-file and database static-content NPC validation compatible
- preserve `dialogue_opened`, `dialogue_node_presented`, `dialogue_closed`, and
  `dialogue_command_failed` payloads
- do not hot reload active sessions in D4

D4 should not change runtime dialogue session semantics unless the export
integration reveals a necessary compatibility fix.

## D5 - Hardening And Playthrough Verification

Status: complete for the non-quest runtime-compatible dialogue slice.

Verification targets:

- byte-stable export
- migration of `test_npc_greeting`
- graph validation parity between Content Studio and `DialogueDefinitionCatalog`
- playthrough simulator parity with `DialogueSessionService`
- NPC `test_npc` opens the exported dialogue
- continue, choice, close, stale node, stale command sequence, and duplicate
  command behavior remain server-authoritative
- recoverable session replacement republishes the current node
- manual movement, outgoing combat, world-object interaction, newer NPC
  interaction, hostile attack, defeat, and terminal cleanup close/cancel as
  currently documented
- disable/delete blocked while published NPC definitions reference a dialogue
- no quest fields or unsupported condition/effect rows reach runtime export

Manual playthrough:

1. Launch Content Studio.
2. Load `test_npc_greeting` in Dialogue Studio.
3. Play through both branches in preview.
4. Save draft, publish, and export.
5. Launch MMO Project with generated-file or database static content.
6. Click `test_npc`.
7. Verify the modal dialogue opens, branches, reaches the end node, and closes
   only after acknowledgement.

## Deferred Work

- quest predicates and effects
- objective progress
- quest rewards
- quest journal data
- content-access gates
- localization
- portraits and mini-scenes
- non-dismissible enforcement beyond current payload flags
- camera focus, emotes, and cutscenes
- shops, banking, trainers, and service menus
- tutorial, mob, boss, world-object, quest, and server-script initiation wiring
- runtime hot reload

## Acceptance Criteria By Phase

D2 is complete when:

- schema health reports the dialogue schema
- `/api/v1/dialogues` route family exists
- complete aggregate round-trips transactionally
- graph validation matches current runtime
- preview signatures and optimistic concurrency are enforced
- no quest fields are accepted

D3 is complete when:

- Dialogue is a top-level workspace
- graph canvas can author every current node/transition shape
- playthrough preview covers current runtime semantics
- NPC cross-navigation opens a referenced dialogue
- mutation lifecycle uses `AuthoringWorkspaceSupport`
- D3 docs mark the Godot Dialogue workspace complete and D4/D5 work queued

D4 is complete when:

- Published dialogues export to the current runtime JSON shape
- MMO Project loads exported definitions without behavior changes
- `test_npc_greeting` is migrated/exported
- NPC dialogue references validate against the exported catalog

D5 is complete when:

- automated and manual playthroughs verify runtime equivalence
- reference safety prevents disabling/deleting active dialogue references
- documentation marks D1-D5 complete without claiming quest integration
