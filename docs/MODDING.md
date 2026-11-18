# Modding

Heroes Not Included should be as open as the games we started on: someone should be able to add a
new trade, a new mini-game, a new town or a new way to play a round, and a coding agent should know
how to build one from the start. The core game is meant to be the first mod, built through the same
doors a modder uses: when the game needs a back door, the API isn't finished yet.

This is the plan and, as it lands, the reference. What's built is marked; the rest is the order it's
being built in (ROADMAP.md, Now, 8).

## What a mod is

A folder:

```
mods/<id>/
  mod.json          id, name, version, authors, gameVersion, dependsOn[], loadAfter[], runsCode
  defs/**/*.json    merged after the core's, in load order
  textures/         extra roots for the atlas (ids are paths relative to the root, as today)
  sounds/           its own sounds.json, merged into the manifest
  <Id>.Sim.dll      optional: the deterministic half (references HeroesNotIncluded.Sim only)
  <Id>.dll          optional: the presentation half (work views, HUD, sounds; references the game)
```

- **The core is `data/`**, always first: the implicit core mod (moving it would churn the tools and
  packaging for nothing).
- **Load order:** dependencies first (`dependsOn`, `loadAfter`), then by id. Def indices of the
  core don't move, so a game with no mods hashes as it always did.
- **Two halves, the same rule as the game's:** a mod's `.Sim.dll` is refused if it references the
  engine, GLFW, Vulkan, ImGui or the game assembly. Its sim half must be deterministic like the
  game's own (below).
- **Code mods are C# assemblies.** Steam Workshop hosts any files, and other games ship DLL mods
  through it. Code isn't sandboxed, so the Mods window says plainly when a mod runs code.
- **Where they're found:** `mods/` beside the game (the checkout's when running from one), then
  `mods/` in the config folder (where Workshop items will go). The first to have an id keeps it.
- **Turning them on:** the title screen's More, then Mods (saved in `settings.json`'s `mods`), or
  `--mods a,b` for one run. Headless runs (bench, sweeps) see only `--mods`.
- **What follows them:** defs (`GameContent.LoadDefs`), the atlas's roots, the sound manifests, the
  def reloader (F8 and file watching cover a mod's `defs/` too), co-op's build tag (every file of
  every mod, the ids at its end: `0.1.0-…-2e283fc5be+long-season`), and saves (their meta names the
  mods; loading with others turned on is refused in plain words). Replays follow the build tag.

## Data mods

Most content is already defs (`data/defs`, merged in name order; references resolve once everything
is loaded):
- **New things:** buildings, furniture, items, recipes, quests, parties, adventurer kinds, incidents,
  barks, runes and mendings, outfits, and maps (a town is a `MapDef` with a building in the Tavern role).
- **Overriding:** a def with the same name replaces the earlier one.
- **Patching:** `"$patch": "merge"` merges only the fields given into the existing def (objects merge
  field by field; arrays and values are replaced whole), and `"$patch": "remove"` drops it, so a mod
  can change a price without restating the def. Merging happens on the raw JSON before anything is
  typed, so a def keeps its place and the core's indices don't move. Example: `mods/long-season`.
- **Art and sound:** a mod's `textures/` and `sounds/` are further roots; a later root's file with the
  same id replaces the core's, so a retexture is a folder of PNGs.

## Code mods: the extension points

A mod's assembly has an entry type the loader finds:

```csharp
public sealed class BardMod : ISimMod  { public void Register(SimRegistry r) { ... } }
public sealed class BardView : IGameMod { public void Register(GameRegistry r) { ... } }
```

The core registers its own trades the same way. What can be registered, and what it replaces:

| Point | Replaces |
|---|---|
| Sim systems, placed `after:`/`before:` a named one | `Simulation.CreateStandard`'s fixed chain |
| Work sessions (mini-games), found by type | `WorkSession.Read`'s switch |
| Component codecs, named `modid:Name` | `ComponentCodecs.All` |
| Commands, found in every content assembly | `CommandCodec`'s scan of one assembly |
| Work givers per work type (`WorkTypeDef`) | `WorkGivers`' arms and the `WorkType` enum |
| Town roles as defs (`RoleDef`, with what a workplace is stocked with) | the `TownRole` enum, `VillageRound.Stock*` |
| Station actions (what F does at a station with what's in hand) | the dispatch in `AvatarSystem` |
| Lessons per trade | `Lessons.All` |
| Crises by name, for `season.json` beats | the `CrisisKind` enum |
| Game modes (`IGameMode`, `IModeView`) | `VillageGame`'s mode flags, `RoundPhase` |
| Work views per session type (game side) | `VillageGame.DrawWork`'s switches |
| Bot hooks per trade (game side) | `BotPlayer`'s per-workplace partials |
| Practice-yard customers per trade | `Practice`'s switch on trade names |

### A new trade

A trade is the unit both this doc and the agent skills teach: the trade's def (`trades.json`: workplace,
kit, priorities, services, lessons' words), its workplace's role and building, and two modules:
- **`ITradeModule`** (sim): its work sessions, station actions, AI jobs and work givers, lessons,
  and setup.
- **`ITradeView`** (game): its work views, HUD, bot hook and sounds.

The carpenter is the worked example once it moves out to `mods/carpenter`. The bard (BARD-PLAN.md)
is the first trade built as a mod from nothing.

### A new game mode

An `IGameMode` sets a round up (map, deal, extra systems), names its phases, decides when it's won or
lost and what the reveal shows. The season, the practice yard and the arena are the core's three.

## The rules a mod keeps

The game's own rules (CLAUDE.md) bind its mods:
- **Determinism:** randomness only through `sim.Rand` or a `SimRandom`; trig and powers through
  `DetMath`; no clocks, no unordered iteration that affects state. The ModKit ships the same banned-API
  list the sim builds with (`src/HeroesNotIncluded.Sim/BannedSymbols.txt`), so a slip is a compile error.
- **Secrets:** hidden state is shown only through `Perception`, never read by a view.
- **Mini-games:** `WorkSession.Advance` is pure; anything reading the world goes in `StillValid` or
  `Finish`.
- **Saving:** every component has a codec; state outside components implements `ISaveState`.
- **Co-op:** every peer runs the same mods; the build tag covers the ordered mod set.

## Checking a mod

- `--check-mod PATH` (or an installed mod's id): loads the core, the mod and what it depends on
  headless, resolves defs, audits sprites and sounds as `--check-art` does, and lists what the mod
  adds, changes and removes, kind by kind. The exit code is the error count.
- A mod's tests use `HeroesNotIncluded.Sim.Testing` (`TestWorld.Millbrook` and friends).
- Then play it: `--new-game --mods <id> --trade <Trade>`.

## The ModKit and coding agents

- **Templates** (`dotnet new`, in `modkit/`): `hni-content`, `hni-trade`, `hni-minigame`, `hni-map`,
  `hni-mode`, each with `mod.json`, both projects, the banned-API list, a scenario test and a README.
- **Skills** in `.claude/skills/` (`new-trade`, `new-minigame`, `new-map`, `new-mode`, `content-mod`): the
  steps, the files, the registry calls, the rules and the check loop, pointing at the carpenter and the
  bard. The same skills, with an `AGENTS.md`, ship inside the templates, so a modder's agent knows
  how without this repository.
- **A trade's mini-games are found, not written once.** Every trade here took many rounds of play,
  feel and fixes before its games were right (the forge's heat, the stew's seasoning, the bow's
  tiller). The skills say so up front: get a rough game playable fast, then iterate with the modder.
  Play it (`--practice <Trade>`, `--lesson`), take screenshots, ask what felt wrong, change one
  thing at a time, and expect many passes. The plumbing (defs, registration, codecs) is the quick
  part; the mini-game is the real work, and it's the modder's taste that decides when it's done.

## Status

- [x] Guard rails: the Moddability rule in CLAUDE.md; the sim builds against a banned-API list.
- [x] Data mods: folders and `mod.json` (`Narsil.Mods`), several atlas and sound roots, `$patch`,
  the mod set in the build tag, saves and replays, the Mods panel, `--mods`, `--check-mod`; the
  example `mods/long-season`.
- [ ] A third town as a data mod (a map and a building), played in co-op: the proof data mods are
  enough for a town.
- [ ] Registries and mod assemblies (a pure refactor: `--bench` hashes unchanged).
- [ ] The carpenter moved out to `mods/carpenter`.
- [ ] Game modes.
- [ ] The bard as a mod; the ModKit and the skills.
- [ ] Steam Workshop (ROADMAP.md, Later).
