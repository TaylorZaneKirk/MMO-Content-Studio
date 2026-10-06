# Browser Shops

Shops extends the existing Items presentation at `/studio/shops.html`. Items remains
at `/studio/index.html`. Both pages use the same responsive workspace/list/detail
layout, labels, touch targets and keyboard-safe actions.

`shops.js` owns the complete Shop workflow. `studio-common.js` contains only the
browser request boundary and pane/navigation helpers. Full-page navigation and
browser Back out of the page use the native unsaved-work prompt; hash Back/Forward
within a page retain the editor and draft. Pending/uncertain writes also block
workspace-link clicks. Browser/device termination is not durable draft storage.

## Content and actions

- Stable ID, display name, notes, buying unstocked items and stock-price adjustment.
- Ordered stock with searchable item selection, exact base-price/policy labels,
  default stock, restock ticks, move up/down and removal.
- Missing/unavailable item references remain visible and preserved on load.
- Preview and apply Save Draft, Save & Publish, Publish, Disable and Delete using
  the existing Shop service. New definitions save as Draft first; Save & Publish
  requires an existing Published Shop; Delete requires Disabled state.
- Saved-only actions require no unsaved edits. Current item eligibility and NPC
  dependencies are still validated by the host, including at apply time.
- Timestamp strings are never parsed/reformatted. Item Int64 prices remain decimal
  strings; editable stock counts/rates are validated Int32 values.
- Preview snapshots are invalidated by edits or action changes. Failed/uncertain
  writes require reload/compare, with explicit choices to accept the server version
  or retain local edits against the newly read version. No automatic write retries.
- Database commit, Shop catalog export and live-game restart are distinct outcomes.
  This workspace never restarts the game. Export failures do not invite repeating
  a content mutation.

## Boundaries

`BrowserShops.cs` exposes only `/studio/api/shops` and its explicit list/options,
load, preview and lifecycle routes. It delegates authoring decisions to
`ShopAuthoringService`; no database schema or desktop contract changed.
`BrowserJson.cs` shares the existing 1 MiB request bound, strict unknown-field
handling, Int64 transport and safe export diagnostics with Items.

The existing configured HTTPS listener, allowed subnet, Host checks, Origin/CSRF
and passwordless-network authorization apply unchanged. Legacy `/api/v1` remains
unavailable over LAN. New static files are individually allowlisted.

Production build, JavaScript syntax and independent source review are required.
Live runtime verification is read-only: entry URLs, static dependencies, sessions,
catalog/options/detail and preserved desktop handshake. No live content is mutated
for QA. Phone interaction acceptance remains Taylor's decision.
