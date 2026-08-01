# T0 Acceptance

## Implemented

- Godot 4 application shell
- Loopback-only .NET 8 authoring-host project
- Shared API v1 response and error envelope
- Version handshake
- PostgreSQL connection-profile and schema-health reporting
- Asset-root health reporting
- Versioned empty catalog seams for items, mobs, and NPCs
- Godot connection, retry, health, and catalog presentation
- Transactional authoring-operation conventions for T1+
- Source and runtime contract-test harnesses
- Local run scripts

## Runtime acceptance procedure

On a development machine with .NET 8 and Godot 4 installed:

1. Copy `host/appsettings.Local.example.json` to
   `host/appsettings.Local.json`.
2. Replace the example PostgreSQL connection string and asset roots.
3. Run `./tools/test.sh`.
4. Run `./tools/run-host.sh`.
5. In a second terminal, run `./tools/run-studio.sh`.
6. Confirm the Studio displays:
   - connected host and API v1
   - PostgreSQL/schema health
   - configured asset-root health
   - empty Items, Mobs, and NPCs catalog sections
7. Stop the host and confirm the Studio retry state is understandable.

## Environment limitation for the initial commit

The repository was authored in an environment without the .NET SDK or Godot 4.
The Python source-contract suite passed there. The included runtime suite will
start and exercise the host automatically when .NET is available, and the
included Godot headless fixture will execute when Godot 4 is available.

## T0 closure condition

T0 can be marked fully runtime-verified after the acceptance procedure succeeds
against the MMO Project development database and asset directories. No T1 item
mutation should begin until the host reports the expected item table and
`runtime_enabled` publication column.
