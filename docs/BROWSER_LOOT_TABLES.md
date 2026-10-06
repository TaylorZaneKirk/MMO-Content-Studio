# Browser Loot Tables

`/studio/loot-tables.html` adds a complete Loot Tables workspace using the accepted
Items/Shops shell. `loot-tables.js` owns editing, preview/apply and recovery;
`BrowserLootTables.cs` delegates to the existing Loot Table authoring service.

## Complete content

- Identity/description and every group/outcome field are preserved.
- Guaranteed, Pre-roll, Main and Tertiary sections; Guaranteed All, Weighted One
  and Independent roll behavior; explicit group/outcome order and roll count.
- Three pre-roll failure/success controls, item/nested-table/no-drop outcomes,
  quantity ranges, weights and exact probability numerator/denominator.
- Searchable item/table choices retain unpublished and missing references. The
  picker prevents direct self-reference; manually entered references remain
  subject to the existing host's cycle/depth/expansion/publication validation.
- Kind changes never silently clear data. Populated incompatible fields remain
  editable, and an explicit confirmed clear action removes only incompatible
  fields. Nullable blanks differ from zero. Int64 probability/reference values
  stay strings; editable Int32 values use range-checked numbers.
- Move controls swap explicit order values among peers in a section/group;
  manual order fields allow repairing duplicates. Numeric gaps survive unchanged.
- Structured expected-value reporting includes validity, exact total/no-drop
  fractions, item/section/path contributions, zero-value/currency information and
  host diagnostics. Reports are labeled stale after edits; no client EV rounding.

## Lifecycle and recovery

The existing operations are Save Draft, Publish, Disable and Delete. There is no
Save & Publish operation. Save Draft changes Published content to Draft; the UI
states this explicitly. Publish/Disable/Delete operate on saved content and require
no local changes. Delete requires Disabled state. The host remains authoritative
for current references, cycles, rules and version/signature matching.

Requests preserve exact timestamp strings and the reviewed complete draft. Every
edit/action change invalidates preview. Selection/search/options responses have
stale-response protection. Transport/server/conflict ambiguity locks further writes
until reload/compare; local edits remain available for explicit reconciliation.
Hash Back/Forward retains local draft state, and full-document navigation uses the
shared native unsaved-work guard. Browser/device termination is not durable storage.

The service does not request a MapPublisher export. Successful mutation responses
state DB commit, no requested catalog export and no live-game restart separately.
No schema, desktop API, gameplay or authoring rules were changed.

## Access and verification

Only narrow `/studio/api/loot-tables` routes and explicit static files are added.
Existing HTTPS/subnet/Host/Origin/CSRF/passwordless configuration is unchanged;
legacy APIs remain unavailable over LAN. The shared strict 1 MiB JSON boundary and
exact numeric transport apply.

Production build, JS syntax, independent source review, actual Chrome visual
inspection and read-only CA-validated runtime diagnostics are required. No tests
or live authored-content mutations are permitted for QA. Physical-phone and live
mutation acceptance remain Taylor's responsibility.
