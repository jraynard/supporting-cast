---
name: new-map
description: Make a new town (a map) for Heroes Not Included, as a mod or in the core. Use when asked for a new town, village, map or layout.
---

# A new town

A town is a `MapDef` (`src/HeroesNotIncluded.Sim/Defs/MapDefs.cs`): ground and levels as row strings, a
legend, features, and buildings placed by def, variant, facing and town role. It's listed on the title
screen when one of its buildings has the Tavern role.

- **Don't hand-write the JSON.** The game's towns are generated: `tools/village_map.py` (Millbrook) and
  `tools/lake_map.py` (Reedmere, on stilts), with shared helpers in `tools/mapkit.py`. Write a script like
  them (for a mod, in `mods/<id>/tools/`, writing `mods/<id>/defs/maps/<name>.json`) and run it with
  `--preview OUT.png` to see the layout. Edit the script, never the generated JSON.
- **A town needs its workplaces**: a building with each role a trade works from (Tavern, Kitchen, Forge,
  Shrine, TinkerShop, Woodshop), homes enough for the villagers (`role: "Home"`), a stockpile and a centre.
  A workplace whose building is a mod's (the carpenter's Woodshop) is simply left empty when that mod is off.
- **Check it**: `--check-mod mods/<id>`, then `--new-game --mods <id> --map <Name>`, screenshots with
  `--look-at X,Y`, and `--season-sweep --map <Name> --seeds 4` to see a whole season played there by bots.
  `ReedmereMapTests` shows what a town's tests check.
- Plan the town with the user first (a sketch, or a reference image like `art/reference/village-map2.webp`):
  a town's layout is played in every round, and like a mini-game it's found over a few passes.
