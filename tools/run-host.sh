#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "${ROOT}/host"
exec dotnet run --project MMO.ContentStudio.AuthoringHost.csproj
