# Local Authoring API v1

The Godot Content Studio communicates with the local .NET Authoring Host over
loopback HTTP. The T0 default endpoint is:

```text
http://127.0.0.1:5187
```

The host is intentionally bound to loopback. It is not a remote administration
API and must not be exposed publicly.

## Shared envelope

Every JSON response uses one envelope:

```json
{
  "api_version": "1",
  "request_id": "caller-or-server-generated-id",
  "success": true,
  "data": {},
  "errors": []
}
```

Errors include a stable code, user-facing message, severity, optional field, and
optional remediation. Validation severities are `Info`, `Warning`, and `Error`.

## `GET /api/v1/system/handshake`

Confirms that the GUI and host support the same local API version before any
content operation is attempted.

The response includes:

- service name
- host assembly version
- current API version
- supported API versions
- server UTC time

## `GET /api/v1/system/health`

Reports:

- overall health
- active connection profile
- PostgreSQL connectivity
- required `item_definitions` table presence
- required `item_definitions.runtime_enabled` column presence
- configured asset-root existence

An absent connection string is reported as `Unconfigured`, not as an unhandled
failure. This allows the GUI shell to start and explain configuration work.

## `GET /api/v1/catalog`

Returns the versioned content-catalog shape. T0 returns empty `items`, `mobs`,
and `npcs` sections so future workspaces can extend the response without
changing the shell-to-host boundary.

## Request correlation

The GUI sends `X-Request-Id`. The host preserves a non-empty supplied value or
generates one when absent. The value is returned in the response envelope.

## Compatibility policy

Breaking request or response changes require a new API route prefix. Additive
fields may be introduced within v1 when existing clients can safely ignore them.
