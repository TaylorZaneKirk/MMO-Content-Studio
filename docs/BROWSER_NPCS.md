# Browser NPCs and shared actor calibration

`/studio/npcs.html` contains the complete existing NPC contract: identity/name/notes,
flat/composite source texture, dimensions/anchor/scale, footprint, movement/radius/
tick/idle probability, interaction enabled/range/type, Dialogue/Shop references,
rig/calibration/pose policy/fixed direction/frame and cosmetic item selections.
References are searchable and missing typed IDs survive unrelated edits. Existing
contracts support one default interaction; no new interaction list is invented.

NpcAuthoringService remains the database lifecycle owner. The browser uses distinct
preview/draft/publication/deletion DTOs, exact timestamps and operation signatures.
Complete normalized preview diffs include all authored fields. Mode/movement/
interaction changes retain incompatible data until explicitly cleared. The browser
blocks normalization that would remove composite descriptors, fixed-pose fields,
dialogue references, wander radius or unsupported descriptor metadata. Runtime-only
computed descriptor properties are excluded from browser authoring responses. A
stored-descriptor check also blocks replacement when sensitive extension metadata
was omitted from the browser response; only explicit whole-descriptor removal
bypasses that preservation guard.

Save Draft/Publish/Disable/Delete retain existing validation and reference guards.
Publish invokes MapPublisher; the other three operations do not export in the
current NPC service. DB commit and export outcomes are separate; neither implies
live reload/restart. Transport/server failures, missing mutation targets and version
conflicts require reload/compare. NPC database/schema failures are HTTP 503 and
postcommit verification failure is 500. Unsaved local drafts survive recovery.

## Actual pixels and actor appearance

The small shared actor-appearance module draws resolved base/cosmetic/foreground
layers using their source coordinates, flips and z ordering. Source-pixel previews
are fitted for inspection; they are not an in-world movement/placement simulation.
The authored anchor/render-scale/footprint remain editable fields. Direction/frame
selectors feed the existing preview resolver, respecting a fixed pose policy.
Actual changes clear and mark the old appearance stale, including shared calibration
changes. Async image responses cannot reinstall an obsolete preview.

Existing PNGs are selected through canonical res:// identities. Host paths become
confined image URLs; catalog paths and operational path diagnostics are removed.
The shared PNG boundary rejects traversal, linked ancestry/descendants and oversized
files. No arbitrary host path or new upload route is exposed. Calibration enumerates
all 16 exact NPC/mob frames using the existing resolver; a missing exact image disables
that pose's calibration edits. Existing cosmetic/grip metadata remains read-only.

## Separate calibration file lifecycle

The dedicated touch canvas edits actor sockets and foreground-overlay rectangles,
with direction/frame, zoom, explicit Pan/Edit tool, numeric coordinates, one-pixel
nudges, inherited/override display and reset. Socket copy and mirror use the exact
target frame width (width minus one minus X), matching desktop semantics. Foreground
rectangles use exact source-pixel bounds; desktop does not offer overlay copy/mirror.
Numeric input immediately activates dirty protection, retains pending values until
Set numeric value, and blocks pose switching/review/save until applied or discarded.

Calibration is a separate local draft and whole-catalog hash. Load accepts an existing
or new calibration ID against that hash. Review shows before/after socket/rectangle
maps; Save explicitly writes the shared catalog, not the NPC. The unchanged service
retains unknown root/entry/overlay metadata and atomically replaces its catalog after
checking the hash again. Unknown overlay metadata remains read-only and is not sent
as an editable rig override. The complete stored entry is visible for inspection.

Conflicts and uncertain file-write responses preserve edits, block another save, and
require reload/compare with explicit use-server or keep-local-on-current-hash choice.
A saved calibration can be selected into the NPC draft via a separate action. Actor
DB actions are blocked while calibration edits are pending; calibration controls
are disabled during NPC requests, preventing successful NPC responses from clearing
new calibration edits. Changing actor/context clears old frames and blocks loading
until the new exact-frame request resolves. Item grip/pose editing retains its
explicit desktop-Items exception; actor socket/overlay calibration is included here.

## Verification and limits

Production build passed with zero warnings/errors; JS syntax and whitespace checks
passed. Independent source review covered field preservation, separate concurrency,
recovery, exact pixels and confinement. No tests were added, changed, generated or run.

CA-valid reads covered NPC pages/static resources, catalog/options/details, calibration
hash and all 16 exact frames for the real composite test_npc source. Actual PNG pixels
were inspected. Flat nir_innkeeper and composite test_npc exact nonmutating previews
succeeded; the latter resolved the requested W/F2 pose. Both saved definitions and
the shared calibration hash remained unchanged afterward. Wrong Host/excluded source
and legacy LAN API requests remained denied; image traversal returned 404.

Actual Chrome at 1440px and 390px showed the composite appearance, socket canvas,
foreground rectangle and local numeric/nudge editing with no horizontal page overflow
or runtime exceptions. Local calibration edits left the NPC Saved badge unchanged;
no calibration-file or authored DB QA save was performed. Screenshots are under
`/tmp/studio-npcs-visual`. Existing orc_v1 values were loaded against test_npc's exact
source image for read-only inspection/local draft editing; this does not establish
that the shared calibration is appropriate for that NPC and no reference was saved.

Physical-phone acceptance, mutation/export behavior and actual calibration-file writes
remain unverified at runtime. Source review covers those paths. Only Studio restarted
with unchanged approved configuration. No game restart, network/security changes,
merge or original-user-worktree edits. Stop before Mobs.
