#!/usr/bin/env bash
# Package HeroesNotIncluded for alpha testers: self-contained Windows, Linux and macOS (Apple Silicon)
# builds, checked and archived.
#
#   tools/package.sh                 # version from Directory.Build.props
#   tools/package.sh 0.1.0-alpha.2   # or a given version (CI passes the release tag's)
#
# Output in dist/: HeroesNotIncluded-<ver>-win-x64.zip, HeroesNotIncluded-<ver>-linux-x64.tar.gz,
# HeroesNotIncluded-<ver>-osx-arm64.tar.gz (a HeroesNotIncluded.app) and SHA256SUMS.txt. If /srv/heroes-not-included exists (the LAN share; SC_SHARE_DIR picks another, and
# DW_NO_SHARE=1 skips it), the archives and README-ALPHA.txt are copied there too, replacing older builds.
#
# Every platform is built here from one compiler run on purpose: co-op only connects builds with
# the same tag, which hashes the game assemblies and defs, so the script refuses to package
# platforms whose game DLLs or defs differ.
#
# macOS has no Vulkan: the Mac build ships MoltenVK (Vulkan on Metal), downloaded once into
# .cache/ from its pinned GitHub release and checked against MOLTENVK_SHA256. It goes in as
# libvulkan.1.dylib, the name BRUTAL's libVulkanEx.dylib links (Narsil's Platform/MacVulkan.cs).
# The .app is ad-hoc signed when rcodesign is on PATH (CI signs it on a Mac); unsigned, testers
# clear the quarantine flag first (README-ALPHA).
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"

version="${1:-}"
if [[ -z "$version" ]]; then
    version="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' Directory.Build.props | head -1)"
fi
[[ -n "$version" ]] || { echo "error: no version" >&2; exit 1; }
rids=(win-x64 linux-x64 osx-arm64)
dist="$root/dist"
name="HeroesNotIncluded-$version"

MOLTENVK_VERSION=1.4.2
MOLTENVK_SHA256=f95765a6229cb7b915990a2890ce12ebe36a730b021545d3d52ae69ce4c4024e

fail() { echo "error: $*" >&2; exit 1; }

# MoltenVK's macOS dylib and license, downloaded and verified once. Prints the folder holding them.
moltenvk() {
    local cache="$root/.cache/moltenvk-$MOLTENVK_VERSION"
    local tar="$cache/MoltenVK-macos.tar"
    if [[ ! -f "$cache/libMoltenVK.dylib" ]]; then
        mkdir -p "$cache"
        echo "==> download MoltenVK $MOLTENVK_VERSION" >&2
        curl -fsSL -o "$tar.partial" "https://github.com/KhronosGroup/MoltenVK/releases/download/v$MOLTENVK_VERSION/MoltenVK-macos.tar" \
            || fail "couldn't download MoltenVK"
        echo "$MOLTENVK_SHA256  $tar.partial" | sha256sum -c --quiet - >&2 || fail "MoltenVK download doesn't match MOLTENVK_SHA256"
        mv "$tar.partial" "$tar"
        tar -xf "$tar" -C "$cache" --strip-components=5 MoltenVK/MoltenVK/dynamic/dylib/macOS/libMoltenVK.dylib
        tar -xf "$tar" -C "$cache" --strip-components=1 MoltenVK/LICENSE
        rm "$tar"
    fi
    echo "$cache"
}

# The Mac build as HeroesNotIncluded.app: everything in Contents/MacOS (the game finds data/ beside its
# executable), plus an Info.plist so Finder launches it with a proper name and menu bar.
make_app() {
    local out="$1" app="$1/HeroesNotIncluded.app"
    local stage="$out.stage"
    mv "$out" "$stage"
    mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
    mv "$stage"/* "$app/Contents/MacOS/"
    rmdir "$stage"
    cp "$root/packaging/icon.icns" "$app/Contents/Resources/icon.icns"
    cat > "$app/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key><string>Heroes Not Included</string>
    <key>CFBundleDisplayName</key><string>Heroes Not Included</string>
    <key>CFBundleIconFile</key><string>icon</string>
    <key>CFBundleIdentifier</key><string>com.jraynard.heroesnotincluded</string>
    <key>CFBundleExecutable</key><string>HeroesNotIncluded</string>
    <key>CFBundlePackageType</key><string>APPL</string>
    <key>CFBundleVersion</key><string>$version</string>
    <key>CFBundleShortVersionString</key><string>${version%%-*}</string>
    <key>LSMinimumSystemVersion</key><string>13.0</string>
    <key>LSArchitecturePriority</key><array><string>arm64</string></array>
    <key>NSHighResolutionCapable</key><true/>
</dict>
</plist>
PLIST
}

rm -rf "$dist"
mkdir -p "$dist"

for rid in "${rids[@]}"; do
    out="$dist/$name-$rid"
    echo "==> publish $rid"
    # No trimming or AOT (the command and save codecs use reflection), no ReadyToRun (it would make
    # the game DLLs differ per platform, and with them the co-op build tag). Debug info is embedded:
    # a separate pdb's path names the platform's obj folder, which would make the DLLs differ too,
    # and embedded it gives crash reports line numbers. For the same reason every platform compiles
    # in one intermediate folder (no RID in its path), emptied first so nothing stale is reused.
    # PlatformTarget=AnyCPU too: publishing an executable for a RID otherwise stamps that RID's CPU
    # (x64, arm64) into HeroesNotIncluded.dll, so the Mac build's would differ. IL runs on any of them.
    find src external/narsil/src -type d -path '*/obj/package' -prune -exec rm -rf {} +
    dotnet publish src/HeroesNotIncluded -c Release -r "$rid" --self-contained true \
        -p:Version="$version" -p:PublishTrimmed=false -p:PublishSingleFile=false -p:PublishReadyToRun=false \
        -p:DebugType=embedded -p:PlatformTarget=AnyCPU -p:IntermediateOutputPath=obj/package/ -p:AppendRuntimeIdentifierToIntermediateOutputPath=false \
        -o "$out" --nologo -v quiet

    echo "==> check $rid"
    for need in data/defs data/shaders data/fonts data/textures; do
        [[ -d "$out/$need" ]] || fail "$rid: $need is missing"
    done
    # BRUTAL License: the notice, its license and the third-party notices must ship (docs/LICENSING.md).
    for need in NOTICE.txt licenses/BRUTAL-LICENSE.txt licenses/FiraSans-OFL.txt licenses/Cinzel-OFL.txt; do
        [[ -f "$out/$need" ]] || fail "$rid: $need is missing"
    done
    (( $(find "$out/licenses" -name '*.txt' | wc -l) >= 4 )) || fail "$rid: the BRUTAL third-party notices are missing from licenses/"
    case "$rid" in
        win-*) [[ -f "$out/HeroesNotIncluded.exe" && -f "$out/glfw3.dll" && -f "$out/shaderc_shared.dll" ]] || fail "$rid: exe or native DLLs missing" ;;
        linux-*) [[ -x "$out/HeroesNotIncluded" && -f "$out/libglfw.so" && -f "$out/libshaderc_shared.so" ]] || fail "$rid: executable or native libraries missing" ;;
        osx-*)
            [[ -x "$out/HeroesNotIncluded" && -f "$out/libglfw.dylib" && -f "$out/libshaderc_shared.dylib" && -f "$out/libVulkanEx.dylib" ]] \
                || fail "$rid: executable or native libraries missing"
            mvk="$(moltenvk)"
            cp "$mvk/libMoltenVK.dylib" "$out/libvulkan.1.dylib"
            cp "$mvk/LICENSE" "$out/licenses/MoltenVK-LICENSE.txt"
            ;;
    esac

    cp docs/alpha/README-ALPHA.md "$out/README-ALPHA.txt"
    cp docs/alpha/CHANGELOG.md "$out/CHANGELOG.txt"
done

echo "==> compare builds (co-op needs identical game code and defs)"
a="$dist/$name-${rids[0]}"
for rid in "${rids[@]:1}"; do
    b="$dist/$name-$rid"
    for dll in HeroesNotIncluded.dll HeroesNotIncluded.Sim.dll Narsil.dll Narsil.Sim.dll Narsil.Voxel.dll; do
        cmp -s "$a/$dll" "$b/$dll" || fail "$dll differs between ${rids[0]} and $rid: co-op between them would be refused"
    done
    diff -r -q "$a/data/defs" "$b/data/defs" > /dev/null || fail "data/defs differs between ${rids[0]} and $rid"
done

echo "==> smoke test (linux-x64, headless)"
linux="$dist/$name-linux-x64"
"$linux/HeroesNotIncluded" --version | tee "$dist/version.txt"
"$linux/HeroesNotIncluded" --check-art > /dev/null || fail "--check-art failed"

echo "==> macOS app bundle"
mac="$dist/$name-osx-arm64"
readme_mac=("$mac/README-ALPHA.txt" "$mac/CHANGELOG.txt")
mkdir -p "$dist/.mac-docs"
mv "${readme_mac[@]}" "$dist/.mac-docs/"
make_app "$mac"
mv "$dist/.mac-docs"/* "$mac/" # beside the .app, where testers see them
rmdir "$dist/.mac-docs"
if command -v rcodesign > /dev/null; then
    rcodesign sign "$mac/HeroesNotIncluded.app" > /dev/null || fail "rcodesign couldn't sign HeroesNotIncluded.app"
    echo "(ad-hoc signed with rcodesign)"
else
    echo "(rcodesign not found: HeroesNotIncluded.app is unsigned; CI signs it on a Mac)"
fi

echo "==> archive"
find "$dist" -name '*.pdb' -delete # the runtime's own; ours are embedded
if command -v zip > /dev/null; then
    (cd "$dist" && zip -qr "$name-win-x64.zip" "$name-win-x64")
else
    (cd "$dist" && python3 -m zipfile -c "$name-win-x64.zip" "$name-win-x64")
fi
(cd "$dist" && tar -czf "$name-linux-x64.tar.gz" "$name-linux-x64")
(cd "$dist" && tar -czf "$name-osx-arm64.tar.gz" "$name-osx-arm64")
(cd "$dist" && sha256sum "$name"-*.zip "$name"-*.tar.gz > SHA256SUMS.txt)

echo
echo "Packaged $version:"
(cd "$dist" && ls -lh "$name"-*.zip "$name"-*.tar.gz SHA256SUMS.txt)

# Publish to the LAN share (a folder Samba serves, see docs/alpha/README-ALPHA.md), when there is
# one: SC_SHARE_DIR, default /srv/heroes-not-included. Only the newest build is kept there so nobody grabs
# an old one. Files are copied under temporary names and renamed, so a tester copying while this
# runs never gets half a file.
share="${SC_SHARE_DIR:-/srv/heroes-not-included}"
if [[ -n "${DW_NO_SHARE:-}" ]]; then
    echo "(DW_NO_SHARE set: not copying to the share)"
elif [[ ! -d "$share" ]]; then
    echo "(no share folder at $share: not copying; set SC_SHARE_DIR to use another)"
elif [[ ! -w "$share" ]]; then
    fail "can't write to the share folder $share (it should belong to $(id -un))"
else
    echo "==> copy to the share ($share)"
    files=("$name-win-x64.zip" "$name-linux-x64.tar.gz" "$name-osx-arm64.tar.gz" SHA256SUMS.txt)
    for f in "${files[@]}"; do
        cp "$dist/$f" "$share/.$f.partial"
        chmod 644 "$share/.$f.partial"
        mv -f "$share/.$f.partial" "$share/$f"
    done
    cp docs/alpha/README-ALPHA.md "$share/.README-ALPHA.txt.partial" && chmod 644 "$share/.README-ALPHA.txt.partial" \
        && mv -f "$share/.README-ALPHA.txt.partial" "$share/README-ALPHA.txt"
    # Older builds go once the new one is in place.
    find "$share" -maxdepth 1 -name 'HeroesNotIncluded-*' ! -name "$name-*" -delete
    ls -lh "$share"
fi
