# Browser Items workspace

The existing .NET 10 host serves `/studio/`. The Godot desktop Studio, its local-file
import and legacy `/api/v1` contract remain available on the loopback listener.
The browser has one Items workflow owner (`host/wwwroot/studio/items.js`) and uses
`UnifiedItemAuthoringService` for the same preview, validation and transactional
lifecycle decisions. No game service lifecycle commands are exposed.

## Scope

Search, list, new item, full ordinary fields, canonical icon selection/PNG upload,
preview, Save Draft, Save & Publish, Publish, Disable and Delete are included.
Consumable requirement/effect rows, equipment requirements/modifiers, all combat
bonuses, weapon/melee/ammunition profiles, tool capabilities and all economy fields
are editable. Optional sections are removed only by an explicit user action;
incompatible weapon metadata is rejected rather than silently cleared on a slot change.

Equipped asset/rig/binding/layer/socket references are editable. Secondary socket,
nudge, grip anchors and per-pose flip/hidden/over-grip settings are visible read-only.
The browser retains their stored values, checks them again before preview/save, and
blocks a save when the existing normalizer cannot round-trip them. Precise paper-doll
and actor socket calibration remain desktop work. No browser visual-editor parity is claimed.

Desktop layout has workspace navigation, list and editor. Under 800 CSS pixels the
workspace/list/detail views have Back navigation. The form is single-column under
1150 pixels. Sections collapse, controls have visible labels, row actions require no
hover, and the action area leaves normal flow when a mobile keyboard shrinks the
visual viewport. Browser Back to the list retains the draft; selecting a different
item asks before discarding it. Closing/reloading a dirty tab uses the browser's
unsaved-change warning. Drafts are in memory, not durable browser storage.

## Integrity and outcomes

- All item writes carry the exact version string and operation-specific preview
  signature. Editing invalidates the preview. Obsolete asynchronous results are ignored.
- Browser Int64 fields are decimal JSON strings in both directions. Never convert
  economy values to JavaScript Number. This does not change desktop JSON.
- Unknown JSON request properties are rejected. Nullable values and pose maps are
  retained. Ordered consumable/melee rows keep their explicit indices; skill/tool
  normalization continues to follow the existing server rules.
- Save Draft removes Published state. Save & Publish edits an already Published item
  without temporarily disabling it. New items use Save Draft then Publish.
- A mutation response reports database commit separately from equipment visual
  catalog export (`not_requested`, `succeeded`, `skipped`, `failed`). No result means
  that the game restarted or necessarily reloaded new content.
- A lost write response, server failure or concurrency conflict blocks another apply.
  Reload / compare displays local and server definitions. The author must explicitly
  choose the server result or retain edits against the new version and preview again.
  Matching stored content cannot prove export success. Export retries are host-only.
- Existing domain validation and reference/possession protection remain authoritative.

## Access defaults and activation (not performed)

By default, with no `Browser:PasswordHash`, browser access is read-only on the existing loopback
listener; no password is generated. The local shell/new-item form can be inspected
without a database. Asset selection does not write; upload and all POST/PUT operations
are unavailable without a configured login. `Browser:ReadOnly=true` also blocks
browser preview/mutation/upload even after authentication. Legacy desktop behavior
is independent of that browser read-only switch.

LAN access is opt-in using `Browser:LanUrl`. It requires all of:

- One literal RFC1918 IPv4 HTTPS address with a separate port; no wildcard address.
- `Browser:AllowedSubnet`, an explicit private CIDR of /16 or narrower; prefer the
  actual home subnet (usually /24), not a whole private address range.
- `Browser:CertificatePath` and optional `Browser:CertificatePassword` for a certificate
  trusted by Taylor's phone and valid for that address.
- Either `Browser:PasswordHash`, an ASP.NET Core Identity PasswordHasher-compatible
  hash for the single Studio owner login, or the explicit trusted-home-LAN mode below.
  No password or example working credential is shipped.

These settings belong in the existing ignored host configuration or protected host
environment. Do not put secrets in Git or shell history. No configuration file is
created by this implementation. Do not use reverse proxies/forwarded headers, tunnels,
router port forwarding or public binding for this slice.

The LAN listener only admits `/studio` routes. Legacy authoring routes and local-path
imports remain restricted to the loopback listener and reject browser-origin calls.
The Host header must match the selected address and port; LAN peers must match the
configured subnet. No CORS access is enabled. Same-origin antiforgery tokens protect
browser writes (including sign-in), and the owner session is HttpOnly, SameSite Strict,
secure on HTTPS, four-hour non-sliding. Login is limited to five attempts per minute
across the host. Sessions and data-protection keys are memory-only and expire on restart.
The framework may log its generic "No XML encryptor" warning; the configured key
repository is in memory and does not persist keys to the user's profile.

Before activation, Taylor must explicitly approve the concrete address/port/subnet,
certificate provisioning/trust, credential creation and any necessary host firewall
change. Separately confirm DB profile, asset root and MapPublisher output destination.
Start/restart only the authoring host after approval. Verify the phone URL, authentication,
CSRF rejection, denied non-Studio routes and excluded networks. Never restart the game
as part of browser activation. Stop the authoring host and remove its opt-in LAN
configuration to roll back exposure; do not alter the game listener.

The existing `AuthoringHost:ListenUrl` defaults to `http://127.0.0.1:5187`.
A local preview may select a different loopback port with blank credentials/database
and `Browser:ReadOnly=true`; it does not exercise persistence or authenticated flows.

## Explicit passwordless home-LAN mode (not activated)

Taylor requested no password for the initial home-LAN setup. The narrow opt-in is
`Browser:TrustedHomeLanWithoutPassword=true` (default **false**). It authorizes
browser access by network location, not by a user identity. It requires the same
explicit private IPv4 HTTPS listener, matching Host header and allowed source subnet.
The actual local socket address and port must also match the configured listener.
It cannot be enabled without a LAN URL or together with a password hash; startup
fails for either ambiguous configuration. To restore password access, remove this
opt-in before configuring a password hash.

Proposed configuration, only to be written during the separately approved activation:

```json
{
  "Browser": {
    "LanUrl": "https://192.168.0.96:5188",
    "AllowedSubnet": "192.168.0.0/24",
    "CertificatePath": "/home/taylor/.config/mmoproject/content-studio/tls/studio-server.pfx",
    "TrustedHomeLanWithoutPassword": true,
    "ReadOnly": false
  }
}
```

Omit `PasswordHash`; no password-entry helper or credential setup is needed or
provided. The proposed certificate file does not yet exist. Private certificate/key
material stays owner-readable on the host; only the public CA certificate is handed
to Taylor for explicit installation/trust on the phone. Do not bypass certificate
warnings or substitute HTTP. Recheck the address before generating the IP-SAN
certificate or activating the listener.

**Consequence:** every person, device or program able to connect from the allowed
subnet receives the same Items access, including viewing, PNG upload, edits, publishing,
disabling and deleting (subject to existing authoring validation/reference rules).
This includes guests or compromised devices on that subnet. There is no individual
login, per-user attribution, logout-based revocation or client identity check.
Server-certificate trust authenticates the server to the phone, not the phone to the
server; a non-browser client on the subnet is not excluded by phone trust setup.

The UI clearly labels this shared access and uses an explicit `can_edit` permission;
it does not pretend the caller signed in. HTTPS, Origin checks and antiforgery tokens
still protect all browser writes. The anonymous antiforgery cookie is Secure over
HTTPS and SameSite Strict; no login session or credential is manufactured. These
protections prevent cross-origin misuse, not deliberate access by an allowed LAN
peer. Existing asset confinement and route restrictions remain unchanged.

Unconfigured loopback browser access remains read-only even when this LAN opt-in is
set. Legacy desktop routes remain loopback-only and are never exposed on LAN.
`Browser:ReadOnly=true` still blocks browser preview/write/upload in either mode.
Stop the Studio host or remove this opt-in (and the LAN URL if there is no password)
to revoke network-authorized access. Do not change the game service.

This code change does not activate a listener, provision certificates, write host
configuration, or alter firewall policy. Firewall changes require separate approval.

## Assets

Browser APIs use canonical `res://assets/items/…` identities and confined authenticated
image URLs, not host paths. Directory traversal and symlinks in root/descendant paths
are rejected. No arbitrary game directory or filesystem root is served. Desktop
`source_file_path` import is unchanged and unavailable through the LAN listener.

Uploads are raw PNG bytes, capped at 16 MiB, 4096 pixels per side and 16 million pixels.
The host checks signature, dimensions, chunk CRCs and bounded scanline decompression,
including Adam7 images. Filenames have a small allowlist; imports use atomic creation,
reuse byte-identical files and never replace a different file. Uploading creates the
asset immediately, independently of item save. Failed/uncertain uploads can safely be
retried with the same name and bytes. Only a trusted host user may administer asset
folders; the application does not defend against concurrent malicious host filesystem
administrators swapping directories during a request.

## Verification and acceptance

No tests were added, modified, generated or run, per Taylor's instruction.
Production builds and JavaScript syntax parsing are used for this delivery. The safe
preview uses an empty database and no credentials, so persistence, authenticated login,
export and live-content behavior are not claimed as runtime-verified.

A real browser/phone acceptance pass must cover navigation, keyboard-safe actions,
long/nested forms, error focus, unsaved changes, login expiry, image selection/upload,
all supported item types, concurrent desktop/browser edits and uncertain-response
reconciliation. Use an explicitly approved isolated authoring database/content root
for mutation acceptance. Do not create or change live content merely for QA.

Framework boundary follows ordinary ASP.NET Core facilities:
[Kestrel endpoint configuration](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/kestrel/endpoints?view=aspnetcore-10.0).

## Passive defensive responses

Combat bonuses include `parry_base_chance_basis_points` and
`block_base_chance_basis_points` in native and browser editors, catalog detail,
preview and save/publish. Zero disables eligibility; 100 bp is 1%. Parry requires
a right-hand melee weapon; positive block explicitly declares a left-hand shield.
The approved starter values are 500 bp. Base Defence adds 5 bp per level above 1,
capped at 1000 per response. No Concentration cost; the outgoing Block combat style
is separate. Migration 106 is required before activating these host binaries.
These gameplay facts load from SQL; the equipment visual export stays appearance-only.
