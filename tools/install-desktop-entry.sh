#!/usr/bin/env bash
# Install a Linux desktop entry for the game, for the current user: the app launcher entry, and the
# taskbar icon on Wayland (where a window can't set its own icon; the compositor matches the
# window's app_id, "heroes-not-included", to heroes-not-included.desktop and takes the icon from there).
#
#   tools/install-desktop-entry.sh                    # launches from this checkout (dotnet run)
#   tools/install-desktop-entry.sh /opt/ev/HeroesNotIncluded # launches a packaged build
#   tools/install-desktop-entry.sh --uninstall
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
id="heroes-not-included"
data="${XDG_DATA_HOME:-$HOME/.local/share}"
entry="$data/applications/$id.desktop"

if [[ "${1:-}" == "--uninstall" ]]; then
    rm -f "$entry"
    for size in 32 48 64 128 256; do rm -f "$data/icons/hicolor/${size}x${size}/apps/$id.png"; done
    echo "Removed $entry and its icons."
    exit 0
fi

if [[ $# -ge 1 ]]; then
    exec_line="\"$(realpath "$1")\""
    workdir="$(dirname "$(realpath "$1")")"
else
    exec_line="dotnet run --project \"$root/src/HeroesNotIncluded\""
    workdir="$root"
fi

for size in 32 48 64 128 256; do
    install -Dm644 "$root/packaging/icon-$size.png" "$data/icons/hicolor/${size}x${size}/apps/$id.png"
done
mkdir -p "$(dirname "$entry")"
cat > "$entry" <<EOF
[Desktop Entry]
Type=Application
Name=Heroes Not Included
Comment=Run the village a party of heroes walks past
Exec=$exec_line
Path=$workdir
Icon=$id
Terminal=false
Categories=Game;Simulation;
StartupWMClass=$id
EOF

command -v update-desktop-database > /dev/null && update-desktop-database "$data/applications" 2> /dev/null || true
command -v gtk-update-icon-cache > /dev/null && gtk-update-icon-cache -q "$data/icons/hicolor" 2> /dev/null || true
echo "Installed $entry (runs: $exec_line)."
