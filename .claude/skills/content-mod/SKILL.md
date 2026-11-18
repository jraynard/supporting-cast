---
name: content-mod
description: Make a data-only mod for Heroes Not Included (new items, buildings, recipes, quests, a changed price or season, a retexture) in mods/<id>, with no code. Use when asked to change or add game content as a mod rather than in the core.
---

# A content mod

A folder in `mods/` with a `mod.json` and any of `defs/`, `textures/` and `sounds/` (docs/MODDING.md,
"Data mods"). No code.

```sh
dotnet new install modkit/templates/hni-content     # once
dotnet new hni-content -n LongNights -o mods/longnights
dotnet run --project src/HeroesNotIncluded -- --check-mod mods/longnights
dotnet run --project src/HeroesNotIncluded -- --new-game --mods longnights
```

- **Defs** merge after the game's, in load order. A new name adds a def; the game's name replaces it (every
  field); `"$patch": "merge"` changes only the fields given (objects merge, arrays are replaced whole);
  `"$patch": "remove"` drops it. Read the game's def in `data/defs/` before patching it, and look in
  `src/HeroesNotIncluded.Sim/Defs/` for what each field means.
- **Art**: a PNG in `textures/` at the same path as the game's replaces its sprite (32x32 items; see docs/ART.md);
  a new path is a new sprite a def can name.
- **Sounds**: `sounds/sounds.json` (the format of `data/sounds/sounds.json`), files beside it; the same id replaces the game's.
- `--check-mod` lists what the mod adds, changes and removes, and fails on a broken reference, a missing
  sprite or a missing sound file. Run it after every change.
- Content that should be in the core for everyone goes in `data/defs` instead; a mod is for what a player
  may turn off. Example: `mods/long-season` (one patch).
