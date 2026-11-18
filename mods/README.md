# Mods

Mods that ship with the game. Each folder is one mod: a `mod.json` and any of `defs/`, `textures/`
and `sounds/` (docs/MODDING.md). They're off until a player turns them on (the title screen's Mods,
or `--mods id,id`). Check one with `dotnet run --project src/HeroesNotIncluded -- --check-mod mods/<id>`.

- `long-season`: the example data mod, a single `$patch` on the season.
