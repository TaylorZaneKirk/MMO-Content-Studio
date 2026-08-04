# Remote and Mobile Content Studio Plan

Status: **Planned and deliberately deferred**

Priority: **Last among currently known MMO Content Studio roadmap work**

## Decision

MMO Content Studio should eventually support secure remote use through a mobile
browser or installable Progressive Web App. A native Android package may follow
if native distribution or platform integration provides enough value.

This work is not a near-term priority and is not a prerequisite for the current
world-object, gathering, quest, dialogue-integration, Quest Studio,
contribution-publication, or hot-reload plans. It remains last among the
currently known roadmap items unless a concrete operational or MMO Project
deployment dependency justifies moving it earlier.

The preferred first delivery target is a responsive Godot Web/PWA client over a
secure remote authoring host. A separate mobile application rewrite is not the
planned direction.

## Why preserve the idea

The Content Studio is a useful lower-risk proof of concept for infrastructure
that MMO Project will eventually need:

- remote Godot clients connecting to a hosted .NET service;
- HTTPS, authentication, authorization, and session handling;
- browser and mobile export pipelines;
- reconnect and optimistic-concurrency behavior;
- remote asset upload and delivery;
- responsive Godot interfaces and touch interaction;
- deployment, logging, monitoring, and update workflows.

Content Studio is a safer proving ground than the live game because it has no
real-time combat loop, player economy, public player accounts, or anti-cheat
requirements. It can validate shared transport and deployment ideas without
pretending that authoring-host security is sufficient for the complete MMO
runtime.

## Current constraints

The present application intentionally uses a trusted local-desktop boundary:

```text
Godot desktop client
        ↓ loopback HTTP/JSON
.NET authoring host
        ↓
PostgreSQL + MMO Project checkout + canonical asset directories
```

The current host accepts loopback HTTP only. The Godot client expects
`127.0.0.1`, selects local files by absolute path, and reads local game assets
directly for visual previews. Those behaviors are appropriate for the current
single-machine workflow but cannot be exposed directly to a browser or remote
mobile client.

Remote support therefore requires a deliberate security and asset-transport
boundary. Merely exporting the current Godot project to Web or Android would
not produce a safe or complete remote workflow.

## Locked direction

### Preserve two operating modes

The existing workflow must remain available:

1. **Local trusted mode**
   - loopback-only authoring host;
   - launcher-managed host process;
   - local database and asset paths;
   - no dependency on remote hosting.

2. **Remote authenticated mode**
   - explicitly enabled and separately configured;
   - HTTPS-only access;
   - authenticated and authorized mutations;
   - host-owned database, repository, export, and filesystem operations;
   - audit evidence for publication and destructive operations.

Remote mode must not weaken the safety assumptions of local mode.

### Prefer Web/PWA before native mobile

The first proof should use a responsive Godot Web export delivered as an
installable PWA because it offers:

- one client for Android, iOS, tablets, laptops, and desktop browsers;
- immediate deployment without app-store review;
- reuse of the existing GDScript client and host API;
- a direct proving path for MMO Project browser deployment concerns.

A Godot Android export is a follow-on packaging target, not a separate product
or a prerequisite for the Web proof.

### Keep server authority unchanged

The mobile or browser client may edit drafts and request previews, but the host
continues to own:

- schema and reference validation;
- preview signatures;
- optimistic concurrency;
- transactional persistence;
- publication and disable guards;
- canonical asset naming and mutation;
- runtime catalog export;
- audit records.

No remote client connects directly to PostgreSQL or receives arbitrary
filesystem access.

## Scheduling rule

This milestone remains after all currently known roadmap work, including:

- interactable world-object authoring;
- gathering-resource and processing-station authoring;
- MMO Project quest foundations;
- Dialogue Studio quest integration;
- Quest Studio;
- contribution bundle export and maintainer publication;
- validated candidate-snapshot hot reload;
- other higher-value authoring work scheduled later.

Future features may be placed after it. "Last" describes its current priority,
not a permanent promise that it must be the final feature ever implemented.

## Conditions that may move it earlier

Reprioritization is justified only when at least one concrete dependency exists:

1. MMO Project approaches its own Web/mobile deployment work and needs a
   lower-risk infrastructure proof immediately beforehand.
2. Remote authoring becomes operationally valuable enough to remove a recurring
   development or content-production bottleneck.
3. Remote collaborators or contribution workflows require authenticated access
   before the rest of the planned authoring roadmap is complete.
4. Another required feature already needs the same remote asset-upload,
   authentication, or hosted-authoring boundary.

General convenience or technical curiosity alone is not enough to displace
higher-value gameplay and authoring work.

## Current architectural guardrails

No remote implementation work is required now. New development should simply
avoid making the eventual transition unnecessarily expensive:

- keep database and publication authority behind the host API;
- do not add direct PostgreSQL access to Godot;
- keep canonical filesystem mutations host-owned;
- avoid adding new API contracts that pass client-local absolute paths when
  uploaded bytes or stable asset identifiers would be more appropriate;
- preserve configurable transport and API-version seams;
- keep catalog, editor, preview, validation, and operation panels separable;
- avoid making required actions depend exclusively on hover, right-click, or
  desktop-only shortcuts;
- preserve preview-before-apply and optimistic-concurrency behavior.

These are normal architecture and usability practices, not an active mobile
initiative.

## Future implementation phases

### RM0 — Architecture and threat-model audit

- Audit the current Godot Web and Android export compatibility.
- Inventory all loopback, absolute-path, local-file, and local-preview
  assumptions.
- Define local-trusted and remote-authenticated host modes.
- Lock authentication, authorization, HTTPS, origin, session, secret, and audit
  policies.
- Identify infrastructure that can later transfer to MMO Project.

### RM1 — Secure remote authoring host

- Preserve loopback-only local mode as the default.
- Add an explicit remote host configuration.
- Require HTTPS and authenticated sessions.
- Authorize read, draft, publish, disable, delete, export, and asset operations.
- Add audit records for publication and destructive mutations.
- Add deployment health, logging, backup, and recovery procedures.

### RM2 — Network-safe asset pipeline

- Replace absolute source-path imports with streamed file uploads.
- Validate file type, dimensions, size, names, and destination policy on the
  host.
- Add authenticated, narrowly scoped asset retrieval endpoints.
- Convert paper-doll, item, mob, NPC, dialogue, and future quest previews away
  from client access to the host filesystem.
- Preserve canonical host-owned asset and export mutations.

### RM3 — Responsive and touch-capable Studio

- Add phone, tablet, and desktop layout classes.
- Convert wide workspaces into navigable full-screen views on phones.
- Add touch-sized controls, mobile keyboard avoidance, back navigation, and
  unsaved-change protection.
- Provide touch-friendly graph connection and node-inspection workflows for
  Dialogue Studio and Quest Studio.
- Preserve dense desktop layouts where they remain more productive.

### RM4 — Web/PWA proof

- Add and validate the Godot Web export.
- Serve the Web client and API through a compatible HTTPS deployment.
- Add installable PWA metadata and update behavior.
- Verify Android Chrome and iOS Safari.
- Exercise representative read, draft, preview, publish, disable, delete,
  upload, reconnect, and concurrency-conflict workflows.

### RM5 — Optional Android-native packaging

- Reuse the responsive Godot client.
- Add Android export, signing, secure credential storage, document picking,
  back-button handling, and distribution procedures.
- Proceed only when native installation or platform integration provides value
  beyond the PWA.

## Proof-of-concept acceptance condition

> From a supported mobile browser, an authenticated maintainer can remotely
> load, create, edit, validate, preview, save, and publish representative item,
> NPC, dialogue, and quest content; upload an asset; recover safely from a
> connection interruption; and receive a deterministic concurrency conflict if
> another client changed the same aggregate.

The proof must also demonstrate that:

- unauthenticated users cannot read or mutate authoring data;
- remote clients cannot access arbitrary host files or database credentials;
- publication and destructive operations are authorized and audited;
- local desktop mode still works without the remote deployment;
- the design documents which pieces are reusable by MMO Project and which are
  Content Studio-specific.

## Non-goals

This milestone does not by itself solve:

- MMO Project player authentication;
- public game-service scale;
- SignalR combat and movement throughput;
- anti-cheat or hostile-client command validation;
- public matchmaking, shard selection, or account recovery;
- offline authoring or automatic multi-author merge resolution;
- a separate Flutter, React Native, Kotlin, or Swift rewrite.

Those concerns may reuse lessons from the proof but require their own game
runtime threat models and acceptance criteria.
