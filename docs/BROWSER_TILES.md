# Browser Tiles

Tiles is the twelfth web workspace, at `/studio/tiles.html#list`. There is no
native Godot Tiles tab. It edits existing external TSX metadata, without a new
DB catalog, importer semantics, map-placement editor or image editor.

## Files and workflow

Configure `AssetRoots:Roots:tiled_tilesets` explicitly to the approved TSX directory.
There is no fallback derived from the checkout or another asset root. Images must
resolve under the existing `game_client_assets` root. Directory/file symlinks,
path escapes, DTDs/entities, non-UTF-8 XML, duplicate tile IDs/properties and unsupported
image layouts are rejected. Reads are bounded to 4 MiB XML and 16 MiB PNG.

The approved local installation uses the original project's
`prototype/shared/maps/tiled/tilesets` directory, while code runs from the separate
development checkout. Never copy development tilesets over original authoring data.
Local configuration stays ignored; deployments must choose their own explicit root.

Select a tileset, filter/search thumbnails, select a tile, edit, Preview changes,
then Save reviewed changes. Collection tiles retain sparse local IDs; atlas previews
use the source margin/spacing/columns. Browsing pages 60 tiles at a time does not
truncate data or search. Unknown and unsupported properties remain read-only.
Missing references remain visible; choosing a new reference requires an available
catalog and an existing ID. Draft/Disabled references are labeled and warned about.
Reference choices do not publish their definitions.

Supported fields are `ignitable` and `slick` as bool (unset/false/true), plus
`world_object_definition_id` and `mob_definition_id` as string on image collections.
`item_id` is editable only on the `item_spawns` collection. Atlas reference fields
remain read-only because the current importer does not consume them. Unsupported
structures/types on otherwise known fields remain read-only rather than normalized.
Fire/slick eligibility applies to Ground placements. NPC references and respawn
settings remain map-instance metadata; collision stays in map layers and World
Object footprints. Definition detail editing remains in the corresponding Studio tab.

## Preservation and save boundary

`TileMetadataAuthoringService` owns discovery, validation, preview and saving;
`TilesetXml` owns XML syntax and property-span replacement. No whole-file XML
reserialization occurs. Unrelated source text, image references, IDs, unknown
properties, ordering, animations, Wang/terrain and collision shapes remain intact.
Only edited supported property nodes and necessary enclosing nodes are written.

Preview returns a diff and a host-signed result bound to file, tile, input hash and
output hash. Save recomputes that result, serializes Studio writes, rechecks the whole
file immediately before a flushed temporary-file/atomic replacement, and retains
existing Unix permissions. This is not an OS compare-and-swap with external Tiled.
**Save and close the tileset in Tiled before editing in Studio; reopen it afterward.**
An open external editor can overwrite newer changes despite optimistic hash checks.

Conflict/uncertain saves retain the local draft and block further saves until
Reload / compare. Keep draft reapplies only fields actually edited against the new
baseline; unrelated external edits stay intact. Every rebased save requires preview.

Success means TSX saved only. Map import, generated bundles, publication/package
refresh and game reload remain separate. No automatic map or DB mutation occurs.
Existing browser Host/subnet/auth/CSRF rules cover every new API route.

## Validation at delivery

Production build: zero warnings/errors. JavaScript syntax and whitespace checks pass.
Independent source review found and corrected static-file allowlisting, preserving
unrelated edits during rebase, and bounding PNG reads before allocation.
CA-verified HTTPS reads covered ten original tilesets (eight collections/two atlases),
three reference catalogs, the page/module/CSS and collection/atlas PNGs. Nonmutating
previews covered replacement, new collection property and new atlas tile metadata.
No original file was saved; all ten original TSX hashes remained unchanged.

No tests added/changed/generated/run. Browser visual inspection could not complete:
in-app browser does not trust the existing local CA; Chrome is unavailable. No
certificate/security changes were made. Desktop/390px/physical-phone acceptance,
actual save roundtrips and external conflict behavior remain manual verification gaps.
Sandbox browser QA remains deferred; no live writes substitute for it.

## Reference evidence

- [Tiled TMX/TSX format](https://doc.mapeditor.org/en/stable/reference/tmx-map-format/):
  external TSX, sparse collection IDs, atlas geometry and custom properties.
- [.NET XML DTD processing](https://learn.microsoft.com/en-us/dotnet/api/system.xml.xmlreadersettings.dtdprocessing):
  prohibit DTD processing and disable external resolution.
- Existing `ActorRigCalibrationAuthoringService` supplies the narrow local donor for
  content hashes and temporary-file replacement; no generic authoring framework added.
