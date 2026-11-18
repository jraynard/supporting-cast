#!/usr/bin/env bash
# Every ModKit template, made into a mod in this repository, built and checked (docs/MODDING.md), then
# taken away again. Run after changing the mod API or a template: a template that doesn't build or check
# is a modder's first five minutes gone wrong. Templates install into a private hive, not yours.
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
hive="$(mktemp -d)"
trap 'rm -rf "$hive" "$root/mods/kitcontent" "$root/mods/kittrade"' EXIT
cd "$root"
dotnet build src/HeroesNotIncluded -v q -nologo | tail -2
for t in modkit/templates/*/; do dotnet new install "$t" --debug:custom-hive "$hive" > /dev/null; done
dotnet new hni-content -n KitContent -o mods/kitcontent --debug:custom-hive "$hive" > /dev/null
dotnet new hni-trade -n KitTrade -o mods/kittrade --debug:custom-hive "$hive" > /dev/null
dotnet build mods/kittrade/src/KitTrade -v q -nologo -warnaserror | tail -2
for m in kitcontent kittrade; do
  echo "== $m"
  dotnet run --project src/HeroesNotIncluded --no-build -- --check-mod "mods/$m" | grep -v "info:"
done
