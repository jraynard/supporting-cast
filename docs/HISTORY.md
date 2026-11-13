# History

What's been built, milestone by milestone, newest last. What's next is in [ROADMAP.md](ROADMAP.md);
design intent in [DESIGN.md](DESIGN.md).

### M0: Fork and strip (2026-11-10)
- Imported DragonWorld at `cf61dfc` without its history; Narsil at `7b6ac5a`.
- Removed dragon raising, bonding, breeding, riding, elements, the story starts and turn-based
  combat, with their UI, defs and tests. Saves reset to format 1.
- `Scenarios/VillageRound`: a generated town with a longhouse, smithy, clinic (a healer's shrine since M2) and dwellings, six AI
  villagers, and players walking in as avatars, alone or in co-op.
- Raiders now strike in real time (`AssaultJob`); before, turn-based combat did it for them.
- `VillageDayScenarioTests` replaces the dragon slice.
- Bench baselines: seed 1234 state `274EE1FDFF381E51`, full `3117DAE617562E37`; seed 99 state
  `5984BD546BB34042`, full `C3F30EEDDF3BEE6F`.

### M1: Being someone (2026-11-10)
- `Round/`: a lobby in the sim (the session stands paused until the deal), trades from
  `data/defs/trades.json` dealt from player preferences. Players inhabit AI villagers, so the cast
  stays fixed; a late joiner takes over a villager and keeps its face. Global chat is lobby only.
- `Perception/`: every read of other characters and items goes through a `PerceptionView`.
  `PerceptionBoundaryTests` fails on a new direct read of the creature list.
- `Speech/`: say, whisper, shout, resolved when spoken from position and rooms, never light.
- `Work/`: Lifeline's framework, reworked so a session is a pure state machine a client runs
  ahead of the sim (`WorkPredictor`). `HammerSession` is its first game, at the forge, in a round.
- `Bots/BotPlayer`: plays through commands and `Perception`. `--bots N` for solo playtests.
- Bench baselines: seed 1234 state `274EE1FDFF381E51`, full `AE4E1F3EDB115358`; seed 99 state
  `5984BD546BB34042`, full `4A0A22D943D107A0`. State hashes are M0's; the full hashes moved only
  because `Round` and `Speech` add saved system state.

### M2: The first party (2026-11-11)
- `Adventurers/`: parties arrive during a round, road-worn, with silver in their packs. They're
  neutral, wait where their orders are served, and move on.
- `Shops/`: every adventurer's want is an order asked aloud, so only those who hear it know of it.
  Each shop has a price sign its keeper sets (the Shop panel, J). An adventurer orders from the
  cheapest shop whose price is within what the service is worth to them (rolled per want, times
  their kind's spend and their need) and what they can spare; too dear, they say so, sulk a little
  and go without for a while. A bargain pleases them more. Customers line up at the shop (each
  building def's "line" cells: the inn's bar, the smithy's and woodshop's open fronts, inside the
  shrine) and say what they want when they reach the front; keepers serve front first, and someone
  who's handed over a blade or a box steps out of the line until it comes back. Bots keep their
  sign as a simple market: a price called too dear drops, a long line puts it up. Goods: a keeper
  puts gear lying in their shop up for sale at a price (the item inspector); an adventurer who'd
  be better off with a piece (a tier up on their weapon of the same kind, or armor for a bare or
  lesser slot) and thinks it worth the price lines up and buys it, keeping what it replaces.
  Villagers leave what's for sale alone; an AI keeper sells what's on show. Coin is real silver that changes hands. `Favours/`: players post, take, finish and call off
  favours for each other.
- The trades, each with its own hands-on game and an AI fallback (the Quest Broker started as a
  fourth and was folded into the Innkeeper: the keeper hears the rumours anyway):
  - **Innkeeper:** pour ale at the cask (head and tilt); customers taste the pour.
  - **Blacksmith:** weapons dull and chip. Sharpen on a whetstone, hammer chips out at the forge
    (which flattens the edge), and judge which a blade needs.
  - **Healer, an empath:** draws wounds into themselves against their pain and knows them only by
    feel. They cast the mending that fits and meditate their mana back. They work from a shrine
    (it replaced the clinic) with pallets for the wounded, a meditation stone that steadies the
    breath and gathers more mana, and a chest of bandages for when the mana runs dry.
  - **Innkeeper as quest broker:** matches parties to quests on intel built from a rough guess,
    rumours over the bar and returning parties' reports.
  - **Carpenter, the lockwork first:** a party that clears a quest brings back a strongbox with
    part of the reward locked in it, often trapped. Feel along the seam for the trap and ease its
    spring out, then pick the lock pin by pin; a trap missed goes off as the lid comes up. The
    town has a woodshop for it.
- `Expeditions/`: parties go off the map as records. Their strength (gear upkeep, wounds, food,
  mood) against a quest's hidden danger decides deaths, wounds, wear and loot, and survivors
  come back changed.
- `Bots/BotPlayer` plays all four trades. `PartyLoopScenarioTests` is the exit: bots serve a
  party, send it out, it comes back, and what it brought back gets opened.
- Fixed on the way: AI packs dropped their coin at every job's end (and their trade's tools,
  which now start on the belt), and an item in your own pack hid its condition from you.
- Bench baselines:
  - seed 1234: state `8C73C48227F3E496`, full `71517EF6742999F6`
  - seed 99: state `285714EBDFEB4418`, full `5C666264F50686B2`

  The state hashes moved only for the round's town: the longhouse's cask, the shrine in place of
  the clinic, the woodshop, and kit tools dealt onto the belt.

### The village: Millbrook (2026-11-11)
- Rounds are played on a hand-made map instead of DragonWorld's generated valley. `MapDef`
  (`data/defs/maps/`) holds text grids of ground, levels and features, the buildings with exact
  anchors, the landing, the stockpile and the livestock. `Maps/MapBuilder` stamps it in, with rock
  and ore still underground. Size a round's sim with `VillageRound.NewSimulation`. `--map NAME`
  picks another map.
- **Millbrook** (96×96) is traced from `art/reference/village-map2.webp`, one battlemap square to
  two cells, with a 12-cell band of woods round it. It has a square round a well, lanes out north,
  east, west and south-west, a river with a stone bridge, a lake, a pig pen, fenced cabbage
  gardens, and thirteen buildings. `tools/village_map.py` writes it; `--preview` draws it beside
  the reference. Edit the script rather than the JSON.
- Buildings can have a second storey (`upperLayout`, stairs `S`, phases `UpperFloor` and
  `UpperWalls`). Saves are format 2 and convert format 1's build phases.
- The **Inn** is the Innkeeper's workplace: a common room with a bar counter downstairs, and rooms
  to let upstairs. **Lodging:** adventurers staying the night ask for a room, and whoever takes it
  on walks up to them to let one of the inn's beds (`LetsBeds`). Nobody else sleeps there.
- The half-timbered look (`timber_plaster`, `timber_shingle`) is now the default variant of the
  village's buildings, with the Norse looks kept as alternatives. New buildings: Cottage and Barn.
  New content (`data/defs/village.json`):
  - packed-earth lanes and tilled soil
  - cabbages
  - the well, barrels and crates
  - the stone bridge
  - fences with rails
  - penned pigs
- The art comes from `tools/village_art.py`, which writes only its own files.
- Bench baselines:
  - seed 1234: state `5BA0216B86F0F306`, full `AAB07A6C746B1A2D`
  - seed 99: state `EE89C4B72A134C2D`, full `C33639D6C1427D2A`

  These are a new town, so every hash moved. Before the switch, lifting the generator's helpers
  for the builder left M2's hashes unchanged. Ticks and saves are cheaper on the smaller map.

### UX pass (2026-11-11)
- **Shared kit:** UI that DragonWorld and this game copied between them now lives in
  Narsil.Ui (theme, panels, anchored HUD, toasts, credits, sprites and nine-slices, bars, key
  caps). DragonWorld uses it too, with its look unchanged.
- **Carved wood and iron:** oak-plank panels in an iron rim, carved title plates, dark wells, an
  iron bar trough, key caps.
- **An anchored HUD:**
  - **Where things sit:** a character card (top left), the clock and a journal strip (top right).
    The journal panels (errands J, heard T, quests K, gift G, log L) stack under the clock.
    Toasts sit top centre; the hotbar, the build drawer (B) and the work window bottom centre;
    the inspector bottom left.
  - **Rules:** nothing overlaps, everything follows the UI scale.
  - **Controls card (H):** replaces the long hint lines.
- **Speed and dev tools:** players have no speed controls. Dev tools are for Debug builds or `--dev`.
- **The hammering game as a scene:** the anvil, a bar glowing as it cools, a hammer whose swing
  is the timing cue, sparks and a word for each blow.
- **Errand rows as cards:** an icon, the price, and time left as a bar.
- **Speech plates:** said lines get a wrapped plate over the speaker's head.

### Control pass (2026-11-11)
- **Solo round:** pick your trade after making your character (or let the deal pick).
- **The view follows your floor:** under an upper storey the cutaway drops to your level;
  `[`/`]` take it over by hand, Home hands it back. Nothing below a drawn floor shows through
  it (sprites, bubbles, labels); a ring on the floor marks you when you're hidden.
- **Click to move:** clicks land on the floor as drawn (they fell cells short indoors), never
  select yourself, interact only when pointing at a thing, pick tall sprites by their art, and
  pin their cell unless dragged. Unreachable clicks go as near as they can, or say so. Rings show
  the hovered cell and where you're heading. Switching walks never snaps you back.
- **WASD:** a blocked diagonal looks ahead and slides the way that gets on (into doorways,
  round corners, up stairs); facing is the step taken; two-key presses and releases a frame
  apart no longer start stray steps; the keys don't pan the camera.
- **Solid furniture:** tables, the bar, casks, crates, chests, benches, the hearth and the well
  are walked round. Tables and the bar can be climbed (F); any step gets you down. Nothing is
  put down on solid furniture. `BuildingDef.LayoutProblem` (tested over every preset) keeps
  layouts walkable.
- **Roomier buildings:** inn 13x9 (aisle to the bar, three rooms upstairs), smithy and woodshop
  7x5. Bots carry mended pieces and opened strongboxes back to customers wherever they are.

### Blacksmith polish (2026-11-11)
- **Forging a heat at a time** (`Work/Smithing`):
  - **The piece:** a `WorkPiece` in the smith's tongs.
  - **The fire** (`HeatSession`): bellows on F, draw it out with D, heat read only as colour.
  - **The anvil** (`HammerSession`): the hammer's timing game, now against a cooling piece.
  - **The quench** (`QuenchSession`): lift it as the steam dies to a hiss.
  - **The edge:** grind it on the grindstone.
  - **Flaws:** hammering cold cracks, a long quench leaves it brittle, a short one soft, and
    burning costs grades or the piece. The flaw is hidden (`Flawed`) and tells in use.
  - **Mending:** the same stations.
  - **Bots** play the whole loop, feeding the forge from the bin when it goes out.
- **The smithy, furnished:** an anvil, a quench tub, a grindstone, both bellows, a tool rack, and
  a fuel bin stocked with charcoal (`tools/norse_smithy.py`). Smithy roles are a building def
  field (`smithy`).
- **The forge window:**
  - Recipes are grouped by metal, with one Forge button that brings the materials and puts the
    billet in your tongs.
  - Each recipe's tooltip gives its working colours and number of blows.
  - The fire is shown by its colour.
  - Customers' pieces come first under Mend.
  - Between steps, an "In your tongs" panel shows where the piece goes next.
- **The hammer strikes with its head**, and the smith swings in the world while working.
- **After the first playtest:**
  - **The forge window** is pick a metal, then a shape (icons), then Begin. It closes once the
    billet is in your tongs. Smelting and mending fold away.
  - **The fire** is shown in the "In your tongs" panel instead.
  - **F** ignores what's behind a window, and next to the smithy's stations it picks the right
    next step.
  - **More patience:** customers give four hours from the hand-over. While their piece is still
    with you they ask after it and wait, up to three times their patience. They put off leaving
    town for it. Setting out on a quest, they leave it in your keeping instead of crying thief.

### Innkeeper polish (2026-11-12)
- **The inn grows a kitchen:** 13x13 now, with a kitchen (its own fire, the stew pot, a larder,
  the wash tub, a shelf of bowls) and a storeroom of kegs behind the common room, and six rooms
  upstairs. Cottage H moved two cells east to make room. The inn opens stocked, and the carter
  restocks it each morning (`InnSupplySystem`).
- **The pot** (`Work/Cooking`): whatever's in the larder goes in.
  - **Tending it** (`StewSession`): F stirs, A/D bank or feed the fire. The heat shows only as
    the surface (still, steaming, a simmer, a rolling boil). Past a simmer it catches unless
    it's stirred.
  - **Seasoning it** (`SeasonSession`): a pinch at a time, tasting as you go; a less practised
    tongue tells less. Salt can't come back out.
  - **Serving it:** a pot ladles many bowls, each with a quality the diner tastes. A turned
    ingredient spoils it unseen.
- **Crockery** (`Work/Vessels`): drinks go in clean tankards and stew in clean bowls. Customers
  leave them dirty where they finished.
  - **Washing up** (`WashSession`): at the tub, on the brush's beat.
  - **Changing the keg** (`TapSession`): a dry cask takes a keg from the storeroom, its tap
    knocked in as the mallet comes down. A miss sprays servings away.
- **Rooms by the night** (`Hospitality/Lodging`): a guest pays each evening and keeps their room
  while in town. They check out when they move on or leave a night unpaid. The bed's left unmade
  until it's made up again, three movements on cue (`MakeBedSession`).
- **Mess** (`Hospitality/MessSystem`): spilled ale, mud, crumbs and blood on the floor, drawn as
  decals.
  - **Its cost:** customers in a messy room think less of the town, and say so.
  - **Cleaning it** (`ScrubSession`): A/D back and forth, faster with the mop now in the kit.
  - **Blood** shows only as a dark stain, and scrubbed, it's gone.
- **Mixed drinks** (`Work/MixSession`): at the bar, a press-on-cue sequence (F, A, D).
  - **What goes in:** a serving from the cask, one of each add, and a clean tankard.
  - **Named drinks:** the hearthwarmer, bramblecup and stillwater have their own cues.
  - **House mixes:** anything else, one cue per add.
  - **What a drink does** is a property of the drink (`DrinkMix`): the sum of what's in it.
- **Drink taking hold** (`Hospitality/Drinking`): drink goes to the head and wears off.
  - **Merry:** drinkers volunteer truer rumours.
  - **Loud:** their speech carries further.
  - **Drunk:** they spill.
  - **Past it:** they sleep.
  - **Named orders:** customers sometimes ask for a named drink, and care whether they get it.
- **Directions** (`Hospitality/Directions`): adventurers know their haunt and the shops their
  party has seen. For the rest they ask aloud. Anyone who hears can point the way (the Shop
  panel), a villager does if nobody else has, and everyone in earshot learns it. A party back
  from a quest knows the town.
- **The inn has a name:** "The Wild Bear" (`BuildingDef.DefaultName`). Its keeper can paint a
  new one over the door from the Shop panel (`ShopName`). Customers line up at it, guests are
  welcomed to it, and directions name it.
- **AI keepers** fill and tend the pot, ladle from it, wash up, change kegs, make beds and mop.
  **Bots** play every new mini-game, answer the way, and mix the named drinks they hear asked for.
- **Fixed on the way:**
  - Jobs reach what's kept in solid furniture from beside it (`Navigation.EndFor`); AI fetches
    from chests, shelves and larders hadn't worked since furniture went solid.
  - Haulers leave the inn's crockery be.
  - A creature leaving the map takes its pack's contents with it; they used to stay stored in a
    pack that was gone.
  - A party back with a strongbox waits to have it opened, and counts what's in the box toward
    paying for it.
  - Some entity `FirstOrDefault` calls fell back to entity 0 rather than `Entity.Null`.
- Bench baselines:
  - seed 1234: state `CDBB67F795633E74`, full `025DF195DDF4B771`
  - seed 99: state `A42B93417320CCC2`, full `1465D1BB7BA083D6`

  Every hash moved: the inn and the cottage beside it changed, and the inn is stocked. They
  moved again when berries went on the bar shelf.

### Healer polish (2026-11-12)
- **Feeling them over** (`Healing/FeelSession`): touch is the diagnosis.
  - **The sweep:** A/D run the hands head to feet, and lingering near a wound makes it out.
  - **Felt wounds only:** only a wound this empath has felt is listed or can be drawn
    (`Injury.FeltBy`).
  - **Pressing in** (F) finds festering under a dressing.
- **Each wound draws its own way** (`drawFeels` in `healing.json`): cuts jolt, bites tear twice,
  bruises swell and ebb, burns pull one way, frost numbs the steering, venom grows. Drawing from
  a patient on a pallet is calmer.
- **Weaving runes** (`CastSession`, `Healing/Weaving`): six runes keyed 1–6 on the healer's
  heartbeat, F to let it go.
  - **The mix is the mending**, in any order. Extra lead runes make it stronger for more mana.
    A mix that makes nothing fizzles.
  - **Pain** makes the heart race and stumble.
- **The book** (`Spellbook`):
  - The common mendings are known at the deal (the gift window's Book tab).
  - Searing, Steadying and Grounding are found by weaving them, then written in.
  - Steadying widens the next draws' band for an hour.
  - Grounding heals lightning burns, which had no mending before.
- **Binding by hand** (`Healing/BandageSession`): replaces the timed bandage job.
  - **The wrap:** for each turn of cloth, hold F to pull it tight and let go in the band. How
    well the turns sat, with Medicine, is the dressing's quality.
  - **Supplies:** bandages come from the hands, the pack or a chest in reach (the shrine's).
  - **Cloth** tears into 3 bandages.
- **Pallets first:** the shrine lines its patients up on its pallets (`lineOnBeds`), and they
  lie there awake (`Job.LiesDown`).
- **Charms** (`Healing/Charms`): the healer's work before anyone's hurt.
  - **Weaving a charm:** a mending woven into a bandage keeps.
  - **Tearing one:** it lands on whoever tears it.
  - **Selling them:** charms go on show like gear. Adventurers buy up to two, never two of the
    same mending, priced by the mending's strength.
  - **On a quest:** charms go out with their bearer (`MemberRecord.Charms`). They add a little
    strength, ward the wounds they fit, may cheat a death, and come back if unspent.
- **Meditation as a labyrinth** (`MeditateSession`): replaces the breathing rhythm.
  - **The maze:** a 9x9 labyrinth grown from the session's seed, entered at a corner, to the
    still centre. WASD walks it; holding F stands still and sees further.
  - **Sight:** only what's near is clear, and what's been seen stays dimly remembered.
  - **Mana:** a little for each new place, and a burst at the centre that shrinks over the
    30 s sitting.
  - **Pain** narrows sight, slows steps and scatters thoughts (back three places). The stone
    eases it and gathers more.
- **The weave as a sigil:** a beat ring closing on the heart circle, runes round the rim joined
  by strands, larger glyphs (64 px).
- **Bots** feel, draw, bind when short of mana, weave from their book, and meditate.
- **Art:** rune glyphs (`tools/healer_art.py`).

Save format 6. Bench hashes unchanged (1234: state `D21378177D6C73BA`, full `E0118354F8BBC3E0`;
99: state `B5DB9B55CC58E7B1`, full `B023D3107619F18B`).

### The serving hatch (2026-11-12)
- **Meals are ordered at a hatch in the kitchen wall** (`Shops/Hatch`, `ServingHatch` furniture
  at the inn's [7, 4]). It's a building prop (`hatch`): a small container that bounds rooms like
  a wall, but is heard through like a doorway (`SpeechSystem.RoomsAt`). The building def lists what
  it serves (`hatchServes`) and its own line (`hatchLine`). `ShopSystem.LineAt` and `LineCells`
  take `hatch:`, and an order's lane follows from its kind.
- The cook puts a bowl up by storing it in the hatch: F with it in hand ("Put stew up on the
  serving hatch"). The AI cook (`ServeJob`) and the bot do the same, putting up no more than are
  waiting (`Hatch.Enough`). The customer at the front of the hatch's line takes one
  (`Hatch.TryCollect`) and pays its maker, keeping the bowl's quality, then sits with it. What's on
  the hatch isn't available to other jobs, so no villager eats a customer's stew.
- Drawn: up to three dishes stand on its counter, and from inside the inn its posts and lintel are
  cut down with the walls.
- Design doc: a list of ideas for the Cook, to flesh out later.
- Bench baselines (the inn's layout changed): seed 1234 state `D21378177D6C73BA` full
  `E0118354F8BBC3E0`; seed 99 state `B5DB9B55CC58E7B1` full `B023D3107619F18B`.

### Sitting over a drink (2026-11-12)
- **Served, a customer sits with it.** A drink or a meal handed over is paid for and tasted at
  once (a named drink is judged then too: `DrinkingSystem.Served`). Then it stays in their hand
  (`Hospitality.Savouring`) and they take it to the nearest free seat in the room (`SavourJob`; a
  building prop's `seat`, the stools). They sit there about 0.6 game hours over a drink and 0.8 over
  a meal, and order no more drink or food meanwhile. When it's finished it takes hold
  (`DrinkingSystem.Drank`) or fills them, and the dirty tankard or bowl is left at their seat.
- **Talk at the table:** a third and two thirds of the way through, each may tell a rumour (50%, more
  with drink in them), heard by whoever sits near.
- Ending a job no longer drops what a customer is savouring. Leaving town finishes it in a gulp.
- **Art:** people have a `sit_<view>` pose (147 frames now), with the cup drawn in hand and lifted for
  a sip.
- Tests about serving wait for the customer to finish (`TestWorld.TillFinished`).
  `ServedTheyTakeASeatAndSitOverItTalking` covers seats, a stool each, saving, and the talk.
  Bench hashes unchanged.

### Slower days (2026-11-12)
- **A game hour is 40 real seconds, a day 16 minutes** (was 25 s and 10 minutes), so a 30 to 40
  minute round is 2 to 2.5 days. `TicksPerHour` is 2400 because systems tick every hour/12, /6 and /4.
  Everything set per game hour or day (needs, rot, wounds, prices' patience, arrivals) slows with it
  in real time. Walking, mini-games and fights stay in real time.
- Heat exchange and fire were per update, tuned against a 1500-tick hour: they're scaled by
  `PerTunedUpdate`, so forges, rooms and fires settle where `EnvironmentTests` tuned them.
- Directions: shop sightings are checked every 2 real seconds. Asking the way again keeps the first
  question open (its wait for an answer unchanged) and adds whoever newly heard it.
- Test fixes: fire tests pin seeds where the fire takes (spread along a line of walls is a coin flip
  at any hour length). Tests about lodging and directions heal the party first, and lodging takes quests off the sign.
- Bench baselines: seed 1234 state `1EB48993F8038C03` full `DF6CBE9347E7261E`; seed 99 state
  `FC1148287E60DEAF` full `2D877DFA76813B15`.

### After the innkeeper playtest (2026-11-12)
- **A Cook trade** (`trades.json`): the inn's kitchen, split off from the innkeeper.
  - **Who does what:** each trade lists the services it `serves`; the AI's jobs, the bot and
    the sign's prices go by it.
  - **Cook:** meals, the pot, the larder and bowls.
  - **Innkeeper:** drinks, mixing, kegs, rooms, quests and tankards.
  - **Cleaning:** the kitchen's floor is the cook's, the common room's the keeper's.
  - **The name:** only the innkeeper renames the inn (`namesShop`).
- **One at a time:** taking from a chest, shelf or larder takes one (`TakeFromStorageCommand`'s
  count), with an All button for the whole stack.
- **Mixing works from the shelf:** the ingredients and a clean tankard can be on the shelf
  you're standing by, not only in your hands or pack, and the drink lands in your hand.
  - The bar panel greys out a drink you can't mix and says why. Before, it failed with only a
    line in the log.
  - I couldn't reproduce the playtest's lost herbs in the sim. The likeliest cause was the
    juggling these changes remove: the whole stack grabbed into an empty hand, a refused mix
    reported only in the log, and walking off with things in hand.
- Bots go by what they're carrying, not what's on a shelf they happen to stand by.
- **Berries behind the bar:** the bramblecup's berries were only in the kitchen's larder. The bar
  shelf holds six stacks now, with berries beside the spirits, honey and herbs, and the carter
  brings more.
- **Washing up with a brush:** the wash game is a piece of crockery seen from above, with blots of
  grime on it. WASD moves the brush (a second axis, `WorkInputKind.Axis2`) and holding F scrubs
  under it. A piece is done when it's clean enough, then it shines and the next comes up. A careful
  scrubber takes about 6 s a piece. Stop halfway and what's washed stays washed.
- **Mixed drinks have their own price:** `ServiceKind.MixedDrink`, 4 silver by default against an
  ale's 2, on the sign as its own line. About a third of thirsty customers want a named drink; if it's
  dearer than it's worth to them they settle for an ale. The AI innkeeper pours an ale for one. Tests
  about ale prices take mixed drinks off the sign (`TestWorld.AleOnly`). Bench hashes unchanged.

### Roofs on (2026-11-12)
- **Roofs are on by default.** Only the building you're standing in opens up: its roof and
  anything of it above your level (the inn's upper storey, from the common room) aren't drawn,
  and only its near walls come down. Every other building keeps its roof and full walls; before,
  every building's near walls were cut down and the follow-your-floor cut took every roof in
  town off with the inn's.
- `\` (or the toolbar) still cuts every roof and front wall away at once (`--hide-roofs` to start
  that way). The yellow outline of what's sheltered is gone.
- Done in the mesher (`WorldMesher.OpenBuilding`), the renderer's hiding of what's under a drawn
  floor, and clicks, which reach through what isn't drawn.

### The Tinker (2026-11-12)
The last of the starting trades: locks and clockwork, split off the Carpenter (benched until
building and fletching make a trade of their own). Design: DESIGN.md, "Tinker". `Tinkering/`
(was `Locks/`), `data/defs/tinkering.json`, `VillageGame.Tinkering.cs`, `tools/tinker_art.py`.
- **The split:** the Tinker replaces the Carpenter in the deal (five named trades and a labourer
  for six villagers). A new tinker's shop on Millbrook, south of the square (`village_map.py`,
  `TinkerShop`): a door, the tinker's bench, a tool rack, a stock chest (iron, wood, charcoal) and
  the collector's crate. `Unlock` is served there. The woodshop stays, empty.
- **Locks and keys:** `MakeLock` at the bench is pinned by hand (`PinSession`: hold F to file each
  pin to its mark, D to set it); a crisp lock is tight (`LockCut.Tightness` narrows a pick's room)
  and its key turns sweetly (`Key.Fit`; a poor one sticks). Fitted to any door (`FitLockCommand`),
  which then locks for its keys (`TurnKeyCommand`; `SetLocked` takes a fitted lock) or a pick
  (`PickDoorCommand`, `PickSession` on a door). Keys copied at the bench from an iron bar
  (`CopyKeyCommand`). F at a locked door, or one you hold a key or a lock for, opens its window;
  otherwise doors are walked through as ever. AI villagers never lock doors.
- **Contraptions:** tapping rod, grapnel and winch, clockwork lantern, spring snare, signal
  rocket (`ContraptionProps.Use`; quests are flagged `dark` and `beasts`). Fitted part by part
  and wound (`FitSession`); a part forced home leaves a hidden `Contraption.Binding`, which may jam
  it for the whole quest. Carried out as `MemberRecord.Contraptions` (save format 7) and back
  unspent; the rod turns wounds aside, the grapnel and rocket turn deaths into bad wounds, the
  lantern and snare add strength where they fit. On show and sold like gear (two, not alike). AI
  tinkers make them from bills, never binding.
- **Wagons:** half the parties bring one (`PartyDef.WagonChance`), mostly damaged: a buckled wheel
  (`TrueSession`: F tightens the spoke at the gauge, A slackens) or a cracked axle (`FitSession`,
  2 wood). Parked outside near their haunt; the richest member asks for it mended
  (`ServiceKind.WagonRepair`, paid once it's sound). Set out with it broken, the party brings no
  strongbox or curios home (`Expedition.Hauls`). The AI tinker mends them (`WagonJob`). The wagon
  is a 3D box prop now, the town's too, not a sprite: 3 cells long and 2 wide, solid (a bed with
  sides, the driver's bench, stepped canvas hoops, four wheels on two axles, a tongue). Parked
  only where the whole of it fits with a walkable ring round it; the map's carts likewise
  (`village_map.py` reserves their footprint). F on any cell of a wide thing marked to come apart
  now reads the mark on its own cell, as the job does (a wide thing's other cells never worked).
- **Curios and the collector:** a cleared quest sometimes sends one or two curios home, a third of
  them fakes. True worth is hidden (`Curio`); `ServiceKind.Appraise` hands one over. Under the loupe
  (`LoupeSession`: A/D along it, F held over a tell to read it) each tell narrows the reckoning
  (`Appraisal`, the appraiser's own), and a fake's giveaway tell shows it up. Say what it's worth
  aloud (`QuoteCurioCommand`), hand it back (the owner believes it), or buy it near what they
  believe (`OfferForCurioCommand`). The collector's crate is emptied by the morning carrier, who
  brings back what each was really worth, less a fifth, the morning after (`CollectorSystem`).
  The AI tinker appraises honestly and doesn't buy (`AppraiseJob`).
- **Bots** play the Tinker's orders: boxes, wagons (truing, axles, wood from the shop's chest) and
  appraisals. They don't pin locks or make contraptions.
- Tests: `TinkerTests` (the lockwork, renamed from `CarpenterTests`) and `TinkerTradeTests`. Tests
  about something else take the party's wagon away, keep quests closed, or tell the party the way
  about town. The innkeeper's lodging test keeps its party off the quest board.
- After the first tinker playtest: fitting a part is aligning and pressing, not filing. The part
  wanders off true over its seat; A/D keep its mark over the seat's, F presses it in. Square, it
  slides home; pressed off true it rides on the seat's edge and binds, and binding all the way
  jams it in crooked (the hidden `Binding`). Drawn in section: the housing and bore, the part and
  its mark, the pressure as an arrow, the edge glowing as it binds. Winding draws the ratchet
  turning a click a tap, the pawl dropping into a tooth at the catch, and the spring tightening
  and reddening as it strains (nothing jitters any more). The tinker's chest starts with 16 wood.
- After the second: each part is named (frame, gear train, pawl...; axle, linchpins) and listed
  beside the seat, and a seated one sits home a second, flushing green ("Home, and square. Next:
  the pawl."), before the next comes up. The ratchet marks the teeth the pawl catches in (brass)
  and the ones past them where it snaps (red), so winding is bringing the brass round to the pawl.
  Latching short of the catch no longer ends it: the pawl slips, and the winding goes on.
- The tinker's stock stays the tinker's: villagers were carrying the chest's wood off for fires
  and building (anything in any container was up for grabs). A building can keep its stock
  (`BuildingDef.KeepsStock`, the tinker's shop does): no job takes from its containers, only
  someone opening them by hand (`Simulation.IsAvailable`). That covers the collector's crate too.
- **The inn's storeroom is locked**, with a fitted 4-pin lock, from the start of every round: a
  door to pick, and to fit a lock of your own beside. The innkeeper holds the key
  (`TradeDef.HoldsKeys`: a key to every lock at the workplace, dealt with the trade). AI villagers
  use keys now: a job with no way through but a locked door they hold the key to walks to it,
  unlocks it and goes on (`Locksmithing.KeyedDoor`, `Job.GoTo`; reachability counts it,
  `Navigation.CanReach`), and it's locked again behind them once they're clear (`RelockSystem`).
  Players turn their own keys. AI work packs keep keys between jobs.
- **Picking with tension:** one finger keeps tension on the plug while the pick lifts the pin
  (A/D). Tension is analog: W eases it on, S eases it off, and it stays where it's left,
  wandering a little with an unsteady hand (it was F held to build and released to drain, which
  playtesting found fiddly). Every 2 to 5 seconds the plug pushes back against the finger (or now
  and then gives under it) for half a second or so, gently (a little harder on a tight lock), so
  the tension wants correcting; the gauge shows the shove as an arrow. Too much tension freezes the
  pins (set ones stay set; the pin won't lift and F does nothing); too little drops every pin set
  at once. Coming back from too much is hitchy: while it's too tight and for ~1.7 s after, the plug
  springs back toward slack, hard (just under W's strength), and has to be caught or it goes slack
  and the pins drop. Too light and set pins drop back one by one;
  too heavy and the pin binds and won't lift. Near its set point the pin stiffens under the pick
  (it slows, the pick shudders, the shear line glows); there, with the tension in the band, F
  sets it with a click (the plug jolts). F anywhere else and the pin falls back to the bottom (no
  slip, no wear: just the lift to do again). Lifted past it, it oversets: a slip. With the last pin set
  the plug turns over before it opens. Drawn as the lock in section (chambers, key and driver
  pins, springs, the shear line, the pick, the tension wrench and the finger's pressure) beside an
  end view of the plug turning; the old jittering needle is gone (`Feel` is smooth now).
- **Narsil: no reaching through floors.** Touch (working on a cell from beside it) crossed a level
  anywhere, so a guest in the room over the storeroom could take from its chest through the
  floorboards. On a grid it now crosses a level only where the lower cell is open overhead
  (`NavGraph.Touches(grid, ...)`, used by regions and the pathfinder); a step on open ground still
  counts. Changes DragonWorld and Lifeline the same way. The gridless `Touches` is unchanged.
- Save format 7. Bench baselines: seed 1234 state `57207EEE9FA2C71F` full `46AFDADEBB1266A1`; seed
  99 state `16FBF26C8FB6EAB0` full `41C91768EF877819`.

### Heroes Not Included (2026-11-12)
- The game is **Heroes Not Included** throughout: the solution, projects and namespaces
  (`HeroesNotIncluded`, `HeroesNotIncluded.Sim`, their tests), the exe and the Mac `.app`, the
  packages, the Linux desktop id (`heroes-not-included`: run `tools/install-desktop-entry.sh` again),
  the Mac bundle id and the co-op game id (older builds can't join newer ones).
- Settings, saves, screenshots and logs live in `~/.config/Heroes Not Included` now. Nothing moves
  over from `Supporting Cast`: old saves stay there.
- The LAN share's default is `/srv/heroes-not-included` (`SC_SHARE_DIR` to change it). Still the
  old name: the repo folder. Bench hashes unchanged.

### Playtest prep (2026-11-12)
- **The icon** is the brand emblem (`art/brand/`, `tools/brand_logo.py`; the small-size sign at
  48 px and under), rendered by `tools/gen_icon.py` for the exe, window, Mac app, Linux desktop
  entry, and `packaging/icon-512.png` for Discord and itch.io.
- **Publishing:** tagging `v<Version>` runs `.github/workflows/playtest-release.yml`. It tests,
  packages, pushes the three platforms to a restricted itch.io page through butler, and announces
  the build in Discord's #builds. Pushes to main only test and package. `docs/alpha/` has the
  testers' README and CHANGELOG, and PUBLISHING (the one-time setup and the release steps). The
  title screen's Report a Bug says where reports go and opens the logs folder.
- **Legibility, from `docs/archive/PLAYTEST-AUDIT.md`:**
  - A quieter HUD: only the shop panel opens at the start, and Heard and Quests mark their tab
    when there's something new. No tooltip trails the cursor over ordinary ground. Mood reads as
    a word, an order's time left in words. Save, Load, Options, Screenshot and Credits fold into
    the Menu dropdown, and Back to title asks first. Bigger hotbar slots. The prompt over your
    head draws its key as a key cap.
  - The title screen wears the stacked logo and the brand's tagline, its menu flush left.
  - **Space is a mini-game's other action** (`WorkInputKind.Act`): drawing the piece out of the
    fire, tasting the stew, setting a pin, latching a spring. A/D only steer or mean less/more;
    seasoning's "that'll do" is stepping away (Esc). Space doesn't pause while working.
  - **Customers' wants over their heads:** the service's icon (`icon` in services.json) in a
    bubble that reddens as patience runs out, with the ember quest marker (`ui/icon_marker`) when
    it's your trade's work. Only orders you heard (`PerceptionView.Orders`).
  - **A first day:** each trade's `tips` under your portrait, and a parchment ring on each of its
    `stations` till you've stood beside it (trades.json; Options > Interface > Tips).
- Save format 8. Bench hashes unchanged.

### M3.1: The season and its reckoning (2026-11-12)
- **A round is a season** (`data/defs/season.json`, `SeasonDef`):
  - **Length:** three days from the deal.
  - **Festival:** `RoundPhase.Festival` starts at 18:00 on the last day. No new parties arrive and
    nobody sets out on a quest, but the shops keep serving.
  - **The end:** two hours later the town is reckoned and the round is `Over`.
  - **`RoundSystem.Live`:** means playing or at the festival. Everything that used to check
    `Playing` checks it now.
  - **`Endless`:** keeps a round going for tools and tests.
- **Renown and Dread** (`Director/Reckoning`):
  - **Renown:**
    - It rises by each adventurer's `Satisfaction` as they leave town, clamped to [-5, 10].
    - Those still in town at the festival's end count too.
    - Each quest cleared adds to it, and each adventurer lost takes from it.
  - **Dread:**
    - It grows each hour by 0.4 for every quest nobody has taken.
    - A failed quest or a death raises it, and a cleared quest lowers it.
  - **Reaching the limit (100)** ends the round lost (`RoundOutcome.Dread`). The outcomes are
    `Won`, `FellShort`, `Dread` and `TownFell` (nobody of the village standing).
- **The HUD:** the clock shows the season's day, the festival's countdown, and a word for each
  number (`PerceptionView.Standing`: "Travellers speak of the town", "A pall hangs over the
  valley"). Nobody in town sees the numbers.
- **The reveal** (`VillageGame.Reveal.cs`, from `RoundSummary`, built in the sim and saved):
  - The outcome, then Renown against its target and Dread against its limit, with an hourly
    graph.
  - Parties, quests and losses.
  - Everyone's trade and silver earned since the deal, players first.
  - "Look around" hides it, and the session stands still. Character commands are refused once
    it's over (`CommandRules`).
- `SeasonTests`. Save format 9. Bench state hashes unchanged (1234 `57207EEE9FA2C71F`, 99
  `16FBF26C8FB6EAB0`). The full hashes moved for the new saved state: 1234 `184E22DC013BCF66`, 99
  `A5E56D2BC8C32CC2`.

### M3.2: Nights and suppers (2026-11-12)
- **The night passes while everyone sleeps.**
  - **When:** `Players.AllAsleep` is true once every character a player is steering is asleep. It's
    a sim read, so every peer agrees.
  - **How fast:** while it holds, the host (or solo game) runs at 8x, so an 8-hour night takes
    40 seconds. Anyone awake holds 1x.
  - **What you see:** the sleeping prompt says "Everyone's asleep: the night passes", or that time
    runs on once everyone sleeps.
- **Getting up:** a player's character gets up at the first 06:00 after lying down, once at least
  0.6 rested (`SleepJob.NextDawn`, `DawnRest`). A nap mid-morning isn't cut short.
- **Bots go to bed at night** when they're tired and have no order in hand (`BotPlayer.Bedtime`),
  so a solo round with bots has nights that pass.
- **Gentler needs:** Food 0.9 → 0.7 a day (two stews), Rest 1.1 → 0.8.
- **Supper at the inn** (`ShopSystem.ShopForSupper`):
  - From 18:00 to 21:00, hungry villagers the AI runs (Food below 0.6) order a meal at the hatch
    like any customer. They line up, pay the cook and sit with it (`HumanBrain` step 2c,
    `QueueJob`/`SavourJob`).
  - Whoever cooks doesn't buy from themselves.
  - Everyone starts the round with a 12-silver purse (`SeasonDef.StartingPurse`); the reveal's
    earnings leave it out.
- `NightTests`. Bench baselines moved for the need rates: seed 1234 state `412D36A8850D6AD2` full
  `AA0974F679D047A8`; seed 99 state `A79FED518CE17A4C` full `0739283CD3C51B04`.

### M3.3: The Director (2026-11-12)
- **`Director/Director`** sends a round's crises on the season's beats (`season.json` `beats`):
  - **The beats:** a small raid late on the first night (20–23), a crisis on day two (10–17) and
    the dragon on day three (11–15).
  - **Timing:** each beat's moment is picked in its window when the round starts. Dread pulls it
    earlier and makes it stronger (×1 to ×1.5).
  - **Kinds not built yet** are passed over: so far only raids can come (`Director.CanSend`).
- **Off unless the game enables it:** the game enables it for every new village it starts for a
  round, at Normal unless `--difficulty` says otherwise (`Off` for none, `Peaceful` sends
  nothing). Tests and the bench leave it off.
- **The Storyteller stands down** in rounds and still sends raids outside them. Its raid helpers
  moved to `Raids.Points`, `Raids.Choose` and `Raids.Send` for both to use.
- **`ICrisis`:** each crisis reports itself met or not, and `Reckoning.CrisisResolved` moves
  Renown and Dread (met +6 Renown, −4 Dread; unmet +10 Dread).
  - **`RaidCrisis`:**
    - **When it starts:** the villager nearest the road shouts the alarm.
    - **Met:** every raider is dead, down or fled before `crisisHours` (6), and no villager died.
    - **Unmet:** otherwise, and those still about leave with what they came for.
- `DirectorTests`. Bench state hashes unchanged; the full hashes moved for the Director's saved
  state: 1234 `2C1EE4B896631A9D`, 99 `3B07FA28C5E95F88`.

### M3.4: The town fights back (2026-11-12)
- **Villagers defend themselves** (`Ai/Defence`):
  - **When:** a raider or predator comes within 10 cells.
  - **The armed and fit** go out and fight it (`DefendJob`: close in, strike on the weapon's
    cooldown, break off when it falls, flees or they're hurt).
  - **Everyone else** gets indoors, further from it (`HideJob` once inside), or just away.
  - **Waking:** it's an emergency (`AiSystem.InDanger`), so sleepers wake to it and work is
    dropped.
- **Adventurers in town fight too,** from 16 cells. Raiders and predators come for them as well:
  `Factions.Hostile` now counts adventurers on the village's side (`Factions.Defends`).
- **Players can strike at raiders** with F, as at game (`AvatarHunting.FoeAt`). It trains Melee
  or Shooting. Nobody of the town or the parties can be attacked (M4).
- **Raids sized for a town of tradesfolk:**
  - **Size:** `season.json` `raidPointsFactor` is 0.4. At full size a first-day raid killed
    everyone on Millbrook.
  - **Timing:** a beat waits in its window for a party to be in town (they're what raiders come
    for, and who fights them), and comes at the window's end regardless.
  - **With heroes in town:** a strength-1.5 raid of two was met in two of three seeds, with
    nobody killed.
- **The town falls** only when nobody of the village is alive. Before, everyone being down ended
  the round.
- `DefenceTests`, `DirectorTests.ARaidOnTheTownIsFoughtOver`. Bench hashes unchanged.

### M3.5: The bridge comes down (2026-11-12)
- **A proper raid may pull the bridge down** (`Tinkering/Repairs`):
  - **When:** a raid of strength 1 or more, with chance `season.json` `raidBreaksBridge` (0.6).
  - **What goes:** a line of the span across its middle is destroyed, along its longer side, with
    whatever falls with it. The gap is real: Millbrook's river has no other crossing.
  - **What it costs:** parties come round by another edge, the carter can't get to the inn
    ("couldn't get over the river"), and Dread grows 0.5 an hour while it's down.
  - **The `Breach`** remembers the cells and what stood there, and is saved.
- **The tinker mends it** (`TradeDef.MendsTown`, the tinker's new tip):
  - **The game:** with 2 wood, F at the gap starts **bracing the span** (`SpanSession`, a cousin
    of truing a wheel). Six stones sag off level. A level slides along them and back, reading low
    where one sags and high where one rides up. F drives a wedge under the stone at the level, A
    eases one out, and each nudges its neighbours.
  - **When it's level:** the stones go back and the road is open.
  - **The window** draws the span over the river, the band, the stones and the level's bubble.
  - **The AI tinker** goes out and does it (`MendBreachJob`, no wood, like its wagons). The bot
    fetches wood from the shop and plays it.
- `RepairTests`. Bench state hashes unchanged; the full hashes moved for the new saved state: 1234
  `5FFE13EAE6A878AC`, 99 `A322D5DFEEAF6181`.

### M3.6: Fire, shortage and fever (2026-11-12)
The Director's second-day crisis can now be any of the four (`Director.CanSend`; `Director/Crises.cs`).
- **Fire** (`FireCrisis`):
  - **It starts:** a building catches at a cell beside something that burns, and the nearest
    villager shouts it.
  - **Fighting it:** anyone can beat it out (F, and the AI's existing firefighting work; no
    bucket line was needed).
  - **Met:** the fire's out before the building loses a fifth of its parts.
- **Shortage** (`ShortageCrisis`):
  - **It starts:** rats spoil half of every food stack in the inn's larder, and the carter stays
    away for its 24 hours ("The carter didn't come").
  - **Met:** the inn serves 6 meals or drinks meanwhile, counted by `ShopSystem.Served`.
- **Plague** (`PlagueCrisis`, `Healing/FeverSystem`):
  - **The fever:** an adventurer in town comes down with it, a new `DamageType.Fever`: an injury
    on the torso, with nothing to see.
  - **How it goes:** untended it grows (1 an hour, to 12, short of killing). It passes to whoever
    stays within two cells (5% a quarter hour). Bound, it stops growing.
  - **Healing it:** the empath feels it as "a heat in the blood that comes and goes in waves" and
    quells it, a new common mending (Rime and Still). Adventurers ask the healer about it like any
    wound.
  - **Met:** nobody in town is fevered within 12 hours, and nobody left town with it.
- `CrisisTests`. Save format 10. Bench state hashes unchanged; the full hashes moved for the new
  saved state: 1234 `6977970C16F19263`, 99 `CFBD91FACBA4EFA6`.

### M3.7: The dragon (2026-11-12)
- **The season's last crisis** (day three, 11:00–15:00): a wild adult dragon (`DragonCrisis`)
  comes in from an edge with a name ("Cinderwing", "Old Ashjaw"...). The town shouts
  "Dragon! DRAGON! Get indoors!"
- **`Ai/DragonBrain`** makes passes (`DragonPassJob`):
  - **Each pass:** it flies in and comes down within breath's reach of someone out in the open,
    or of a building. It breathes (`CombatRules.Breathe`, so the town burns), then stays down
    20 seconds snapping at anyone in reach.
  - **Fighting it:** the town can hurt it on the ground. Bows reach it in the air.
  - **Driven off:** hurt enough (pain over 0.3, or 30 damage), it leaves for good.
  - **The breath code**, there since DragonWorld, finally has a caller. Its doc no longer names
    classes that don't exist.
- **Everyone answers a dragon:** `Defence.Threat` counts one on the ground from anywhere in town.
  The armed go for it, and everyone else gets indoors.
- **Met:** driven off or killed within 2 hours: +20 Renown (`dragonMetRenown`). Unmet: it leaves
  when it pleases, +20 Dread.
- **In a test on Millbrook,** a party in town drove it off with four wounds, nobody killed, and
  several roofs burned.
- `CrisisTests` (the dragon). Bench hashes unchanged.

### M3.8: The festival (2026-11-12)
- **At the festival** (18:00 on the last day), the town and its visitors leave off work and gather
  in the square, milling about the well (`Ai/Festival`; `RoundSystem.Square`, found when it
  begins and saved). Urgent needs, the wounded and fights still come first.
- **The reckoning aloud:** at its end, whoever stands nearest the well shouts how the season went,
  in the words everyone knows it by: "Friends! The season's over. Travellers speak of the town,
  and a pall hangs over the valley. It wasn't enough, friends." Then the reveal.
- `SeasonTests.TheTownGathersInTheSquareAndHearsTheReckoning`. Bench state hashes unchanged; the
  full hashes moved for the saved square: 1234 `AC1A82C53B37D20C`, 99 `22437529DFFF4EAD`.

### M3.9: The notice board (2026-11-12)
- **Millbrook has a notice board** in the square by the well (`NoticeBoard` in village.json, a box
  model: two posts, a board with notes pinned to it, a shingle cap; placed by
  `tools/village_map.py`).
- **Favours are pinned to it,** not asked aloud (`FavourBoard`):
  - **Posting:** you have to be at the board, and its window says so otherwise.
  - **Knowing:** F at the board ("Read the notice board", `ReadBoardCommand`) is how you learn
    what's on it, and it opens the Favours list.
  - **Unchanged:** taking one on, finishing it and calling it off.
- **Errands:** a favour can ask for so many of a thing, brought to you (`PostErrandCommand`; the
  panel's "an errand: bring me..." picker).
  - **Doing one:** bringing it is doing it. Whoever took it on hands it over in reach
    (`DeliverFavourCommand`, "Hand it over") and is paid on the spot.
  - **The labourer** the AI runs checks the board, takes on an errand there's enough of lying
    about or in common stores, gathers it and brings it (`Favours/ErrandJob`, under Haul).
- **Tests:** `FavourTests` rewritten for the board. Two older tests were tied to the town's timing
  and got sturdier: the bot keeper's party is healed so it lounges within earshot, and the fever
  gets a day to spread.
- Save format 11. Bench baselines moved for the board on the map: seed 1234 state
  `BD2586001B6090EF` full `ABEA0CC24C779DF9`; seed 99 state `5E71028AC8D8CA95` full
  `DF94104B81C1236D`.

### M3.10: A season played through (2026-11-12)
- **The exit test** (`SeasonScenarioTests`): an Honest round on Millbrook, with bots as innkeeper,
  cook, blacksmith, healer and tinker and the Director at Normal, played from the deal to the
  reveal. It checks the round ends with an outcome, crises came and were each met or not, parties
  came, and, if it reached the festival, the dragon came and the reckoning was called. It prints
  how the round went. The seed-1234 run below took about 8 seconds.
- **Bots look after themselves** the way a careful player would:
  - **Hunger:** they eat when hungry, from a larder or a pile (they never used to, and starved).
  - **Heat:** they step back from heat, hours at the forge's mouth having killed them.
  - **Fire:** they leave a building that's alight, and get away from fire close by.
  - **Danger:** they keep indoors, or get out of the way, when raiders, beasts or the dragon come.
- The dragon lands only out of doors.
- **One run, seed 1234:**
  - **Parties:** 5 came and 8 adventurers had their say; one quest was cleared and one failed.
  - **Crises:** a raid, a fire and the dragon, one of them met.
  - **The end:** Renown 34 against 60 and Dread 72, so the season fell short. The labourer and
    one bot (bled to death) were lost.
  - **Earlier runs, the same seed,** before the bots ate and kept out of harm's way: two ended
    with the town through at Renown 79 and 104, with bots dead.
  - **What that says:** the numbers swing a lot on small things. That's for the playtest.

### After M3: trims before the playtest (2026-11-12)
- **Gathering is the labourer's:** in a round, only a trade that gathers (`TradeDef.Gathers`, the
  labourer for now) is offered digging (Z), mining, chopping or harvesting, and the other trades'
  AI has that work turned off (`RoundSystem.MayGather`, `GatheringWork`). Outside a round,
  anyone gathers as before. These are for the Miner and Forager when they come.
- **No pausing for players:** whoever plays a character can't stop the clock, Space and saves
  included (dev mode aside). The controls card no longer offers a pause.
- **Signs on every shop:** the smithy's hangs from its front post, the shrine's and tinker's on
  their front walls. A sign may now hang on a post as well as a wall.
- **Cottage K is gone**, and the tinker's shop moved three cells north into its place, clear of
  the road and the shrine (`village_map.py`). Seven cottages remain.
- **Homes:** the deal gives everyone a bed of their own (`Systems/Homes`, `BedOwner`):
  - **One each first:** a cottage to themselves (`BuildingDef.Home`), the nearest to their work,
    while there are cottages to go round.
  - **Then sharing:** anyone left over takes a spare bed in a cottage.
  - **Inn staff choose last:** with no cottage bed left, they get a room at the inn, which is
    never let.
  - **Late joiners** get one too.
  - **Held:** nobody else sleeps in a bed that's someone's (`FindFreeBed`), while its owner lives.
    Before this, villagers took the nearest free bed, and the inn's beds were kept for guests only.
- **Roadmap:** every milestone's leftovers are gathered into the polish pass.
- `RoundTests.InARoundOnlyTheLabourerDigsChopsAndForages`; `NightTests` (homes). Bench baselines
  moved for the map: seed 1234 state `B21A0A9DEA5BAFF1` full `51CF6D49C62A656B`; seed 99 state
  `6F4F5C7500D2CD22` full `2C87E5DE1CF65026`.

### Steam Deck pass (2026-11-12)
- **The Deck spotted:** with no settings file yet, `HandheldDefaults` finds a Deck (`SteamDeck=1`,
  or the board's DMI name) and starts at UI scale 1.2, borderless and a 45 fps cap. 40 and 45
  join the fps caps. `--deck`, `--size WxH` and `--ui-scale N` show other screens from the desktop
  without saving.
- **Fits 1280×800:** windows that skipped `Gui.S` scale; free windows are capped at the screen and
  scroll (`ScreenFit`); the journal, inspector, station, sign and tips take at most a share of the
  width. In Narsil, `Hud` moves the centre columns (toasts, hotbar, work) aside from the side
  panels at their height, and narrows them only when there's no room.
- **Controls through Steam Input:** `-`/`=` zoom (`Camera3DController.ZoomInKey`); F acts in front
  once you've walked away from the pointer (`PointerIntent`); text fields ask Steam for its
  keyboard. Layout and setup in docs/STEAM-DECK.md.
- Left for the device: tune the scale, check every mini-game's panel, export the controller layout
  to `tools/steamdeck/`, and measure frame time at 45 fps with HD-2D on.
- `SettingsTests` (Deck detection, first-run defaults), `PointerIntentTests`, Narsil
  `CameraTests.ZoomKeysZoomOnlyWhenSet`.

### M3: A whole season (done)
Slices M3.1–M3.10 above. What it set out to do is below, kept for the record; it all landed,
with the leftovers each slice notes.

*Exit: an Honest round is playable start to finish* (`SeasonScenarioTests`).
**Playtest gate: is serving heroes fun with friends, without traitors?** Tune before going further:
- the season's numbers (`data/defs/season.json`): the Renown target, the Dread rates, crisis
  weights and raid size;
- the dragon's toughness;
- the night speed;
- the need rates.

#### The plan, as written
A round has no shape yet: `RoundPhase` stops at Playing, nothing reads `Adventurer.Satisfaction`
or the expedition tallies, the Storyteller's 5-day grace outlasts a round, villagers ignore
raiders, and the dragon's breath has no caller. Decided: night skips only while every player
sleeps; a fixed season with the festival on day 3's evening; raids, fire, shortage, plague and
the dragon as crises; a physical favour board replaces asking aloud. In slices, each with tests
and a Done entry:

1. **The season and its end:**
   - `RoundPhase` gains `Festival` and `Over`, run by the clock from `season.json`. Losing ends
     the round early.
   - When it's `Over` the sim pauses, and a reveal screen shows the outcome, Renown and Dread
     over time, parties served, the dead and each player's earnings. M4 adds its traitor rows to
     the same screen.
2. **Renown and Dread** (`Director/Reckoning`):
   - Renown is built from Satisfaction as parties leave, and from quests cleared.
   - Dread grows from quests left open, quests failed and crises the town didn't meet.
   - Players see each as a word with the clock. The numbers appear only at the reveal.
3. **Nights, needs and evenings:**
   - **The night skip:** `Players.AllAsleep` is a sim read. While it's true the host runs at 8x,
     so anyone awake holds 1x, and in M4 a traitor who won't go to bed stands out.
   - **Waking:** players wake at dawn.
   - **Needs:** tuned so a day is breakfast and supper.
   - **Supper at the inn:** AI villagers buy it at the hatch from 18:00, sit and talk.
   - **Staying up:** late drinkers, lodging and the odd night raid are reasons to.
4. **The Director:**
   - It replaces the Storyteller in rounds; `Phase.None` keeps the Storyteller, so the bench is
     unchanged.
   - Day 1 is quiet, day 2 brings a crisis, day 3 the dragon. Dread brings crises sooner and
     makes them harder.
   - Each crisis reports itself met or unmet, and it's shouted in the world, not announced.
5. **Villagers defend themselves:**
   - Armed villagers and adventurers fight hostiles (`DefendJob`, on `AssaultJob`'s pattern).
     The unarmed shelter indoors.
   - Players can attack `Faction.Attacker` creatures only. Attacking people stays M4.
6. **Raids break things; the Tinker mends them:**
   - Raiders bash the gate, doors and walls. A structure below a quarter of its Hp is `Broken`:
     impassable, but not gone.
   - A broken bridge cuts the road for parties and the carter.
   - `MendSession` (like `TrueSession`) braces a span or rehangs a gate. The AI tinker uses
     `MendStructureJob`.
7. **Fire, shortage and plague:**
   - **Fire:** fires with a cause, and a bucket line from the well (`DouseJob`).
   - **Shortage:** the carter doesn't come.
   - **Plague:** a party brings a `Fever` that spreads to those they sit with. The Healer feels
     it ("a heat in the blood") and mends it.
8. **The dragon:**
   - A hostile, flying `DragonBrain` strafes with `Breath.Breathe`, lands when it's pressed, and
     flees when hurt.
   - Driven off, it's a lot of Renown. If the inn burns, it's a lot of Dread.
9. **The festival:**
   - Day 3's evening gathers everyone in the square. The parties' Satisfaction counts one last
     time.
   - The reckoning is read out at the well, then the reveal.
10. **The favour board:**
    - A `NoticeBoard` in the square (`village_map.py`). Favours are posted there and known by
       reading it (F).
    - Structured errands ("bring N of X here") are taken by AI labourers, who are paid on
      delivery.

Bench hashes should hold except at slices 6 (passability) and 10 (a new prop). The exit test is
`SeasonScenarioTests`: bots play an Honest round from the deal to `Over`, through a crisis, the
dragon and the festival.
*Exit: an Honest round is playable start to finish.*
**Playtest gate: is serving heroes fun with friends, without traitors?** Tune before going further.

### Polish pulled forward (2026-11-12)
- **The bridge** (`WorldMesher.AddBridge`, `BridgeLook`):
  - Millbrook's river runs a short straight reach where the south-west road crosses, and the
    road goes straight over it: a 4x7 span instead of a diagonal staircase of cells.
  - Parapets with coping, abutments, buttressed piers and round arches with pale rings, all drawn
    from the span's run. Broken, it stops ragged: torn parapets, a torn vault, rubble in the water.
  - A dev button breaks it (`DevBreakBridgeCommand`).
- **Fireplaces** set in the walls of the cottages, dwellings and the inn (the longhouse keeps its
  central hearth):
  - Stone, under an oak mantel, its stack up through the thatch and smoke from it.
  - `setInWall`: worked, fed and warmed at only from the room, and its heat, light and warmth go
    into the room in front of it (`Warmth.FireCell`). Without that the inn's common room sat at 5°C.
  - The stew pot hangs in the opening; stew is a campfire's recipe needing the pot only a
    building's fire has.
- **Elves**, the first of the fantasy folk (`Appearance.Folk`, save format 12):
  - Long ears swept up through the hair (`chibi_people.py`), their own skin and hair swatches,
    no beards.
  - About one villager or adventurer in six; picked first in the creator's Look tab; `--folk Elf`.
  - A look only for now.
- **The title screen, by the brand guide:**
  - Cinzel (Narsil's new display font slot) for the menu.
  - Labels from the word bank, each line underneath the picked entry; Host game and Join game
    split; Credits opens the Credits window.
  - Ember stays rare: barrel-gold and oak for the picked entry; the wash is cellar dark.
  - Millbrook going about its day behind it, in golden light; loading lines while a village is
    made ready. The lobby and the round start get their lines too.
- **The fireplace in its wall:** its chimney breast now rises the full width to the walls' cap beam
  (the oak mantel still stands out), and the walls beside it are no longer cut down for it.
- **The inn's lanterns** (`BuildingDef.Lamps`): hung from the beams over the tables, by the bar, in
  the kitchen, the storeroom and the hall upstairs. No fuel and no floor taken; the dark corners of
  the common room are about half again as bright at night.
- **Players order meals:** F at the inn's serving hatch orders one at the price on its sign
  (`ShopSystem.PlayerOrder`, `AvatarActionKind.OrderMeal`). It's said aloud and waits in the hatch's
  line like anyone's, and at the front, with a bowl up, it's taken, paid to its cook and sat with.
  The cook doesn't order from their own hatch. F there again says where you are in the line.
- **README** brought up to date.
- **Docs:** the Done log moved here from the roadmap; dragon-era sections of ART.md pruned or
  condensed; the dragon-egg start message gone.
- Starting herds are placed by `Map.Distance`, as sight measures, so none grazes in view of town.
- **Sound, the first pass** (docs/AUDIO.md):
  - **Engine:** Narsil's new `Narsil.Audio`, OpenAL Soft and NVorbis behind `AudioEngine`
    (one-shots, held loops, music by mood, buses, falloff, muffling). A `sounds.json` manifest,
    live reloads, and silence with no device.
  - **The village** heard as you perceive it (`SoundDirector`):
    - fights and dragon fire, placed, and muffled through walls
    - babble for lines your character heard
    - wind, birds or crickets, the river, hearths and blazes, a full room
    - a bell for danger, coin, and music that follows the day, danger, the festival and the reveal
  - **Every mini-game** has its sounds (`WorkSounds`); the anvil rings by how clean the blow was.
  - **Effects** synthesized from scratch (`tools/sfx_synth.py`), with CC0 recordings where
    synthesis was thin (`tools/import_sounds.py`, data/sounds/CREDITS.md).
  - **Placeholder music**, two free tracks per mood, fetched rather than kept in git
    (`tools/fetch_music.py`).
  - **A Sound tab** in Settings: five volumes, babble, mute in background.

- **Saved characters, every mode:** the roster (`Characters/CharacterRoster`, `characters.json`)
  used to be co-op only. Now every way into a village (a solo round, hosting, joining) starts by
  picking or making a character, who's saved.
  - A **Characters** screen on the main menu: make, change and let go of them; each card shows the
    trades they've played.
  - **Joining** goes by your character's name: the Join panel's name box became "Playing as ...
    Change", and you're seated as them without being asked again.
  - The roster holds 16 and never drops anyone to make room. The file is versioned (old lists are
    read and rewritten; one from a newer build is left alone).
  - **Progress** (`CharacterProgress`): every round played to the end adds to the character's
    record for that trade (rounds, survived, silver earned). Titles and unlocked cosmetics have
    their slots, empty until progression fills them.
  - `--character NAME` works with `--new-game` as well as `--host`.

### The art pass (2026-11-13)

- **The dragon era pruned:** the egg, the hatchling and juvenile, the Norse breed, the recolour
  layers, the saddle, the care icons and gear, and their generators (`norse_dragon.py`,
  `placeholder_dragoncare.py`) are gone. The dragon has one stage, the adult. The canvas the
  side-on creatures share moved to `tools/chibi_canvas.py`, and the breath's core to `norse_fx.py`.
- A campfire no longer offers stew: a recipe that needs an upgrade is listed only on stations that
  can take it.
- **A dragon of our own:** the wild dragon is redrawn (`tools/chibi_dragon.py`) to the design in
  ART.md (upright, a prow neck, swept horns, rib-like plates, a bone-plated front, rune-stepped
  ember veins, a narrow burning eye) at four times the detail, replacing DragonWorld's adult sheet.
- **Nothing borrowed:** the inn's stations, the barrel, crate and barn, the whetstone, the
  meditation stone, herbs and the pyre have icons of their own (`tools/station_art.py`); the cask's
  hoops, the stew pot and the wash tub wear their own faces instead of stone and the quench tub's.
  The pig is drawn as a pig (`chibi_animals.py`), not the boar shaven and recoloured.
- **Props, torches and stairs:** the bellows stand on a frame as a box model, turned to point at
  the forge, instead of lying flat. Buildings can mount lamps on their walls (`facing`, per-lamp
  `def`): wall torches by the inn's door, its stair and far corner, and in the smithy. Stairs are
  four solid steps (`steps`) rather than a sloped face with stringers poking through it. The forge
  and kiln stay textured blocks: their fronts already carry the art.
- **Mini-games as scenes, the first batch:** seasoning (the pot steaming, pinches falling from the
  salt crock), making a bed (the sheet as far as it's got, the hand coming round), binding (the
  turns on the arm, the one being pulled tinted by how tight), drawing a hurt (its glow drawn up into
  the hand), sharpening (the blade on the stone at your angle, warming, sparks) and probing a
  strongbox (the pick along its seam). Props from `tools/work_ui_art.py`; `--work-scenes` shows them all.
- **Mini-games as scenes, the second batch:** studying a curio (the loupe moved along it, a glint
  where the eye catches), feeling for wounds (the patient lying head to feet under the hands, only
  what's been felt glowing), truing a wheel (it turns on its stand, the rim swinging out and in at the
  gauge), bracing a span (dressed stones and a spirit level) and filing pins (the rasp at the pin).
- **The smithy keeps its stock:** the chest by the forge (the carter's ore, the smith's wood and
  bars) is the smith's, like the tinker's. No labourer's haul, refuel or errand takes from it; whoever
  works at a shop that keeps its stock still uses it (`IsAvailable(item, taker)`).
- **Mini-games, the last of the props:** pouring and tapping a cask show the cask's head, its bronze
  tap and the mallet; the stew sits in the iron pot over the fire. The games that are diagrams (the
  lock in section, the cue lane, the scrubbing grid) keep their drawn shapes.
- **A loading screen** (`Ui/LoadingScreen`): the logo on the cellar dark, the brand's loading lines
  in turn and three embers breathing, while a new village is made ready (`VillageGame.PrepareWorld`)
  or a save read on a worker thread. Starting, loading, hosting, joining and the direct-launch
  flags all go through it; a start that fails comes back to the title with what went wrong, now
  shown under the tagline rather than below the menu.
- **Dwarves and halflings:** two more folk, as looks. Dwarves are short and broad (every man and
  some women bearded, ruddy skins, red to grey hair), halflings small and slight (warm browns). Each
  has a build of its own, every people sheet warped to it (`tools/chibi_builds.py`), so every garment,
  tool and hair style fits. One villager in eight of each about town; pick one in the creator (Look,
  then Folk) or with `--folk Dwarf|Halfling`.
- **Hair and beards, lock by lock:** every style is now built as swept and the braided ponytail
  were: a soft crown, tapered locks of every length with strand lines and strays, curls as coils
  and ringlets of every size (not a stamped pattern), woven braids, a wound bun, a bound topknot,
  each with its own back view. Beards grow from the jaw as locks, with a sweeping moustache and,
  for the braided one, a woven plait and a bead. `--hair-preview` shows them all.
- Braids grow out of the hair rather than sitting on it, and the curls are tighter: springy
  ringlets drawn as columns of turns, and dense coils over a crown of small bumps. `--turn-test`
  tries a real three-quarter turn of the head and hair on a few styles.
- The curly styles redrawn after reference art: a wide, airy bob edged in loops with spiral line-work,
  and long hair in big S-waves wrapping the head and fanning out, each lock its own. Braids lying on
  the head are shaded into the hair with no outline. The turn test keeps the jaw over the neck.
- The short curls are built of curl clumps (defined springy locks, not a lumpy mass), and the long
  curls sweep across the brow from a deep side parting, the forehead bare on the parting's side.
- Beards after reference art: the short one is the jaw, with a clean cheek line and a notch under
  the lip; the full one hangs to ragged points under a curled moustache. The curly bob's dark shows
  only between its curls, not as a solid shape behind them.
