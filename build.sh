#!/usr/bin/env bash
set -euo pipefail

project_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$project_dir"

if ! command -v dotnet >/dev/null 2>&1; then
  echo "dotnet SDK is required. Install the .NET SDK, then rerun this script." >&2
  exit 1
fi

dotnet restore VonNeuman.csproj
dotnet build VonNeuman.csproj -c Release --no-restore

echo "Build complete. MDK2 packaging output is in the configured game script directory."
