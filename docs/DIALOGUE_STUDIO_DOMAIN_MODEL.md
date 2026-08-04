# Dialogue Studio Domain Model

Status: D1 domain lock for future D2-D5 implementation. No production schema,
API, or Godot editor is implemented in D1.

## Product Boundary

Dialogue Studio belongs inside MMO Content Studio as a first-class workspace.
It is not a separate application and must not be embedded as a full graph editor
inside the NPC form.

Initial navigation order:

```text
Items
Mobs
NPCs
Dialogue
Environment
later: Quests
```

The NPC workspace may later show:

```text
Default dialogue: test_npc_greeting
[Open Dialogue]
```

That action should route to the Dialogue workspace and load the definition.
It should not create a direct script dependency from `npc_editor.gd` to a future
dialogue editor.

## Initial Aggregate

The D2 aggregate should author the complete current runtime dialogue definition
without quest semantics:

```text
DialogueDefinition
  dialogue_definition_id
  display_name
  publication_state
  schema_version
  entry_points[]
  nodes[]
  metadata_description
  notes
  created_at_utc
  updated_at_utc

DialogueEntryPoint
  entry_id
  node_id
  priority
  order
  conditions[]  # empty until a runtime condition type is implemented

DialogueNode
  node_id
  node_type
  speaker
  text
  next_node_id
  dismissible
  canvas_x
  canvas_y
  editor_notes

DialogueChoice
  choice_id
  text
  target_node_id
  choice_order
  conditions[]  # empty until a runtime condition type is implemented
```

`dialogue_definition_id` maps to the current runtime `dialogue_id`. The API may
use the explicit Content Studio name while export writes `dialogue_id`.

The initial aggregate explicitly excludes:

```text
quest_id
quest_started
quest_completed
quest_stage
quest_stage_equals
start_quest
advance_quest
complete_quest
objective_progress
quest_rewards
quest_variables
quest_journal_data
quest_specific_content_locks
```

It also excludes localization, portraits, mini-scenes, camera cues, audio,
shops, banking, trainers, service menus, cutscenes, and arbitrary executable
scripts.

## Node-Owned Graph Representation

The runtime model is naturally node-owned. D2 should preserve that rather than
forcing every transition into a separate edge table.

Recommended storage:

```text
dialogue_definitions
dialogue_entry_points
dialogue_nodes
dialogue_choices
```

Recommended condition storage seam:

```text
dialogue_entry_point_conditions
dialogue_choice_conditions
```

Those condition tables may exist in D2 only as a typed registry-backed seam.
They must have no enabled authorable condition types until MMO Project
implements a runtime evaluator beyond "empty list is eligible." Draft and
publish validation should reject unsupported condition rows.

Effect tables should be deferred until MMO Project has an implemented effect
contract. The current runtime has no effect fields, no execution timing, and no
transaction boundary for effects, so adding effect persistence in D2 would be
premature.

## Proposed Tables

Initial D2 schema:

```sql
CREATE TABLE dialogue_definitions (
    dialogue_definition_id TEXT PRIMARY KEY,
    display_name TEXT NOT NULL,
    publication_state TEXT NOT NULL,
    schema_version INTEGER NOT NULL DEFAULT 1,
    metadata_description TEXT NULL,
    notes TEXT NULL,
    created_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE dialogue_entry_points (
    dialogue_definition_id TEXT NOT NULL REFERENCES dialogue_definitions(dialogue_definition_id) ON DELETE CASCADE,
    entry_id TEXT NOT NULL,
    node_id TEXT NOT NULL,
    priority INTEGER NOT NULL,
    entry_order INTEGER NOT NULL,
    PRIMARY KEY (dialogue_definition_id, entry_id)
);

CREATE TABLE dialogue_nodes (
    dialogue_definition_id TEXT NOT NULL REFERENCES dialogue_definitions(dialogue_definition_id) ON DELETE CASCADE,
    node_id TEXT NOT NULL,
    node_type TEXT NOT NULL,
    speaker_kind TEXT NULL,
    speaker_display_name TEXT NULL,
    speaker_actor_id TEXT NULL,
    text TEXT NULL,
    next_node_id TEXT NULL,
    dismissible BOOLEAN NOT NULL DEFAULT TRUE,
    canvas_x REAL NOT NULL DEFAULT 0,
    canvas_y REAL NOT NULL DEFAULT 0,
    node_order INTEGER NOT NULL,
    editor_notes TEXT NULL,
    PRIMARY KEY (dialogue_definition_id, node_id)
);

CREATE TABLE dialogue_choices (
    dialogue_definition_id TEXT NOT NULL,
    node_id TEXT NOT NULL,
    choice_id TEXT NOT NULL,
    text TEXT NOT NULL,
    target_node_id TEXT NOT NULL,
    choice_order INTEGER NOT NULL,
    PRIMARY KEY (dialogue_definition_id, node_id, choice_id),
    FOREIGN KEY (dialogue_definition_id, node_id)
        REFERENCES dialogue_nodes(dialogue_definition_id, node_id)
        ON DELETE CASCADE
);
```

If D2 includes condition seam tables, they should be:

```sql
CREATE TABLE dialogue_entry_point_conditions (
    dialogue_definition_id TEXT NOT NULL,
    entry_id TEXT NOT NULL,
    condition_order INTEGER NOT NULL,
    condition_type TEXT NOT NULL,
    condition_payload JSONB NOT NULL DEFAULT '{}'::jsonb,
    PRIMARY KEY (dialogue_definition_id, entry_id, condition_order),
    FOREIGN KEY (dialogue_definition_id, entry_id)
        REFERENCES dialogue_entry_points(dialogue_definition_id, entry_id)
        ON DELETE CASCADE
);

CREATE TABLE dialogue_choice_conditions (
    dialogue_definition_id TEXT NOT NULL,
    node_id TEXT NOT NULL,
    choice_id TEXT NOT NULL,
    condition_order INTEGER NOT NULL,
    condition_type TEXT NOT NULL,
    condition_payload JSONB NOT NULL DEFAULT '{}'::jsonb,
    PRIMARY KEY (dialogue_definition_id, node_id, choice_id, condition_order),
    FOREIGN KEY (dialogue_definition_id, node_id, choice_id)
        REFERENCES dialogue_choices(dialogue_definition_id, node_id, choice_id)
        ON DELETE CASCADE
);
```

These are not generic scripting payloads. They are typed registry payloads with
no initially registered runtime condition types.

## Identity Rules

Use author-facing stable IDs rather than database-generated numeric identities.

Recommended formats:

- `dialogue_definition_id`: lower snake case, starts with a lowercase letter
- `node_id`: lower snake case, starts with a lowercase letter
- `choice_id`: lower snake case, starts with a lowercase letter

Examples:

```text
test_npc_greeting
welcome
where_answer
where_am_i
```

Publication stability:

- `dialogue_definition_id` is immutable after creation once any NPC reference
  exists, and should be treated as immutable after publication.
- `node_id` should remain stable across edits because future localization,
  trace logs, analytics, and condition/effect references may target it.
- `choice_id` should remain stable because the runtime command protocol already
  uses it and future quest/effect systems may need idempotent choice identity.
- Display labels and text are editable independently from stable IDs.

## Validation Rules

Draft save should preserve incomplete work but still reject malformed aggregate
shape:

- invalid definition, node, entry, or choice IDs
- duplicate entry, node, or per-node choice IDs
- unsupported node types
- malformed enum values
- impossible graph references in committed rows
- unsupported condition or effect types

Publish validation should require runtime-compatible completeness:

- at least one unconditional entry point
- all entry points target existing nodes
- every nonblank `next_node_id` targets an existing node
- every choice target exists
- every `player_choice` has at least one choice
- every `end` node has no outgoing `next_node_id`
- `speaker_text` nodes have a valid continuation or are intentionally blocked
  from publication
- no non-empty condition collections until a runtime condition evaluator exists
- no effect collections until a runtime effect contract exists
- no quest fields

Warnings should include:

- unreachable nodes
- cycles
- self-loops
- entry point targeting an `end` node
- empty visible text
- duplicate priorities whose deterministic order depends on `entry_id`

## Publication And References

Publication states:

```text
Draft
Published
Disabled
```

Draft:

- saved in Content Studio only
- appears in Dialogue Studio catalog
- not exported to runtime
- may contain warnings and incomplete text

Publish:

- validates the complete aggregate strictly
- makes the dialogue eligible for deterministic runtime catalog export
- must not expose draft-only data to active runtime

Disable:

- removes the dialogue from future exports
- must be blocked when a published NPC definition references it

Delete:

- allowed only for Disabled definitions
- blocked by known references from NPC definitions
- future references from quests, world objects, tutorials, or server-script
  triggers should plug into the same reference provider seam

Published edits:

- save as Draft should be allowed on the same aggregate
- publish should export the newly saved graph after validation
- active MMO Project runtime sessions do not hot reload in D2/D3

Concurrency:

- one root `updated_at_utc` token covers the complete aggregate
- child-only edits must advance the root token
- preview signatures must cover the complete normalized graph

## Cross-Reference Model

Current D1 reference source:

- NPC definitions through `default_dialogue_id`

Future sources:

- quests
- world objects
- tutorial triggers
- mob/boss encounter triggers
- server-script/system triggers

D2 should introduce an internal `DialogueReferenceProvider` seam that returns
references by source type without adding those future source types to the
dialogue aggregate.

## Runtime Export Shape

Recommended future handoff:

```text
Content Studio database
  -> Published dialogue catalog export
  -> prototype/shared/dialogues/catalog.json
  -> DialogueDefinitionCatalog
```

For D4, preserving the current runtime JSON shape is the safest path:

- `schema_version = 1`
- `dialogues` sorted by `dialogue_id`
- entry points sorted deterministically by priority descending, then `entry_id`
  for runtime equivalence or by stored order when priorities differ only for
  editor convenience
- nodes sorted by `node_order`
- choices sorted by `choice_order`
- empty condition arrays emitted for current compatibility
- no effect fields emitted until runtime supports them

`test_npc_greeting` should be seeded or migrated into the authoring database and
exported byte-stably before replacing hand-edited runtime JSON.

## Quest Extension Seam

Dialogue Studio should own a typed registry boundary, not arbitrary scripts.

Conceptual future shape:

```text
condition
  type
  typed payload

effect
  type
  typed payload
```

D1 registers no condition or effect types. Future MMO Project quest foundations
must define the authoritative payloads and evaluation/application owners before
Dialogue Studio adds quest predicates or effects.

Future additions may include quest predicates, quest transition effects,
objective effects, and reward requests, but their payloads are deliberately not
defined in D1.

## Initial Dialogue Studio UX Requirements

Top-level workspace:

```text
Dialogue catalog | Graph canvas | Node/edge inspector
                                  | Validation
                                  | References
                                  | Playthrough preview
```

Required workflows:

- list/search/new
- load definition
- add node
- duplicate node
- delete node
- connect `next_node_id`
- connect choice targets
- edit node details
- preview/play through
- validate and preview logical changes
- save draft
- publish
- disable
- delete

The first version should use Godot `GraphEdit`/`GraphNode` capabilities where
practical:

- draggable nodes
- zoom and pan
- connection ports
- selection
- automatic layout command
- start-node marker
- end-node marker
- visual distinction by node type
- invalid-edge indicators

Multi-select and minimap can be deferred unless GraphEdit makes them cheap.

## Node Inspector

Inspector sections:

- Identity
- Speaker
- Text
- Transition
- Choices
- Conditions
- Effects
- Editor Notes

Only relevant fields should be visible for each node type:

- `speaker_text`: speaker, text, continue target, dismissible
- `player_choice`: speaker, text, ordered choices, dismissible
- `end`: speaker, text, dismissible

Conditions and effects should be visible as empty/read-only "not supported by
current runtime" sections until runtime types are implemented.

## Playthrough Preview

The safest first preview is a pure Content Studio validator/simulator that uses
the same semantics as current runtime:

- choose the highest-priority unconditional entry point
- show speaker/text
- continue through `next_node_id`
- show visible choices
- select choices by `choice_id`
- keep end nodes visible until acknowledged
- detect loops and offer restart
- show hidden/invalid reasons for unsupported conditions where possible

It must not call production MMO Project session endpoints and must not commit
effects. A later D4/D5 equivalence test can compare Content Studio preview
output against `DialogueSessionService` behavior.
