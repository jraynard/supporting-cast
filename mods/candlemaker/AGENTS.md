# Building this trade (for coding agents, and people)

This folder is a mod for Heroes Not Included: a trade players take. Its two halves are C# assemblies the
game loads from this folder; its defs, art and sounds are layered over the game's.

## Work with the person, in passes

A trade's mini-game is found by playing it, not written once. Make it playable, hand it over with the command
to play it and a couple of specific questions, change one thing at a time from what they say, and expect many
passes. The plumbing is the quick part; the feel is the work, and the person's taste decides when it's done.

## Build and check

```sh
dotnet build src/Candlemaker -p:HniGame=<the game's install folder>   # both halves, into this folder
<game> --check-mod <this folder>                                       # defs, art, sounds and code, checked
<game> --new-game --trade Candlemaker --mods candlemaker --drill candlemaker
```
In the game's own repository (`mods/<id>/`), no `HniGame` is needed, and `.claude/skills/new-trade` there has more.

## The rules

- `src/Candlemaker.Sim` may reference only the game's sim: it must be deterministic. Randomness only through
  `sim.Rand` or a session's `Rand`; trig through `DetMath`; no clocks; no unordered iteration that changes
  state. `BannedSymbols.txt` makes the worst of these compile errors.
- A `WorkSession`'s `Step` and input handlers read only the session; `StillValid` reads the world,
  `Finish` changes it; lists and arrays are copied in `Cloned()`. Every field is written and read in
  `WriteState`/`ReadState`, bounded (it may come off the network).
- Views and bots read hidden state only through `PerceptionView`; a view never changes the world.
- Every component you add needs `registry.Component<T>(...)`, or it's lost on load.
- Your types are saved as `candlemaker:Name`: renaming one breaks saves (use `SessionFormerly` /
  `formerly:` to keep old ones loading).
- No trademarked tabletop terms or monsters.

The game's docs/MODDING.md lists every hook (`ITradeModule`, `SimRegistry`, `GameRegistry`, `WorkView`, `IBotTrade`).
