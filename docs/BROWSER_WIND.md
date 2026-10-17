# Browser foliage wind

Open **Tiles → select tile → Author foliage wind**, or `/studio/wind.html`.
Select an existing family or a native32px atlas rectangle (up to7×7). Enable wind,
choose an existing profile, paint moving red/fixed green, inspect the game shader,
then **Validate preview → Save source family**. Keep roots/trunks fixed. Undo
restores one stroke; reload resolves a stale source conflict.

The existing browser owner/session/CSRF boundary applies. `game_client_assets` and
`tiled_tilesets` must resolve to the intended source checkout. New source PNGs must
be non-interlaced8-bitRGB/RGBA; collection images support one32px tile. Source art,
TSX, collision and harvest metadata are never written by this workflow.

`FoliageAuthoringService` owns load/source assembly, validation, signed previews
and save. Derived art/masks are content-addressed and installed before the atomic
`families.json` replacement. Prior derived files remain available. The fixed game
shader resource drives the WebGL preview; there is no independent motion formula.

Save changes source assets only. Use the existing client export/publication process
for delivery. Every matching supported placement uses the family; transformed,
non-default-surface and building tiles retain native rendering. New families use
`tile_foliage`; existing family fields and profiles are retained.
