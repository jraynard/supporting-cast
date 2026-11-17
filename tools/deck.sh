#!/usr/bin/env bash
# Quick test builds on a Steam Deck over ssh: publish linux-x64 and rsync only what changed into
# the Deck's install folder, so its non-Steam shortcut runs the new build. See docs/STEAM-DECK.md.
#
#   tools/deck.sh [push]   # publish (Release) and send it
#   tools/deck.sh push --debug
#   tools/deck.sh logs     # fetch the Deck's logs (rounds, crashes) and screenshots into dist/deck-pulled/
#   tools/deck.sh tail     # follow the newest log live while you play
#   tools/deck.sh ssh      # a shell on the Deck
#
# DECK_HOST (default deck@steamdeck.local) and DECK_DIR (default ~/Games/HeroesNotIncluded) pick
# where. Unlike tools/package.sh this skips the other platforms, the music fetch and the archives,
# and keeps dist/deck between runs so publishes are incremental and rsync sends only the changes.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"

host="${DECK_HOST:-deck@steamdeck.local}"
dir="${DECK_DIR:-~/Games/HeroesNotIncluded}"
config='~/.config/Heroes_Not_Included'
out="$root/dist/deck"

fail() { echo "error: $*" >&2; exit 1; }

push() {
    local configuration=Release
    [[ "${1:-}" == "--debug" ]] && configuration=Debug

    echo "==> publish linux-x64 ($configuration)"
    # As package.sh: no trimming (the command and save codecs use reflection), self-contained
    # because SteamOS has no .NET, debug info embedded so crash logs have line numbers.
    dotnet publish src/HeroesNotIncluded -c "$configuration" -r linux-x64 --self-contained true \
        -p:PublishTrimmed=false -p:PublishSingleFile=false -p:PublishReadyToRun=false \
        -p:DebugType=embedded -o "$out" --nologo -v quiet

    [[ -x "$out/HeroesNotIncluded" && -d "$out/data/defs" ]] || fail "the publish in $out is incomplete"
    local version
    version="$("$out/HeroesNotIncluded" --version 2> /dev/null || echo '?')"

    echo "==> send to $host:$dir"
    ssh "$host" "mkdir -p $dir" || fail "can't reach $host (one-time setup: docs/STEAM-DECK.md)"
    # --delete so removed defs and art don't linger on the Deck.
    rsync -az --delete --info=stats1 "$out/" "$host:$dir/"
    echo "Pushed $version. Launch it from the Steam library."
}

logs() {
    local into="$root/dist/deck-pulled"
    mkdir -p "$into"
    echo "==> fetch logs and screenshots into $into"
    for sub in logs screenshots; do
        rsync -az "$host:$config/$sub" "$into/" 2> /dev/null || echo "(no $sub on the Deck yet)"
    done
    local newest
    newest="$(ls -t "$into"/logs/*.log 2> /dev/null | head -1 || true)"
    if [[ -n "$newest" ]]; then
        echo "==> tail of $(basename "$newest")"
        tail -n 30 "$newest"
    fi
    ls -t "$into"/logs/crash-*.txt 2> /dev/null | head -3 | sed 's/^/crash: /' || true
}

case "${1:-push}" in
    push) shift || true; push "$@" ;;
    logs) logs ;;
    tail) ssh -t "$host" "tail -F \"\$(ls -t $config/logs/*.log | head -1)\"" ;;
    ssh) ssh "$host" ;;
    *) fail "unknown command '$1' (push, logs, tail, ssh)" ;;
esac
