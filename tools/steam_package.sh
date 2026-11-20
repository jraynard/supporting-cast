#!/usr/bin/env bash
# Steam depots from tools/package.sh's three packages: each unpacked into its own folder, a steam.json
# beside the game (src/HeroesNotIncluded/Platforms/SteamConfig.cs) saying which app it is, and a SteamPipe
# app build script for steamcmd. The game's files are the same bytes as the itch build's: only steam.json
# makes a copy Steam's, so itch and Steam players of one version can still play together.
#
#   tools/steam_package.sh game  DIST OUT      # the full game
#   tools/steam_package.sh demo  DIST OUT      # the Next Fest demo (docs/NEXT-FEST.md)
#
# DIST holds HeroesNotIncluded-<ver>-{win-x64.zip,linux-x64.tar.gz,osx-arm64.tar.gz} (the Mac one signed).
# OUT gets content/{windows,linux,mac}/ and app_build.vdf. Then:
#
#   steamcmd +login "$STEAM_USERNAME" +run_app_build "$OUT/app_build.vdf" +quit
#
# From the environment:
#   STEAM_APP_ID (game) or STEAM_DEMO_APP_ID (demo)   the app being uploaded
#   STEAM_APP_ID                                       also the store page the demo's Wishlist opens
#   STEAM_DEPOT_WINDOWS, _LINUX, _MAC (or STEAM_DEMO_DEPOT_*)   default to the app ID + 1, 2, 3
#   STEAM_BRANCH       the branch set live, default "testing"; never "default" (going live is by hand)
#   VERSION            for the build's description, default from the archives' names
#
# The demo's own content (mods/demo, docs/NEXT-FEST.md) isn't built yet: for now a demo depot is the game's.
set -euo pipefail

kind="${1:-}"; dist="${2:-}"; out="${3:-}"
fail() { echo "error: $*" >&2; exit 1; }
[[ "$kind" == game || "$kind" == demo ]] || fail "usage: tools/steam_package.sh game|demo DIST OUT"
[[ -d "$dist" && -n "$out" ]] || fail "usage: tools/steam_package.sh game|demo DIST OUT"

store="${STEAM_APP_ID:-}"
[[ -n "$store" ]] || fail "set STEAM_APP_ID"
if [[ "$kind" == demo ]]; then
    app="${STEAM_DEMO_APP_ID:-}"
    [[ -n "$app" ]] || fail "set STEAM_DEMO_APP_ID"
    win="${STEAM_DEMO_DEPOT_WINDOWS:-$((app + 1))}"; linux="${STEAM_DEMO_DEPOT_LINUX:-$((app + 2))}"; mac="${STEAM_DEMO_DEPOT_MAC:-$((app + 3))}"
else
    app="$store"
    win="${STEAM_DEPOT_WINDOWS:-$((app + 1))}"; linux="${STEAM_DEPOT_LINUX:-$((app + 2))}"; mac="${STEAM_DEPOT_MAC:-$((app + 3))}"
fi
branch="${STEAM_BRANCH:-testing}"
[[ "$branch" != default ]] || fail "uploads never set the default branch live: do that in Steamworks"

shopt -s nullglob
zips=("$dist"/*-win-x64.zip); linuxes=("$dist"/*-linux-x64.tar.gz); macs=("$dist"/*-osx-arm64.tar.gz)
[[ ${#zips[@]} -eq 1 && ${#linuxes[@]} -eq 1 && ${#macs[@]} -eq 1 ]] || fail "$dist needs exactly one package per platform"
version="${VERSION:-$(basename "${linuxes[0]}" | sed -n 's/^HeroesNotIncluded-\(.*\)-linux-x64\.tar\.gz$/\1/p')}"

rm -rf "$out"
mkdir -p "$out/content/windows" "$out/content/linux" "$out/content/mac" "$out/output"
out="$(cd "$out" && pwd)"
unzip -q "${zips[0]}" -d "$out/content/windows"
tar -xzf "${linuxes[0]}" -C "$out/content/linux"
tar -xzf "${macs[0]}" -C "$out/content/mac"

# Each archive holds one folder (HeroesNotIncluded-<ver>-<rid>); the depot is its contents.
flatten() {
    local dir="$1" inner
    inner="$(find "$dir" -mindepth 1 -maxdepth 1 -type d | head -1)"
    [[ -n "$inner" ]] || fail "nothing in $dir"
    (shopt -s dotglob; mv "$inner"/* "$dir/") && rmdir "$inner"
}
for p in windows linux mac; do flatten "$out/content/$p"; done
[[ -d "$out/content/mac/HeroesNotIncluded.app" ]] || fail "the Mac package has no HeroesNotIncluded.app"
for lib in windows/steam_api64.dll linux/libsteam_api.so mac/HeroesNotIncluded.app/Contents/MacOS/libsteam_api.dylib; do
    [[ -f "$out/content/$lib" ]] || fail "$lib missing: is external/steamworks/ in the build?"
done

# Which app this copy is. On the Mac it sits beside the .app, not in it: the bundle is signed.
for p in windows linux mac; do
    printf '{ "appId": %s, "storeAppId": %s }\n' "$app" "$store" > "$out/content/$p/steam.json"
done

cat > "$out/app_build.vdf" << EOF
"AppBuild"
{
	"AppID" "$app"
	"Desc" "Heroes Not Included $version ($kind)"
	"ContentRoot" "$out/content/"
	"BuildOutput" "$out/output/"
	"SetLive" "$branch"
	"Depots"
	{
		"$win"
		{
			"FileMapping" { "LocalPath" "windows/*" "DepotPath" "." "recursive" "1" }
		}
		"$linux"
		{
			"FileMapping" { "LocalPath" "linux/*" "DepotPath" "." "recursive" "1" }
		}
		"$mac"
		{
			"FileMapping" { "LocalPath" "mac/*" "DepotPath" "." "recursive" "1" }
		}
	}
}
EOF
echo "==> $kind $version for app $app (depots $win, $linux, $mac; branch $branch): $out/app_build.vdf"
