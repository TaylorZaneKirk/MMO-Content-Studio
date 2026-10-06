# Disposable Studio sandbox — proposal only

Prepared 2026-10-06. No database, role, credential, authentication rule, configuration
or listener has been created. These are exact proposed targets for the next approval,
not defaults for other machines or standing permission in the delivery skill.

## Finding and recommended boundary

Read-only inspection found the existing configured database connection authenticates
as `postgres` with superuser/CREATEDB/CREATEROLE. Unix-socket login as OS user `taylor`
fails because that database role does not exist; peer login as `postgres` fails for
`taylor`. No role or database named like a sandbox was found. The global HBA/ident
files were not readable; their full rules were not assumed. Reusing the live superuser
would defeat the desired protection, and creating a role in that cluster can inherit
PUBLIC privileges on other databases. Do not change the live cluster to solve this.

Recommend a separate disposable PostgreSQL 16 cluster owned by OS user `taylor`,
using the already-installed initdb/pg_ctl binaries. This is one more local process,
but it avoids live-cluster administration and a new database password. PostgreSQL peer
authentication identifies the local OS user and supports explicit user mapping:
[official peer documentation](https://www.postgresql.org/docs/16/auth-peer.html).
PUBLIC grants are additive, not explicit-deny isolation:
[official GRANT documentation](https://www.postgresql.org/docs/16/sql-grant.html).

## Exact proposed resources

- Sandbox root: `/home/taylor/.local/share/mmoproject/studio-sandbox-v1` (currently
  absent), mode 0700, owned by Taylor. No symlinks into live trees.
- Cluster data: `<root>/postgres/data`; socket directory `<root>/postgres/socket`,
  both private. PostgreSQL port number 55432 for the Unix socket; `listen_addresses`
  empty, so no PostgreSQL TCP listener. Recheck socket/port collisions before launch.
- Database: `mmo_studio_sandbox` in this new cluster only.
- App role: `mmo_studio_sandbox`, LOGIN, NOSUPERUSER, NOCREATEDB, NOCREATEROLE,
  NOREPLICATION, NOBYPASSRLS, NOINHERIT, no role memberships and no password. Grant
  CONNECT to this DB, USAGE on its public schema, SELECT/INSERT/UPDATE/DELETE on its
  disposable public tables, USAGE/SELECT/UPDATE on their sequences, and EXECUTE only
  on reviewed required routines. No ownership, DDL, TRUNCATE or grant option. Revoke
  PUBLIC database/schema/routine privileges inside the sandbox before app grants.
- Bootstrap role: initdb's `taylor`, superuser **in the new disposable cluster only**,
  used for reviewed schema creation/migrations and fixture grants. Never use it as
  the Studio/MapPublisher role. No role is added to the existing cluster.
- Sandbox HBA/ident only: local databases / `taylor` role use peer for sandbox-cluster
  administration; local `mmo_studio_sandbox` DB / app role uses peer with the exact
  map `studio_sandbox taylor mmo_studio_sandbox`; reject other local/host connections.
  No trust rule, OS user, sudo policy,
  global pg_hba/pg_ident edit, firewall change or TCP database exposure.
- Studio: separate foreground process at `http://127.0.0.1:5189/studio/` (port 5189
  was unused during inspection; recheck). Host publish output, content root and
  private `appsettings.Local.json` live in `<root>/host`; logs in `<root>/logs`.
  Build from a clean tracked-source copy under `<root>/build/studio`, including
  the host and its linked `integrations/mmo-project/BlacksmithingRecipe.cs`. Exclude
  ignored/local settings and inspect publish output before launch; publishing the
  live working directory directly could copy its private appsettings.Local.json.
  No systemd unit, LAN URL, TLS certificate, allowed subnet or trusted-LAN opt-in.
  Existing LAN Studio and game processes remain untouched.

The launcher must use an explicit environment/config allowlist, not inherit live
connection or Browser settings. Select profile `sandbox`; its password-free Npgsql
connection names the private socket, port 55432, database and app role above.
Never source the live local.env into this process. MapPublisher receives the same
sandbox-only connection from the host; no live credentials enter subprocess args.

## User-entered browser credential

A database password is unnecessary with the private peer-mapped cluster. A **new,
sandbox-only browser login password** is necessary to enable the existing browser
write boundary on loopback without changing production code or enabling LAN trust.
After approval, provide a local foreground .NET helper using the existing ASP.NET
Core Identity PasswordHasher implementation. Taylor enters and confirms a password
through masked `Console.ReadKey(intercept: true)` input. The helper writes only the
hash to `<root>/host/appsettings.Local.json` (0600); plaintext is not echoed, logged,
returned through Codex, included in argv/environment, or saved. Taylor enters that
password in the ordinary Studio login form. Do not ask for it in chat. Losing it
means resetting only this sandbox hash after approval. No credential is set now.

## Asset/export copies and fixtures

`game_client_assets` points to `<root>/prototype/client/assets`;
`content_studio_workspace` points to `<root>/content-workspace`. Copy selected
nonprivate game artwork plus permitted/synthetic PNG/WAV/OGG/MP3 fixtures. Place
rigs, shared calibrations and equipped-visual catalogs under
`<root>/prototype/client/actors/appearance/data/{rigs,rig_calibrations,equipped_visuals}`.
Copy the current MapPublisher project into `<root>/prototype/tools/MapPublisher`
and its direct server project/source dependency into `<root>/prototype/server`,
including required build inputs. The server project is a compile dependency only;
do not start it.
Do not copy local settings, secrets, player data or live binary output wholesale.

The copied prototype must contain `shared` and `client/assets` because the exporter
finds its ancestor root using those directories. Its outputs stay under
`<root>/prototype/shared/{blacksmithing,shops,quests,dialogues}`,
`<root>/prototype/shared/maps/{world_objects,npcs,mobs}` and the copied equipped-visual
catalog directory. Verify every resolved read/write/export path is inside `<root>`
and every DB target is the private socket + sandbox DB before the first write.
No changes to the source checkout's live assets or packaged catalogs.

Build schema using reviewed repository DDL/migrations in the new cluster, not a data
dump. Inspect seed side effects and install only required nonprivate reference data.
Synthetic `sandbox_` definitions are created in dependency order: Items and factions;
World Objects; Shops/Loot Tables/recipes; Quests/Dialogue; NPCs/Mobs; Spells. Include
Draft/Published/Disabled states, ordered children, nested/no-drop loot, flat/composite
actors, calibration overlays, elemental effects and known-format audio. Empty player/
account/possession tables may exist if schema dependencies require them, but never
copy real rows. Invalid/missing references are preview-only rejection fixtures.

Manually exercise real lifecycle, uploads, exact integer round trips, dirty navigation,
two-session versions/signatures/calibration hashes and explicit reload/compare after
sandbox-only uncertain writes. Inspect actual DB/export/file outcomes separately.
No automated tests. No physical-phone/LAN access in this proposal. Teardown is limited
to these two sandbox processes and this exact root after checking their identities;
never stop a process merely because it occupies the proposed port.

## Proposed approval wording

“Approve provisioning only `/home/taylor/.local/share/mmoproject/studio-sandbox-v1`:
a private PostgreSQL 16 cluster using Unix socket port 55432 with no TCP listener,
its sandbox-only bootstrap `taylor` role, `mmo_studio_sandbox` DB and restricted
`mmo_studio_sandbox` app role with the described peer mapping; reviewed schema and
synthetic fixture writes; copied assets/MapPublisher outputs; and a foreground Studio
at `127.0.0.1:5189` with a locally entered sandbox browser password stored only as a
0600 hash. Approve manual authoring/recovery QA and cleanup only inside these named
resources. Do not modify the existing PostgreSQL cluster, live content, current LAN
Studio, game, global authentication, certificates, firewall or network exposure.”

Any required deviation, inaccessible dependency or request for privileged live
credentials returns for a concrete decision. This proposal itself approves nothing.
