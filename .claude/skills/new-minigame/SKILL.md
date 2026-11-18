---
name: new-minigame
description: Add or rework a hand mini-game (a WorkSession and its view) for a Heroes Not Included trade, the game's or a mod's. Use when asked for a new mini-game, a new way to work at a station, or to change how one feels.
---

# A mini-game

## First: it will take many passes, with the user

A mini-game is found by playing it. The forge's heat, the stew's seasoning, the bow's tiller and the pipe's
lead-in each took many rounds of the user playing and saying what felt wrong. So the job is a loop, not a
delivery:
1. Agree in a few lines what the hands do and what makes it good or bad (a skill a person could get better at).
2. Make the **roughest playable** version: the session, a plain view, a drill to reach it at once.
3. **Hand it over**: the command to play it, a screenshot, and two or three specific questions ("is the sweep
   too fast?", "does the mark feel fair?"). Wait for answers.
4. Change **one thing at a time** from what they say; keep what they liked. Repeat.
Taste decides when it's done, and the taste is the user's.

## How a mini-game is built

- **The session** (`WorkSession` subclass, sim side; found by scanning, saved as `modId:Name`):
  - `Step()` once per step (60 a second); `Press()`, `Axis(int)`, `Axis2(int)`, `Act()`, `Rune(int)`,
    `Pointed(u, v)`, `Clicked(u, v)`, `Custom(int)` for inputs; `Sample(q)` grades it; `Done = true` ends it.
  - **Pure**: these read only the session. `StillValid(sim)` reads the world; `Finish(sim)` changes it;
    `Interrupted(sim)` keeps what lasts if it's stopped early. Lists and arrays are copied in `Cloned()`.
  - Randomness from the session's `Rand`; timing from `StepsDone`; no clocks. `Triangle(steps, period)` is a sweep.
  - `WriteState`/`ReadState` for every field (bound what you read: it may come off the network).
  - Hidden state (a flaw only shown by its effects) is a field the view never prints; read it through
    `PerceptionView` where anyone else must see it.
- **Starting it**: from a station (`ITradeModule.AppliesAt`/`WorkAt`), from F somewhere (`EarlyActionAt` or
  `SiteActionAt`, then `Perform`), or from a command; `WorkSystem.Start(sim, who, session)`.
- **The view** (`GameRegistry.WorkView<T>`): `view.Begin(title, (keys, what)...)` ... `view.End()`; draw with
  ImGui's draw list in `Theme` colours, sizes through `Gui.S`; `view.TakesPointer(min, max)` where the mouse
  works the game; `view.Aside()` for work out in the world. Show heat as colour, a flaw as its effect: never a number the
  character couldn't know.
- **Keys of its own** (`GameRegistry.Control`, `GameRegistry.WorkInput<T>`): pack each into an `int` for
  `Custom`. Otherwise the game's own inputs reach it already (F, A/D, W/S, Space, 1-9, the mouse).
- **Sound** (`GameRegistry.WorkSound<T>`): a sound each time a count the player can see moves on
  (`ctx.A` keeps the last count); never keyed to hidden state.
- **A bot** (`IBotTrade.Play`) plays it from the session as a player would see it.
- **A drill** (`SimRegistry.Drill`) puts the player straight into it: `--drill <name>`.

Worked examples: `mods/musician/src/Musician.Sim/Musician.cs` (`PlaySession`: custom inputs, a tune carried
on), `mods/carpenter/src/Carpenter.Sim/*Session.cs` (felling's aim and lean, the saw's stroke, the pit saw
for two), and the template `modkit/templates/hni-trade`.

## Check it

`dotnet build HeroesNotIncluded.sln`, `--check-mod mods/<id>`, a test that feeds inputs to `Advance` and
checks the outcome (and that `Clone()`'s copy doesn't touch the original), then a screenshot through the drill.
