# Browser Dialogue workspace

`/studio/dialogue.html` provides the full Dialogue aggregate over the existing
Dialogue service, validator, graph analyzer, repository and structural playthrough
service. Godot desktop remains available. `dialogue.js` owns browser form state,
selection, graph viewing, preview/apply, simulation and recovery; BrowserDialogue
is the bounded transport adapter. No game or database schema behavior changes.

## Complete authored scope

Search/list/create/load and edit stable identity, display name, schema, description
and notes. Entry points include ID, target, signed priority, explicit order and
ordered conditions. Each node exposes ID/type/speaker/text/next target/dismissible,
exact canvas X/Y, notes and choices. Choices expose ID/text/target/order, conditions
and ordered effects. Conditions expose quest/status/step/item/quantity. Effects
expose ID/order/type/quest/transition/item/quantity/skill/XP. Nullable effects and
all other authored values survive unrelated edits. Int64 XP uses decimal JSON
strings, never JavaScript Number. Existing service normalization is shown in the
complete preview diff, including same-count edits, metadata, order and whitespace.

Searchable reference selection covers draft nodes, published quests and their
steps/transitions, items and skills. Typed missing references remain editable.
Changing node/condition/effect type does not automatically clear any field.
The browser rejects node normalization that would silently remove choices, a
next target or Dismissible=false. Explicit field removal is required first.

## Graph and phone interaction

The searchable node list and complete inspector work at phone widths, with
explicit target pickers: connections never require precision dragging. The graph
renders authored coordinates and directed next/choice edges with entry labels.
Pointer panning, pan buttons, zoom and fit affect view state only. Tapping a node
selects its inspector. Numeric coordinates and 40-pixel nudges edit saved layout;
no automatic layout runs when loading or selecting. All nodes remain accessible
through the list when a large graph is too small to read in overview.

Desktop keeps workspace/list/detail; phone uses workspace/list/detail/back.
Sections collapse, forms have visible labels, actions need no hover, and the
shared keyboard handling moves sticky actions into normal flow while editing.
Node selection and graph navigation do not dirty the conversation or invalidate
its preview. Actual edits invalidate preview, saved analysis and simulation.

## Diagnostics, simulation and outcomes

Saved graph/reference diagnostics are GET-only, version-checked, and never install
an Apply signature. Current-draft preview shows reachability, dangling targets,
terminal paths, cycles, duplicate order and known/incomplete reference checks.

Simulation is structural only: entry priorities determine the default entry, but
conditions are displayed rather than evaluated. The author can select another
entry or choice. Effects are listed as would-apply facts; no inventory, quest,
XP, authored row or live character changes. Saved simulation uses a bounded
header on GET (avoiding URL length limits); unsaved simulation uses a bounded,
CSRF-protected POST when editing is permitted. Its visited path/current node are
separate from the authored draft. Editing requires restarting simulation; loop
warnings stop progression. End nodes offer acknowledgement, not Continue.

Save Draft, Publish, Disable and Delete retain exact timestamp concurrency and
operation-specific signatures. Publish requests the existing MapPublisher export;
Save Draft, Disable and Delete do not request export in the existing Dialogue
service. Database commit and export outcome are reported separately. No response
implies a game restart or live reload. Version conflicts, missing mutation targets,
transport failures and server/database failures block further Apply until explicit
reload/compare. Actual Dialogue database/schema error codes map to HTTP 503;
postcommit reload verification failure maps to 500. Local edits remain available.

## Delivery evidence and limits

Production build completed with zero warnings/errors; JavaScript syntax and diff
whitespace checks passed. Independent source review found and verified corrections
for End progression, stale simulation display, Disable export reporting and actual
Dialogue database-error status mapping. No tests were added/changed/generated/run.

CA-valid reads covered catalog/options/details, saved graph/reference analysis,
static routes, and structural playthrough. A CSRF-protected exact draft preview
and unsaved-snapshot simulation returned success; the saved definition remained
byte-for-byte equal after JSON decoding. The existing Published Corren conversation
has six entries, 20 nodes and three choices; all nodes are reachable, with no dangling
nodes/cycles/missing terminal paths. It has a Published NPC reference. Its structural
turn-in listed herb removal, quest completion and exact 150 Cooking XP without
applying any effect. The existing multiple-entry informational warning remains.

Actual Chrome at 1440px and 390px showed the graph, inspector, choice fields and
simulation with no horizontal overflow or runtime exceptions. Graph/navigation
kept the draft Saved; a local node addition marked it dirty and diagnostics/simulation
stale without submitting it. Screenshots are under `/tmp/studio-dialogue-visual`.
This is CSS-width inspection, not physical-phone acceptance. Authored mutations,
concurrent-write conflicts and catalog export were source-reviewed, not exercised
against live content. No live-content QA writes, access configuration changes,
network changes, game restart or merge occurred. Stop before NPCs.
