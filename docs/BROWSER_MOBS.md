# Browser Mobs

`/studio/mobs.html` exposes the complete existing Mob aggregate: identity/name,
flat/composite appearance and shared calibration, source dimensions/anchors/scale,
footprint/Health/movement, aggression/leash/return behavior, faction and hostile-Mob
scan settings, optional primary combat profile (all levels, weight, ranges and
speed), all fourteen signed bonuses, explicit ordered guaranteed drops and root loot.
Missing references survive unrelated edits and have searchable available choices.
No gameplay rules or new combat types are introduced.

The workspace owns its complete editing/preview/apply/recovery flow. Existing
MobAuthoringService owns normalization, diagnostics, persistence and lifecycle.
Whole-draft normalized diffs show every field. Composite extension metadata is
protected by checking the stored descriptor before replacement; explicit whole
removal remains available. Mode changes retain values and incompatible composite,
fixed-pose, wander, aggression or scanning fields must be cleared explicitly.
Computed bonus flags are excluded from actor browser payloads.

Exact timestamp and operation-specific signature travel separately from the draft.
Save Draft (including unpublishing) does not export; Publish exports the Mob catalog;
Disable and Delete do not export. The service allows deletion of Draft/Disabled rows,
rejects Published rows and retains its existing database-reference guards. Deferred
EnemySpawn reference warnings remain visible; no complete spawn-reference claim is
made. DB commit/export/game lifecycle outcomes stay distinct. Database failures map
to 503 and failed postcommit reload verification to 500. Uncertain writes/version
conflicts retain edits and require explicit reload/compare before another mutation.

Saved and preview combat level diagnostics come from the existing host. Attack
interval copy uses the host's unit duration. Edits mark diagnostics stale. Saved root
loot reports retain exact numerators/denominators and are labeled as saved-definition
reports; preview refreshes combat diagnostics, not an unsaved root loot simulation.

Shared actor calibration retains its independent catalog hash, local draft, review,
file save and uncertain-write recovery. Numeric pending edits block Mob lifecycle
operations; Mob requests disable calibration interaction. Mobs checks descriptor
identity, rig and texture again when applying a calibration reference, preventing an
open file editor from modifying a discarded descriptor. Shared copy says actor rather
than NPC. Existing source-pixel composite rendering, exact frames, sockets, overlays,
copy/mirror/reset and touch pan/edit controls are reused. PNG routes remain confined.

## Verification

Production Release and service Debug builds passed with zero warnings/errors.
JavaScript module syntax and whitespace checks passed. No tests were added, changed,
generated or run. Independent exact-change review identified a stale calibration
callback; it was fixed before delivery and final review is recorded in the handoff.

Only the existing Studio service restarted with unchanged private-LAN configuration.
CA-valid reads covered pages/scripts/catalog/options, flat Cow and composite Orc
exact draft previews (W/F2); both saved definitions and the calibration hash stayed
unchanged. Legacy LAN API, wrong Host and excluded source returned 403; PNG traversal
returned 404. Actual Chrome at 1440px and 390px displayed combat/drop forms, composite
pixels and socket/overlay calibration without page overflow or runtime exceptions.
Unsaved numeric/nudge edits remained separate from the Mob Saved badge. Screenshots
are under `/tmp/studio-mobs-visual`.

Physical-phone acceptance, live mutation/export and real calibration-file writes
remain unverified. No live content/calibration QA write, game restart, network/security
change or merge occurred. Stop after Mobs; Spells and Environment remain subsequent
slices. Sandbox and repository-local delivery skill remain queued afterward.
