# Foreground Grip Overlay Authoring

R4D.3 extends the shared actor attachment calibration workflow with actor-owned
foreground grip rectangles. The canonical data remains in MMO Project:

```text
prototype/client/actors/appearance/data/rig_calibrations/catalog_v1.json
```

## Ownership and precedence

The rig owns every `overlay_id`, its associated `socket_id`, source layer, and
directional depth. Actor calibration owns only sparse source-rectangle
overrides. An effective rectangle resolves as actor override, then rig default,
then no rectangle. Items, item grip anchors, and gameplay equipment slots do
not own foreground overlays.

## Geometry

An overlay is the source crop `[x, x + width) x [y, y + height)` from the
selected actor image. Values are integer source pixels and must stay fully
inside the exact selected actor frame: nonnegative `x`/`y`, positive width and
height, and no edge beyond the image. There is no compatibility-frame fallback.

The shared editor uses the same one calibration state, catalog hash, optimistic
concurrency, save, discard, and conflict lifecycle as socket calibration. It
saves complete `socket_overrides` and `foreground_overlay_overrides` together.
An omitted overlay payload remains backward compatible and preserves the raw
canonical overlay data exactly.

Viewing an inherited rectangle does not create an override. Moving, resizing,
or editing fields creates one. Revert removes only the selected sparse
rectangle. A pose with no rig rectangle may create a local 16x16 override near
its associated effective socket, clamped to the exact frame.

## Follow-up boundary

R4D.4 will provide the combined actor-and-item alignment workspace. R4D.3 edits
only actor-art crop geometry; it does not render or edit equipped item art in
the calibration canvas.
