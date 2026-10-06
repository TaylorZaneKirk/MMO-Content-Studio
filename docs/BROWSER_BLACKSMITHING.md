# Blacksmithing in the browser

`/studio/blacksmithing.html` follows the accepted desktop sidebar/list/detail and
phone workspace/list/detail/back shell. The existing BlacksmithingAuthoringService,
repository and mirrored BlacksmithingRecipe remain authoritative. No gameplay,
bronze content, database schema or station/tool rules are changed.

The complete aggregate is editable: recipe ID/name, smelt/forge operation, station,
ordered input item IDs/quantities, output item/quantity, required level, XP, duration
and nullable inventory tool. Recipe ID remains stable after the first save. Draft
references and invalid/unknown values stay visible rather than being normalized away.
Smelt requires bronze_smelter and no tool; Forge requires blacksmithing_anvil and
blacksmithing_hammer in inventory. Switching operation retains the prior references
and explains any mismatch. An explicit confirmed action applies the required setup.
The host validates restrictions for drafts and again for publication; publication
also requires all item/station references to be Published.

Searchable reference choices show identity and publication state. Existing missing
references can be entered directly and remain preserved. Duplicate input items and
ineligible station/tool choices are marked in the picker. Reference links open the
existing Items/World Objects workspace in another tab with a filtered catalog; the
recipe draft stays in its original tab. Catalog refreshes only update lookup labels,
never rebuild or truncate a draft. Failed catalog reads have an explicit retry path.

XP is presented in units with one fractional decimal digit and stored as exact
integer xp_tenths. Parsing uses decimal digits and BigInt arithmetic before bounded
Int32 conversion, never floating-point multiplication or rounding. The maximum
2147483647 tenths displays as 214748364.7 XP. More than one decimal digit is invalid;
invalid text survives row additions/reorders. All other numbers retain full Int32
bounds; required level is 1–99, XP is nonnegative, quantities/duration are positive.

Save Draft, Publish, Disable and Delete use complete requests, exact timestamps and
operation-specific signatures. There is no Save & Publish. Saving Published content
unpublishes it; Delete accepts Draft/Disabled only. Edits invalidate the preview and
stale async results are ignored. Conflicts, missing-after-write results and uncertain
transport/server outcomes lock further applies until explicit reload/compare.

Database commit and requested MapPublisher export are separate outcomes. Export is
requested when the prior or resulting recipe is Published. No response implies live
game restart/reload; an export failure is not grounds for repeating the mutation.
The existing LAN/access/CSRF boundary is unchanged and no lifecycle command controls
the game. Desktop authoring remains available.

No tests are added, modified, generated or run. Production build, JS syntax,
independent exact-change source review, Chrome desktop/390px visual inspection and
CA-valid read-only runtime requests are the delivery checks. No live recipes are
saved/published/disabled/deleted for QA. Their mutation, concurrency and export paths
are source-reviewed but remain unverified against live data; phone acceptance is
separate from a desktop Chrome viewport inspection.

Delivery inspection loaded the existing Published smelt_bronze and
forge_bronze_dagger definitions without changing them. Chrome displayed 62 tenths
as 6.2 XP and 125 tenths as 12.5 XP, with the saved badge retained. Item/station
choices loaded from the existing catalogs. Desktop and 390px views showed no
horizontal overflow or runtime exceptions. A separate unsaved local draft displayed
214748364.7 XP without truncation; it was never submitted. Screenshots are under
/tmp/studio-blacksmithing-visual and selected images are saved to Library.
