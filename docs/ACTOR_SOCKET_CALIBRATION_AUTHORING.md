# Actor Socket Calibration Authoring

R4D.1A establishes the file-backed authoring boundary for actor-specific rig
socket calibration. R4D.1B adds one shared draggable calibration editor to the
NPC and Mob workspaces without changing runtime actor content or rendering.

## Ownership

The canonical catalog remains in MMO Project:

```text
prototype/client/actors/appearance/data/rig_calibrations/catalog_v1.json
```

Content Studio resolves that file below the configured `game_client_assets`
root. The local .NET authoring host is the only Content Studio component that
mutates it. Godot UI code never writes the catalog directly.

The shared rig catalog owns base sockets. A calibration such as `orc_v1`
contains sparse actor-specific overrides, so editing it never changes
`humanoid_v1` for other actors.

## Host API

The host exposes:

- `GET /api/v1/actor-appearance/calibrations/{calibrationId}`
- `PUT /api/v1/actor-appearance/calibrations/{calibrationId}`
- `POST /api/v1/actor-appearance/calibration-frames`

Loading a missing ID succeeds with `exists = false`, `calibration = null`, and
the current `catalog_hash`. Every save includes `expected_catalog_hash`; a
stale value returns `actor_calibration_catalog_conflict` rather than overwriting
an external edit.

Socket saves replace the complete `sockets` set for one calibration. Existing
`foreground_overlays` and unknown forward-compatible fields are retained.
The host validates the rig and socket against the canonical rig catalog,
directions `N/E/S/W`, frames `1` through `4`, and signed integer source pixels
from `-4096` through `4096`. Rig IDs are immutable for existing calibrations.

Writes validate the resulting catalog in memory and flush a temporary file
beside the canonical catalog. Immediately before replacement, the host rereads
and rehashes the canonical file; a manual or external change returns a conflict
and removes the candidate temp file. Output uses deterministic ordering, UTF-8
without BOM, and one trailing newline.

## Calibration Frames

Calibration-frame lookup resolves only exact source art. NPC Chars families use
the established direction/frame filename convention, including their distinct
F4 files. Mobs derive their normalized actor key from the authoritative fallback
basename and look only for `actors/mobs/<key>-F<frame>-<direction>.png`.

Missing exact poses are reported unavailable. This authoring path never uses
runtime directional-F1, static-image, or other compatibility fallbacks.

## Shared Editor Workflow

The NPC and Mob workspaces pass their current unsaved composite descriptor,
selected rig, actor kind, and visual texture path to one shared calibration
editor. The editor requests the exact frame list from the host and draws only
the selected returned PNG. This is intentionally separate from the ordinary
actor preview, which answers a different presentation question.

The editor resolves each selected socket pose with the same precedence as
runtime: an actor calibration override wins; otherwise the canonical rig socket
is shown as inherited. Inherited markers are hollow and actor overrides are
solid, with accessible source text beside the canvas. Dragging or changing the
integer X/Y fields creates only the selected sparse override. Reverting removes
only that pose and cleans empty sparse containers.

Mouse drags are clamped to the visible exact source image and quantized to
integer source pixels. Numeric X/Y controls remain available across the full
signed `-4096..4096` range so intentional virtual coordinates are preserved.
Out-of-frame numeric coordinates remain valid but the marker is clipped with a
status message.

The art view provides Fit, 100%, 200%, 400%, and 800% zoom. A scrollable
canvas provides transparent padding around every image edge, uses nearest
display, and draws a source-pixel grid at high zoom. An unavailable exact pose
does not display a fallback and disables coordinate mutation and dragging.

Calibration state is independent of the NPC/Mob preview-and-apply workflow.
The editor retains the complete loaded `sockets` dictionary locally, so editing
one pose cannot erase other overrides when the complete dictionary is saved.
Unsaved calibration edits are visibly tracked and block a calibration context
switch until discarded. Reload requires a second confirmation when edits are
dirty. A catalog conflict preserves local edits, disables save, and requires an
explicit reload; it never retries or overwrites the newer catalog.

Typing or loading a valid missing calibration ID does not create a catalog entry.
The first saved override creates it. Linking that calibration to an NPC or Mob
is a separate explicit action that updates only the current unsaved composite
descriptor; the normal workspace validation and apply operation remains solely
responsible for saving or publishing actor content.

## Follow-up Boundaries

R4D.2 owns equipped-item grip anchors. R4D.3 owns foreground-overlay rectangle
editing. R4D.4 owns combined actor-and-item alignment, and R4D.5 owns full
production pose calibration. R4D.1B does not edit grip anchors or foreground
overlays and does not change MMO runtime behavior.
