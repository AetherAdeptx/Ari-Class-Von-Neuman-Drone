#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
target=""
if [[ -f "$root/mdk.local.ini" ]]; then target="$(sed -n 's/^output=//p' "$root/mdk.local.ini" | head -n1)"; fi
if [[ -z "$target" ]]; then
  for p in "$HOME/.steam/steam/steamapps/compatdata/244850/pfx/drive_c/users/steamuser/AppData/Roaming/SpaceEngineers/IngameScripts/local" "$HOME/.local/share/Steam/steamapps/compatdata/244850/pfx/drive_c/users/steamuser/AppData/Roaming/SpaceEngineers/IngameScripts/local"; do [[ -d "$p" ]] && target="$p" && break; done
fi
[[ -n "$target" ]] || { echo "ERROR: Space Engineers IngameScripts/local directory not found." >&2; exit 1; }
src="$root/build scripts/VonNeuman.cs"
[[ -f "$src" ]] || src="$(find "$root" -type f -path '*/VonNeuman/script.cs' | head -n1)"
[[ -n "$src" ]] || { echo "ERROR: packed VonNeuman script not found; run ./build.sh first." >&2; exit 1; }
mkdir -p "$target"; cp "$src" "$target/VonNeuman.cs"; echo "Installed VonNeuman.cs to $target"
