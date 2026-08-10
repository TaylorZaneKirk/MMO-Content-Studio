# Actor Socket Calibration Authoring

R4D.1A establishes the file-backed authoring boundary for actor-specific rig
socket calibration. It does not add editor controls yet.

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

Writes validate the resulting catalog in memory, flush a temporary file beside
the canonical catalog, and then atomically replace the canonical file. Output
uses deterministic ordering, UTF-8 without BOM, and one trailing newline.

## Calibration Frames

Calibration-frame lookup resolves only exact source art. NPC Chars families use
the established direction/frame filename convention, including their distinct
F4 files. Mobs derive their normalized actor key from the authoritative fallback
basename and look only for `actors/mobs/<key>-F<frame>-<direction>.png`.

Missing exact poses are reported unavailable. This authoring path never uses
runtime directional-F1, static-image, or other compatibility fallbacks.

## Follow-up Boundaries

R4D.1B will add the draggable socket workflow using the host API and shared
preview geometry. R4D.2 owns equipped-item grip anchors. R4D.3 owns foreground
overlay rectangles. Neither is edited by R4D.1A.
