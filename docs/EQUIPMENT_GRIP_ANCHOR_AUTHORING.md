# Equipment Grip Anchor Authoring

R4D.2 formalizes the existing **Equipped Appearance** grip-anchor controls in
the unified Items workspace. It does not add an item API, database schema, or
runtime rendering path.

## Socket-Bound Items

For a socket-bound visual, **Grip Anchor X/Y** identifies the integer
source-art pixel on the selected item PNG that aligns to the selected actor
socket. The preview shows both markers:

- **Actor Socket** is gold and read-only.
- **Item Grip Anchor** is pink and draggable.

Dragging moves only the item grip anchor. It never changes actor-rig socket
metadata. Coordinates are persisted per direction and frame for `N`, `E`,
`S`, `W` and frames `1` through `4`. Copy Previous, Copy Next, Clear Pose, and
the one-pixel nudges change only the selected pose anchor. Clear Pose removes
that anchor and leaves visibility, flip, depth, and all other pose metadata
unchanged.

The numeric controls accept the existing signed `-4096..4096` contract for
intentional virtual anchors. Direct canvas dragging is deliberately stricter:
while calibrating an exact selected PNG it clamps to that PNG's source bounds.
When a pose is flipped, its stored anchor stays unchanged; the preview mirrors
the effective X coordinate from the exact PNG width.

## Exact Art Required for Calibration

Ordinary paper-doll presentation retains its established compatibility
fallbacks. Grip calibration does not use them. It requires the exact selected
file:

```text
<asset_key>-F<frame>-<direction>.png
```

If that file is absent, the Item workspace shows `Item art: unavailable for
<direction>/F<frame>`, hides the attachment markers, and disables grip-anchor
dragging and mutation. A hidden pose is likewise not editable, but its stored
anchor remains intact. Select another visible pose or add the exact item art
before calibrating it.

Rig-layer visuals are not socket attachments. Their normal preview settings
remain available, while Grip Anchor X/Y, marker legend, and grip actions are
hidden so the editor cannot fabricate socket data.

## Combined Actor Alignment Handoff

The NPC/Mob calibration workspace also provides an **Item Grip Anchor** mode
when its current composite cosmetic selects a socket-bound item. That view
combines the exact actor and item source frames with the rig-owned hand overlay
to calibrate their visible alignment. Its pink marker edits a local copy only.

Choosing **Open Item Save Workflow** opens the selected item through the
existing unified Items workspace and transfers that local per-pose anchor map.
The developer must use the existing Validate and Save Draft/Publish flow there
to persist it. This handoff never writes a rig calibration file and never
persists preview-only changes automatically.

The combined workspace supports explicit copy and mirror to a selected target
pose. Mirror requires both exact source frames and uses the target width plus
the source and target `flip_x` metadata; it does not infer directional pairs.

## Local Host Note

`./mmo-content-studio` reuses a reachable authoring host. Health checks do not
identify the host build revision, so after changing host code developers should
restart an existing host before testing that change. Automatic stale-host build
identity is deferred as a launcher improvement; R4D.2 does not alter launcher
behavior.
