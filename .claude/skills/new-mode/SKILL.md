---
name: new-mode
description: Make a new game mode (a way to play a round, like Siege Night) for Heroes Not Included as a mod. Use when asked for a new mode, scenario, challenge or way to play.
---

# A new game mode

A mode is how a round is played (docs/MODDING.md, "A new game mode"): an `IGameMode`
(`src/HeroesNotIncluded.Sim/Modding/GameMode.cs`) registered with `registry.Mode(...)` in a mod's
`ISimMod`. The round keeps its mode by name (saved); the title screen lists a mod's modes under
"Other ways to play"; `--mode <name>` starts one. Worked example: `mods/siege` (Siege Night).

## What a mode says (each has a default: say only how it differs from a season)

- `Name` (its id, saved: don't rename it), `Label`, `Description` (the title screen's words, the brand's voice).
- `Seasonal`: the season's clock runs it (the festival, the reckoning, Dread ending it). Off, the mode ends
  the round itself.
- `Crises`: the Director sends the season's crises. Off, it sends none; the mode may send its own
  (`sim.Director.SendRaid`, `sim.Director.Send`), and they play out as usual.
- `StartHour`: the clock moved on to it at the deal (a siege at dusk).
- `Started(sim)`: the trades are dealt; set out what the mode needs.
- `Check(sim)`: how it ends now (`new RoundEnd(outcome, headline)`), or null. Nobody left alive ends it anyway.
- `Status(sim)`: its line under the clock ("Siege Night: dawn in 6 hours").

A mode object is shared: keep a round's state in a system of the mod's own (`registry.System`,
`ISaveState`), as `SiegeSystem` keeps its waves; the mode reads it. Everything the mode does goes through the
sim, deterministically (CLAUDE.md, Determinism), so co-op and replays hold.

## Steps

1. A mod with a sim half (copy `mods/siege`, or `dotnet new hni-trade` and cut the trade out): `mod.json`,
   `src/<Name>.Sim` with the `ISimMod`, the mode and any system.
2. Play it with the user early: `--new-game --mode <name> --mods <id> --trade Blacksmith`, screenshots with
   `--screenshot shot.png --screenshot-at 6 --quit`. A mode, like a mini-game, is tuned by playing it: how
   hard, how long, what makes it tense. Expect passes.
3. A test (`tests/HeroesNotIncluded.Sim.Tests/GameModeTests.cs` shows how, in the `SimContentTests`
   collection): the round starts in the mode, runs to its end, ends as it should, and saves and loads.
4. `--check-mod mods/<id>` lists its ways to play.
