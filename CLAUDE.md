# CLAUDE.md

Supporting Cast (working title) is a multiplayer fantasy village game in C#/.NET 10: players take
the trades a tabletop party usually walks past, with optional traitors. It was forked from
DragonWorld at `cf61dfc` and runs on the **Narsil** engine (git submodule at `external/narsil`) on
BRUTAL (GLFW + Vulkan + Dear ImGui). Design: docs/DESIGN.md. Done and next: docs/ROADMAP.md.

## Commands

```sh
git submodule update --init
dotnet build SupportingCast.sln
dotnet test -c Release                                     # sim, game and Narsil tests
dotnet test --filter VillageDayScenario --logger "console;verbosity=detailed"
dotnet run --project src/SupportingCast                    # title screen
dotnet run --project src/SupportingCast -- --new-game --name Arin        # straight into a solo round (--trade Blacksmith, --bots N)
dotnet run --project src/SupportingCast -- --host --name Arin            # host (--port N, --relay HOST, --difficulty D)
dotnet run --project src/SupportingCast -- --join 127.0.0.1 --player-id bea --name Bea
dotnet run --project src/SupportingCast -- --furniture-demo --zoom 80    # also --building-demo
dotnet run --project src/SupportingCast -- --art-preview
dotnet run --project src/SupportingCast -- --check-art     # headless: every sprite and tag exists
dotnet run --project src/SupportingCast -- --check-gpu
dotnet run --project src/SupportingCast -c Release -- --bench --seed 1234   # per-system cost and hashes
dotnet run --project external/narsil/src/Narsil.Relay -- --port 47050       # relay for online play
python3 tools/chibi_people.py --preview                    # art generators: see docs/ART.md
```

Data files are copied to the output on build. From a checkout, `data/defs` edits reload by
themselves in Debug (F8 forces it), F7 recompiles shaders, F5 reloads art.

## Architecture

- `src/SupportingCast.Sim`: **headless, deterministic.** References only `Brutal.Numerics` and
  `Narsil.Sim`, never the engine, GLFW, Vulkan or ImGui.
  - `Simulation` runs `ISimSystem`s in the fixed order in `CreateStandard`. 60 ticks = 1 game second;
    a game day is 10 real minutes at 1x.
  - ECS in `Ecs/World.cs`: `Query<T>()` snapshots, `Each<T>()` doesn't allocate but throws if the
    store changes mid-loop.
  - The map is a 3D cell grid (`(z*H + y)*W + x`). Spawn, move and destroy through
    `Simulation.Things.cs`; designate through `Map.Designate/...`. Anything affecting passability
    bumps `Map.MarkStructureChanged`.
  - `Scenarios/VillageRound` sets up a round's town. `Scenarios/VillageSetup` holds shared helpers.
  - `Round/RoundSystem`: the lobby and deal. `Phase.None` means no round (tests, demos, bench), and
    behaviour that belongs to rounds checks it. Players inhabit AI villagers.
  - `Perception/`: what a character knows. Sight reads `Map.Light`, so it must never feed sim state.
  - `Speech/`: who heard what, decided when spoken from positions and rooms, and saved.
  - `Work/`: mini-games. `WorkSession.Advance` is pure, so `WorkPredictor` can run it ahead of the
    sim; anything reading the world goes in `StillValid` or `Finish`. Inputs arrive in batches.
    New sessions go in the `WorkSession.Read` switch.
  - `Adventurers/`: `PartySystem` brings parties in a round; `AdventurerBrain` lounges, waits where
    their errand is served, and leaves.
  - `Errands/`: every demand is an `Errand`, asked aloud (only those who hear know of it). Coin is
    real `SilverCoin` items; paying moves them. AI trades answer errands (`MendJob`, `ServeJob`,
    `HealErrands`). An AI work pack keeps coin and `Entrusted` items between jobs.
  - `Locks/`: the Carpenter's lockwork on strongboxes from quests. A box's trap is hidden item
    state: show it only through `PerceptionView.Strongbox`, which never says an armed one is there.
  - `Healing/`: the empath. Wounds drawn in are `Injury.Absorbed` and known only by feel
    (`PerceptionView.FeltWounds`); never show their kind by name.
  - `Expeditions/`: quests with hidden truth, per-person `Intel`, parties off the map as
    `MemberRecord`s, the resolver, the return.
  - `Bots/BotPlayer` plays only through commands and `Perception`, a trade's whole game included.
  - Jobs (`Ai/Jobs.cs`) are iterator coroutines: use `GoTo`/`Reserve`, `Fail(reason)` then
    `yield break`, and check `Alive(target)` after every wait. `ReleasePawn` is everything ending a
    job does to the world. "Job" here is an AI task; a player's role is a **`Trade`**.
  - Brains (`Ai/Brains.cs`): emergencies, urgent needs, gear, work by priority, comfort, idle.
  - Health: injuries on body parts; hurt bodies only through `HealthSystem.Damage`.
  - Fights are real time: every attack goes through `Combat/RealtimeAttacks.TryAttack`, odds and
    wounds from `CombatRules`. There's no turn-based combat, and nothing may stop the clock for one
    player's fight.
  - Player input reaches the sim only as `ISimCommand`s, through `VillageGame.Issue`. `CommandCodec`
    serializes by constructor parameter. Commands declare `IActorCommand`, `ISubjectCommand`,
    `IPlayerCommand`, `IHostCommand` or `IDevCommand`; `CommandRules.Refusal` checks them on every peer.
    `CommandTests.HostileCommandsDoNoHarm` sends each one hostile values.
  - Co-op: `PlayerCharacter` stays on a character, `Avatar` only while they're connected, so a
    player who leaves becomes an AI villager. See `external/narsil/docs/NETWORKING.md`.
  - Saving: every component needs a hand-written codec in `Saving/ComponentCodecs.cs`; state outside
    components implements `ISaveState`. Jobs aren't saved: a load equals `AiSystem.InterruptAll`.
  - Materials, crafting, tools, mining, apparel, mood, social, prisoners and rot work as their
    XML doc summaries describe. Read through `Thing.MaxHp` and friends, not the def's raw number.
- `src/SupportingCast`: `TitleGame`, `VillageGame` (partials by area), `WorldRenderer`, `WorldMesher`,
  `Net/CoopSession`. Draw creatures at `WorldRenderer.DrawPos`, never raw `Position.Pos`.
- `external/narsil`: make engine changes there and commit them first, then the submodule pointer.
  Keep it game-agnostic. A change to `Narsil.Sim` or `Narsil.Voxel` affects DragonWorld and Lifeline.
- `data/defs/*.json`: all content. Files merge in name order; references resolve at load.

## Rules

- **Determinism:** all randomness through `sim.Rand` or a system's own `SimRandom`. No
  `System.Random`, `DateTime` or unordered iteration that affects state. Trig and powers through
  `DetMath`. A pure refactor must leave `--bench` hashes unchanged for seeds 1234 and 99.
- **Secrets (from M1):** the game may show other characters, allegiances and hidden item properties
  only through `Perception`. Never read that state directly from UI or rendering code.
- Add a test for new sim behaviour. `VillageDayScenarioTests` checks the town survives unattended;
  when it fails, read the message log before loosening anything.
- Rates in defs are per game day or hour. Heat balance is tuned by `EnvironmentTests`.
- No trademarked tabletop terms or monsters, and no assets or names from the games that inspired this.
- BRUTAL License: keep the splash, Credits window, `NOTICE.txt` and the licensing targets import
  (`external/narsil/docs/LICENSING.md`).
- BRUTAL gotchas: `external/narsil/docs/BRUTAL-NOTES.md`. Use `Narsil.Ui.ImGuiText` or the helpers in
  `VillageGame.Ui.cs` for coloured, wrapped or tooltip text, `"..."u8` labels, `in` for ImGui arguments.
- The build is warning-free; keep it that way.

## Style

File-scoped namespaces, `readonly`/`record struct` for small value types, XML doc summaries on
public types explaining *why*, brief comments otherwise. Prefer adding content through defs JSON.
