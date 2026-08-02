# Development Workflow

Use one command for the normal local Content Studio loop:

```bash
./tools/dev.sh
```

The launcher:

1. Requires `host/appsettings.Local.json` so a missing local configuration fails early.
2. Runs `./tools/check.sh` unless `--skip-check` is supplied.
3. Reuses an already-running authoring host when its health endpoint responds.
4. Otherwise starts the host and waits up to 30 seconds for `/api/v1/system/health`.
5. Launches the Godot Content Studio through the existing engine-resolution script.
6. Stops only the host process it started when Godot exits or the launcher is interrupted.

Set `CONTENT_STUDIO_HOST_URL` when using a non-default loopback port. The host's
`AuthoringHost:ListenUrl` configuration must match that URL.

Use `./tools/dev.sh --skip-check` for a quick restart after the full checks have
already passed. Direct `run-host.sh` and `run-studio.sh` commands remain
available for debugging the two processes independently.
