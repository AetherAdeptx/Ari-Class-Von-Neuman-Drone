#!/usr/bin/env bash
set -euo pipefail

project_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$project_dir"
trap 'echo "ERROR: build prerequisites or compilation failed. Run ./install-sdk.sh and review the output above." >&2' ERR

dotnet_cmd=""
# Prefer a Flatpak-provided SDK when available.
if command -v flatpak >/dev/null 2>&1; then
  for app in com.microsoft.DotNet org.freedesktop.Sdk; do
    if flatpak info "$app" >/dev/null 2>&1 && flatpak run "$app" --version >/dev/null 2>&1; then
      dotnet_cmd="flatpak run $app"
      break
    fi
  done
fi
if [[ -z "$dotnet_cmd" ]] && command -v dotnet >/dev/null 2>&1; then
  dotnet_cmd="$(command -v dotnet)"
fi
if [[ -z "$dotnet_cmd" ]]; then
  # Use an available native package store before downloading the SDK.
  if command -v dnf >/dev/null 2>&1; then
    sudo dnf install -y dotnet-sdk-8.0 || true
  elif command -v apt-get >/dev/null 2>&1; then
    sudo apt-get update && sudo apt-get install -y dotnet-sdk-8.0 || true
  elif command -v pacman >/dev/null 2>&1; then
    sudo pacman -Sy --needed --noconfirm dotnet-sdk || true
  fi
  if command -v dotnet >/dev/null 2>&1; then
    dotnet_cmd="$(command -v dotnet)"
  fi
fi
if [[ -z "$dotnet_cmd" ]]; then
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

eval "$dotnet_cmd restore VonNeuman.csproj"
eval "$dotnet_cmd build VonNeuman.csproj -c Release --no-restore"

echo "Build complete. MDK2 packaging output is in the configured game script directory."
