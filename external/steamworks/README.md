# Steamworks redistributables

Valve's Steam API libraries, which the game loads when it's started through Steam
(`src/HeroesNotIncluded/Platform/`). They are the SDK's `redistributable_bin` files and nothing
else. The SDK's headers and tools stay out of git: Valve's agreement lets a game ship these
libraries, but not publish the SDK. The repo is private; if that ever changes, move them out
(fetched in CI like MoltenVK in `tools/package.sh`).

| File | From the SDK |
|---|---|
| `win64/steam_api64.dll` | `redistributable_bin/win64/steam_api64.dll` |
| `linux64/libsteam_api.so` | `redistributable_bin/linux64/libsteam_api.so` |
| `osx/libsteam_api.dylib` | `redistributable_bin/osx/libsteam_api.dylib` (x86_64 and arm64) |

**SDK version: 1.65** (23 July 2026).

## Updating

1. Download the new SDK from partner.steamgames.com and copy the three files over these.
2. In `public/steam/steam_api_flat.h`, check the interface accessors the game binds
   (`SteamAPI_SteamFriends_v018`, `SteamAPI_SteamUtils_v011`) against `Platform/SteamNative.cs`,
   and update the names there if the numbers moved.
3. Update the version above, then run with `--steam 480` (PUBLISHING.md, "Steam") to check init,
   the overlay and rich presence.
