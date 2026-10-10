#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
HOST_URL="${CONTENT_STUDIO_HOST_URL:-http://127.0.0.1:5187}"
HEALTH_URL="${HOST_URL%/}/api/v1/system/health"
HANDSHAKE_URL="${HOST_URL%/}/api/v1/system/handshake"
BUILD_ROOT=""
RUN_CHECKS=1
HOST_PID=""
HOST_LOG="${TMPDIR:-/tmp}/mmo-content-studio-host.$$.log"

usage() {
  cat <<'USAGE'
Usage: ./tools/dev.sh [--skip-check]

Validates the repository, starts or reuses the local authoring host, waits for
its health endpoint and exact build identity, launches Godot Content Studio, and stops only the host
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
  if [[ -n "${BUILD_ROOT}" ]]; then rm -rf -- "${BUILD_ROOT}"; fi
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

for tool in dotnet python3; do
  if ! command -v "${tool}" >/dev/null 2>&1; then
    echo "${tool} is required to verify the authoring host build." >&2
    exit 1
  fi
done

# Build away from active binaries. Normalize only the disposable output path so
# identical builds in this checkout keep the same deterministic module identity.
BUILD_ROOT="$(mktemp -d "${TMPDIR:-/tmp}/mmo-content-studio-build.XXXXXXXX")"
if [[ "${RUN_CHECKS}" -eq 1 ]]; then
  ArtifactsPath="${BUILD_ROOT}" PathMap="${BUILD_ROOT}=/_studio_build" "${ROOT}/tools/check.sh"
fi
dotnet build "${ROOT}/host/MMO.ContentStudio.AuthoringHost.csproj" \
  --configuration Debug --artifacts-path "${BUILD_ROOT}" -p:PathMap="${BUILD_ROOT}=/_studio_build" --nologo
HOST_DLL="${BUILD_ROOT}/bin/MMO.ContentStudio.AuthoringHost/debug/MMO.ContentStudio.AuthoringHost.dll"
EXPECTED_ID="$(dotnet "${HOST_DLL}" --print-build-identity)"

verify_host_identity() {
  local actual_id
  actual_id="$(curl --silent --show-error --fail --max-time 5 "${HANDSHAKE_URL}" | python3 -c '
import json, sys
try:
    print(json.load(sys.stdin)["data"]["build_identity"])
except (ValueError, KeyError, TypeError):
    sys.exit(1)
')" || actual_id=""
  if [[ -z "${actual_id}" || "${actual_id}" != "${EXPECTED_ID}" ]]; then
    echo "The host at ${HOST_URL} has a missing or different build identity." >&2
    echo "Expected ${EXPECTED_ID}; received ${actual_id:-unavailable}." >&2
    echo "Stop/redeploy that host explicitly with the matching build, then retry. No existing host was stopped." >&2
    return 1
  fi
}

if curl --silent --show-error --fail --max-time 5 "${HEALTH_URL}" >/dev/null 2>&1; then
  verify_host_identity
  echo "Reusing authoring host at ${HOST_URL} (matching build)."
else
  echo "Starting authoring host at ${HOST_URL}..."
  # Preserve the development profile while running the exact verified candidate.
  (cd "${ROOT}/host" && exec env ASPNETCORE_ENVIRONMENT=Development \
    ASPNETCORE_URLS=http://127.0.0.1:5187 dotnet "${HOST_DLL}") >"${HOST_LOG}" 2>&1 &
  HOST_PID=$!

  for _ in {1..60}; do
    if curl --silent --show-error --fail --max-time 5 "${HEALTH_URL}" >/dev/null 2>&1; then
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

  if ! curl --silent --show-error --fail --max-time 5 "${HEALTH_URL}" >/dev/null 2>&1; then
    echo "The authoring host did not become ready at ${HEALTH_URL}." >&2
    cat "${HOST_LOG}" >&2
    exit 1
  fi
fi

# Also reject a different process that won the port while our child was starting.
verify_host_identity
"${ROOT}/tools/run-studio.sh"
