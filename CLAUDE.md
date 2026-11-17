# CLAUDE.md

Heroes Not Included (`HeroesNotIncluded` in code; the repo folder is still `supporting-cast`) is a multiplayer fantasy village game in C#/.NET 10: players take
the trades a tabletop party usually walks past, with optional traitors. It was forked from
DragonWorld at `cf61dfc` and runs on the **Narsil** engine (git submodule at `external/narsil`) on
BRUTAL (GLFW + Vulkan + Dear ImGui). Design: docs/DESIGN.md. Next: docs/ROADMAP.md; done: docs/HISTORY.md. Brand: art/brand/brand-guide.html.

## Commands

```sh
git submodule update --init
dotnet build HeroesNotIncluded.sln
dotnet test -c Release                                     # sim, game and Narsil tests
dotnet test --filter VillageDayScenario --logger "console;verbosity=detailed"
dotnet test --filter SeasonScenario --logger "console;verbosity=detailed"   # a whole round, bots, to the reveal
dotnet run --project src/HeroesNotIncluded                    # title screen
dotnet run --project src/HeroesNotIncluded -- --new-game --name Arin        # straight into a solo round (--trade Blacksmith, --bots N, --bot-skill F, --map NAME, --folk Elf, --friendly-fire)
dotnet run --project src/HeroesNotIncluded -- --host --name Arin            # host (--port N, --relay HOST, --difficulty D)
dotnet run --project src/HeroesNotIncluded -- --join 127.0.0.1 --player-id bea --name Bea
dotnet run --project src/HeroesNotIncluded -- --raid-test --name Arin          # as the smith, bandits down the north road
dotnet run --project src/HeroesNotIncluded -- --fire-test --name Arin          # the inn alight, you at the well with a full bucket
dotnet run --project src/HeroesNotIncluded -- --dragon-test --name Arin        # the dragon coming, you at the well with a full bucket
dotnet run --project src/HeroesNotIncluded -- --bridge-test --name Arin        # the bridge down, you the tinker at the gap with wood
dotnet run --project src/HeroesNotIncluded -- --new-game --die-at 5            # your character drops dead: try being a ghost
dotnet run --project src/HeroesNotIncluded -- --new-game --festival-at 5       # the season's festival, now, you by the well
dotnet run --project src/HeroesNotIncluded -- --practice Healer          # the tutorial in the practice yard: no season, customers on request (--lesson ID: straight into a chapter, `basics` too)
dotnet run --project src/HeroesNotIncluded -- --arena Swordsman --weapon IronSpear  # the practice ring: one foe a bout (any raider kind, or Dragon; Enter for the next)
dotnet run --project src/HeroesNotIncluded -- --furniture-demo --zoom 80    # also --building-demo
dotnet run --project src/HeroesNotIncluded -- --new-game --screenshot shot.png --screenshot-at 5 --quit  # also --screenshot-no-ui, --look-at X,Y, --yaw/--pitch DEG, --camera-mode; any mode
dotnet run --project src/HeroesNotIncluded -- --replay newest   # watch a kept round again (or a path; --keep-replay-at N keeps one mid-round)
dotnet run --project src/HeroesNotIncluded -- --art-preview
dotnet run --project src/HeroesNotIncluded -- --check-art     # headless: every sprite and tag exists
dotnet run --project src/HeroesNotIncluded -- --check-gpu
dotnet run --project src/HeroesNotIncluded -- --input-log     # joysticks, focus and clicks to the log (on a Deck: NARSIL_INPUT_LOG=1 %command%)
dotnet run --project src/HeroesNotIncluded -c Release -- --bench --seed 1234   # per-system cost and hashes (--round: a whole season with bots and crises)
dotnet run --project src/HeroesNotIncluded -c Release -- --season-sweep --seeds 8   # bot-played seasons: the baseline for tuning season.json (also --combat-sweep)
tools/deck.sh push                                        # publish linux-x64 and rsync it to a Steam Deck over ssh (logs, tail: docs/STEAM-DECK.md)
dotnet run --project external/narsil/src/Narsil.Relay -- --port 47050       # relay for online play
python3 tools/lake_map.py --preview OUT.png               # Reedmere, the town on stilts (tools/village_map.py: Millbrook; shared helpers in tools/mapkit.py)
python3 tools/chibi_people.py --preview                    # art generators: see docs/ART.md
python3 tools/sfx_synth.py --preview                       # synthesized sound effects: see docs/AUDIO.md
python3 tools/fetch_music.py                               # placeholder music (not in git); once per checkout
```

Data files are copied to the output on build. From a checkout, `data/defs` edits reload by
themselves in Debug (F8 forces it), F7 recompiles shaders, F5 reloads art. ` (R3) is camera mode: fly the camera for filming, H hides the HUD. F12 saves a screenshot (Shift+F12 without the UI) to
`~/.config/Heroes_Not_Included/screenshots`. F11 takes a playtest note; each round's playtest log
(`Playtest/PlaytestRecorder`) goes to `logs/rounds/` there. Running a playtest: docs/PLAYTEST.md.

## Architecture

- `src/HeroesNotIncluded.Sim`: **headless, deterministic.** References only `Brutal.Numerics` and
  `Narsil.Sim`, never the engine, GLFW, Vulkan or ImGui.
  - `Simulation` runs `ISimSystem`s in the fixed order in `CreateStandard`. 60 ticks = 1 game second;
    a game day is 16 real minutes at 1x (`TicksPerHour` 2400: keep it divisible by 12).
    Per-update physics constants (heat, fire) are scaled to the hour by `PerTunedUpdate`.
  - ECS in `Ecs/World.cs`: `Query<T>()` snapshots, `Each<T>()` doesn't allocate but throws if the
    store changes mid-loop.
  - The map is a 3D cell grid (`(z*H + y)*W + x`). Spawn, move and destroy through
    `Simulation.Things.cs`; designate through `Map.Designate/...`. Anything affecting passability
    bumps `Map.MarkStructureChanged`.
  - `Scenarios/VillageRound` sets up a round's town on a hand-made map (`MapDef`,
    `data/defs/maps/millbrook.json`, stamped in by `Maps/MapBuilder`; size the sim with
    `VillageRound.NewSimulation`). The map is generated by `tools/village_map.py --preview` from
    `art/reference/village-map2.webp`: edit the script, not the JSON. `Scenarios/VillageSetup`
    holds shared helpers. `MapGenerator` (the old valley) remains for the helpers `MapBuilder`
    borrows and the open ground `--building-demo` needs; tests that need a town use `TestWorld.Millbrook`.
  - `Scenarios/Practice` and `Scenarios/Lessons`: the practice yard and its tutorial (`VillageGame.Tutorial`):
    the shared basics (`BasicsLesson`), then the trade's `core` lessons, the rest offered after (`Tutorial`).
    A lesson's step is derived each frame from `Perception` (never a cursor), its words are the trade's
    `lessons` in `trades.json` (and its top-level `basics`); a finished one is kept game-side in
    `CharacterProgress.Lessons`. There are no in-round tips: teaching text lives in the tutorial.
  - `Round/RoundSystem`: the lobby, the deal and the season (`season.json`): `Festival` on the last
    evening (put up that afternoon by the labourers, `FestivalSystem.Sites`), then `Over` with a `RoundSummary` for the reveal. `Phase.None` means no round (tests,
    demos, bench); behaviour that belongs to rounds checks `Round.Live` (playing or festival).
    `Endless` keeps a test's round going. The host's choices are `RoundOptions` (friendly fire; traitors, off till M4). Players inhabit AI villagers. The deal also gives
    everyone a home (`Systems/Homes`: a cottage bed of their own, held for them).
    `Round/PauseVote`: co-op pauses by agreement (all connected agree, all ready to resume; who's connected
    comes from `PeerEvents`); the host seals turns with no steps while it holds. Alone, pausing is game-side
    (`VillageGame.Pause`: the menu or the pause key).
  - `Director/`: `Reckoning` keeps Renown (adventurers leaving pleased, quests cleared) and Dread
    (quests unanswered or gone badly; its limit ends the round). Players see words for them,
    through `PerceptionView.Standing`. `Director` sends the season's crises (`season.json` beats:
    raid, fire, shortage, plague, the dragon), each an `ICrisis` that's met or not. Off unless the
    game enables it, so tests stay scripted; outside rounds the old `Storyteller` sends raids.
    `Ai/Defence`: the armed fight raiders, the rest shelter; `Ai/DragonBrain`. The bridge a raid
    breaks is a `Tinkering/Repairs` breach, braced whole by the tinker (`SpanSession`).
  - `Afterlife/`: death in a round. A body keeps its `Death`; a player's character leaves a `Ghost`
    (its own entity, no creature parts) that sees through `PerceptionView.Ghostly`, talks on
    `Volume.Ghost`, haunts (`Hauntings`, signs read through `PerceptionView.Signs`: frost's words,
    the beasts startled or beckoned, raiders `Unnerved`), whispers dreams (`Dreams`), rides a beast
    (`Possession`) and does errands (`afterlife.json`) until an empath's Calling back is answered at
    the body (`Revival`). A `Possessed` beast is steered like a player's character: where code means
    "a player steers this", ask `Players.Steered`, not `Has<Avatar>`.
  - `Perception/`: what a character knows. Sight reads `Map.Light`, so it must never feed sim state.
  - `Speech/`: who heard what, decided when spoken from positions and rooms, and saved. Emotes
    (`Emotes`, `EmoteCommand`) are seen, not heard: `PerceptionView.EmoteOf`. Quick lines from the
    wheel (`VillageGame.Emotes`) are ordinary `SayCommand`s worded in `barks_quick.json`.
  - `Work/`: mini-games. `WorkSession.Advance` is pure, so `WorkPredictor` can run it ahead of the
    sim; anything reading the world goes in `StillValid` or `Finish`. Inputs arrive in batches.
    New sessions go in the `WorkSession.Read` switch. `Interrupted` keeps what lasts when one is
    stopped early. Work for two (`Work/Pairs`): a lead's session and a `HelperSession` (the striker's
    `SledgeSession`) are each driven by their own player and pass `Beat`s through the sim; a session's
    `Revision` tells the predictor to take it again. Forging (`Smithing`) keeps the piece in the tongs as a `WorkPiece` between its
    sessions (fire, anvil, quench); its heat is shown only as colour. Cooking (`Cooking`) keeps
    its state on the `StewPot`, carried on by `CookingSystem` between sessions; a pot's rot and
    the seasoning it wants are hidden (read pots through `PerceptionView.Pot`). A pot tastes of what went in
    (`Flavour`, plain to anyone; diners may crave one, `Hospitality/Tastes`); rot is read only by a
    noisy sniff (`SniffSession`). Rations (`Work/Rations`, packed in `PackSession`) go out on quests
    like charms; a slack tie (`Ration.LooseWrap`) is hidden. A named pot's flavour is the inn's
    `HouseDish`: later pots that taste like it are it. Chopped food (`ChopSession`) cooks faster. The spit
    (`Roasting`) roasts by crust colour, the oven (`Baking`) bakes bread and pies (`Pies`) by smell; dishes beyond the meal are priced on the sign (`Shops/Menu`).
    The butcher block (`Butchery`) dresses what the carter brings whole; a pluck's tear point is hidden. A fatty pot
    at a boil flares (`StewPot.FlareUntil`): lid it or it's a real fire. The kitchen's fires and stores are its cook's
    alone (`WorkGivers.FeedsHere`, `KitchenStock`). Rats in a shortage (`Hospitality/Rats`), feasts for a triumphant
    party (`Hospitality/Feasts`), broths (`Broths`), salting and smoking (`Larder`), word of the food (`ShopSystem.Fare`).
    Adventurers keep the town's hours (`ShopSystem.SmallHours`, `Mealtime`); `staffOnly` furniture
    keeps idlers out of a room. A fever
    (`Healing/FeverSystem`) is an injury that spreads and is quelled by the empath. Poured and
    ladled things go in clean vessels (`Vessels`); a customer leaves them dirty.
  - `Hospitality/`: the inn beyond its counter. Rooms by the night (`Lodging`), mess on the floor
    (`MessSystem`, through `PerceptionView.Messes`: blood is only a "dark stain"), drink taking
    hold (`DrinkingSystem`; a drink's effects are its `DrinkMix`), customers sitting with what
    they're served (`Table`, `Savouring`: effects land when it's finished), and who knows the way where
    (`DirectionsSystem`: adventurers order only from shops they know).
  - `Adventurers/`: `PartySystem` brings parties in a round; `AdventurerBrain` lounges, waits where
    their order is served, and leaves.
  - `Shops/`: what adventurers buy. Keepers set prices on their shop's sign (`ShopPrices`, defaults
    from `services.json`); an adventurer orders when the price is within what it's worth to them
    and their purse, lines up at the building def's `line` cells, and says the order at the front
    (only those who hear know of it). Each want is an `Order` of a `ServiceKind`; the line is
    derived from open orders, never stored. Coin is real `SilverCoin` items; paying moves them. AI
    trades serve front first (`MendJob`, `ServeJob`, `HealOrders`). An AI work pack keeps coin and
    `Entrusted` items (and a few clean vessels) between jobs. The inn's meals go through its serving
    `Hatch` (their own line; dishes put up, taken by whoever's at the front; players order with F there, `ShopSystem.PlayerOrder`).
    The cook supplies the inn and never sells to parties (`Hospitality/Provisions`, both towns): the innkeeper sends for a hamper
    (`ServiceKind.Provisions`) from the bar (`OrderProvisionsCommand`) or the kitchen's hatch, it's packed at the prep table
    (`PackSession.ForHamper`; an AI cook ties rations itself), put up on the hatch, and paid by what's in it (`Provisions.Charge`).
    Unpacked at the inn, its rations go on sale on the merchandise shelf. Where the inn and the kitchen stand apart (Reedmere,
    `Provisions.Split`) a labourer runs it over for a fee (`HamperRunJob`) and its dishes go on the warming shelf: lodgers' board
    (a meal ordered at the inn, `Provisions.IsBoard`). Gear is sold only from a shop's wares shelf (`display` furniture; `Goods`):
    stored there by its seller (`Goods.CanSellAt`, a shelf's `displayFor` role) it's priced at once (`Goods.Shelved`), and only
    the seller takes it down; no job takes an item for sale (`IsAvailable`). Villagers buy supper there of an
    evening. `Favours/`: pinned to the notice board in the square and known by reading it, for
    silver; errands (bring so many of a thing) are done by bringing, and the labourer takes them.
  - `Carpentry/`: the Carpenter, at the woodshop. Planks sawn (`SawSession`), bows tillered
    (`BowSession`), a quiver's pattern arrow sighted, shaved, headed and fletched (`FletchSession`) with a hidden warp (`Fletched.Warped`, never shown), sold as goods to archers
    and shot away on a quest (`Fletching`, like rations). AI: `SawJob`, `FletchJob`.
  - `Tinkering/`: the Tinker. Lockwork on strongboxes from quests: a box's trap is hidden item
    state, shown only through `PerceptionView.Strongbox`, which never says an armed one is there.
    Locks pinned at the bench (`PinSession`) and fitted to doors (`LockCut`), keys (`Key`);
    contraptions fitted together (`FitSession`) whose hidden `Binding` may jam them on a quest;
    party wagons (`Wagons`, `TrueSession`); curios whose true worth is hidden (`Curio`), read only
    through `PerceptionView.Curio` (your own `Appraisal`, never the truth), and the collector.
    Hand games start from the bench through `Crafting.WorkAt` (`Tinkers.Applies`).
  - `Healing/`: the empath. Wounds drawn in are `Injury.Absorbed` and known only by feel
    (`PerceptionView.FeltWounds`); never show their kind by name. A patient's wounds are known
    only once felt over (`Injury.FeltBy`). Mendings are woven from runes, the mix deciding which
    (`Weaving`); the book (`Spellbook`) is what the empath knows of them. An AI empath works the gift without the games (`LayOnHands`): it
    draws only what its mana will mend at once. Players ask for healing with F at a shrine pallet.
  - `Expeditions/`: quests with hidden truth, per-person `Intel`, parties off the map as
    `MemberRecord`s, the resolver, the return. Each resolved quest keeps a `QuestTale` of whose work
    went with the party, for the reveal (hidden deeds there only) and the festival's toasts.
  - `Bots/BotPlayer` plays only through commands and `Perception`, a trade's whole game included.
  - `Replay/`: a round is recorded (`ReplayRecorder`, on `Simulation.Recorder`) as a snapshot plus every
    command and co-op event (`PeerEvents`) by tick, from a point with no job running, and played back in a
    sim of its own (`ReplayPlayer`), state hashes checked. Anything that changes the sim outside a command
    must `Spoil` it. Kept at the reveal (`Replay/ReplayLibrary`), watched in `Replay/ReplayGame`.
  - Jobs (`Ai/Jobs.cs`) are iterator coroutines: use `GoTo`/`Reserve`, `Fail(reason)` then
    `yield break`, and check `Alive(target)` after every wait. `ReleasePawn` is everything ending a
    job does to the world. "Job" here is an AI task; a player's role is a **`Trade`**.
  - Brains (`Ai/Brains.cs`): emergencies, urgent needs, gear, work by priority, comfort, idle.
  - Health: injuries on body parts; hurt bodies only through `HealthSystem.Damage`.
  - Buckets (`Environment/Buckets`) are filled at the well and thrown (`ThrowSession`); water soaks
    cells (`FireSystem.Soak`), which then have no fuel till they dry. The AI's firefighting uses them.
  - Fires give off heat, light and radiant warmth from `Warmth.FireCell`: their own cell, or for a
    fireplace (`setInWall`) the room's cell in front of it, which is also the only place it's worked from.
  - `Appearance` is cosmetic and left out of the hash, its `Folk` (human, elf) included. AI villagers
    have none: `HumanLook.Derived` hashes their look (and folk) from the entity, the same on every peer.
  - Fights are real time and readable (docs/ROADMAP.md, "Combat pass"): a melee blow is wound up
    first (`Combat/Blows.Begin`, a `Windup` the `BlowSystem` lands) and lands on whoever's in its
    sweep the way it was aimed (`Combat/Reach`), friend or foe, no roll to hit (an arrow stops in a `target` building first: the festival's butts); armour soaks a share
    (`CombatRules.Strike`). With friendly fire off in a round, the town's blows pass through the town (`Reach.Harms`). It's met by `Guard` (block, parry, dodge); an AI decides as it sees it
    coming (`BlowResponse`). A hurt AI hands its turn on and hangs back, and backs off facing the foe
    before it runs (`FreeFight.Wary`, `BackAway`); the unarmed gang up on a raider when there are enough (`Defence.Mob`). Only so many press one target at a time (`Combat/Engagement`). There's
    no turn-based combat, and nothing may stop the clock for one player's fight.
  - Players move freely (`Combat/FreeMove`, `FreeMover`): WASD and the mouse's aim through
    `FreeMoveCommand`; a job's walk follows its path; levels change by the `NavGraph` links. R draws
    a weapon (combat mode). An AI is a free mover while it fights (`Combat/FreeFight`). Co-op predicts
    your own movement (`Net/FreeMovePredictor`), commands carry where you are (`FreeMoveSystem.Claim`),
    and keys are stamped with the tick pressed: blows at a player land `LatencyGrace` late, and a
    player's swing is judged against where everyone was then (`Combat/Trail`).
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
- Sound (docs/AUDIO.md): `Audio/GameAudio` holds Narsil's `AudioEngine`; ids are constants in
  `Audio/Sounds.cs`, mapped to files by `data/sounds/sounds.json`. `SoundDirector` plays the world
  only as the player perceives it (`PerceptionView`, `SpeechSystem.Hear`), `WorkSounds` the
  mini-games from the session in hand. Never key a sound to hidden state. `--check-art` audits
  sounds; `--mute` (or `NARSIL_AUDIO=0`) runs silent; `SC_SOUND_LOG=1` logs each sound.
- Input: in-world controls are actions (`Controls`, ids in `Act`, over Narsil's `ActionMap`), never
  `GlfwKey` literals, so they rebind and work from a gamepad; name keys in text through
  `Controls.Glyph`/`Keys`. Windows the pad should drive call `PadWindow` after their Begin.
- UI: the Narsil.Ui kit (`external/narsil/docs/UI.md`). Panels go through `Panel.Begin/End`, anything
  on screen in a round through `_hud` (`Hud`, anchored, stacked, nothing overlaps), sizes through
  `Gui.S`, colours from `Ui/Theme` (the brand's: keep ember rare). Mini-games open with
  `BeginWork(title, hints)`. Titles and the main menu use `Gui.DisplayFont` (Cinzel); words follow the
  brand guide's voice and word bank.
- Saved characters: `Characters/CharacterRoster` (`characters.json` in the config folder) holds the
  player's characters and their `CharacterProgress`, for every mode. Game-side only: the sim sees
  just the name, look and clothes sent from one; `VillageGame` credits the round at the reveal.
- `src/HeroesNotIncluded`: `TitleGame`, `VillageGame` (partials by area), `WorldRenderer`, `WorldMesher`,
  `Net/CoopSession`. Draw creatures at `WorldRenderer.DrawPos`, never raw `Position.Pos`. Presentation
  that lives on defs (`model`, `bridge`, `fireAt`, `smokeAt`, `modelCutAt`) is never read by the sim.
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
