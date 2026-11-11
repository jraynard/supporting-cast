# CLAUDE.md

DragonWorld (shown to players as **Embersong Vale**, a working title: `TitleGame.Title`; the config/save
folder keeps "Dragon World", `TitleGame.DataFolder`) is a RimWorld-like 2D survival / dragon village sim prototype (humans + dragons, fantasy setting) written in C#/.NET 10
on the BRUTAL framework (GLFW + Vulkan + Dear ImGui bindings from Ahwoo). See README.md for the player-facing
overview and docs/ROADMAP.md for what's done and what's next.

## Commands

```sh
git submodule update --init                                      # fetch the Narsil engine (external/narsil)
dotnet build DragonWorld.sln
dotnet test                                                      # headless sim, game and Narsil engine tests (~80 s in Debug; scenario tests dominate). CI runs them before every release
dotnet test -c Release                                           # same, ~11 s
dotnet test --filter SliceScenario --logger "console;verbosity=detailed"   # prints the playthrough's event log
dotnet run --project src/DragonWorld                             # title screen → menu
dotnet run --project src/DragonWorld -- --new-game               # skip the title, straight into the slice (any game flag below also skips it)
dotnet run --project src/DragonWorld -- --new-game --difficulty hard --skip-hours 80   # storyteller on; raids from ~day 4
dotnet run --project src/DragonWorld -- --menu-pick "Dragon Demo" # title screen, then press a menu entry after 3 s (screenshots)
dotnet run --project src/DragonWorld -- --prologue --zoom 120 --yaw 200   # close-up camera for screenshots (--cutaway N too)
dotnet run --project src/DragonWorld -- --demo --skip-hours 13   # slice orders pre-placed, fast-forwarded
dotnet run --project src/DragonWorld -- --dragon-demo            # adult dragon soaring + a burn order on the eastern trees
dotnet run --project src/DragonWorld -- --furniture-demo --zoom 80 # wide furniture, table shapes, a porch roof (--show-roofs draws roofs)
dotnet run --project src/DragonWorld -- --dragon-demo --mounted  # ...with the keeper already on its back
dotnet run --project src/DragonWorld -- --building-demo --zoom 80   # preset buildings: every building and look, and a dwelling at each phase
dotnet run --project src/DragonWorld -- --new-game --freeform    # freeform walls/floors/roofs on the build bar instead of preset buildings
dotnet test --filter FoodChainScenario --logger "console;verbosity=detailed"   # can three villagers feed an adult dragon?
dotnet test --filter PresetSliceScenario --logger "console;verbosity=detailed" # the slice with preset buildings: hatchery, dwelling, longhouse
dotnet run --project src/DragonWorld -- --raid-demo --auto-combat # combat slice, AI playing both sides
dotnet run --project src/DragonWorld -- --prologue --name Arin   # Adventure's egg-bearer prologue (Ashford), no creator
dotnet run --project src/DragonWorld -- --adventure --name Arin  # Adventure mode at a new home: WASD or click to walk, hold F to work, Z to dig
dotnet run --project src/DragonWorld -- --adventure pact --name Arin --dragon-name Ashwing   # a dragon start (escape|pact; --stage site skips the opening); --creator dragon opens that title step
dotnet test --filter RaidScenario --logger "console;verbosity=detailed"    # prints the raid's combat log
dotnet run --project src/DragonWorld -c Release -- --bench       # headless per-system timings for one game day (+ worst ticks, save cost)
DW_PROFILE=1 dotnet run --project src/DragonWorld                # log frame-phase timings every 2 s, and every hitch
python3 tools/gen_placeholder_art.py                             # regenerate placeholder sprites
python3 tools/gen_icon.py                                        # regenerate the game icon (data/icon.png, packaging/)
tools/install-desktop-entry.sh                                   # Linux launcher entry + Wayland taskbar icon (app id embersong-vale)
python3 tools/export_art.py                                      # export art/**/*.aseprite to data/textures (docs/ART.md)
python3 tools/norse_kit.py                                       # preset buildings' kit art: gable triangles, carved barge boards (docs/ART.md "Building kits")
python3 tools/chibi_animals.py --preview                         # deer, boar, hare in the chibi style (4x detail) + .cache/animals.png
python3 tools/chibi_nature.py --preview                          # trees, bushes, boulders, standing stones, wild plants (4x detail) + .cache/nature.png
python3 tools/chibi_dragon.py --preview                          # the dragons (chibi young, majestic adult, 4x detail) + .cache/dragons.png
python3 tools/chibi_people.py --preview                          # the people (hi-res chibi layers, 4x detail): every sheet, + contact sheets in .cache/people/
python3 tools/make_look_aseprite.py                              # a character's look as one layered .aseprite to paint over (art/creatures/wanderer.aseprite)
blender -b --python tools/blender/setup_vrm.py -- model.vrm art/3d/wanderer.json   # a VRoid model into art/3d/wanderer.blend, sorted into layer collections (VRM add-on)
blender -b art/3d/wanderer.blend --python tools/blender/retarget_mixamo.py -- art/3d/wanderer.json   # bake its Mixamo animations (art/3d/anims) into named actions
blender -b art/3d/wanderer.blend --python tools/blender/render_layers.py -- art/3d/wanderer.json   # render a 3D character's layers (docs/ART.md "3D to sprites")
python3 tools/sprites_from_renders.py art/3d/wanderer.json --aseprite   # ...and turn the renders into its sheets (+ an .aseprite to touch up)
dotnet run --project src/DragonWorld -- --art-preview            # every sprite sheet animating; F5/file changes hot-reload
dotnet run --project src/DragonWorld -- --check-art              # headless: every def sprite and animation tag exists
dotnet run --project src/DragonWorld -- --load quicksave          # straight into a save (slot name or path); --open-saves save|load opens a window
dotnet run --project src/DragonWorld -- --check-save quicksave    # headless: load a save, tick an hour, print header + hash
dotnet run --project src/DragonWorld -- --host --name Arin        # co-op host (a new Pact at the tower's site; --host <save> for a save, --port N)
dotnet run --project src/DragonWorld -- --join 127.0.0.1 --player-id bea --name Bea   # co-op client as another player
dotnet run --project external/narsil/src/Narsil.Relay -- --port 47050   # a local relay for online co-op
dotnet run --project src/DragonWorld -- --host --relay 127.0.0.1 --name Arin       # host online: the Players window shows a join code
dotnet run --project src/DragonWorld -- --join ABCDEF --relay 127.0.0.1 --player-id bea   # join by code
dotnet run --project src/DragonWorld -- --join 127.0.0.1 --player-id bea --character Bea  # join as a saved character (no picker)
dotnet test --filter PactFoodScenario --logger "console;verbosity=detailed"   # can a lone adult dragon feed itself on a Pact site?
dotnet test --filter "MultiplayerTests|CoopSessionTests"          # co-op: players, permissions, join barrier, whole sessions
dotnet run --project src/DragonWorld -- --version                 # version + co-op build tag (headless)
tools/package.sh [version]                                        # alpha packages (win-x64, linux-x64, osx-arm64 .app) in dist/; CI publishes every green push to main as the release for <Version> (bump it for a new one)
dotnet run --project src/DragonWorld -- --check-gpu               # headless: does Vulkan load (MoltenVK on macOS) and list a GPU
```

Data files (`data/**`) are copied to the output directory on build. While the game runs from a checkout,
def edits in `data/defs` reload by themselves (Debug builds or `DW_WATCH=1`; F8 forces it), F7 recompiles
the shaders from Narsil's `src/Narsil.Voxel/data/shaders` (quads) and `src/Narsil/data/shaders`, and F5 reloads art from the
output's `data/textures` (see docs/ART.md). `DevPaths` finds the checkout. A def reload is a save round
trip onto the new defs (`VillageGame.ReloadDefs`): jobs restart, it waits out a fight, and bad defs
only show an error toast.

## Architecture

- `src/DragonWorld.Sim` — **headless, deterministic simulation.** Must never reference the engine, GLFW,
  Vulkan or ImGui; it only depends on `Ahwoo.BRUTAL.Numerics` for `int2`/`float2` and Narsil's headless
  `Narsil.Sim` (`SimRandom`, `DetMath`, grid nav; global usings `Narsil.Sim.Core`/`.Grid`).
  - `Simulation` owns everything and runs `ISimSystem`s in a fixed order (`Simulation.CreateStandard`):
    environment → needs/plants/fuel/eggs/dragons → AI → movement. 60 ticks = 1 game second.
  - ECS: `Ecs/World.cs` sparse sets. `Query<T>()` returns a snapshot list, so mutating while iterating is fine.
    `Each<T>()` iterates without allocating (renderer, UI, read-only searches) but throws if the
    store gains or loses entities mid-loop.
    Components are structs, except ones holding collections/references (`Mind`, `Mover`, `Needs`, `Construction`).
  - The map is a 3D grid of cells (`int3`, index `(z*H + y)*W + x`). `Map.Block` holds air or a solid block;
    creatures, structures and items live in air cells, standing on the solid block below (`Map.GroundUnder`,
    `Map.Standable`). Ground-surface properties (move cost, flammability, water) come from that block.
    `MapGenerator` lands the village at z = 4 with hills up to 4 levels higher (`RecommendedDepth` 12);
    test maps from `TestWorld.Create` are flat with the walk level at z = 1 (`C(x, y)`).
  - Walking connectivity is Narsil's `NavGraph` (`Narsil.Sim.Grid`: 8-way per level, ramps/stairs
    onto a neighbor one level up, ladders straight up), shared by `Pathfinder` and `Regions` (thin
    subclasses of Narsil's `GridPathfinder`/`GridRegions`) so they agree. All three see the map
    only through `INavGrid` (`Maps/MapNav`, a struct so the generics inline). `ClimbKind` and
    `PathEnd` live there too. Changing nav means a Narsil change, shared with Lifeline.
    `Position.Cell` is stored, not derived: mid-ramp the creature is over the next column.
  - Jobs move via `Ai/Navigation.PlanRoute`, which walks or (for life stages with `canFly`) flies: walk to
    open sky, climb to a cruise level clearing the route, fly straight, land on open ground, walk on.
    Airborne steps are just path cells that can't be stood in. Use `Navigation.CanReach(sim, pawn, ...)` in
    creature-facing queries so flyers count regions that touch open sky (`Regions.HasSky`) as reachable.
  - Floors are their own layer (`Map.Floor`, `Map.HasFloor` for finished ones), so a cell can hold a floor
    and a structure (a bed upstairs). `CanPlaceStructure(cell, def)` lets blueprints rest on blueprints;
    `CanBuildNow` makes construction wait for finished support. A def's `size` ([across, deep];
    `footprint: N` is shorthand for N×N: the nest is 3, beds [1, 2], workbenches [2, 1]) makes a
    structure hold every cell of its footprint in `Map.Structure`, turned by `Thing.Facing`
    (`Maps/Footprint`: odd sizes centre on the anchor, even ones grow towards +X and the front; its
    `Rotate` is the one `PropModel` uses). Placement checks them all, removal clears them all,
    `Thing.Cell` stays the anchor (a bed's head), the mesher draws it once from there (its model's
    boxes reach past 0..1), and a load refits footprints to the defs (`RefitFootprints`). Jobs at a
    building use `Job.GoToBeside` (outside a wide footprint, front first); reach checks take the
    entity (`AvatarSystem.InReach(sim, at, thing)`). `shapeOf` groups defs as one build button with
    a shape picker (C cycles: Table, LongTable, SquareTable); `ThingDef.IsA` matches a shape to its
    base. Losing a floor drops what it held.
  - Preset buildings (a trial, docs/plans/preset-buildings.md; `Systems/Buildings.cs`,
    `data/defs/buildings.json`): a `BuildingDef` plan stamps ordinary `partOnly` structures (plinth
    floors, walls, doors, roof) phase by phase (Foundation → Walls → Roof → Furnish), so every other
    system sees plain cells. The building itself is a map-less entity (`PresetBuilding`); its parts
    and added extras carry `PartOf`. Each raised phase is one unreserved `Construction` site off
    the map's layers, its def generated at load (`ThingDef.PhaseOf`, `BuildingVariant.PhaseDefs`):
    up to `Buildings.MaxBuilders` build it together as far as the materials brought allow, and
    `CompleteConstruction` → `Buildings.CompletePhase` lays all its parts at once. Furniture goes in
    as ordinary blueprints; `Buildings.Tend` (also `BuildingSystem` a few times an hour) moves the
    phases on. With `sim.Presets.PresetMode` on (new games; `--freeform` off) the player may only
    place `outdoor` defs outside a building, and inside one only in its free `slots`
    (`Buildings.PlayerProblem`, `FillSlotCommand`). `fence` walls never bound a room. Plans turn
    like footprints (`Footprint.Contains` undoes a turn with `(4 - facing) & 3`). `upgrades` fit a
    finished building's furnishing (a site of their own, `ThingDef.UpgradeOf`; recorded in
    `PresetBuilding.Upgrades`) and unlock recipes naming them (`RecipeDef.Upgrade`,
    `Crafting.Fitted`); `eatThought` is a memory for eating inside. The preset slice
    (`SliceDemo.ApplyPresetOrders`, `PresetSliceScenarioTests`) starts with a hatchery: a hearth
    keeps a house at ~20 °C, too cold for an egg. They're drawn
    with their look's `BuildingKitDef` (`Rendering/BuildingKit`, presentation only): walls pieced
    per cell from plan neighbours (span / corner + arms / post / door frame, cut to stubs like any
    wall), a pitched roof generated per roof cell (no generated flat roof over a plot). Phases
    going up draw the same pieces as ghosts (`WorldRenderer.DrawRaising`).
  - Roofs (`BuildingProps.IsRoof`, with `isFloor`): in the floor layer on top of the walls (the build
    tool places them a level above what you point at), within `supportSpan` of a wall or door below.
    `Map.HasRoof` (derived on load, not saved) counts as cover (`Map.Covered`), and `Map.Ceiling`
    (floor or roof) stops smoke, light and flyers, but a roof isn't standable or built on. The game
    draws built roofs and a generated roof over enclosed rooms (`Roofed && !CoveredAbove`) as
    `Quad.Roof`, hidden by default (`\`, `QuadRenderer.HideRoofs`, push constant `misc.w`), with a
    `Quad.RoofHint` outline shown instead; `Rooms.Rebuild` marks chunks whose `Roofed` changed.
    With roofs hidden the walls in front of indoor rooms come down too (`WorldRenderer.CutawayWalls`,
    `WorldMesher.CutHeading`/`CutAt`: the camera heading snapped to 8 directions; changing it
    remeshes chunks a few a frame): they mesh as `StubHeight` stubs, doors follow
    (`DoorAnimator.HeightOf`) and picking passes over them. `Quad.Occluder` quads (walls, rock,
    roofs, doors) dither away in a circle round `QuadRenderer.XrayFocus` (your character, else the
    ground under the cursor past walls) where they're in front of it (push constant `focus`).
    In the game, build tools act on the drag's starting level; holding Ctrl raises it one level.
    Deconstruction (`Designation.Deconstruct`, done under Construct work) goes top-down: `CanDeconstruct`
    refuses while something rests on or hangs from the target. In Adventure mode the player marks
    with the same tool and holds F at a marked building (`AvatarActionKind.Dismantle`). Floors can't be placed over stairs.
  - `Map.Active` marks air cells that are standable or hold a structure. Temperature, lighting and rooms
    only process those; open sky reads as outdoor air (`Simulation.TemperatureAt`). Iterating every
    cell is ~12x the work of one level, so don't add new per-cell loops over `Map.Count` lightly.
  - Materials (`MaterialDef`, `Thing.Material`): wood comes as oak/pine/birch, stone as sandstone/
    limestone/granite/slate/basalt (rough stone has none). Any kind pays a cost
    for its item, stacks only merge with the same material (`CanAcceptItem(..., material)`), and a
    construction becomes its most-delivered material (`Construction.Materials`). Use `Thing.Label`
    for names ("birch wall", "cut granite wall") and pass the material along when creating or splitting items.
    Materials change stats (`hpFactor`, `insulation`, `flammability`, `workFactor`, `massFactor`):
    read them through `Thing.MaxHp/Insulation/Flammability/UnitMass`, never the def's raw number;
    birch and rough stone are the neutral baseline the heat tests were tuned on.
  - Spawning, moving and destroying things go through `Simulation.Things.cs`, which keeps the map layers
    (`Map.Structure`, `Map.Item`) in sync. Don't write those arrays directly.
  - Orders and stockpiles change only through `Map.Designate/ClearDesignation/SetDesignations` and
    `Map.SetZone` (the arrays are read-only spans), which keep sorted indexes. Searches iterate
    `Map.Designated(flag)` / `Map.ZonedCells` (ascending index, the same order as a full scan)
    rather than every cell; collect stale cells and clear them after the loop.
  - Changing anything that affects passability or walls must bump `Map.MarkStructureChanged(idx)`. Path costs,
    regions and rooms rebuild lazily from `StructureVersion` (a region or room rebuild mid-tick refreshes
    path costs first: `Simulation.EnsurePathCosts`); render chunks remesh from `Map.ChunkVersion`
    (the parameterless overload remeshes everything). Purely visual block changes use `MarkCellChanged`.
  - Jobs (`Ai/Jobs.cs`) are C# iterator coroutines that yield once per tick. Use the `GoTo`/`GoToEntity`/`Reserve`
    helpers, call `Fail(reason)` and `yield break` on problems, and check `Alive(target)` after every wait.
    Reservations release automatically when a job ends, and `AiSystem.EndJob` drops anything still carried.
    With `Simulation.StrictJobs` off (the game) a job that throws just fails; it's on by default so
    tests and the bench surface the bug.
  - Health (`Systems/HealthSystem.cs`, `Components.Health`): injuries live on body parts (`data/defs/bodies.json`);
    pain, blood and per-part health give capacities (Moving, Manipulation, Sight, Flying, Consciousness) that
    feed movement speed, flight and combat accuracy. Hurt bodies only through `HealthSystem.Damage`.
    `Creature.Hp` is still the pool hunger and cold drain. `Creature.Downed` creatures don't think or move;
    Doctor work rescues them to bed (`Carried`) and tends wounds. `Systems/Rescues` says who can carry
    whom (people carry people and dragons up to `Encounter.MaxDragSize`, to a nest; a grown dragon
    carries up to its own size, and a Devoted one brings its downed keeper home); a downed adult
    dragon is fed where it lies (`FeedPatient`) and mends faster by a lit fire. The player carries a
    downed hatchling or a carcass in their arms (`AvatarActionKind.Carry`), kept through jobs
    (`ReleasePawn` leaves the avatar's `Carried` alone) and dropped if they go down.
  - Combat (`Combat/`) is turn-based. `Encounter` (`sim.Combat`) starts when raiders come into sight
    (`CombatSystem`). During a phase the clock is frozen: moves animate through `Encounter.Animate`
    (`MovementSystem.Step` for one unit) without ticking; after both phases `EndPhase` runs `RoundTicks`
    of world time with combatants' AI suspended. Anything render-side that fades by `Clock.Tick` must
    also fade by real time (see `WorldRenderer.Age`). `CombatRules` are pure functions plus `sim.Rand`
    rolls; `TacticalMoves` is a TU-budgeted Dijkstra over `NavGraph` plus air steps; `Tactics` is the AI
    for raiders, the village on auto and dragons that refuse orders. `Encounter.Resolve` plays a fight
    out headless. Units spend time units (`Combat/TimeUnits.cs`: per-cell walking, action shares,
    `ShotMode` snap/aimed) on moves and actions in any order (`Move`, `Attack`, `Breathe`, `PickUp`,
    `Interact` for story actions); `Wait` ends a unit's turn and what's left pays for reactions.
    Reactions (`Reactions.cs`: snap shots at movers, swings as they pass, answers to attacks via
    `AfterAttack`; once per enemy action each) fire from inside
    `Encounter.Animate` as a mover leaves or enters a cell, so they roll the same in the game and
    headless; `Reactions.Threats` predicts them without rolling (move preview, AI).
    Overwatch fires once a phase, at an enemy stepping into range and sight; Response answers only
    an attack on the unit itself, once a phase (`CombatUnit.OverwatchUsed/ResponseUsed`).
    Player orders in a fight are commands (`Core/CombatCommands.cs`, never `enc.Move(...)` from the UI);
    one arriving mid-move waits in `Encounter.Queued`, and what it came to goes to the
    render-only `Encounter.Feedback`. The fight's pacing (animation, reaction holds, AI steps)
    counts `Encounter.Advance` steps (60 a second), not real time, so peers play it alike.
  - Adventure mode (formerly Bearer mode): a creature with `Avatar` is the player's character. `AvatarSystem` (before AI) walks it one
    `NavGraph` step per held direction, its brain never runs, `Job.Work` returns 0 unless the work key is
    held, and it keeps what it carries between jobs. Player input reaches the sim only as `ISimCommand`s
    — keep it that way for replays and multiplayer. The game sends every action through
    `VillageGame.Issue` (single player: `Simulation.ApplyNow`; co-op: the session); tests may
    `Enqueue` (applied at the start of a tick). Hunting (`Ai/AvatarHunting.cs`): F at a wild animal starts
    `AvatarHuntJob` (shoots through `RealtimeAttacks` on the weapon's cooldown while held, never moves
    the avatar, ends out of range); F at a carcass in reach is the village's `ButcherJob`.
    Overseer tools and the inspector are commands too
    (`Core/OrderCommands.cs`). `CommandCodec` serializes commands by constructor parameter, so a new
    positional record needs no codec, only supported types (primitives, enums, `Entity`, `int2/3`,
    defs, arrays, component structs). Decoding refuses undefined enum values and nulls in parameters declared
    non-nullable, so mark optional ones `?`; `CommandTests.HostileCommandsDoNoHarm` sends every
    (non-dev) command with hostile values, and with `StrictJobs` off a command that throws only fails itself.
    In combat, other village units are `CombatUnit.Companion` (AI) unless `Encounter.PlayerCommands`.
    `SettlerSystem` (only with an avatar) brings a newcomer for each unowned bed indoors. Companions
    take `Request`s (follow, guard the egg) via `RequestCommand`; `HumanBrain` honours them after urgent needs.
  - Adventure starts (docs/plans/adventure-mode.md): `sim.Story` (`Scenarios/StoryState`, an
    `ISaveState` system, hashed only once set) holds the opening (`StartKind`) and a pending
    difficulty, shared by co-op peers. `Companions.SpawnBondedDragon` gives the player an adult
    partner; adults with `huntsAlone` feed themselves (`Ai/DragonHunting.cs`, after meat on the
    ground): flyers hop up and stoop on prey (`Navigation.StoopRoute`, leading it, striking from the
    air), since game outruns them on foot. Dragons also eat `ThingDef.DragonFood` (the dragon cake);
    `ThingDef.EdibleBy` is the one rule for who eats what.
    Breeding (`Systems/Breeding.cs`, save format 16 for `Dragon.Sex`, rolled from a hash at spawn):
    a contented grown female and male of the village, with a nest free, court (an hourly hash of
    her and the hour against `BreedProps.MatingChancePerDay`); she's `Gravid`, lays 1-2 eggs of
    either parent's breed and element in a nest (`LayClutchJob`), and rests (`LaidClutch`).
    No `sim.Rand`; one dragon or none plays as before. `MakeKeeperCommand` takes the dragon. `QuestStage.TakeWing` + `Departure.CantFlyOff` let a rider fly to a new map:
    `Travel.Pack` (no wagon) carries the dragon and both their wounds as plain data.
    `DragonLook` is cosmetic like `Appearance` (tinted sheet layers, docs/ART.md).
    `AdventureStart.Setup(sim, kind, Hero, difficulty)` is the one entry for the dragon starts
    (title: start → `CharacterCreator` → `Ui/DragonCreator` → difficulty); Escape turns the
    storyteller on, Pact leaves it in `Story.PendingDifficulty` (and so does a Pact arrival).
    The Pact opens in `Scenarios/EnchantedForest` (moonpetal, the sorcerer's `StoryNpc`, a bargain
    from `DialogueDef`s in `data/defs/story.json`); its new home has a `LeyStone` and
    `sim.Story.Pact`, checked hourly by `Systems/TowerPact` against a `TowerDef` (rooms stacked
    by the stone): settling it starts the pending storyteller. Co-op and `--stage site` start at
    the site (`AdventureStart.PactSite`); joiners there get a dragon, not an egg.
    Escape opens in `Scenarios/EscapeCamp` (`sim.Story.Escape`): `Captive` avatar, `Chained`
    dragon (no AI, not recruited into fights), captor `StoryNpc`s of `Faction.Captors` until the
    alarm makes them raiders. The supper round runs off the clock; Overpower/Unchain are avatar
    actions; Escape may fly off mid-fight from beside the dragon; going down recaptures you.
  - The prologue (`Scenarios/Prologue.cs`) is its own map: Ashford-faction NPCs, the `StoryKeeper`, and the
    avatar's `Quest`. Leaving packs a `TravelParty` (plain data, defs by name) and `Travel.Arrive` sets it
    down on a fresh map in a new `VillageGame`; the overworld will carry parties the same way.
  - Containers (`BuildingProps.StorageStacks`, the wagon) hold items off the map with a `Stored` marker
    and the container's cell. Item searches use `AvailableItems`/`IsAvailable` (spawned or stored),
    `TakeFromStack` takes things out, `Store` puts them in; hauling leaves stored items alone.
    `Appearance` is cosmetic (not hashed); the game draws people as tinted layers (`Rendering/HumanLook`, docs/ART.md),
    plus what's in hand (`ThingDef.Held` shapes; the job's `Job.ToolKind` tool via `Crafting.ToolInHand`, else the weapon: `WorldRenderer.HeldOf`),
    or, with `Appearance.Model`, from a 3D model's own sheets (`creatures/<model>`, `apparel/<worn>_<model>`; docs/ART.md "3D to sprites").
  - Brains (`Ai/Brains.cs`) decide in this order: emergencies → urgent needs → gear lying around
    (`FindGear`) → work by priority (`Ai/WorkGivers.cs`, `WorkType` enum order breaks ties) → comfort → idle.
    `AiSystem` picks the brain: raiders and anyone `Leaving` (herds moving on too) → `RaiderBrain`,
    `Faction.Prisoner` → `PrisonerBrain`, `Race.Animal` → `AnimalBrain`, dragons, else humans.
    Dragons use `WorkGivers` too: `CreatureDef/LifeStage.Work` lists what a creature may be given
    (`CreatureDef.CanDo`; Burn also needs breath), and `DragonBrain` rolls obedience on each job.
    Finders must stay side-effect free (no reserving) since a refused job is just dropped.
  - Wildlife (`Systems/WildlifeSystem.cs`, `sim.Wildlife`): off by default, on for generated maps
    (`MapGenerator.Options.Wildlife`). Below `minAnimals` herds come sooner and are the `startingKind`
    (deer, which a dragon can take). Arrivals and `AnimalBrain` choices draw from
    `sim.Wildlife.Rand`, so animals nobody touches don't shift `sim.Rand`; it also keeps fish stocks
    (`Systems/Fishing.cs`). Dead animals stay as carcasses (`Creature.Dead` + `Rot`) until butchered.
    Real-time attacks (hunting, a boar goring) go through `Combat/RealtimeAttacks.TryAttack`, not
    `Encounter`; game animals never join turn-based fights. Predators do: wolves and bears
    (`AnimalProps.Predator`, commonness 0 so herds never bring them) come only as storyteller
    events (`Systems/WildEvents`), as `Faction.Predator` (`Factions.Attacker`: the raiders' side),
    think like raiders and fight in an `Encounter`; a pack breaks at half strength (`Tactics.PackBroken`). With a butcher spot built
    (`BuildingProps.ButcherSpot`, `Systems/ButcherSpots`), `ButcherJob` first carries the carcass
    there (`Carried`, slowed by `AnimalProps.Mass`) and cuts it up faster and with less waste.
    Standing hunting orders: a dragon's `HuntPolicy` (absent = stores first; `DragonHunting.Meal`
    is the one place a self-feeding dragon's meal order is decided, `BringHome` carries the kill to
    a butcher spot) and the village's `sim.Larder` (`LarderSystem`: keep N meat, marks `HuntOrder`s
    hourly, never the last of a herd).
  - Spoilage (`Systems/RotSystem.cs`): items whose def has `rotDays`, and carcasses, carry `Rot`.
    Create, split and merge food through `CreateItem`/`SpawnItem(rot:)`/`TakeFromStack`/
    `PlaceItemNear`/`MergeStacks` so the progress follows the units. Past `FoodPoisoning.SpoiledAt`
    food can make the eater ill (`Systems/FoodPoisoning.cs`, the `FoodPoisoned` component; rolled
    from a hash, never `sim.Rand`, scaled by `CreatureDef.FoodPoisonFactor`): pass the eaten
    stack's rot to `EatJob.Nourish`, which every way of eating goes through. People skip spoiled
    food while anything fresh is in reach; cooking halves rot.
  - Dragon breeds and elements (docs/plans/dragon-types.md): a breed is a `CreatureDef` with
    `breed` props (`Dragon` is the Hearth dragon, `NorseDragon` the Norse one; every `Race.Dragon`
    def is one), reached through `Systems/Dragons` (`Breeds`, `BreedOrDefault` for anything from a
    command or file, `Spawn`, `SetElement`). The element is sim state in `Dragon.Element`
    (`DragonLook.Element` only carries the creator's pick) with numbers in `ElementDef`s
    (`dragons.json` "elements"): `Breath.ShapeOf`/`RangeOf` scale the stage's breath, `CanDo` gates
    breath work by `ElementDef.Work` (Burn/Stoke fire, Chill ice, Quarry lightning, Blight poison,
    Nurture nature: `Ai/BreathJobs.cs`), `Dragons.Resists` replaces the bare fireproof test for breath
    damage, and `ColdTolerance` (derived, not saved) lowers an ice dragon's cold limits. Statuses go in
    `Afflictions` (`Systems/AfflictionRules.cs`: tick-based, read by walking, `TimeUnits.WalkCost`,
    `Encounter.BeginPhase` and `RealtimeAttacks`; never `sim.Rand`); poison leaves `VenomCloud`
    entities (`Environment/VenomClouds.cs`). Eggs carry `Egg.Breed`/`Element` (`Dragons.ChooseEgg`).
  - Dragon care (`Systems/Bonding.cs`, `Systems/DragonCareSystem.cs`, `Ai/CareJobs.cs`, defs in
    `data/defs/dragons.json`; docs/plans/dragon-interactions.md): every act of care (pet, hand-feed,
    play, groom, comfort, train, rest together, a gift) is a `DragonInteractionDef` and goes
    through `Bonding.Interact`, the one rule for the player (`DragonCareCommand`,
    `AvatarActionKind.TendDragon/AnswerDragon`), village keepers (`WorkGivers.KeeperCare` →
    `DragonCareJob`) and gifts (`Hoard.Give`): novelty (the same thing again soon counts less),
    answering a want, imprinting in the first day, `trustLevels` that slow affinity as it climbs,
    tricks. The dragon's side is the `DragonCare` component: `sim.DragonCare` rolls its wants
    from a stream of its own (saved, always in `StateHash`) and turns long neglect into a sulk or
    a departure; its `Recent` list is render-only. Hand-fed hatchlings eat from a trough
    (`BuildingProps.DragonBowl`). The social Pet in `mood.json` is care too (`answers: false`:
    in passing it never answers a want). In a fight, Devoted and Soulbound dragons add reactions
    (`ReactionKind.Guard`: a swing at whoever steps in beside the keeper; `Defend`: answering an
    attack on the keeper, free of TU). The game shows care with `pet`/`play`/`groom` tags
    (`CreatureAnimator.CareTag`), hearts and floating lines (`WorldRenderer.CareLine`).
  - Riding (`Systems/RidingSystem.cs`): `Mount`/`Riding` components; the mount is drafted and its
    movement step carries the rider (`MovementSystem.Step`), whose AI is skipped. In combat the pair
    are two units: riders have no move budget, sit higher (`CombatRules.Seat`) and may have blows land
    on the mount. Saddles (`Systems/Saddles`, the `Saddled` component, `ThingDef.Saddle`) are a
    dragon's one piece of apparel, kept apart from people's `Apparel` slots: put on by the keeper
    (`SaddleCommand` → `SaddleJob`), they add `MountedAim` to the rider's odds and armor on top of
    the scales, and their saddlebags are a container like a pack. A rider's AI only runs jobs
    marked `IFromTheSaddle` (`AvatarHuntJob`: F shoots game from the saddle);
    `MountedHuntCommand` → `RidingSystem.OrderHunt` sends the mount after an animal
    (`DragonHuntJob(mounted: true)` leaves the kill), and a pair on the hunt doesn't dismount. The game draws
    `apparel/<worn>_<dragon sheet>` over the dragon (`tools/chibi_dragon.py` draws it per frame). Anything that downs or kills a creature goes through `RidingSystem.OnIncapacitated`.
  - Skills (`Components/Skills.cs`, `Systems/SkillRules.cs`): humans only; everyone else counts as
    `Skills.Average`. Work loops add `Job.Work(skill)` per tick instead of 1 (it also grants XP).
    Starting levels come from their own seeded stream so spawning doesn't shift `sim.Rand`.
  - Inventory (`Systems/Inventory.cs`, `Core/InventoryCommands.cs`): hands stay one stack
    (`Carrying`); a pack is apparel in the Back slot with `ThingDef.Container` (capacity in kg; every
    item def needs `mass`). Its contents are `Stored` in it like the wagon's, but `IsAvailable` never
    offers them to jobs, and `LocationOf` follows them to the wearer. Containers fill through
    `Store`/`ContainerRoom` (slots for buildings, weight for packs). Companions' packs only hold a
    job's load (`Inventory.WorkPack`: `ReleasePawn` sets it down); a player character's is their own.
  - Crafting (`Systems/Crafting.cs`, `Ai/CraftJobs.cs`, `Core/CraftCommands.cs`, recipes and stations
    in `data/defs/workshop.json`, `RecipeDef` in `Defs/CraftingDefs.cs`): a station is a building with
    `workbench`; its `Workbench` component holds the `Bill`s and the craft under way, whose
    ingredients are used up as they're brought (`Staged` counts, like `Construction.Delivered`), so
    a load or interruption keeps them and the progress, and cancelling (`Crafting.Cancel`, also on
    destruction) gives them back. `CraftJob` serves bills (Craft/Cook work) and the player
    (`CraftCommand`, then hold F). Products take the main material (`ThingDef.MaterialsOf`: planks
    come in wood's materials; `MaterialDef.Fits`), gear gets `Crafted` (quality from `sim.Rand`, maker).
    Fuel with `onDemand` (kiln, forge) only burns while `Crafting.WantsFiring`: go through
    `Simulation.Burning`. Dragons' Stoke work leaves `Stoked`, extra heat in `TemperatureSystem`.
    Recipe books (`Systems/RecipeBooks.cs`): skill unlocks are computed, never stored (village: any
    member's level; player: their own); discoveries (`handle:`/`built:` triggers) go in
    `sim.RecipeBook` and the player's `RecipeBook`. New skills (Crafting, Cooking, Hunting) roll after
    the first seven from their own stream (`SkillRules.RollAdded`), so existing rolls never move.
  - Tools (`ThingDef.Tool`: kind, speed, tier): tiers are named `TierDef`s in `data/defs/mining.json`
    (1 stone, 2 copper, 3 bronze, 4 iron, 5 steel, 6 dragonforged; defs write `"tier": "Bronze"`,
    resolved through `TierRef`). Gathering (Chop, Mine,
    rock with `BlockDef.DigTool`, fishing's "Rod") goes at `Crafting.GatherFactor` (best tool to hand,
    `BareHands` 0.35 without; non-humans 1). `RecipeDef.ToolTier` gates its `Tools`; a crafter whose
    tool is elsewhere fetches it (`WorkGivers.Craft`). Weapons and armor rank by `ThingDef.Tier`
    (`GearTier`), which the storyteller matches raiders to (`RaiderKindDef.Tier`, `Mining.VillageGearTier`). People carry tools
    on a belt (`Systems/Toolbelt`, 4 slots): items `Stored` with the creature itself as the container,
    so they're never `IsAvailable`, follow `LocationOf`, drop on death and leave with `Depart`.
    `GiveStartingPack` also hangs a stone axe and pick on it; `FindTool` fetches better ones.
  - Mining (`Systems/Mining.cs`, docs/plans/mining-progression.md): `BlockDef.DigTier` is the
    lowest pick tier that breaks a block (`Mining.BreakProblem`, enforced in `DigJob`, the Dig
    finder and the avatar's dig; better picks get `Mining.DigFactor`'s bonus; tier 0 digs as it
    always did). `MapGenerator.PlaceStrata` lays the rock kinds and `PlaceVeins` the `VeinDef`s
    (each with its own salt), both hash-based and clear of the landing. Ore (`BlockDef.Ore`) is
    hidden until a face is open: `Mining.ShownBlock` poses it as a neighboring rock, derived from
    the blocks (nothing fills a dug cell, so it needs no state); everything shown on screen or in
    text reads blocks through it. Digging ore (or `HarvestProps.Bonus`/`BlockDef.DigBonus` extras,
    hashed per cell, never `sim.Rand`) teaches its `handle:` recipes.
    Fuel: `FuelProps.Also` lists other fuels (coal), `Fueled.Loaded` the one put in; `reach` is
    the heat a station holds on each (OreTests checks them) and haulers pick by it
    (`Crafting.HeatToMake`). Bellows beside a station (`BuildingProps.Bellows`) are pumped by the
    smith inside `CraftJob` (`Blown`), lifting it to iron and steel heat on coal.
    Treasure (`ThingDef.HoardValue`, `Systems/Hoard.cs`): dragons sleep happily on it, a keeper's gift
    of it raises affinity (`AvatarActionKind.Gift`), and it adds to raid points. Jewelry is worn in
    the Neck slot. Save format 14 (breeds and working elements; 10 added `DragonCare`).
  - Apparel (`Apparel` component, `ThingDef.Apparel`): go through `Simulation.Wear/WearItem/TakeOff/Worn`.
    Clothes are apparel too (`Systems/Clothing.cs`, `data/defs/outfits.json`): Tunic, Legs, Feet and
    Cloak slots under the armor slots, cloth ones dyed by their material (`materials` of `Cloth`).
    `OutfitDef`s dress villagers (`Clothing.DressVillager`, rolled from the person's own stream),
    raiders (`RaiderKindDef.Outfit`), story people and the player (`Hero.Clothes`,
    `CreateCharacterCommand.Clothes`, `"Def:Dye"` strings), and are the creator's presets. Starts
    dress people *after* their other spawns, so the new entities don't shift rolls seeded by entity
    index (skills, traits, a dragon's care stream).
    The player's standing gear orders live in `Outfit` (slots kept bare, an item to put on), set by
    `GearOrderCommand`/`WearCommand`; `HumanBrain.FindGear` honours them.
    `CombatRules.ArmorAt` gives a part's armor (dragon scales or the best covering item); attacks pick
    the part first, then roll the deflect.
  - Injuries carry tend `Quality`, an optional `Infection` and `PeakDamage`; healed deep wounds may
    become `Health.Scars`, which count in `PartDamage`.
  - Prisoners (`Systems/Prisoners.cs`): `Faction.Prisoner` is nobody's enemy. `CaptureOrder` (player) →
    Doctor `CaptureJob` → `Prisoners.Imprison` (prisoner `BedOwner`, bed tagged `ForPrisoners`).
    Doctors also tend downed raiders once `Combat` is over. Warden work feeds, recruits and releases.
    Prisoners have moods; below `Prisoners.BreakBelow` `PrisonBreakSystem` may roll a break
    (`Escaping`: a raider again until recaptured). It only rolls for prisoners with a chance.
    New thoughts go at the end of `mood.json`'s list: def indexes feed `StateHash`.
  - Mood (`Systems/MoodSystem.cs`, `Components.Mood`, thoughts in `data/defs/mood.json`): humans and
    dragons ease toward 50 plus their thoughts. Situational thoughts (`when`) are re-checked each update
    and not saved; memories go in only through `MoodSystem.Remember`, and code names them via
    `sim.Defs.Thoughts` (`KnownThoughts`: field name = def name, null if a def set lacks it).
    `MoodRules` holds the effects: human work speed in `Job.Work` (not the avatar), dragon obedience.
    Opinions live in the same memories: a memory with `Other` set and a def `opinion` moves what
    its owner thinks of them (`MoodRules.Opinion`, plus a stateless per-pair `Compatibility`);
    friend/rival are derived from it, never stored.
  - Social (`Systems/SocialSystem.cs`, `sim.Social`): people near each other (same room or open
    air) start `interactions` from `mood.json` that leave memories on both sides. It draws only
    from `sim.Social.Rand` (saved, always in `StateHash`), so chatter never shifts `sim.Rand`.
    `Social.Recent` is render-only (speech bubbles, the inspector's Social tab).
  - Traits (`Components.Traits`, `Systems/TraitRules.cs`, `traits` in `mood.json`): rolled per
    entity from their own seeded stream like skills; `TraitRules.Factor` scales a thought's mood
    and opinion and an interaction's weight. Companion requests roll `Requests.Willingness`
    (mood, opinion of the asker); a refusal is a one-hour `TurnedYouDown` memory, not extra state.
  - `sim.Storyteller` (`Systems/Storyteller.cs`) is **off by default** (null difficulty): tests, the
    bench and demos stay scripted. It uses its own `SimRandom`, and `StateHash` only includes it when on.
    Raiders are spawned through `Combat/Raids.cs` from `raiderKinds` in `data/defs/storyteller.json`.
    Wild events have a timer of their own (`NextEventTick`, `DifficultyDef.EventsMeanDays`, save
    format 17): a predator pack (not on Peaceful or in the grace days), a migrating herd
    (`Migrating`: crosses to the far edge and leaves) or a wild dragon `Visitor` (wild dragons do no
    village work; fed `VisitorSystem.MealsToStay` meals from the stores, it joins the village).
  - Co-op (`Systems/Players.cs`, `Core/CommandRules.cs`; game side `Net/CoopSession.cs` over
    Narsil's lockstep, see `external/narsil/docs/NETWORKING.md`): `PlayerCharacter { PlayerId }`
    stays on a player's character for good, `Avatar` only while they're connected
    (`Players.Join/Leave`, run as lockstep turn events), so leaving makes them a villager.
    `CreateCharacterCommand` spawns a newcomer by the wagon with an egg whose `Egg.Keeper` is
    them (it hatches to them). Players keep reusable characters game-side (`Net/CharacterRoster`,
    `characters.json`; `Ui/CharacterPicker` before the creators when hosting or joining, `--character`):
    only name, look, traits and the dragon's name and look carry over. Commands declare `IActorCommand` (performed as that character),
    `ISubjectCommand` (aimed at a creature: not another player's, nor their dragon),
    `IPlayerCommand` or `IDevCommand` (host only); `CommandRules.Refusal` checks them on every
    peer. A join's barrier is `AiSystem.InterruptAll` and its snapshot a save: the same contract
    as `SaveLoadTests`. The checksum is `StateHash(withMap: false)`.
    The handshake's `CoopSession.BuildTag` hashes the game assemblies and defs, so alpha packages
    (`tools/package.sh`) build every platform in one run and refuse DLLs that differ between them
    (no ReadyToRun, embedded pdbs, one intermediate folder, `PlatformTarget=AnyCPU`). The Mac
    package is a `DragonWorld.app` carrying MoltenVK as `libvulkan.1.dylib` (pinned, downloaded
    into `.cache/`; Narsil's `Platform/MacVulkan`); CI ad-hoc signs the bundle on a Mac runner. Resyncs leave `desync-*` saves in
    `BuildInfo.LogDirectory`, next to the file log Narsil's `Log.Initialize` writes. Online co-op goes through a relay (`Net/CoopRelay`: the Network setting or `--relay`): the host
    also opens a `RelayHost` (join code in the Players window) and the Join box takes a code. If an
    online host vanishes, the first player in line takes over (`CoopSession.BecameHost`). Fights run
    in lockstep: `CoopSession.Step` calls `Encounter.Advance` instead of ticking (at normal speed, whatever
    the host's), each player ends their own turn (`CombatEndTurnCommand`) and the village's phase passes
    once every player's units are done. A fight in progress is in snapshots (`SaveGame.Snapshot`,
    format 14: `CoopSession.Barrier` first plays out a move under way, since movers' paths aren't
    saved) and in `StateHash` (`Encounter.Fingerprint`, only while one is on), so players can
    join and resync mid-fight. Raids come as in single player (the host picks a difficulty;
    `--host --difficulty`); the Debug panel's Send raiders (`DevRaidCommand`) starts one on demand.
  - Saving (`Saving/SaveGame.cs`): every component needs a hand-written codec in
    `Saving/ComponentCodecs.cs` (saving throws otherwise, and a test checks the Components
    namespace). State a system keeps outside components and the map, including caches the sim
    acts on before they refresh, implements `ISaveState`. Jobs aren't saved:
    a load equals `AiSystem.InterruptAll` on the original (`ReleasePawn` is everything ending a job
    does to the world, so put new job side effects there, not in `OnEnd`). `SaveLoadTests` compares
    hashes for hours after a load; when it fails, find the first diverging tick and the state behind it.
    Player saves refuse a fight (`SaveGame.CannotSave`); the game autosaves when `Encounter.StartsNextTick`.
    Only co-op snapshots (`SaveGame.Snapshot`) carry one.
    `SaveGame.Capture` serializes on the sim's thread; `Image.Encode` (Brotli) only touches those
    bytes, so autosaves compress and write in the background (`SaveSlots.WriteInBackground`).
- `external/narsil` — the **Narsil** engine, a git submodule shared with other games (own repo, own
  CLAUDE.md): window/input/loop (`GameHost`), Vulkan core, `SpriteBatch`, `TextureAtlas`, cameras,
  ImGui (`ImGuiLayer`, `ImGuiText`). It also ships the sprite shaders and the BRUTAL logo into
  `data/`. Keep it game-agnostic: DragonWorld-only rendering stays in `src/DragonWorld`. Engine
  changes are committed in the submodule first, then the submodule pointer here.
  Game code only touches `QuadRenderer`, `Camera3D`, `SpriteBatch` (screen-space HUD, render test),
  `TextureAtlas` and ImGui, never raw Vulkan. GPU memory comes from Narsil's VMA allocator
  (`MappedBuffer`, `GpuImage`); release anything a frame in flight may still read with
  `Host.Renderer.Defer(...)`, and record per-frame uploads in `Game.PrepareFrame` (before the
  render pass) rather than `Host.Upload`, which blocks.
  - The 3D view is all quads (Narsil's `Narsil.Voxel/QuadRenderer.cs`, `quad.vert`, shared with
    Lifeline; DragonWorld's own side is `WorldMesher` and `WorldRenderer`): 64-byte instances expanded from `gl_VertexIndex`, like sprites.
    Static chunk meshes (`QuadMesh`) draw first, then dynamic opaque (alpha-tested) and translucent quads.
    `Camera3D` is orthographic and passes basis vectors, not a matrix, as push constants.
  - Levels look twice as tall as cells are wide (`Rendering/WorldScale`, `Camera3D.VerticalScale`):
    presentation only, world coordinates are untouched. `Camera3D.Up`/`Forward` are already the world
    vectors for camera-facing sprites and rays; vertical faces stack their art (`WorldScale.Tile`) so
    pixels stay square. Offsets measured against a sprite (a bubble over a head, a held item, particle
    heights) go through `WorldScale.Rise`; offsets against blocks (a bar over a wall) stay in world z.
  - Cutaway is a push constant: quads carry their cell level in `Quad.Flags` and hide above it; buried top
    faces are emitted as `Quad.Cap` and only show exactly at the cut, so moving the cut never remeshes.
  - HD-2D look: the world draws into Narsil's post-processing scene pass (`DrawsScene`/`DrawScene`
    in `VillageGame` and `TitleGame`; `QuadRenderer` is built against `Host.Post.ScenePass`) and is
    composited with `Rendering/VisualStyle` settings (tilt-shift, bloom, vignette, warmth; F10 toggles,
    Options → Interface tunes). `Quad.Glow` quads (fire, embers) are boosted over 1 so only they bloom.
  - Box models (`ThingDef.Model`, `Rendering/PropModel.cs`): furniture is meshed into chunks by
    `WorldMesher`, item piles and blueprint ghosts are drawn per frame via `WorldRenderer.AddModel`.
    `Thing.Facing` (save format 3) turns them; it's presentation-only and not in `StateHash`.
  - Standing sprites (creatures, plants, items, effects) carry `Quad.Upright`: `quad.vert` gives the
    camera-facing quad the depth of an upright card at its feet (bottom middle; AxisV must point down
    the sprite), so a tall sprite (hair included) doesn't sink into a wall behind or beside it.
    Trimmed look layers (eyes, clothes) add `Quad.FeetInAo` (Ao holds how far below the quad the
    feet are) so they sort at their figure's depth, and step forward by `LayerStep` so they don't z-fight. Slope tops use `Quad.Billboard` only
    (lighting), not `Upright`.
  - Doors aren't in the chunk meshes: `Rendering/DoorAnimator` draws them per frame, swinging open
    (away from whoever pushes) while a creature is in the doorway or stepping into it. Render-only.
    Doors with `lockable` can be locked (`Simulation.SetLocked`, the `Locked` component: impassable,
    paths and regions rebuild); the animator shows them shut with a padlock, or standing open.
  - Night lighting samples a per-frame-slot 3D light volume uploaded from `Map.Light` (`WorldRenderer.UpdateLight`,
    called from `PrepareFrame`). `QuadRenderer` owns every `QuadMesh` it creates and frees them on `Dispose`.
- `src/DragonWorld` — the game: `TitleGame` (main menu over a live valley; switches games with `Host.SwitchTo`), `VillageGame` (tick loop, tools, ray picking, input), `VillageGame.Ui.cs` (panels),
  `WorldRenderer` (reads sim state, draws), `WorldMesher` (chunk meshes from blocks, walls, ramps),
  `Debug/` (bench, render stress test, art preview).
  - `Rendering/CreatureAnimator` picks an animation tag per creature from sim state (job type,
    `Job.Focus`, `BreathEvent.Source`, mover steps) and plays it on an animation clock: real time
    × game speed, or real time in combat. Riders play `ride` (astride, facing the way the mount
    goes; `WorldRenderer.RideSeat` lowers them so the hips sit on the saddle point; sheets without
    it sit idle). People's sheets are four-way (`walk_down/_up/_side`,
    chosen by `CreatureAnimator.ViewOf`/`Directional`); dragons and animals are side-on. Sheets (`SpriteSheet`, PNG + `.anim.json`) are drawn at
    native size (32 px = 1 cell, divided by a sheet's `density`: 4 for art at four times the detail, box-filtered when shrunk via `Quad.Smooth`) around their pivot and flipped for facing. `Particles` is a
    render-only pool for dust/embers/Zzz with its own `System.Random`, which is fine because it never touches the sim.
  - Screen space: the camera's `ViewportSize` is in window units (cursor and ImGui units); the
    framebuffer is `Host.FramebufferScale` times larger (1.25 on this machine).
  - Smooth motion: draw creatures at `WorldRenderer.DrawPos`, never raw `Position.Pos`.
    `Rendering/Interpolation` captures positions before every tick (single player, and co-op via
    `CoopSession.BeforeStep`) and lerps by the leftover tick fraction (`Narsil`'s `StepFraction`
    in co-op): the picture is one tick behind but doesn't stutter. The follow camera runs after
    the frame's ticks. In co-op the local avatar is drawn by `Net/AvatarPredictor`, which walks
    ahead of the round trip and sends turns and stops with the cell they happened on
    (`AvatarMoveCommand.At`, held in `Avatar.HasPending` until the sim gets there).
  - Profiling: `Simulation.SystemPeaks` (slowest run per system, plus "Derived" for the
    path/region/room rebuild), the Debug panel's 5 s peaks, the menu bar's frame-time tooltip
    (`Host.FrameTimeMaxMs`, hitches). The co-op host runs Narsil's pump thread (`StartPump`);
    clients show their turn buffer and jitter in the Players window.
- `data/defs/*.json` — all content. Files merge in name order, and a later def with the same name overrides
  an earlier one. String references are resolved and validated at load (`Defs/DefDatabase.cs`).

## Rules for sim changes

- **Determinism:** all randomness goes through `sim.Rand` (`SimRandom`). Never use `System.Random`, `DateTime`,
  or iteration over unordered collections where order affects state (sort first, as `FireSystem` does).
  Trig and powers go through Narsil's `DetMath` (`Sin`, `Cos`, `Tan`, `Atan2`, `PowInt`), never `MathF`'s:
  those call the C runtime, which can round differently per OS and CPU, and co-op peers on different
  platforms must agree to the bit (`DetMathTests` scans the sim's source; Narsil tests its accuracy). `Sqrt`, `Round`, `Floor` are fine.
  `DeterminismTests` compares `StateHash()` across runs. `StateHash` is hand-picked fields and
  is the fingerprint refactors are checked against, so leave it alone; `FullHash()` hashes
  everything a save holds (through the codecs: `SaveGame.Hash`) and is what `SaveLoadTests` and
  `DeterminismTests` also compare, so a field a save forgets or a peer computes differently shows. `--bench` prints the hash too: a pure
  refactor (performance, cleanup) must leave it unchanged for `--seed 1234` and `--seed 99`.
- Gameplay changes should be checked against `SliceScenarioTests`, which plays the whole vertical slice headless
  on two seeds. Read its message log when it fails: failures have usually been real emergent AI problems
  (e.g. starved work types, villagers eating the dragon's meat) rather than test bugs. Don't loosen
  assertions just to pass; decide whether the outcome is legitimate emergent behavior first.
- Rates in defs are per game day/hour. Systems convert them using their `Interval`, so keep the comments
  in `data/defs/core.json` accurate when changing units.
- Heat balance was tuned by tests in `EnvironmentTests` (insulation ~0.9+, campfire 900°C/h capped at 50°C,
  floors 0.92, heat rises 0.5 / sinks 0.15 of horizontal conductance). Re-run them after touching
  temperature, insulation or heat values. Keep `TemperatureSystem` within its stability bound (see `Conductance`).
- Creatures judge cold by what they feel (`Environment/Warmth`: the air plus `radiantWarmth` from a lit
  fire within 3 tiles, same room or both outdoors) against `Creature.Tolerance` (a `LifeStage` may
  override the def's comfort/safe minimums: adult dragons sleep out by a fire). Eggs, food and crafting
  use the bare air. Sleeping below comfort restores rest slower (`NeedsSystem.ColdSleep`).
- Breath (`Environment/Breath.cs`) is a cone of `Map.Raycast` rays jittered with `sim.Rand`; it ignites
  along rays, stops at rock/walls and splashes on impact. `Simulation.RecentBreaths` is render-only, not state.
  Fireproof creatures (SafeTempMax ≥ 300) don't treat flames as an emergency. Hatchling sneezes use
  `BreathShape.SparesBuildings`: they never ignite structures, floors or indoor rooms.
  Other elements share the cone and differ per cell (`ElementDef.Effect`): its fire branch must stay
  byte-identical (the bench hash is the check), so keep the `sim.Rand` call order and leave
  `BreatheFireJob`/`StokeJob` named as they are (`StateHash` mixes job type names).
- Fire climbs (up ×2, diagonally up ×1, down ×0.3) and will burn through wooden floors above it; smoke
  (`SmokeSystem`, sparse, per-hour rates) is trapped by floors, rock, walls and doors.

## BRUTAL gotchas (there are no public docs)

The general ones (learning the API, render pass, UNORM swapchain, ImString arena, printf-format ImGui
calls, `ref` overloads, Wayland FIFO pacing) are in `external/narsil/docs/BRUTAL-NOTES.md`. Read it
before touching rendering or ImGui code. DragonWorld-specific:

- Read text boxes with `Narsil.Ui.ImGuiText.Value(input)`, never `ImInputString.ToString()`: the
  string caches its length, so typed text gets cut to the starting length (see BRUTAL-NOTES).
- Use the escaping helpers in `VillageGame.Ui.cs` (`TextC`, `Tip`, `Wrapped`, `Bullet`, wrapping
  `Narsil.Ui.ImGuiText`) for colored/wrapped/bullet/tooltip text; plain `ImGui.Text` is safe.
- UI look lives in `Ui/Theme.cs` (colours, rounding, `Theme.Header`); fonts are Fira Sans from `data/fonts`
  (OFL, shipped as `licenses/FiraSans-OFL.txt`). Atlas sprites show in ImGui through `Host.AtlasImage(sprite)`
  (its page's texture and UVs on that page) (toolbar icons, portraits).
- License (BRUTAL License v1.0, applies to all distributed copies, commercial or not): logo splash before gameplay
  (`GameHost.ShowSplash` in Narsil, must stay unaltered except scaling and can't be skipped in Release), copyright in the
  Credits window, `NOTICE.txt` and `licenses/` (copied by `external/narsil/build/Narsil.Licensing.targets`,
  imported in `src/DragonWorld/DragonWorld.csproj`). Don't remove any of these. See README "Credits & licensing".

## Style

- Match the surrounding code: file-scoped namespaces, `readonly`/`record struct` for small value types,
  XML doc summaries on public types explaining *why*, brief comments otherwise.
- Prefer adding content through defs JSON over hard-coding. Add a test for new sim behavior, and a scenario
  assertion if it changes the slice loop.
- The build is warning-free; keep it that way. The usual ones: BRUTALCORE0011 wants `"..."u8`
  for literal ImGui labels and formats, CS9191 wants `in` (not `ref`) for ImGui's size/colour arguments.
