# Browser Environment

`/studio/environment.html` completes the eleven-tab browser expansion. Environment
is a read-only overview, matching the desktop Environment panel in `main.gd` and
`Main.tscn`: host/API, PostgreSQL and schema health, configured asset-root status,
and saved-content catalog counts. Desktop Environment has no configuration editing,
migration execution or service controls; none are added to the browser. Desktop's
global Retry reconnects and Refresh reloads the scene. Browser Refresh reads only
Environment, without reloading or discarding another workspace's draft.

## Safe read boundary

BrowserEnvironment projects health into an explicit whitelist. It returns numeric
host/API versions, UTC observation time, status, connection/schema-verification
booleans, code-generated schema check IDs/statuses and fixed asset-root labels.
Connection/profile/database identities, schema-contract configuration text, paths,
credentials, service configuration and raw exception messages are omitted. Schema
identifiers can contain words such as `profile` or `source_path`; no such stored
values are returned. Unknown configured roots receive numbered generic labels.
Existing BrowserAccess authorization and strict static-file allowlisting apply.

Actor catalog availability/counts come from the existing appearance service. Root
health means directory presence; actor counts mean readable metadata, not proof
that every image reference decodes. Each of the ten catalog summaries calls the
existing authoring service directly and returns only total/publication counts.
Failed service reads return 503 with static guidance. The desktop aggregate catalog
is deliberately not reused: some providers turn failed reads into empty lists.
A failed browser summary never reports a misleading zero count.

## Refresh and diagnostics

One Environment controller owns all reads and presentation. Session is checked
first; independent health, actor-assets and catalog panels complete separately.
A 45-second deadline bounds a refresh. Old content is cleared while reading;
revision checks and cancellation prevent superseded results from reappearing.
Unavailable/denied/timed-out reads have explicit retry guidance. Sign-in/out use
the existing session routes. Refresh does not initiate any authoring operation.
Schema checks default to failures and support searching or viewing all checks.
Constraint/trigger existence uses pg_catalog so a role with only SELECT on gameplay
tables can inspect metadata without false failures or added gameplay write privileges.
Each successful panel shows its observation time. Counts represent separate reads,
not a transactionally consistent snapshot or the running game's loaded content.

## Verification and limits

Production build: zero warnings/errors. JavaScript syntax and whitespace checks
passed. Independent source review was clean; the parent handoff records the exact
commit review. No tests were added, changed, generated or run.

CA-validated HTTPS reads returned 200 for Environment assets, all twelve Environment
read endpoints, and all ten prior workspace pages with Environment navigation.
The health projection contained 742 healthy schema checks and two healthy roots;
actor catalogs contained one rig, one calibration and six equipped visuals. All ten
content summaries loaded. Legacy LAN API, wrong Host and excluded source remained
403, unknown catalog 404, and the desktop health endpoint remained Healthy.
The whitelist response was inspected for sensitive fields and host paths.

Actual Chrome desktop/390px inspection covered status, schema search/filter,
refresh, catalog cards and eleven-link mobile navigation. No horizontal overflow
or runtime exceptions. Screenshots are local under `/tmp/studio-environment-visual`;
no Library upload was attempted. Physical-phone acceptance, deliberate outage/
authentication-expiry recovery and long-running timeout observation remain unverified;
source review covers those branches. Only the approved Studio service restarted
with unchanged configuration. No live content/calibration QA writes, game restart,
network/security changes, migration execution or merge occurred.

The expansion checklist and aggregate acceptance gaps live in the parent
`docs/modernization/BROWSER_STUDIO_EXPANSION_PLAN.md`; CURRENT_HANDOFF owns current
operational status. The disposable sandbox and repository-local delivery skill are
scoped next, without provisioning a database or credentials in this slice.
