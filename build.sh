#!/usr/bin/env bash
set -euo pipefail

project_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$project_dir"

if command -v dotnet >/dev/null 2>&1; then
  dotnet_cmd="$(command -v dotnet)"
else
  dotnet_root="$project_dir/.tools/dotnet"
  dotnet_cmd="$dotnet_root/dotnet"
  if [[ ! -x "$dotnet_cmd" ]]; then
    command -v curl >/dev/null 2>&1 || { echo "curl is required to install the .NET SDK." >&2; exit 1; }
    mkdir -p "$dotnet_root"
    echo ".NET SDK not found; installing a local copy in .tools/dotnet..."
    curl -fsSL https://dot.net/v1/dotnet-install.sh | bash -s -- \
      --channel 8.0 --install-dir "$dotnet_root" --no-path
  fi
fi

"$dotnet_cmd" restore VonNeuman.csproj
"$dotnet_cmd" build VonNeuman.csproj -c Release --no-restore

echo "Build complete. MDK2 packaging output is in the configured game script directory."
