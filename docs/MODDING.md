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
  barks, runes and mendings, outfits, supplies (what the carter brings a workplace to order, `supplies.json`),
  and maps (a town is a `MapDef` with a building in the Tavern role).
  A map may be any width and height, and says what's under its ground: `veins`, the seeded veins it has
  (none named: all of them); `seams`, ore bodies it's sure of (`{ block, at, radius, levels }`, their edge the
  seed's); `tunnels`, passages carved at a level (`{ level, cells }`, with rock two levels above them).
  Millbrook's mine is the worked example (`tools/village_map.py`, `tools/mapkit.py`'s `seam` and `tunnel`).
  `"offered": false` keeps a town loaded (`--map`, tests) but off the title's town picker: `mods/demo` patches
  it onto Reedmere.
- **Overriding:** a def with the same name replaces the earlier one.
- **Patching:** `"$patch": "merge"` merges only the fields given into the existing def (objects merge
  field by field; arrays and values are replaced whole), and `"$patch": "remove"` drops it, so a mod
  can change a price without restating the def. Merging happens on the raw JSON before anything is
  typed, so a def keeps its place and the core's indices don't move. Example: `mods/long-season`.
- **Art and sound:** a mod's `textures/` and `sounds/` are further roots; a later root's file with the
  same id replaces the core's, so a retexture is a folder of PNGs.

## Code mods: the extension points

A mod ships its code as DLLs at the top of its folder: a **sim half** that references only
`HeroesNotIncluded.Sim` (sessions, commands, components, systems), and optionally a **game half**
that references the game (how its mini-games look and sound). `Mods/ModCode` loads each mod in its
own load context, the game's assemblies shared, and refuses an assembly with sim types that
references the engine, a window or the game. A mod's types are saved and sent as `modId:Name`.
`tests/ExampleMod.Sim` is the smallest working sim half (a session and a system).

Each half has an entry type the loader finds:

```csharp
public sealed class MusicianMod : ISimMod  { public void Register(SimRegistry r) { ... } }
public sealed class MusicianView : IGameMod { public void Register(GameRegistry r) { ... } }
```

The core registers its own trades the same way. What can be registered, and what it replaces:

| Point | Replaces |
|---|---|
| Sim systems, `SimRegistry.System(make, after:, before:)` (built) | `Simulation.CreateStandard`'s fixed chain |
| Work sessions (mini-games), found by type (built) | `WorkSession.Read`'s switch |
| Component codecs, `SimRegistry.Component<T>` (built) | `ComponentCodecs.All` |
| Commands, found in every content assembly (built) | `CommandCodec`'s scan of one assembly |
| A trade's AI jobs, under the game's work types (`ITradeModule.JobFor`, built) | `WorkGivers`' arms (`WorkType` stays the game's: see below) |
| Workplaces of a mod's own: a role named in its defs ("Glassworks"), built | the `TownRole` enum; stocking is `ITradeModule.Stock` |
| Services of a mod's own: a service def with a new name ("Song"), built; `defs.ServiceKindOf(name)` | the `ServiceKind` enum |
| Station actions (what F does at a station with what's in hand) | the dispatch in `AvatarSystem` |
| Lessons per trade | `Lessons.All` |
| Crises by name, for `season.json` beats, `SimRegistry.Crisis(CrisisType)` (built; a mod's named `modId:Name`) | the `CrisisKind` enum and the Director's, festival's and replay's switches on it |
| Game modes (`IGameMode`, `IModeView`) | `VillageGame`'s mode flags, `RoundPhase` |
| Work views and sounds per session type, `GameRegistry.WorkView<T>`/`WorkSound<T>` (built) | `VillageGame.DrawWork`'s switches (now a table), `WorkSounds`' switch |
| Bot hooks per trade (game side) | `BotPlayer`'s per-workplace partials |
| Practice-yard customers per trade | `Practice`'s switch on trade names |

### A new trade

A trade is the unit both this doc and the agent skills teach: the trade's def (`trades.json`: workplace,
kit, priorities, services, lessons' words, and `standingOrders`: what the carter keeps its workplace in
while the AI runs it, from `supplies` the mod lists for its role), its workplace's role and building, and two
modules (its workplace's chests are its own without any flag: the AI takes only from its own workplace):
- **`ITradeModule`** (sim): its work sessions, station actions, AI jobs and work givers, lessons,
  and setup.
- **`ITradeView`** (game): its work views, HUD, bot hook and sounds.

The carpenter is the worked example once it moves out to `mods/carpenter`. The musician (MUSICIAN-PLAN.md)
is the first trade built as a mod from nothing.

### A new game mode

An `IGameMode` (`Modding/GameMode.cs`, `registry.Mode`) is how a round is played: whether the season's
clock runs it (`Seasonal`: the festival, the reckoning, Dread's limit), whether the Director sends its
crises (`Crises`), the hour it starts at, what it sets out once the trades are dealt (`Started`), when
it ends and with what headline (`Check`), and its line under the clock (`Status`). The round keeps
its mode by name (saved; one whose mod is off is played as a season), the title screen lists the
mods' modes under "Other ways to play", and `--mode <name>` starts one. The game's own are the
season and the practice yard. `mods/siege` (Siege Night: dusk, three waves of raiders, dawn) is the
example; the skill is `.claude/skills/new-mode`. The arena isn't a round (a test ring of its own) and
stays the game's.

### What the musician needed (the first trade built from nothing)

Building the musician (`mods/musician`) only through the doors above showed what a new trade needs that a moved
one didn't. Each is now part of the API:
- **A trade's level** (`"level"`: `Beginner`, `Intermediate` or `Experienced`; DESIGN.md, "Trade levels"): who it
  suits, shown where a trade is chosen. A gathering trade should aim for beginner. The old `"pace"` key
  (easygoing, steady, busy) still reads as a level.
- **A trade only players take** (`"playerOnly": true` on the trade): never dealt to an AI villager or a
  seat that didn't ask, and its player comes to town as a newcomer.
- **Defs of a mod's own kind** (`"instruments"` in its defs): kept, merged and patched like the game's,
  read typed with `DefDatabase.ModDefs<T>(kind)`; an `IModDef` resolves its references.
- **Inputs of a mod's own** (`WorkInputKind.Custom`, a session's `Custom(int)`): its game half packs them
  each frame (`GameRegistry.WorkInput<T>`), stamped and batched like the game's, so co-op and replays carry them.
- **Controls of a mod's own** (`GameRegistry.Control`): rebindable in Settings, with a context of their own
  so they don't clash with the game's.
- **An action of a mod's own** (`AvatarActionKind.Custom`, done by the module that offered it).
- **Marks on cells** (`GameRegistry.CellMarks`): sprites a mod lays flat on cells for anyone to see, as a dig order's
  mark is (the miner's chalk on a rock face), or floating over them (`Floating`: where to take something). Drawn for
  every player, so only what anyone may know, unless the mod sets them from its own player's screen.
- **How full a thing's drawn** (`GameRegistry.ModelCount`): a one-item thing that holds more (a barrow's load) shows its
  model's `minCount` boxes by the mod's count. A def's `pushed` draws it held as its model, pushed ahead of its carrier.
- **An overlay** (`GameRegistry.Overlay`): every frame in the world, a HUD panel of the mod's own and its controls read
  (`WorkView.Playing`) and sent as commands: the miner's barrow, leaned back with Q/E as it's pushed.
- **Drills** (`SimRegistry.Drill`, `--drill NAME`): a moment staged at once for trying a game out.
- **A crisis** (`SimRegistry.Crisis`, a `CrisisType`: how it begins, how a save reads it back, its label and festival line):
  weighed by name in `season.json`'s beats (`"kinds": { "mymod:Flood": 2 }`) and the pacing's `answers`, saved by name.
- **A pastime** (`SimRegistry.Pastime`, an `IPastime`): something the town does with its free time, asked by
  villagers' brains once their work's done and adventurers' before they lounge about: the musician's audience
  (`Listening`, an `AudienceJob`). A job says it's danced (`Job.Dancing`, `DanceTempo`) for the renderer's step.
- **Thoughts of a mod's own** (`"thoughts"` in its defs, mood.json's format), remembered through
  `DefDatabase.ThoughtOrNull`, so a thought gone with its mod is nothing rather than an error.
- **A lesson's own setup** (`ITradeModule.SetUpLesson`, after the core's stock and customers): the musician's lute
  put out of tune for its strings lesson.
- **A mod's own controls in its words** (`~@id~` in lesson and intro text, `Controls.Keys`): any action by its id,
  a mod's as `musician:note1`, shown as the key or button bound now. `TutorialWordsTests` registers the musician's
  game half to check its marks.
- **A bot's own inputs** (`BotPlayer.Custom`): a trade's bot adds `WorkInputKind.Custom` values each frame, as its
  game half packs a player's keys (the musician bot's notes and breaths).
- **A simulation keeps the modules it was made with** (`Simulation.TradeModules`), and `--check-mod`
  puts the content back after looking: a check once left the carpenter's hooks reaching into a
  simulation made without its systems.

### Kinds a mod adds by naming them

- **A workplace** is a town role: name one in a building's `roles`, a trade's `workplace` or a service's
  `at` that the game doesn't have, and it's a role of its own (`TownRoles.Parse`, the next free bit).
- **A service** is a `services` def with a name the game's `ServiceKind` hasn't: numbered after the
  game's in load order, priced on signs and saved by name. A mod's code gets its kind with
  `defs.ServiceKindOf("Song")`.
- **A share of the gathering** is a flag on the trade: `fells` (trees, the carpenter's) and `mines` (rock and
  boulders, the miner's) let it do that much in a round without `gathers`' crops and timber.
- **What a trade supplies** (`supplies`: thing names) the carter brings less of while a player holds it
  (season.json's `suppliedShare`): the miner's iron ore.
- **What a trade keeps in** (`keeps`: `[{ "recipe", "below" }]`) its AI holder makes without a bill, at a station
  in its workplace or out of doors beside it (`Crafting.KeptAt`, within `YardReach`), while fewer than `below` of
  the product are at the workplace or free about the town: the smith's charcoal, baked at the smithy's kiln. It
  lights an on-demand fire as a bill would; a bot holder makes it too (`BotPlayer.KeepIn`), a player keeps their own.
- **Work types stay the game's** (Craft, Cook, Haul, Doctor...): they're each villager's priorities, so a
  new one would be a new column for every villager and the work tab. A trade's AI jobs come under one
  of them (`ITradeModule.JobFor(sim, pawn, WorkType.Craft)`), as the carpenter's do.

### Art and sounds that tools make

A sprite or a sound a generator makes goes to the game's `data/`, or to the mod whose folder already
holds it (`tools/artroot.py`: `texture_path`, `sound_path`). Move a file into a mod once, and every
generator writes it there from then on. A mod's own tool writes into its folder outright
(`mods/musician/tools/musician_assets.py`). The carpenter's five sprites of its own and its saw are in
`mods/carpenter` this way.

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
  musician. The same skills, with an `AGENTS.md`, ship inside the templates, so a modder's agent knows
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
- [x] Mod assemblies: loaded per mod (`Mods/ModCode`), sorted into the sim half and the game half;
  sim types beside the engine refused; `--check-mod` loads the code, builds a sim and runs it a minute.
- [x] Registries, first part: work sessions and commands found by scanning the content, mods' codecs,
  mods' systems placed by name, work views and work sounds (game side). Hashes unchanged.
- [ ] Registries, the rest, as the carpenter moves out and shows what a trade needs: work givers and
  `WorkType`, town roles, station actions, lessons, crises, bot hooks, practice customers; the game's
  own work sounds and scenes moved onto the registry trade by trade.
- [x] The carpenter's sim half moved out to `mods/carpenter` (`Carpenter.Sim`, on by default): it reaches the core
  only through `ITradeModule` (station work, mending, the AI's jobs, a player's F, stocking, the fire),
  `SimRegistry` (systems, codecs, lessons, bots) and `IBotTrade`; old saves load through `formerly` names.
  Bench hashes and bot-played seasons unchanged.
- [x] The carpenter's game half moved into its own `Carpenter.dll` (`mods/carpenter/src/Carpenter`): its work
  views and sounds, the aim line of a tree to fell, a tree toppling (`Tilt`), its world sounds, the bench's
  Rebuild tab (`StationTab`), mend hints and a wagon's woodwork (`WagonPanel`), all through `GameRegistry`;
  `WorkView` is the drawing API they use. The game never names it (`ModBoundaryTests` in both test
  projects), never references it, and builds the mods it ships after itself (`BuildBundledMods`).
  Drills are a mod's too (`SimRegistry.Drill`, `--drill felling`).
- [x] The carpenter's defs (its trade, stations, recipes, the woodshop and its services) moved into
  `mods/carpenter/defs` (`tools/move_defs.py` lifts defs out with their comments). Turned off
  (`--mods -carpenter`, or the Mods panel), a round deals no carpenter, the woodshop's plot stands
  empty (`MapDef.Absent`), and its services aren't asked for; `WithoutTheCarpenterTests` plays a
  season to the end that way. Turned on, bench hashes and bot-played seasons are unchanged.
- [x] The carpenter's art and sounds moved into the mod: the generators write a mod's files into the mod
  (`tools/artroot.py`); its five sprites and its saw (`carpenter.saw`) are `mods/carpenter`'s.
- [x] Workplaces and services of a mod's own, named in its defs (`TownRoles.Parse`, `ServiceKindOf`);
  work types stay the game's, by design.
- [x] Game modes: `IGameMode`, the season and the practice yard as the game's own, a mode saved with the
  round, "Other ways to play" on the title screen, `--mode`; `mods/siege` as the example.
- [x] The musician as a mod, first pass: a player-only trade with a tin pipe, played free (eight scale keys and
  the octave above), notes heard as you press them and from others in earshot. `--new-game --trade Musician
  --mods musician --drill pipe`. Not on by default yet. The rest of MUSICIAN-PLAN.md follows, by ear, with you.
- [x] The ModKit (`modkit/`: `hni-trade`, `hni-content`, each with an `AGENTS.md`; `modkit/check.sh` makes,
  builds and checks a mod from each) and the skills (`.claude/skills/new-trade`, `new-minigame`,
  `content-mod`, `new-map`), the mini-game's many passes with the user first among them.
- [x] The skills' own test: a fresh agent, given only the skills, the ModKit and "a candlemaker who dips candles
  (dip again before the wax has cooled and it's lumpy)", built `mods/candlemaker` in one pass: warning-free,
  `--check-mod` clean, five tests passing, a screenshot, and the questions it would ask the player. What it
  stumbled on went into the skills and the template: the test project's reference, a ware's quality
  (`Crafted`), a ware's own sprite, the game's sound ids, colours that read on the panel, drills that start
  part-way, a private template hive in a worktree, `mods/README.md`.
- [ ] Steam Workshop (ROADMAP.md, Later).
