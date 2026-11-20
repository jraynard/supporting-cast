# Mods

Mods that ship with the game (docs/MODDING.md). Each folder is one mod: a `mod.json` and any of `defs/`,
`textures/`, `sounds/` and assemblies built from `src/`. A mod marked `"defaultOn": true` is on unless a
player turns it off; the rest are off until turned on (the title screen's Mods, or `--mods id,id`; `-id`
turns one off). Check one with `dotnet run --project src/HeroesNotIncluded -- --check-mod mods/<id>`.

- `carpenter` (on by default): the carpenter, moved out of the core: a workplace, AI jobs, systems, a bot.
- `musician`: the first trade built as a mod from nothing: a player's trade, an instrument, a tune book.
- `miner`: a player's trade at the mine in Millbrook's hill: rock faces hewn by their cracks, iron and coal
  for the smith (the carter brings less while a player's the miner). A first pass, to be played and changed.
- `candlemaker`: a trade a fresh coding agent built from the skills alone, in one pass (the skills' test):
  candles dipped layer by layer, lumpy if dipped before the wax has set. A first pass, to be fleshed out.
- `siege`: Siege Night, a game mode: dusk, three waves of raiders, held till dawn.
- `long-season`: the example data mod, a single `$patch` on the season.
- `demo`: the Next Fest demo's content (docs/NEXT-FEST.md): a two-day season, Millbrook alone. On only in a
  demo copy (`BuildInfo.IsDemo`), which plays it and the carpenter and no other mod; `--mods demo` tries its season.

A new mod adds itself here. Templates for new ones: `modkit/`.
