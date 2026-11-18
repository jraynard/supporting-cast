---
name: new-trade
description: Build a new trade (a class players take, like the carpenter or the musician) for Heroes Not Included as a mod in mods/<id>, through the mod API only. Use when asked to add a trade, a profession, a class or a job players can be dealt.
---

# A new trade, as a mod

A trade is a mod (docs/MODDING.md, "A new trade"): defs for what it is, a sim half for what it does,
a game half for how it looks and sounds. The core game never names a trade, and yours mustn't need it
to: if you need a door the API hasn't got, add the door to the API (a hook in
`src/HeroesNotIncluded.Sim/Modding/TradeModule.cs` or `src/HeroesNotIncluded/Mods/GameRegistry.cs`),
never a special case in the core. `ModBoundaryTests` enforce this for the game's own trades.

## Before you start: the mini-game is the work

A trade's heart is its hand mini-games, and they are **found, not written**. Every trade here took many
rounds of play and change before its games were right (the forge's heat, the stew's seasoning, the
bow's tiller, the pipe's lead-in). So:
- Agree with the user what the trade is *for* in the town, and what its hands do, in a few lines.
- Get a **rough game playable fast** (the template gives you one in minutes), then stop and hand it to
  the user to play. Ask what felt wrong. Change one thing at a time. Expect many passes.
- Never call a mini-game finished after the first pass. The plumbing below is the quick part.

## The steps

1. **Make the mod from the template** (it builds and checks as it stands):
   ```sh
   dotnet new install modkit/templates/hni-trade        # once
   dotnet new hni-trade -n Glassblower -o mods/glassblower
   dotnet build mods/glassblower/src/Glassblower         # both halves (the sim half first)
   dotnet run --project src/HeroesNotIncluded -- --check-mod mods/glassblower
   dotnet run --project src/HeroesNotIncluded -- --new-game --trade Glassblower --mods glassblower --drill glassblower
   ```
   Add both projects to `HeroesNotIncluded.sln` (`dotnet sln add --solution-folder mods ...`).
2. **Defs** (`mods/<id>/defs/<id>.json`): the trade (`trades`), its tools and wares (`things`), and kinds of
   your own if you need them (any top-level key the game doesn't know; read with
   `sim.Defs.ModDefs<T>("kind")`; the musician's `instruments` and `songs`). Read the game's
   `data/defs/trades.json` for every field a trade can have. `"playerOnly": true` for a trade no AI takes.
3. **The sim half** (`src/<Name>.Sim`, references only `HeroesNotIncluded.Sim`): an `ISimMod` that registers
   - `registry.Trade(new <Name>Module())`: an `ITradeModule`, answering only the hooks that are yours (each
     defaults to "not mine"): work at a station, mending, what F does (`EarlyActionAt`, `SiteActionAt`,
     then `Perform`), the AI's jobs (`JobFor`), stocking its workplace, practice customers;
   - its work sessions (subclasses of `WorkSession`: found by scanning, nothing to register);
   - `registry.Component<T>(...)` for every component it adds (an unsaved one vanishes on load);
   - `registry.System(...)` for a system, placed `after:`/`before:` a named one;
   - `registry.Lesson(...)`, `registry.Bot(...)`, `registry.Drill(...)`.
4. **The game half** (`src/<Name>`): an `IGameMod` that registers each session's `WorkView`, `WorkSound`
   and, if it reads keys of its own, `WorkInput` and `Control`s; and anything else it draws
   (`StationTab`, `WagonPanel`, `AimLine`, `Tilt`, `WorldSounds`). Draw through `WorkView`
   (`view.Begin`/`End`, `view.TakesPointer`, `view.Perceived`), colours from `HeroesNotIncluded.Ui.Theme`.
5. **Art and sounds** go in the mod's own `textures/` and `sounds/sounds.json`; make them with a tool in the
   mod's `tools/` that writes into the mod (see `mods/musician/tools/musician_assets.py`).
6. **Tests** in `tests/HeroesNotIncluded.Sim.Tests/<Name>ModTests.cs`, in the `SimContentTests` collection
   (the content is process-wide), turning the mod on as `MusicianModTests.WithMusician` does: the deal, the
   session's inputs and outcome, save and load, the predictor's copy.

## The rules a trade keeps (CLAUDE.md is the law; these bite mods most)

- **Determinism.** Randomness only through `sim.Rand` or the session's `Rand`; trig through `DetMath`; no
  clocks; no unordered iteration that changes state. The sim half builds against `BannedSymbols.txt`.
- **A session's `Advance` is pure**: `Step` and the input handlers (`Press`, `Axis`, `Custom`...) read only
  the session. Read the world in `StillValid`, change it in `Finish`. Copy any list or array in `Cloned()`,
  or the predictor's copy writes into the sim's.
- **Secrets**: views and bots read hidden state only through `PerceptionView` (`view.Perceived`), never the
  sim directly.
- **Inputs** reach the sim only as commands or session inputs; a view never changes the world.
- Words follow the brand's voice (art/brand/brand-guide.html); no trademarked tabletop terms.

## The check loop, every change

```sh
dotnet build HeroesNotIncluded.sln                  # warning-free
dotnet run --project src/HeroesNotIncluded -- --check-mod mods/<id>
dotnet test --filter <Name>ModTests
dotnet run --project src/HeroesNotIncluded -- --new-game --trade <Name> --mods <id> --drill <id> \
    --screenshot shot.png --screenshot-at 4 --quit   # look at it; then hand it to the user to play
```
A pure refactor of the core must leave `--bench --seed 1234` and `--seed 99` hashes unchanged.

## Worked examples

- `mods/musician`: built from nothing. A player-only trade, an instrument played anywhere (`EarlyActionAt`),
  its own def kinds, inputs and controls, a lead-in that plays a tune on, sounds heard by others in earshot.
- `mods/carpenter`: moved out of the core. A workplace the AI runs too: station work, AI jobs (`JobFor`),
  systems, components (with `formerly` names), lessons, a bot (`CarpenterBot`), felling (`SiteActionAt`, `Tilt`).
- `docs/MODDING.md`: every extension point, and what each trade taught the API.
