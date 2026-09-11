#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
if command -v dotnet >/dev/null 2>&1; then echo " .NET SDK: $(dotnet --version)"; exit 0; fi
if command -v flatpak >/dev/null 2>&1; then
  flatpak install -y flathub org.freedesktop.Sdk//24.08 || true
fi
if command -v rpm-ostree >/dev/null 2>&1; then sudo rpm-ostree install dotnet-sdk-8.0 || true
elif command -v dnf >/dev/null 2>&1; then sudo dnf install -y dotnet-sdk-8.0 || true
elif command -v apt-get >/dev/null 2>&1; then sudo apt-get update && sudo apt-get install -y dotnet-sdk-8.0 || true
elif command -v pacman >/dev/null 2>&1; then sudo pacman -Sy --needed --noconfirm dotnet-sdk || true
fi
if ! command -v dotnet >/dev/null 2>&1; then
  command -v curl >/dev/null 2>&1 || { echo "ERROR: install curl, then rerun install-sdk.sh" >&2; exit 1; }
  mkdir -p "$root/.tools/dotnet"
  curl -fsSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 8.0 --install-dir "$root/.tools/dotnet" --no-path
fi
"$root/.tools/dotnet/dotnet" --version 2>/dev/null || dotnet --version
