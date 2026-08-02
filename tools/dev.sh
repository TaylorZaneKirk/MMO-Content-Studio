#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
HOST_URL="${CONTENT_STUDIO_HOST_URL:-http://127.0.0.1:5187}"
HEALTH_URL="${HOST_URL%/}/api/v1/system/health"
RUN_CHECKS=1
HOST_PID=""
HOST_LOG="${TMPDIR:-/tmp}/mmo-content-studio-host.$$.log"

usage() {
  cat <<'USAGE'
Usage: ./tools/dev.sh [--skip-check]

Validates the repository, starts or reuses the local authoring host, waits for
its health endpoint, launches Godot Content Studio, and stops only the host
process that this script started.

Environment:
  CONTENT_STUDIO_HOST_URL  Override the host URL (default: http://127.0.0.1:5187)
USAGE
}

for argument in "$@"; do
  case "${argument}" in
    --skip-check)
      RUN_CHECKS=0
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      echo "Unknown argument: ${argument}" >&2
      usage >&2
      exit 2
      ;;
  esac
done

cleanup() {
  if [[ -n "${HOST_PID}" ]] && kill -0 "${HOST_PID}" >/dev/null 2>&1; then
    kill "${HOST_PID}" >/dev/null 2>&1 || true
    wait "${HOST_PID}" 2>/dev/null || true
  fi
  rm -f "${HOST_LOG}"
}
trap cleanup EXIT INT TERM

if ! command -v curl >/dev/null 2>&1; then
  echo "curl is required to wait for the authoring host health endpoint." >&2
  exit 1
fi

if [[ ! -f "${ROOT}/host/appsettings.Local.json" ]]; then
  echo "Missing host/appsettings.Local.json." >&2
  echo "Copy host/appsettings.Local.example.json and configure the database and asset roots." >&2
  exit 1
fi

if [[ "${RUN_CHECKS}" -eq 1 ]]; then
  "${ROOT}/tools/check.sh"
fi

if curl --silent --show-error --fail "${HEALTH_URL}" >/dev/null 2>&1; then
  echo "Reusing authoring host at ${HOST_URL}."
else
  if ! command -v dotnet >/dev/null 2>&1; then
    echo "The .NET SDK is required to start the authoring host." >&2
    exit 1
  fi

  echo "Starting authoring host at ${HOST_URL}..."
  "${ROOT}/tools/run-host.sh" >"${HOST_LOG}" 2>&1 &
  HOST_PID=$!

  for _ in {1..60}; do
    if curl --silent --show-error --fail "${HEALTH_URL}" >/dev/null 2>&1; then
      echo "Authoring host is ready."
      break
    fi
    if ! kill -0 "${HOST_PID}" >/dev/null 2>&1; then
      echo "The authoring host exited before becoming ready:" >&2
      cat "${HOST_LOG}" >&2
      exit 1
    fi
    sleep 0.5
  done

  if ! curl --silent --show-error --fail "${HEALTH_URL}" >/dev/null 2>&1; then
    echo "The authoring host did not become ready at ${HEALTH_URL}." >&2
    cat "${HOST_LOG}" >&2
    exit 1
  fi
fi

"${ROOT}/tools/run-studio.sh"
