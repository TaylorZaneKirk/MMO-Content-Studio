# World Objects in the browser

`/studio/world-objects.html` follows the accepted Items/Shops/Loot shell. The
existing WorldObjectAuthoringService remains the single lifecycle decision owner;
placements, surfaces and elevation remain in Tiled. No game lifecycle is exposed.

The complete definition is editable: stable identity/name, blocking and tile
footprint, canonical base PNG, source dimensions, pixel anchors, render scale,
optional FPS, ordered interactions/default action and ordered PNG frames.
No precision fields are deferred. Numeric offsets, one-pixel nudges, fit/zoom,
base/frame selection and play/pause support mouse, keyboard and touch. The canvas
uses the game renderer's uncentered full PNG at placement origin plus unscaled
anchor offsets, with render scale applied to artwork. Tiles are 32 game pixels.
A separate dashed rectangle shows declared source metadata; the client does not
crop/resample to these dimensions. Dimension mismatch/missing PNGs are explained.
Elevation, resource depletion and occlusion are outside a definition-only preview.

The complete aggregate retains unknown/missing references, exact Double values,
nullable FPS and ordered children. Invalid input remains visible, never silently
clamped or cleared. The existing service owns validation and reference rules.
Save Draft, Publish, Disable and Delete require operation-specific preview and the
exact timestamp/signature. There is no Save & Publish operation. Saving Published
content unpublishes it; placement warnings must remain visible. Delete accepts
Draft/Disabled definitions. Pending/conflicting/uncertain writes are blocked until
explicit reload/compare reconciliation. Hash navigation retains drafts; leaving
the document warns about unsaved edits. Drafts are held in memory only.

Database commit and MapPublisher World Object export are reported separately.
Export is requested when the prior or resulting definition is Published. Export
failure after commit is not a reason to repeat the mutation. Nothing implies live
game restart or automatic live reload.

Artwork URLs admit only canonical PNGs inside the configured game asset root,
matching existing World Object references. BrowserPngAssets shares the existing
bounded PNG validator and atomic importer; Items retains its narrower items-only
scope. World Object uploads always target maps/objects/world_objects, with no
browser host paths or directory choice. Traversal and symlink paths are rejected.
PNG limits remain 16 MiB, 4096px per side and 16 million pixels. Different existing
bytes are never overwritten; identical-name/bytes retries reuse the existing PNG.
Import creates the PNG immediately, independently of saving the definition, with
an explicit confirmation. Catalog search covers every PNG; the picker displays
up to 120 matching thumbnails and asks for a narrower search beyond that.

The exact approved LAN/HTTPS/access configuration is unchanged. World routes use
the same access and CSRF boundary; desktop local-path import remains loopback-only.
Options omit the host asset-root path. No new settings or credentials are needed.

No tests are added, modified, generated or run. Delivery verification uses a
production build, JS syntax checks, independent source review, actual Chrome
at desktop/390px widths and CA-validated read-only runtime requests. No live
content saves, publication, deletion or PNG uploads are performed for QA. Their
runtime outcomes and physical-phone acceptance remain Taylor's separate gate.

Delivery inspection loaded 21 existing Published definitions and the 7,351-PNG
catalog. Ale Sign alignment/artwork and the existing four-frame Fishing Spot at
4 FPS rendered in Chrome; playback leaves the definition clean. An additional
animation-controls screenshot uses an unsaved local Ale Sign draft. No mutation
or upload endpoint was invoked. Desktop and 390px viewport inspections observed
no horizontal overflow or runtime exceptions. This is not physical-phone acceptance.
