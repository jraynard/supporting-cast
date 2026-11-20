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

### Combat: blows seen coming (2026-11-13)
The first raid with a player in it showed that fights didn't work: three raiders each swung every
1.5 s at whoever they reached, so a player was down about 5 s after closing in, with nothing to do
about it and no word on how to strike back. Turn-based was weighed and set aside (the day clock is
shared); fights stay real time, Souls-lite.
- **One at a time** (`Combat/Engagement`): only so many press one target at once, one on a player
  and two on anyone else; the rest wait a step back (`Job.AwaitTurn`) or go for someone else.
  Melee comes every 2.5 s, a little slower at a player.
- **Blows seen coming** (`Combat/Blows`, `BlowSystem`): every melee blow is wound up first
  (`Windup`, `AttackDef.windupSeconds`), drawn as a ring closing on its target (amber, ember for a
  heavy one) that flashes as it lands. A raised guard blocks it (a third of the hurt, some wind);
  raised just as it lands, it parries (none, and the attacker is staggered and open to a riposte).
  Space dodges; a heavy blow (Shift+F) breaks a tired guard. Wind is a bar under your needs. AI
  fighters block and parry by their Melee skill, rolled as the blow starts, so fights between them
  run on the same rules.
- **Co-op:** guard and dodge keys are stamped with the tick they were pressed on
  (`CombatInputCommand`), and a blow at a player lands a fifth of a second after its impact
  (`Blows.LatencyGrace`), so a parry pressed in time counts though it arrives late.
- F strikes the foe beside you first, and a line over the hotbar says how to fight.
- Ranged shots and the dragon still strike at once; trade and weapon techniques are next.
- Bench baselines: seed 1234 state `853A200442B00386`, full `EFC778FFC76E0163`; seed 99 state
  `9039CE5D8E55CCBD`, full `36736D4F7C3441D1`.
- **The practice ring** (`--arena [FOE]`, `Scenarios/Arena`, `tools/arena_map.py`): a small fenced
  ring and one foe a bout, any raider kind or a dragon, with a panel to pick the next foe and your
  weapon, and R for a new bout with both of you fresh.
- **Arrows and the dragon:** a bow's draw is its windup, so an arrow is seen coming; it's blocked
  or dodged, never parried. Archers now keep their distance and shoot (before, a raider with a bow
  had nothing to fight with). A dragon's jaws come quicker, can't be parried, get through a guard at
  70% for twice the wind, and are dodged only by a late dodge; its breath is drawn first where it
  can be seen (a wide ember ring), and a guard or a late dodge takes only some of the fire.
- **Free movement, the prototype** (`Combat/FreeMove`, the arena only): your character moves off
  the grid (WASD relative to the camera, easing in and out, sliding along fences) and faces the
  mouse. Left click strikes the way you aim, right click swings heavy, hold Q to guard (it must
  face the blow), Space dashes about two cells with a moment blows pass through, drawn as a tumble.
  With it on, blows land by reach and timing (`Combat/Reach`): each weapon sweeps its own shape the
  way it's aimed (a sword a wide arc, a spear a long narrow line, a club a short wide one; an arrow
  flies to the first in its line), and whoever's in it as it lands is struck, no roll. An AI's
  blow is aimed as it starts, so stepping aside is a dodge. Armour takes a fixed share
  (`CombatRules.Strike`); skill quickens the windup and adds weight. Weapons are drawn swinging, on
  their own about the hand (a blade swept across with a smear, a spear thrust out and back).
  Bench: state hashes unchanged; full hashes moved for the new saved switch (1234 `23418520B4927BD6`,
  99 `9FBC73DE1A8F0D10`).
- **Free movement in the village** (the combat pass's step 1: docs/ROADMAP.md): the Free movement
  setting (or `--free-move`) turns it on for solo games. A job's walk (a click, the way to a station)
  follows its path cell to cell, so stairs, ladders, doors and furniture work as they did; the keys
  change level only where the walking links do. R draws or sheathes your weapon: sheathed, clicks
  walk and act and you face the way you walk; drawn, the mouse fights, Q guards, and you're a little
  slower. A blow landing on you draws it. Free swings hit everyone in their sweep, friend or foe,
  hares included; an AI holds a blow while a friend's in the way. A character handed back to the AI
  walks the grid again. The practice ring's new bout moved to Enter.
- **AI fighters move freely** (step 3, `Combat/FreeFight`): with free movement on, a raider,
  defender or cornered animal leaves the grid for its fight and comes back to its cell after. It
  walks the path when far off or with something in the way, else steers: in to its weapon's reach,
  holding a little off and circling while it waits its turn, clear of anyone near, now and then
  stepping back after a blow. Archers hold their bow's range, give ground when you close and lead a
  moving target. An AI's telegraph is its sweep marked on the ground, so you can step out of it.
- **Bots fight** (step 4): with free movement an armed, unhurt bot draws and takes on a raider or
  predator it can see near, through commands only: to its reach, striking the way it aims,
  stepping out of a blow's sweep (or guarding, winded). The season scenario runs with free movement
  too (`SeasonScenarioTests`, both ways).
- **Free movement in co-op** (step 5, first pass): the host's setting holds for everyone. Your keys
  move a copy of your character at once on your screen, by the sim's own steps
  (`Net/FreeMovePredictor`, sharing `FreeMoveSystem.Wanted` and `Slide`), and your commands say
  where you are; the sim takes it when it's near and somewhere to stand (`FreeMoveSystem.Claim`),
  so the host ends up where you saw yourself, and a swing or a dodge comes from there. Let go and
  you stop dead where you let go. Blows at a player land `Blows.LatencyGrace` late again with free
  movement too, and a blow you dashed clear of says "Dodged". Still to come: judging a sweep
  against where people were when it was pressed.
- Archers hold their range (they walked in and backed off again, the jitter) and shoot at their
  bow's own pace.
- **Feel** (step 6, first pass): hit-stop and a little camera shake for blows you deal or take,
  presentation only (`VillageGame.HitFeel`; Settings, Screen shake); new synthesized sounds for a
  parry, a block, a heavy swing and a spear's jab (`tools/sfx_synth.py`), played by how each blow
  went (`SoundDirector.BlowsMet`); a sheathed weapon slung across the back (`DrawSlung`); a body
  winding up a blow plays its work swing until it has frames of its own.
- **One model** (step 8): every player moves freely, always (`FreeMoveSystem` has no switch; the
  grid walking, `AvatarPredictor`, the grid's dodge and Shift+F heavy, F-to-strike and its hunting
  job are gone; the `Avatar` component dropped its held direction and scheduled turns, save format
  15). There's no roll to hit anywhere (`CombatRules.Resolve` and `TryAttack` are gone): a blow
  lands on whoever's in its sweep (`Reach`), armour soaking what the old roll took off on average
  (`CombatRules.Strike`). An AI slips a blow aimed at it as often as the old roll would have missed
  (`BlowResponse.Dodge`), so fights between AI stay as deadly as they were. Blows at a player come at
  full pace (`Engagement.PaceOnPlayer` gone). Archers lead a moving target. Moving diagonally tries
  both axes' orders, so a stair met at an angle is climbed.
- **Late swings** (the rest of step 5): every creature keeps a short trail of where it's been
  (`Combat/Trail`, saved); a player's swing carries the tick it was pressed on, and lands against
  where everyone was then.
- Bench baselines: seed 1234 state `853A200442B00386`, full `12E0FA3E45392BBC`; seed 99 state
  `9039CE5D8E55CCBD`, full `2D4DFD010E59E7C8`.
- **The parry window** straddles the flash: `Blows.ParryBefore` (0.2 s) before impact to
  `ParryAfter` (0.07 s) after. A blow at a player is settled just past it, alone; with others
  playing, `LatencyGrace` after impact (`Blows.Grace`). Before, it closed at the flash and the
  result came 0.2 s later.
- **A player's bow** (`Combat/Archery`, `BowCommand`): hold the right button to draw and aim, the
  left to loose once fully drawn; the aim line shows where it'll fly. Slower on your feet drawn.
- Raiders with nobody left to get at give up and leave (`RaiderBrain`): standing about, they kept
  the town indoors while its wounded bled out (a round of DirectorTests fell to it).
- **Wind** comes back only `Blows.RegenDelay` (0.8 s) after it was last spent (`Guard.SpentAt`), then
  at 0.18 a second (0.05 guarding): a flurry runs you dry. Save format 16. Every arrow is seen and
  heard flying, a miss included; an early click looses once the bow's drawn; the aim line is a
  ribbon on the ground.

### Control pass: rebinding and the gamepad (2026-11-13)
- **Actions, not keys** (Narsil `Platform/Bindings`): `ActionMap` answers "is guard held?" over keys,
  mouse and pad; `ActionDef`s carry a context so a key may mean two things where they're never live
  together (Space dashes in a fight and works a mini-game), and `Conflicts` reports only real clashes.
  The game's actions are `Controls` (`Act` ids): the keyboard defaults are the old keys. Every in-world
  read went over to them, and every prompt, hint and the controls card show what's bound, for the
  device last touched (`Controls.Glyph`, `Keys` for prose with `~F~` marks, `WorkKeys` for the
  mini-games' hint rows, `Cue` for the mix game's letters).
- **Rebinding:** Settings has a Controls tab (Narsil `Ui/BindingsEditor`): a keyboard-and-mouse and a
  gamepad column, click and press. Saved as text in `settings.json` (`Bindings`, only what differs).
  The god-mode toolbar's letters, Ctrl+speed and the F-keys stay fixed.
- **The gamepad** (Narsil `Platform/Gamepad`, GLFW's mapped pads), Souls-style: the left stick walks
  (any direction, not eight), the right stick aims; RT strikes, LT swings heavy or draws a bow, LB
  guards, B dashes, Y draws, A uses, X digs. At hands-on work A works, the stick steers, X is the other
  action and the runes are the d-pad and shoulders.
- **The wheel:** hold RB, lean the right stick, let go: the panels, the hotbar's stacks, put down,
  sleep, build. **Start** opens a menu of the top bar's things.
- **Menus from the pad:** while a window that takes input is open, ImGui's gamepad navigation has the
  pad (`ImGuiLayer.GamepadNav`; the world hears nothing meanwhile), the newest takes the focus and B
  closes it. Inventory slots are buttons now, so the d-pad lands on them; tooltips show on focus. The
  title screen is navigable throughout.
- The controls card is two columns and fits 1280x800.
- `BindingsTests` (Narsil), `ControlsTests`. No sim change: bench hashes as before.

### Control Pass: on the Deck
- Built (HISTORY.md, "Control pass: rebinding and the gamepad"). Left for the device: play a round
  on a Deck from the pad alone; check every mini-game and window navigates (ImGui focus quirks show
  only in play); tune the stick deadzone and the wheel; export the Steam Input layout to
  `tools/steamdeck/`.


### Death and ghosts (2026-11-13)
Death sticks in a round (DESIGN.md, "Death"); everything is in `Sim/Afterlife/`.
- **Dying:** a body keeps how and when it died (`Death`). A player's character loses its `Avatar`
  and leaves a `Ghost`, its own entity with a `Position` and no creature parts, so no creature or
  avatar query sees it. The dead can't take up another trade (`TakeTrade` is for late joiners);
  with no player alive, time runs fast (`Players.AllAsleep`). Outside rounds (the practice ring,
  the demos) nothing changes. The downed can still speak, weakly, while they have their wits
  (`SpeechSystem.CanSpeak`), and the HUD says they're bleeding out.
- **Ghosts** drift with the walk keys, see a radius around them through walls and in the dark
  (`PerceptionView.Ghostly`: no light read, no packs, no secrets), overhear the living
  (`Utterance.Ghosts`, kept apart from the listeners the living act on) and talk among the dead
  (`Volume.Ghost`). "You have died", then where your body lies and who's with it (`MyBody`). Ghosts
  are pale wisps seen only by ghosts. No god-mode toolbar or orders for anyone in a round without
  a character (`Spectating`).
- **Coming back:** the empath's "Calling back" (Thread, Ember, Still, Flesh; 70 mana) woven kneeling
  at a body puts a `Calling` on it; the ghost comes to its body and presses F (`AnswerCommand`)
  within half a game hour. Fresh for 4 game hours, 12 on a shrine pallet (bodies can be laid out
  there now); once a round (`Revived`); back downed, the fatal wound eased. The caster takes the
  grave's chill (an absorbed frost wound). A ghost whose player is away, or a villager, comes back
  by itself. An AI empath does it for bodies laid out in their shrine (`CallBackJob`).
- **Haunting:** presence gathers (half a pool an hour) and buys a chill (breath in the air), a
  guttering light (a lamp really dark an hour; a low fire goes out) or a knock, perceived only
  through `PerceptionView.Signs` and never saying whose. Hotbar keys 1 to 3 while dead.
- **Unfinished business** (`data/defs/afterlife.json`, `GhostErrandDef`): linger by a mourner, a
  sleeper or a lost adventurer (`DirectionsSystem.Feel`) and it's done, for a little Renown; the
  dead sense who needs them (`PerceptionView.Business`). Bots answer callings and do errands.
- **The reckoning:** a town death adds Dread (`dreadPerTownDeath`), a calling back lifts some
  (`dreadPerRevival`); a dead townsperson's trade is taken up by a labourer at the next dawn
  (`Vacancies`); the reveal says who died on which day, who came back, and the errands done.
- `--die-at SECONDS` drops your character dead to try it (`DevKillCommand`). Save format 18.
- `GhostTests`, `RevivalTests`, `HauntingTests`, `GhostErrandTests`, `DeathReckoningTests`. Bench
  hashes unchanged (rounds only).

### Polish: low-hanging fruit (2026-11-13)
Small leftovers from the polish list and the pre-M4 sections, each its own commit.
- **Dev tools:** send any crisis (`DevCrisisCommand`, through `Director.Send`), and warn of the
  season's next raid now (`DevRaidWarningCommand`, `Director.WarnNext`), to see the town brace.
- **The dead keep what they wear** (`DropGear(keepWorn)`): the called back come back dressed;
  raiders, captors and prisoners are still stripped where they fall.
- **A ghost's knock creaks first:** a synthesized creak (`creak()` in `tools/sfx_synth.py`,
  `ghost.creak`), then one knock, instead of the keg's tap twice.
- **A sleeping player wakes** (`Players.Wake`) to a shout heard clearly or from just outside (news
  called across the square to a shut cottage doesn't), to smoke or raiders near, and to the call
  to arms.
- **Difficulty scales the season:** `renownFactor` and `dreadFactor` on difficulties
  (`Reckoning.RenownTarget(sim)`, `DreadLimit(sim)`); Hard wants more Renown with less Dread to spare.
- **The town's arms go back on the rack** after a muster (`Muster.ReturnArms`, `ReturnArmsJob`).
- **The healer's pallets are kept for the hurt:** a bed for the night (`FindFreeBed(forTheNight)`)
  passes them over (`ShopSystem.IsForPatients`); the wounded are still carried there.
- **The bridge's sides:** a test that its walking links leave only off the ends. A co-op claim
  could land on the bank a level down by an end: `FreeMove.Claim` now wants the cell a few
  walking links away.
- **`--building-demo`** builds the old open valley (`MapGenerator.Generate`) instead of laying
  its showcase over Millbrook.
- **Bots bind their own bleeding** with a bandage to hand, rather than bleed out (`BotPlayer.BindOwn`).
- **A fever weakens a party on a quest** (0.8, 0.92 once seen to; `ExpeditionSystem.Strength`).
- **The forge's heat:** a toast the moment the heat a player feels turns dangerous. AI smiths
  never forge in a round, so they never stood in it.
- **A felt wound that worsens a band** (`Empathy.Worsened`) must be felt again before it's drawn.
- **A player turns mid-step** when a click goes behind them (`AvatarWalkJob.Replan`: a free mover
  steers from where it is).
- Casks already refilled (`TapJob`, the carter's kegs): the roadmap line was stale.
- `SeasonScenarioTests` counts a crisis still under way when Dread ends the round.
- **Each trade's own weapon** (`kit`, held from the deal): the tinker's dagger, the cook's kitchen
  knife, the healer's walking staff, the smith's hammer (a tool and a weapon). The innkeeper's
  cudgel, **Lucky Lou**, lives under the bar (`TradeDef.Stash`, `BuildingProps.Stash`,
  `Militia.Stashes`): fetched when trouble comes, put back after, and nobody else knows it's there.
  They're `makeshift`: the muster still arms their holders from the rack (their own put away, taken
  up again after), raids don't size up against them, and an AI with nothing better fights only
  when cornered (`Defence.CorneredRange`). A weapon swapped out goes to the pack or belt, not the
  floor (`Inventory.PutOn`). The tinker makes a **crossbow** at the bench (`MakeCrossbow`): slow to
  crank, hard-hitting. New held shapes `staff` and `crossbow`.
- **A slung weapon shows over the back** of someone walking away (`CreaturePose.View`); it hid
  behind them in every view.
- **The stone axe and pick are the labourer's:** every other trade has them taken off the belt at
  the deal (`Toolbelt.TakeStartingTools`).
- **Feeling a strongbox for traps reads clearly:** a line saying what to do now, a trace of what the
  probe has felt along the seam (a catch stands up as a hump to come back to), the pressure gauge's
  zones named (sweep, ease, springs) with the light-touch line marked, and a tick when it catches.
  The green band was never for sweeping: it's above the light touch that crosses a trigger safely.
- **The crossbow's made by hand:** a recipe's `fit` sends it through the fitting game at the
  tinker's bench (`RecipeDef.Fit`, `Tinkers.FitOf`): the stock, the steel bow and the trigger and nut
  seated, then the string drawn back to the nut and latched (the window shows the bow spanning).
  It's a weapon, not road gear: no `Contraption` on it.
- **A crossbow stays loaded** (`holdsLoad`, `Combat.Loaded` on the weapon): hold the right button
  to crank it; once it's done it's loaded, let go, sheathed or saved, and a click fires it.
- **Shop signs:** a cogwheel over the tinker's shop (`props/sign_gear`), a mending bindrune over the
  healer's shrine (`props/sign_rune`), from `tools/inn_art.py`.
- Bench baselines: seed 1234 unchanged (state `853A200442B00386`, full `12E0FA3E45392BBC`); seed 99
  state `CEFFA4278018879C`, full `653B0922D876F08D`.

### The festival, made an event (2026-11-13)
The season's last evening (M3.8 was a crowd milling round the well) becomes the round's payoff
and its last chance (`Round/FestivalSystem`, `data/defs/festival.json`, `Defs/FestivalDefs.cs`).
- **Longer:** 17:00 to 22:00 on the last day (`season.json`), in three parts: the opening (the
  bell, everyone to the square), the revels, and the ceremony (its last 1.25 hours). The night
  never fast-forwards through it (`NightPassing`).
- **Put up for it:** the map lists what goes up (`MapDef.Festival`, `MapProp`; placed by
  `tools/village_map.py` and checked like the other features): six post lanterns hung with bunting,
  the bonfire, two long tables and eight stools, the show table, the striker and the archery butt.
  `FestivalSystem.PutUp` spawns them when the festival begins, on the nearest free cell nobody's
  standing on. The lanterns and bonfire are fires filled at the start and never fed (`refuelAt` 0),
  so the square's lit and warm through the fire and lighting code as it is. New textures:
  `props/cloth_*`, `props/target_face` (`tools/placeholder_props.py festival()`).
- **The revels** (`Ai/FestivalJob`): each villager and guest picks something, by weight (the
  town's and the visitors' differ): sit at the feast, warm themselves at the bonfire, dance round
  it (a square ring two cells out), watch a game, look over the show table, or mill about.
  A job ends when the festival moves on to its next part, so the ceremony's bell brings everyone in.
  - **The inn serves outside:** during the revels `Table.FreeSeat` prefers the festival's stools,
    so whoever's served sits down in the square. Anyone with open orders at their workplace keeps
    serving through the revels (`Festival.StillServing`).
  - **The show table** (`BuildingProps.Show`): a container nobody's job takes from. Visitors near
    it look over each crafted piece once, and their satisfaction moves by its quality
    (`admireSatisfaction`); the maker's praised for it. Drawn on the table like the hatch's dishes.
  - **The games** (`Work/FestivalGames`): F at the striker or the butts is a hand game
    (`StrikerSession`: a needle sweeping a gauge, three swings, the top rings the bell;
    `ButtsSession`: hold a swaying aim on the gold against the wind, three arrows, scored by ring).
    AI turns are rolled from the skill now and then for whoever's watching. Visitors who see a
    turn enjoy it, to a cap. The best score of the night takes `prizeSilver` when the games close,
    and each game won adds a little Renown.
  - **The dance:** F at the bonfire joins it (`AvatarActionKind.Dance`). The renderer gives
    dancers a hop and a sway; there are no dance frames yet.
- **The ceremony** (`CeremonyStep`): the bell; each party still in town's **toast**, a line from
  its mean satisfaction, its members counted toward Renown at that moment (`FestivalSystem.Toasted`;
  `Reckoning.FinalCount` passes over them); the **honours** (the games' winners, the finest work on
  the show table, the busiest counter by silver earned (`RoundSystem.EarnedBy`), anyone called back
  from the dead); `Accusation`, a no-op kept for M4's traitors; the **verdict**, read by the crier
  (`RoundSystem.Crier`), which keeps its time whatever's left of the toasts. The round still ends at
  `EndTick`; a clock that jumps past the ceremony runs its steps at once, and the festival starts at
  its hour even when the clock jumped past it. The reveal lists the honours (`RoundSummary.Honours`,
  save format 20).
- **HUD and sound:** the clock names the part ("The festival: the toasts"). The festival's music
  plays within 24 cells of the well (elsewhere the evening's own), with a crowd's murmur out in the
  square; the bell rings from the square at the opening and the ceremony.
- **Bots** lay a piece of their own work on the show table, have a go at each game (they play the
  hand games), then gather for the ceremony.
- **Dev:** "Festival now" in the debug window (`DevFestivalCommand`), and `--festival-at SECONDS`.
- **Fix:** the innkeeper's F at the bar offered "Reach under the counter for stone bridge" once
  the cudgel was out, and threw when pressed: `FirstOrDefault` on an empty stash gave a default
  `Entity` (index 0, a real one). The AI's `Stashes.ArmFrom` had the same slip.
- `FestivalTests`, `TradeWeaponTests.AnEmptyStashOffersNothingToReachFor`. Bench state hashes
  unchanged; the full hashes moved for the festival's saved plan: 1234 `A3387098B3207AA8`, 99
  `74315EC270163CE8`.

### Cook pass, phase 1: flavours, tastes, sniffing, rations (2026-11-13)
The cook had two games and nothing that left town; the other trades five to seven, and work that
goes out on quests. Phase 1 of the plan (DESIGN.md, "Cook"; ROADMAP.md, "The cook pass"): reach and
depth.
- **Flavours** (`Work/Flavour`): savoury, sweet, hot, fresh, on the ingredients' defs
  (`"flavour"`). A pot tastes of the blend of what went in (food by its nutrition), as a drink is
  the sum of its adds; a bowl carries it (`Dish`). Emberroot, herbs and honey are seasoners: up to
  three to a pot, they flavour what's there and add no bowls, and the work key offers them only
  where they'll go in. Quality's "variety" (how many kinds) is now how rounded the flavour is. The
  pot panel, the inspector and `PerceptionView.Tastes` say it in words ("savoury, with some heat").
- **Diners' tastes** (`Hospitality/Tastes`): each adventurer kind likes something
  (`adventurers.json` `"tastes"`). About half say what they fancy with the order, in its words, so
  whoever hears the order hears that too (`Order.Craving`, saved). A bowl that tastes of it pleases
  them; one that doesn't, a little less.
- **Sniffing** (`Work/SniffSession`): Space smells a stack (the larder's and the pack's Smell
  buttons) or the pot ("Smell it"): fresh, fine, on the turn or off, as rough as the nose is
  unpractised. A pot only smells good or "not quite right".
- **Rations for the road** (`Work/Rations`, `Work/PackSession`): at the new prep table in the inn's
  kitchen, three portions of food folded into a cloth on cue (A, D, A, D) and tied with a cord
  drawn by holding F. The cord reads slack, snug or biting; tied at snug's slack end it lets the
  air in and turns twice as fast on the road, which never shows (`Ration.LooseWrap`). Rations sell
  like gear, one to an adventurer, and go out on quests as `RationRecord`s: sound, the traveller is
  a little stronger and comes home less hungry; turned, weaker, and home sick
  (`FoodPoisoning.Sicken`). The party's tale says which.
- **The AI cook** puts one seasoner from the larder in each fill (never the keeper's bar stock);
  the bot does too, and no longer sticks holding something the pot won't take. The larder opens
  with emberroot and herbs, and the carter brings more with the food. Neither packs rations.
- **Fix (test):** `CrisisTests.AFeverComesWithAPartyAndSpreadsToThoseNearby` moved its villager
  mid-walk, and the walk carried them off; it now ends their job first.
- Save format 21. `CookPassTests`. Bench hashes moved (behaviour): 1234 state `777B62DF500687D2`,
  full `7A4D703E6E889E7A`; 99 state `4B144711DB024EDD`, full `9067BDE801CF11E2`.

### Cook pass, phase 2: a name for the kitchen (2026-11-13)
- **The house dish** (`Work/HouseDish`): at a cooked pot, the cook can "Make it the house dish"
  and name it (`NameDishCommand`): only a pot that would ladle Fine or better, and only the inn's
  meal-server. The name and its flavour are kept on the inn and posted on the sign
  (`PerceptionView.HouseDish`). Any later pot whose flavour is near enough the same is the house
  dish again (the pot panel says so), and its bowls are marked (`Dish.House`). About a third of
  diners ask for it by name with their order (`Order.WantsHouse`, saved) and say whether they got
  it.
- **What was wrong:** each bowl remembers what most let its pot down (`Dish.Fault`: salty, bland,
  caught, underdone, flat), and a diner served a Plain or Crude one says it.
- **Tips:** served Fine or better within a third of their patience, an adventurer tips the server
  a coin (two for masterwork), drinks and meals alike (`ShopSystem.FineTip`, `MasterworkTip`).
- Save format 22. `CookPassTests` (phase 2); `InnKitchenTests`' hatch test counts the tip. Bench
  hashes unchanged (the bench runs no round).

### Cook pass, phase 3 begun: chopping (2026-11-13)
- **Chopping** (`Work/ChopSession`): F at the prep table with raw meat, fish or cabbage in hand
  (`"chopsInto"`) cuts it on the knife's beat, a few cuts plus one per piece. It becomes chopped
  meat, fish or cabbage (`"chopped"`): a little more food each, quicker to turn, and a pot cooks
  faster for its share of chopped food (`Cooking.SpeedOf`, up to half again). A wild cut may slip;
  each slip lands as a cut on an arm when the knife's put down. With choppable food in hand the
  table chops; otherwise it packs rations.
- Save format 23 (a stew session keeps its pace). `CookPassTests` (prep). Bench hashes unchanged.

### Cook pass, phase 3: the spit and the menu; playtest fixes (2026-11-13)
- **The spit** (`Work/Roasting`): a new piece of kitchen furniture beside the fire (the larder
  moves along the wall to make room). Raw meat in hand goes on with F, up to four; F turns it, Space
  lifts it off (`RoastSession`). Four sides brown one at a time, read only as crust colour; the
  inside cooks unseen. Fat flares now and then and browns the side over it twice as fast. Scored
  by the crust each side shows and whether it's cooked through (`Roasting.QualityOf`, `FaultOf`).
  Left on the spit (`SpitRoast` on the spit, `RoastSystem`), the side to the fire chars. A roast
  comes off as slices (`Roast`), no bowl.
- **The menu** (`Shops/Menu`, `DishPrices`): dishes with `"menu"` props go on the sign once their
  kitchen has what makes them and the cook ticks them on (`SetDishPriceCommand`, the sign window).
  About a third of diners ask for one by name, at its price (`Order.Dish`, saved); only that dish
  fits at the hatch, and handed something else they say so.
- **From the first playtest:**
  - *People wandering into the kitchen:* idle adventurers (and those waiting on an order) loiter
    within a few cells of the inn's middle, which takes in the kitchen. Furniture marked
    `"staffOnly"` (the pot, spit, prep table, larder, wash tub) makes its room staff-only, and
    loiterers never pick a spot in one (`AiQueries.StaffOnly`).
  - *Drinks but hardly any food:* a meal was asked for only below 0.6 food, and parties came in
    fed; a two-day round with an idle cook saw no meal orders at all. Now they arrive hungry from
    the road and want a meal at breakfast and supper when under 0.85 (`ShopSystem.Mealtime`): the
    same round sees a dozen. A mealtime meal that never comes costs them little.
  - *Orders at 3 or 4 in the morning:* nothing kept adventurers to the town's hours. Now parties
    don't ride in after dark, a party back from a quest at night camps till morning, adventurers
    ask for nothing in the small hours (11 to 6) but a room (till 2) or a bad hurt seen to, they
    turn in at night unless well rested (`AdventurerBrain.NightRestBelow`), and open orders'
    patience doesn't run in the small hours.
- **Tests:** `CookPassTests` (spit, menu), `InnHoursTests` (staff-only rooms, the small hours,
  patience overnight, breakfast). Several older tests assumed fed arrivals or a party leaving the
  moment a quest's offered; they now feed the party or give it time to set out
  (`ExpeditionTests.SendAndWait`). Arrival hunger is set without the party's random stream, so who
  arrives is unchanged.
- Save format 24. Bench hashes moved (the inn's new spit): 1234 state `7FBCA1D753F545A2`, full
  `2A7A081628943F5F`; 99 state `631948CD2DBEA002`, full `D9EA76D2C36AAFC7`.

### Cook pass, phase 4 begun: the oven (2026-11-14)
- **The oven** (`Work/Baking`): a bread oven with its own small wood fire (`"oven"`, `"fuel"`), in
  the kitchen's corner, refuelled like any hearth. Flour comes with the carter and opens in the
  larder.
- **Kneading** (`KneadSession`): two flour in hand at the prep table, pushed from one side then the
  other on the beat, eight times. Makes a ball of dough (`Dough`: how well kneaded, and when).
- **Proving:** the dough rises on its own, read by eye in the inspector (`PerceptionView.Dough`):
  well risen after an hour or two, sunk after three.
- **Baking** (`BakeSession`, `OvenLoad`, `OvenSystem`): into the oven with F, then nothing but the
  smell (`PerceptionView.Oven`). Space takes it out: two loaves, scored by the kneading, the
  proving and the bake (`Baking.QualityOf`); a new fault, heavy (`DishFault.Heavy`), for dough
  that didn't rise right. Left in, it burns.
- **Bread** is on the menu where there's an oven, and keeps: a bundle of rations with bread in it
  (`Ration.Keeps`, carried on the quest record) turns half as fast on the road.
- Save format 25. `CookPassTests` (the oven). Bench hashes moved (the oven): 1234 state
  `7759FA059B13F494`, full `CEBC1389C7C3AF05`; 99 state `262A4AEE206A5686`, full
  `85B38CBD45E13729`.

### Cook pass, phases 4 to 6: the menu, the pressure, the town (2026-11-14)
- **The rest of the menu.** Porridge: a pot that's mostly grain (`ThingDef.Grain`, oats) serves
  it (`StewPotProps.Grains`); the breakfast dish on the menu (`MenuProps.Morning`, `Menu.Asked`),
  asked for before noon only. Pies (`Work/Pies`, `CrimpSession`, `RawPie`): dough and two portions
  of raw food crimped at the prep table, baked by smell into slices of the filling's flavour
  (`OvenLoad.Pie`; a new fault, leaked). The butcher block (`Work/Butchery`): a side of pork jointed
  (`ButcherSession`), a fowl plucked (`PluckSession`, its tear point hidden); an AI cook dresses
  without the games (`DressJob`). Dressed fowl roasts (`ThingDef.Roasts`). The spit, oven and prep
  table take what's in the pack, and show panels saying what they want; the prep table's steps
  are buttons (`PrepCommand`).
- **The fire's old recipes are gone:** simple meals, stew over a hearth, the cooking pot upgrade
  (the `upgrades` mechanism stays, unused). The shrine's fire is a hearth, not a campfire that
  baked the room.
- **Pressure in the kitchen.** A second pot, over the oven. Kitchen fires are fed only by the
  kitchen's cook (`WorkGivers.FeedsHere`), and what's kept in a staff-only room is the cook's alone
  (`WorkGivers.KitchenStock`, in `AiQueries.FindNearestItem`); a pot's flame sets its fire's burn
  (`Cooking.FireDraw`); a log basket, stocked by the carter. Grease fires (`StewPot.FlareUntil`,
  `Cooking.Flares`/`Lid`/`Catch`, in the stew as `StewSession.Flaring`). Rats (`Hospitality/Rats`:
  `Vermin`, `RatSystem`, a `Rat` creature; `RatJob` for an AI cook); the shortage spoils a quarter.
- **The kitchen and the town.** Feasts (`Hospitality/Feasts`, `FeastDue` from a triumphant
  `ExpeditionSystem.Return`, a `Feasted` thought). Broths (`Work/Broths`, `Nourished`, in
  `HealthSystem`). Keeping the larder (`Work/Larder`: `BuildingProps.KeepsAt` in
  `RotSystem.KeptAt`, salting as a prep step, `SmokeSystem` and the smoking rack; `ThingDef.Salty`
  seasons a pot). Word of the food (`ShopSystem.Fare`, brought into `PartySystem`'s arrivals).
- Also: the oven and forge show their fires (an open mouth, an open-topped hearth); the chopping,
  kneading and baking windows give more to go on.
- Save format 30. `KitchenMenuTests`, `KitchenPressureTests`, `KitchenTownTests`. Bench hashes
  moved: 1234 state `BA24F56CFFF133BF`, full `08017AFC6BAB552C`; 99 state `2DE3D58EF6E57094`, full
  `55E942DFA8C0D5A3`.

### From the second playtest
Notes from playing; the healer first.
- **The healer, AI and asked for.** Done: the AI empath lays on hands (`Healing/LayOnHands`):
  it draws what its mana will mend, weaves it away on itself and binds the rest; it sees to badly
  hurt villagers unasked and mends what it carries as its mana comes back. Players ask with F at a
  shrine pallet, and that order waits on the wounds, not the bleeding; the HUD says so when you're
  hurt, and bots ask too. Left: the AI healer weaving charms, and tuning the mana (a busy day
  empties it).
- **Buckets against fire.** Done (`Environment/Buckets`): four hang at the well; F there fills
  one; with a full one, point and hold F to swing it back, let go to throw (the swing decides how
  far). The water knocks a patch's fire down and soaks it, so it won't catch for two hours: soak
  the roofs ahead of the dragon. The AI fights fire with buckets when there's one to be had, and
  takes them back to the well. Left: bots throwing water, seeing it against the dragon in a
  running game, a sound for the splash, and the water drawn in flight.
- **The practice yard.** Done (`Scenarios/Practice`, from the title menu or `--practice TRADE`):
  Millbrook in your trade with no season, Renown, Dread or crises, and no autosave. Its panel
  lists the trade's tips and sends customers set up for the trade (hurt, worn blades, a strongbox
  and a curio, hungry), restocks the workplace, freshens you up, and brings the healer a patient
  with the wound asked for. Left: a step-by-step walk through each game (the smith's since done, below), and the cook's and
  innkeeper's own setups (a pot already going, a named drink asked for).
- Done: the ghost's speech crashed the game (a bubble over a speaker with no body); touchpad zoom
  eases in, and the view stops short of the whole map with a character. Also steam deck d-pad fix.

### The festival, from its first playtest (2026-11-14)
It wasn't clear what to do at first, then it was over too fast. Now:
- **The crier tells the season.** The festival begins at 15:00 on the last day. The bell's heard
  everywhere, and the crier (an AI villager, never a player's character: `RoundSystem.Crier`) is
  chosen then. After `gatherMinutes` the town stands round the well (`Ai/Festival`, `Telling`) and
  hears the season a line at a time (`FestivalSystem.Tale`): the parties, their quests, each crisis
  met or not (`Reckoning.Crises`, saved), the town's dead and those called back. Then what's on and
  where, and the revels begin with a message to everyone.
- **What to do, shown.** A festival card at the top (`VillageGame.Festival`): the gathering, the
  tale, then each game, the dance and the show table on a line (how, and how you're getting on;
  click to look at it), and when the toasts come. Markers over each in the square
  (`WorldRenderer.Markers`), green once done. Through the revels the crier calls folk to each thing
  in turn (`callMinutes`), and a new leader at a game is news (a message).
- **The show table.** F there lays out your best piece from your pack (`AvatarActionKind.ShowPiece`)
  or what you hold that you made. Empty-handed with nothing to show, it says what it's for. A
  visitor's first look at a piece is always remarked on, with a message naming the maker.
- **Time to read.** A line said aloud stays over its speaker's head for as long as it takes to read
  (`SpeechSystem.ReadingTicks`: 4 s plus a little a letter, up to 16), only each speaker's latest
  line shows, and shouts wrap to six lines rather than being cut. The tale and each step of the
  ceremony wait for their line to be read (`beatSeconds` between), and the round ends a few seconds
  after the verdict's been read (`FestivalSystem.DoneTick`, `Finished`), not on a fixed clock. The
  festival can run 7.5 hours, so up to five real minutes.
- **Toasts wrap.** Narsil's `ToastStack` wraps a long notice to four lines (`Gui.WrapLines`) and
  keeps it up longer for each character past sixty.
- `--festival-at` stands you by the well (`DevFestivalCommand(Bring)`), where you'd otherwise start
  the festival shut in your shop seeing nobody.
- Save format 31. `FestivalTests` (the tale, the crowd gathering for it, laying out work, the
  ceremony's pacing, saving mid-tale). Bench state hashes unchanged; full hashes moved: 1234
  `B0173335CD84DC78`, 99 `38895A58EC1B09A7`.
- **The square, tidied; the feast, laid.** The town's starting stores (the valley's settlers' pile:
  food for the first days, planks, the bow, the rod) had been dropped at the map's centre, which in
  Millbrook is the square, and lay about it all season beside the feast tables. A built town puts
  them in its stockpile in the barn (`MapGenerator.Options.PileAt`). An AI villager's trade kit
  stock (the smith's bars, the healer's bandages) goes in their workplace's storage, not a pack
  that's emptied after every job (`RoundSystem.WorkplaceStore`). The feast is on `FeastTable`s
  now: clothed trestles laid with roasts, bread and pies (`festival.json` `feast`,
  `FestivalSystem.LayTheFeast`), on show like the show table: no job clears them, anyone can help
  themselves by hand. `FestivalTests` (the square, the feast). Bench hashes moved (the pile's
  place): 1234 state `A6E67838D1ABD0A8`, full `03C9963BA8B970EA`; 99 state `FF8D045D36C9C535`, full
  `24B36AB2CA3F1C41`.
- **Sitting, and looking.** F at a free seat in reach (a stool, anything `seat`) sits you on it
  (`AvatarActionKind.Sit`, `SitJob`, the seated pose) until you walk off. "Look at" (the festival
  card, a toast, the message log) holds the camera there until your character moves or eight
  seconds pass (`VillageGame.LookAtCell`); before, the follow snapped it straight back.

### The festival: the tale waits for the players (2026-11-14)
- **Nobody misses the start.** After the gathering the crier holds the tale until every player
  who's up and about would hear it plainly from where they stand
  (`FestivalSystem.PlayersInEarshot`). Players asleep, downed or dead aren't waited for, and the
  crier gives up after `taleWaitMinutes` (`festival.json`, 30). Meanwhile the festival card says
  the crier's waiting.
- **The tale so far.** The card has a fold with the lines told so far (`FestivalSystem.Told`, from
  the tale already saved), for anyone who came late or missed a line. The tale is all public.
- `FestivalTests`: the crier waits for a player out of earshot, starts when they come, and waits
  only so long; a save mid-tale keeps what's been told. No new save state. Bench hashes unchanged.

### The festival: the band at the well (2026-11-14)
- **Music from the square.** Narsil could already place a looped sound, so the festival's music is
  now a band held at the well (`festival.band`: a festival track streamed, looped and downmixed to
  mono on the music bus, `range` 40, `near` 8). It's heard fading as you walk away and muffled
  indoors. Within 24 cells the evening's own music fades out and leaves the band. Without band
  tracks (music not fetched), the `festival` playlist plays near the well as before.
- `--check-art` treats a streamed music-bus sound with no files like a playlist with none: a
  warning, not an error. The band lists only the free tracks until ours exist (docs/AUDIO.md).

### The festival: the labourers put it up (2026-11-14)
- **Setting up that afternoon.** From `festivalSetupHour` on the last day (`season.json`, 11:00),
  everything the map lists becomes a construction site in the square, with a message
  (`FestivalSystem.LayOut`, `Sites`). The festival's things come out of store, not the town's
  woodpile: each site starts with its materials and wants only the work.
- **The labourers' job.** Only a trade that gathers takes a festival site (`FestivalSystem.PutsUp`,
  `WorkGivers.Construct`), so the shops stay open until the bell. A built site goes back to the
  festival (`SiteBuilt`) with no "Built" message, and unlit.
- **The bell is the backstop.** `PutUp` finishes any site still going, and places anything with no
  site where it goes, then lights it all as before. Cancelling a festival site refunds nothing.
- Save format 32 (the sites). `FestivalTests`: the sites go up at the hour; a town with its
  labourer gets most of it up before the bell, unlit; only labourers build them; a save mid-build
  ends with each prop up once and lit. Bench state hashes unchanged; full hashes 1234
  `0628FB9242D11B2C`, 99 `FF4AE94FB3201EAB`.

### The festival: real arrows at the butts (2026-11-14)
- **Shot from a mark.** The butts are now played with real arrows. A shooting mark (a red cord,
  `ShootingMark`) goes up `markCells` out in front of the butt (festival.json, 8), tried straight
  out first so the lane runs true. F there starts a turn (`ButtsTurn`); the festival lends its bow
  (`lends`) to anyone without one and takes it back after. Draw and loose as ever; three arrows.
- **Scored where they land.** A building with `target` (the butt) stops an arrow nearer than anyone
  in its line (`Reach.TargetAlong`, from `Blows.Land`). Arrows fly flat and true, so the score is
  how far across the face it lands, by the old rings (`FestivalGames.RingScore`). An arrow wide,
  or into someone in the lane, is a wasted arrow, and that someone is hit as in any fight.
  Walking off the mark, being felled, or the ceremony's bell ends a turn, scored on what's been
  shot.
- **Seen and heard.** The landing floats its word ("Gold!", "8", "On the straw") with the bow and
  a thump, not a fight's din or the danger music. Arrows stay in the face (`FestivalSystem.Stuck`,
  saved) until the ceremony. The festival card says how, and counts your arrows while you shoot.
- **The lane.** The butt moved to the square's open south-west (31,49, facing east), off the feast
  tables (`tools/village_map.py` checks the mark's cell). The revels keep out of the lane
  (`FestivalSystem.InLane`, an optional reject on `AiQueries.RandomReachableNear`), and the butts
  are watched from beside the mark.
- **Bots** walk to the mark and shoot at the gold through `BowCommand`. `ButtsSession`, the old
  hand game, is gone; a save made in the middle of one won't load.
- Save format 33. `FestivalTests`: the rings; three arrows with a lent bow scored where they land;
  a ring off the gold; someone in the lane takes the arrow; walking off ends the turn and the bow
  goes back; arrows survive a save; the lane is clear of the rest of the festival. Bench state
  hashes unchanged; full hashes 1234 `BC3A3396F6D11B2C`, 99 `1CC070DAD1201EAB`.

### The festival: bunting (2026-11-14)
- **Strung round the square.** The renderer strings bunting between the lantern posts
  (`WorldRenderer.DrawBunting`): each post to the next in a ring about their middle, if within 12
  cells, as a sagging cord hung with madder, ochre and woad pennants. It's presentation only, from
  where the posts stand. The posts' cloth flags are now a red cap on top.
- With that, the festival's list on the roadmap is down to its playtest. The dance and cheer frames
  went to the Combat pass's frames, and the ceremony's Accusation to M4.

### The festival, from its second playtest (2026-11-14)
- **The butts, easier to start.** F on or beside the shooting mark starts a turn whichever way
  you face (`FestivalGames.MarkBeside`). F at the butt itself ("from the red mark") walks you to
  the mark and starts it there. You're turned to face the butt, and a panel at the bottom (in
  place of the fight hint) says which arrow it is, your points, and how to draw and loose.
- **No accidents after.** Your own weapon goes back in hand when the lent bow's taken back, and
  combat mode is left as it was before the turn (`ButtsTurn.Before`, `WasDrawn`).
- **Cheering the toasts.** After each party's toast a player may cheer it once (E, or the pad's
  wheel; a button on the festival card): a shout and a couple of hops
  (`FestivalSystem.Cheer`, `CheerCommand`). The town round the well cheers a warm toast on its
  own. It changes nothing in the reckoning.
- Save format 34. `FestivalTests`: a cheer for each toast, once; the town cheering a glowing one;
  F at the butt walks you to the mark and starts there, facing the butt; your own weapon back
  and put away after. Bench hashes unchanged.

### Lessons in the practice yard: the blacksmith (2026-11-14)
- **Guided lessons** (`Scenarios/Lessons`, words in `trades.json`'s `lessons`): pick one from the
  practice panel and it sets the scene (`PracticeAction.Lesson`: customers worn to order, the
  workplace restocked, materials in the pack), names the step at hand, rings its station or flags
  the customer, and ticks the steps off. The step is read every frame from what the player knows
  (`Lessons.Stage`, through `Perception` only), never advanced by a cursor, so a lesson follows
  you back: a blade still chipped after the anvil goes back to the fire, a refused one back to
  taking the order, a forge gone out to the fuel bin. Steps marked `aside` show only when they
  come up. `--lesson ID` (with `--practice`) starts one.
- The blacksmith's four: a dull blade (grindstone), a chipped blade (forge, anvil, grindstone),
  something new (choose, heat, hammer, quench, edge), and ore and prices (a bloom, then the sign).
- Practice customers for the smith come only with what the smithy can mend: the Vanguard's bronze
  spear (a bronze bar the smithy never has) arrives sound. `SendCustomers` takes a `CustomerWear`.
- Finished lessons are kept on the saved character (`CharacterProgress.Lessons`).
- `SmithLessonTests` play each through in Millbrook's smithy with a careful hand; `SmithyKit.Drive`
  can step between stations. Bench hashes unchanged.
- **Finding your customer.** A customer you owe a visit (you took the job, and have yet to collect
  their blade, box or curio, or it's ready to go back) gets an ember tag over them, through walls,
  or an arrow at the screen's edge when they're off it (`VillageGame.Errands`, `WorldRenderer.Pointers`).
  Where they are comes through `PerceptionView.Awaiting` (`KnownErrand`): you keep an eye on your
  customers. The order (~J~) has "Go and collect it" and "Take it back to them": a walk that
  follows them and does the handing (`SeeToCustomerCommand`, `SeeToCustomerJob`); moving calls it
  off. `SeeToCustomerTests`.

### From a multiplayer playtest (2026-11-14)
- **Sleeping through the night.** A player who lies down from 20:00 to dawn sleeps till dawn,
  rested or not (`SleepJob.IsBedtime`), so a full night's rest no longer gets them up at midnight
  and holds the night up for everyone. A rested nap in the day still ends at once. `NightTests`.
- **Aiming a bucket.** The throw no longer locks to where you pointed when you lifted it: the mouse
  points it as it swings (`AimThrowCommand`), and A/D turn it any way, not just the eight you face
  (`ThrowSession.Facing`; D turns clockwise as seen on screen). Save format 35. `BucketTests`.
- The healer's games are noted for a pass of their own (ROADMAP.md).

### Lessons for every trade (2026-11-14)
- **The rest of the trades' lessons**, on the blacksmith's frame (`Scenarios/Lessons`, words in
  `trades.json`). Tinker: a strongbox felt over and picked, a curio looked over, a lock pinned,
  fitted and turned, a crossbow. Innkeeper: an ale poured and handed over, a named drink mixed, a
  party sent on a quest. Cook: a stew from the larder to the hatch, a roast, bread. Healer: a
  wound felt, drawn in and woven, a fever, mana won back at the stone.
- The customer loop (wait, take, collect, the work, give back) is shared (`Lessons.Customer`), as
  are feeding a fire and "made since the lesson began".
- Lesson setups: `CustomerKind` adds thirsty customers and a party wanting work; a lesson can bring
  in a patient or spend the healer's mana. `~K~` names the quests key in text.
- Fixed: the order panel showed a stray "With you" line on orders with nothing to hand over.
- `TradeLessonTests`: every lesson begins on a step with words, and each is walked through.

### The healer's pass (2026-11-14)
From a multiplayer playtest that found the healer the weakest trade.
- **Laying on hands is one sitting** (`Healing/HandsOnSession`, replacing the feel and draw sessions):
  - **Skim to find.** A/D run the hands head to feet; near a wound there's only a warmth. W/S jump
    to the next warmth.
  - **Press to read.** F held with the hands still makes a wound out: its kind beats through the
    hand the way it'll pull when drawn (`PulseShape`: a cut's jolt, a bite's double tear, a
    bruise's swell, a burn's pull, frost's numbness, venom growing), and how bad it is. A dressed
    wound takes a longer press to find festering under it. Pressing while the hands move is rough:
    the patient flinches (`RoughHands` thought), the hands are thrown off and a half-read wound is lost.
  - **Keep pressing to draw.** On a wound read, the link opens (`DrawLink`, the old draw's physics):
    A/D steer composure, letting go rests it, and composure and strain carry from wound to wound.
    A wound drawn whole lands as it comes across (`WorkSession.Settle`, sim-only, each tick).
  - The inspector has one "Lay on hands" button; the window lists what's been read. The Carried
    tab shows the mending your book says answers each feeling. Save format 37 (an old feel or draw
    in a save is dropped on load, `RetiredSession`). Bots and the lessons play the new flow.
- **The AI empath** sits at the meditation stone when its mana runs low (`MeditateJob`), and in
  quiet hours weaves charms and puts two out for sale at the shrine (`WeaveCharmJob`).
- **The shrine, rebuilt** (`tools/shrine_art.py`): 11x7 in pale limestone on its own kit
  (`shrine_stone`: a carved band of violet runes, dark carved oak at the corners, a steep turf roof
  with tall gable horns), so it looks like nothing else in Millbrook. Inside: the rune altar, two
  herb racks, a linen basket (where the shrine's bandages start), a wash basin, three pallets, and
  candles on the walls. The shrine and the straw pallet have icons of their own.
- **The healer's scenes**: one scene for laying on hands (warmths, each read wound's own mark
  beating in its pulse, both hands, the link's thread of light fraying red off balance, a flash
  as a wound comes across). In the world, light drawn from patient into healer, runes flaring
  over the hands as they're woven, a stillness round a meditating empath (`WorldRenderer.Shrine`).
  Sounds: a pulse under the pressing hand, a chime as a wound comes across, the link's snap
  (`heal.pulse`, `heal.drawn`, `heal.snap`).
- **Food with nobody playing the cook:**
  - Every cottage has a **bread crock** (`Homes.Crock`), stocked at the deal and topped up by the
    carter each morning. F at it, hungry, eats. No job takes from it.
  - With a player's character hungry, the AI cook keeps **two bowls up on the hatch**
    (`Hatch.Spare`), and a player's order there takes one at once.
  - Hungry with no food on you, a **tag points to the nearest food** you know of (`VillageGame.Food`).
- **Cutaway walls** are cut off flat (`PropModel.Build`'s `cutAt`): no sliver of a wall plate
  floats over the cut any more.
- Behaviour changed, so `--bench` hashes moved.
- **The shrine's pieces do something** (a playtest's question), each said in its description:
  - The rune altar: weaving within reach, the pain you carry stirs the heartbeat half as much (`CastSession.AltarHelp`).
  - A herb rack: a wound bound within reach gets a poultice, a better dressing (`Bandaging.ShrineHelp`).
  - The wash basin: a wound bound within reach is washed first and won't fester.
  - The linen basket holds the shrine's bandages. The healer's corner, at the head of the end
    pallet, has the basin, a herb rack and the meditation stone all in reach.
- **Pointing at a thing** (furniture, an item, a building) a moment shows its name and what it's for
  (`VillageGame.HoverInfo`), when the cursor has nothing more pressing to say.
- **The practice yard, for the new healer:** patients with several wounds or one festering under a
  dressing (`PatientCase`), and lessons for them: "Many hurts" (W/S to the next wound), "Under a
  dressing" (the long press), and "The shrine's help" (bind at the corner, weave a charm at the
  altar, put it out for sale).

### Round options: friendly fire (2026-11-14)
- `Round/RoundOptions`: the host chooses the kind of round in the lobby (`SetRoundOptionsCommand`,
  host only, lobby only), saved with the round (format 38; older saves keep friendly fire on).
- **Friendly fire**, off by default: the town's blows pass through the town (players and
  townsfolk alike), and an arrow flies on past them to whoever's behind. Settled where a blow lands
  (`Reach.Harms`, used by `Reach.Struck` and as a guard in `CombatRules.Strike`), so every peer
  agrees. An AI villager no longer holds its blow for a friend who can't be hurt. Raiders, beasts
  and the dragon are untouched, and outside a round (tests, demos, the arena, the bench) blows hurt
  everyone as before. `--friendly-fire` turns it on from the command line.
- **Traitors**: in the lobby as a greyed-out choice, "Coming with Whispers"; always off until M4.
- At the festival's butts, someone in the lane only takes the arrow with friendly fire on.

### Combat balance (2026-11-14)
- **The sweep:** `--combat-sweep [--seeds N] [--seed S] [--party|--no-party|--dragon|--duels] [--log]`
  runs `Scenarios/CombatSweep` headlessly. It covers a day-two raid on the unattended town with
  and without a party, the dragon both ways, and AI-against-AI duels in the ring.
  `Director.PassOverBeats` and `PartySystem.HoldArrivals` keep a trial to the one crisis it sends.
  `--log` shows who went down doing what. `CombatBalanceTests` holds the band over 16 seeds.
- **Read from the log:** most raid deaths were villagers bleeding out an hour or two after they
  fell, while everyone near a raider kept hiding. Now someone hiding goes out to stanch a downed
  villager's bleeding when no raider is within 6 cells of them (`Defence.Aid`, a braving
  `TendJob`), and gets back under cover when one comes within 4.
- **Cornered while hiding:** anyone hiding or running with something in hand fights back once a
  raider's on them (`Defence.Reconsider`). Before, they were cut down without lifting the knife
  in their hand.
- **Bowmen shoot:** a defender fights with what they hold (`DefendJob`), so a bowman no longer
  punches.
- **Footwork:** an AI stepping in to strike sidesteps round a friend in its blow's way
  (`FreeFight.Steer`, `Reach.FirstFriendInTheWay`).
- **Dodging by skill:** an AI dodges at the old miss odds times its Melee over average (0.3 to 2),
  halved with nothing in hand (`Blows.DodgeSkill*`). This is what makes heroes in town matter.
- **Raids and the dragon:** raids are bigger (`raidPointsFactor` 0.85 to 1.3). The dragon is
  driven off at pain 0.5 or 60 damage (was 0.3 and 30).
- **Tried and dropped:** fetching a weapon before fleeing (no difference: the muster arms people
  at the bell), and hurt hiders charging or running further (slightly worse).
- **Seeds 1–48, Normal, before and after:**

  | | Before | After |
  |---|---|---|
  | Raids, party in town (seeds 1–8) | met 2/8, town dead 12 | met 30/48 (62%), town dead 23 |
  | Raids, no party (seeds 1–8) | met 2/8, town dead 12 | met 14/48 (29%), town dead 58 |
  | Dragon, party in town | 0/8 | 27/48 |
  | Dragon, no party | 0/8 | 0/48 (nobody's hurt either) |

### AI footwork (2026-11-15)
- **The hurt drop back:** past pain 0.3 (or moving below 0.7) a fighter is wary
  (`FreeFight.Wary`): it hands its turn (`Engagement`) to a friend still pressing the same foe and
  hangs back at `WaryHold`, unless it's the last one at them or the foe is pressing it. Broken
  (`RaiderBrain.WantsToFlee`), it backs off facing the foe for up to `WithdrawTicks`
  (`FreeFight.BackAway`) before it turns and runs. Raiders and the town alike (`Job.FightFree`).
- **The dragon circles:** on the ground after its breath it fights freely (`DragonPassJob`): it
  circles whoever comes at it between bites and lunges in when its jaws are ready, going after
  people within `GroundReach` of it and `Leash` of where it landed, and takes off from the grid.
  Something big holds further off while it waits (`FreeFight.HoldFor`). Four may press a dragon
  at once (`Engagement.OnDragon`).
- **The unarmed mob a raider:** unarmed townsfolk out in the street within `MobRange` of a raider
  gang up on it with their fists when the town there makes `MobSize` (those already fighting it
  and those who'd join, `Defence.Mob`); someone running rethinks when it does. Alone or two, they
  still get away; indoors, they stay in. Not the dragon, not beasts.
- **Tried and dropped:** a size-scaled wall circle for the dragon (under half a cell, it still
  fits a one-cell gap: the leash keeps it in the open instead); a tighter circle and a 1 to 2 s
  stand after each bite (12 to 14 of 48 met, no real gain). A wary fighter first gave up its turn
  even with the foe on it, and was cut down without swinging (raids with a party: 39 town dead);
  now it fights back.
- **Seeds 1–48, Normal, before and after** (`--combat-sweep --seeds 48`; duels unchanged):

  | | Before | After |
  |---|---|---|
  | Raids, party in town | met 30/48, town dead 23 | met 30/48, town dead 30 |
  | Raids, no party | met 14/48, town dead 58 | met 18/48, town dead 75 |
  | Dragon, party in town | met 27/48, town dead 0 | met 12/48, town dead 5 |
  | Dragon, no party | 0/48 | 0/48, town dead 5 |

  Seasons (`--season-sweep --seeds 8`): won 4 to 2, the dragon met 3/8 to 1/8, raids 9/10 to 8/10,
  deaths 1.9 a round either way. The dragon is harder: a moving target makes blows miss. Left to
  the tuning pass, with these levers: `WaryPain`, `WaryHold`, `WithdrawTicks`, `GroundReach`,
  `Leash`, `OnDragon`, `MobRange`, `MobSize`, and the dragon's `FleeDamage`/`FleePain`.

### Solo playtest notes (2026-11-15)
The tuning playtest's "alone first" pass (PLAYTEST.md), and what came of it.
- **Laying on hands, felt:** the palms glow warmer and beat quicker as they near a hurt (the
  session's `Feel`, until now only a line of text), a warmth blooms where one's first sensed, and
  the gift's hum swells and brightens on the way to one, with a soft beat as each is found
  (`HandsOnScene`, `WorkSounds`).
- **The gift and the inspector:** the inspector reserved 400 px whatever it held, and a journal
  panel kept the size it opened at, so the gift opened with no wounds stayed one line tall: drawn
  wounds were there but out of sight. The inspector fits what it shows (up to 400 px); height-less
  journal panels fit theirs every frame.
- **A weave for no wound:** the gift's own Weave button wove for no wound, so a Knitting from it
  "found nothing to take hold of" with a cut to mend. Now a mending woven that way takes hold of the
  worst wound carried that it fits, as a torn charm does (`CastSession.Finish`), and a miss says why.
- **Bracing the span:** the level was carried along the span for you, so a wedge was a timing
  test. Now A/D carry it (let go and it settles on the nearest stone); F or W lifts, Space or S
  eases (`SpanSession`).
- **The dragon in the ring** sometimes came down outside the fence and sat there. A landing has to
  be somewhere its target can be reached from on foot (`DragonPassJob`), and the ring's rails are
  a `RingFence` its fire won't take (the wooden fence burned through and opened the ring). It no
  longer waits out 8 s after each breath with the player more than 5 cells off: in the ring it comes
  after you anywhere (`GroundReach`/`Leash` 14) and takes off again after 3 s alone. On the ground,
  in the ring and on the town, it varies its blows (bite or claws, at random) and breathes mid-fight
  at anyone 3 or more cells off with a clear line, every `BreathEvery` (6 s; 8 s on the town).
- **The dragon in a round** made a pass, breathed once and waited. Set on the town now (`Rampage`):
  three passes over roofs and trees in one flight, never touching down between them, breathing
  from the air as it crosses each (`DragonStrafeJob`, legs from `Navigation.AloftRoute`), then
  down on open ground by the well for good. There it goes after whoever's out (further and longer
  than in the ring), on foot through the streets, and burns buildings when nobody comes. It never
  goes through a door or into a house (`DragonBrain.TooTight`): anyone indoors, it burns out. Hurt,
  or seeing three or more out together, it comes down early on them. The ring's dragon is as it
  was. Seeds 1–48, Normal (`--combat-sweep`; raids and duels unchanged):

  | | Before | After |
  |---|---|---|
  | Dragon, party in town | met 27/48 → 12/48 (footwork), town dead 5 | met 15/48, town dead 17, heroes dead 15 |
  | Dragon, no party | 0/48, town dead 5 | met 1/48, town dead 30 |

  It does real harm now. If that's too much, the levers are `DragonBrain.Passes`, `Crowd`,
  `Hunts`, `DragonPassJob.RampageReach`/`RampageLeash`, and `FleeDamage`/`FleePain`.
- **Seeing it fly:** sight covers two levels up and down, and the dragon's passes are five to seven
  up, so it was never seen (nor drawn) in the air. Anyone under open sky now sees something
  flying overhead within `Perception.SkySight` (28 cells in daylight, less at night, by the light
  where they stand: the light map isn't kept up in the sky), unless something's in the way.
- **Water on the dragon** did nothing (a splash only put out fire). Now a bucket landing on or
  beside it (`Buckets.Splash`) puts its fire out for `DragonBrain.DousedSeconds` (15 s: it can only
  bite, and a breath it's drawing goes up in steam), and `SoaksToDrive` (4) soakings see it off
  (`Doused`). Only once it's down: the air's out of a bucket's reach. The AI's buckets go on fires,
  not the dragon, so the sweep's numbers are unchanged.
- **Crisis banners:** the "Dragon!" headline was placed where it spawned, at the map's edge, so
  nobody witnessed it and the banner never showed. It's for the whole town now, as a raid's bell
  is, and so is "Fire!". "Rats in the larder" is told to the cook, wherever they are. (A line told
  to someone reaches them wherever they stand; anyone else's only if it happened in sight.)
- **Toasts in co-op:** messages had no addressee, so another player's "You draw the wound into
  yourself" turned up as yours if you could see them. A line can be told to someone
  (`MessageLog.Tell`, `Message.To`, saved on a spare bit of the kind); second-person lines are, and
  only they see them. The playtest log marks whose they were.
- **The practice yard:** its intro lines are gone. Six healer lessons became four (the fever joins
  "Several wounds", meditation opens "Use the shrine"), and every trade's lessons are plain
  instructions now, out of character; the flavour stays in the game.
- **The Veteran** felt easy-ish to a practised action-game player. Left as it is.


### Water and dragon-fire effects (2026-11-15)
- **A bucket's throw is seen:** the water used to vanish and the fire just go out. Now it flies from
  the thrower's hands in an arc, shedding drops, and lands in a splash and a spreading ripple
  (`fx/droplet`, `fx/splash_ring`, `WorldRenderer.Water`). Where it hits fire, steam boils up and
  settles over a couple of seconds, and the flames burn on till the water arrives (the sim puts
  them out at the throw; the renderer holds them for the flight). `Buckets.Splash` records a
  `SplashEvent` in `Simulation.RecentSplashes`, render-only like `RecentBreaths`: not saved or
  hashed, and nothing in the sim reads it. Heard too: `water.splash` and `water.douse`
  (`sfx_synth.py`), as it lands.
- **Dragon fire:** the breath's jet is now a stream of particles that come out white-hot and cool
  through the flame's colour to dark red, with black smoke rolling off behind. A flash shows at
  the jaws, and fire, embers and a billow burst where each ray strikes, leaving a scorch that fades
  over ten seconds. While it draws breath, hot specks are pulled into its jaws and the air over them
  shimmers (pale puffs: the post pass has no distortion). Nearby, the camera rumbles, on the
  Screen shake setting. `Particles` gained a colour ramp over life, a floor for droplets, a
  per-second emission helper (`PerFrame`), and room for 4096.


### From two multiplayer rounds (2026-11-15)
Four players through the relay (~100 ms, no desyncs). Round one went to the reveal; in round two a
player vanished on day two and it ended on day three.
- **A downed healer**, bandaged, still couldn't get up. A downed empath now weaves a mending on
  themselves (a narrower beat) and can meditate.
- **Truing a wheel** was unclear and hard. It now stands still: A/D move to a spoke, W/S tighten or
  slacken it, F spins it and chalks the rim.
- **Wood:** villagers took the barn's wood before players could. The round's spare beds were
  blueprints that ate ~140 logs in its first hours; they're now built at the deal. The barn
  starts with more (`season.json` `barnWood`), the woodcutter brings `woodDelivery` each morning,
  a player's take beats a villager's fetch (`Ai/Claims`), and what's put down by hand lies two
  hours before it's hauled.
- **Tinker games "left unfinished" in co-op:** the work predictor ran ahead of a lagging sim and
  its batches were dropped. It now stays near the sim's clock and resends what wasn't played.
- **A second party couldn't be sent on a quest:** its ask lapsed overnight and a party only ever
  asked once. Now it asks again, and the board says why there's no button.
- **Flicker at wall corners** (dark spots, worst upstairs in the inn with the walls cut down): a
  corner post and its arms shared faces. The post now stands proud and each arm a hair apart, cut
  down or not (`BuildingKit`). Furniture models drew boxes flush with each other (a shelf's boards
  and uprights, a keg's hoops); each box is now drawn a hair proud of the one before (`PropModel`).
- **Walking along outside the inn** cut every upper storey away a step at a time
  (`UpdateFollowedLevel` counted building floors). Fixed, and the open building settles before it
  switches.
- Build tools are hidden for players until building's in a round (`BuildTools`). Guide highlights
  (Settings) glow on what needs doing, for the healer so far. Lay on hands is an ability in the
  gift: click it, then someone.
- **The downed bled out:** eleven adventurers and four townsfolk died where they fell, tended by
  nobody but a player. AI first aid, bandaging and rescue now take downed adventurers too, and a
  blow at someone standing goes over the downed at their feet (they were being mangled by the
  fighting over them). A 16-seed sweep's Dread peaked at 69, against 61 before: within noise.
- **Net notices** (joins, drops, reconnects) now go to the log: a player vanished from round two
  without a trace.
- **Adventurers dying in town cost nothing:** six bled to death in round one, and the summary said
  "adventurers lost 0", since it counted only those lost on the road. The dead never leave, so they
  never had their say either: a death cost less than leaving unhappy. A death in town now costs
  what one on the road does (`renownPerAdventurerLost`, `dreadPerAdventurerLost`,
  `Reckoning.AdventurerDied`) and counts in the reveal.
- **Feasts:** none of three was laid on. The kitchen had 2 game hours (80 real seconds) for three
  or four dishes, and two of the calls came at dawn. Now it has 6 (`FeastSystem.Hours`).
- **Parties:** there were only two, so in round two the Gilded Spurs came three times, with two
  Osrics and two Tamsins. Four more (`adventurers.json`), and the next party is one sent least
  this round (`PartySystem.NextParty`), so none comes twice before all have come.
- Once the season's over, players' games stop sending move commands that every peer refuses (they
  filled the host's log through the reveal).
- 8-seed `--season-sweep` with all four: won 2 to 3, Renown mean 43.0 to 58.0, Dread peak 67.8 to
  63.5, adventurers lost (now counting the town) 2.0 a round.
- **The dragon,** met by two or three players with buckets, went down easily enough and felt good.
  Left as it is.

### Inn rooms: keys, the book and turning a room round (2026-11-15)
- **Keys** (`Hospitality/InnRooms`): the six rooms upstairs are locked, their keys on a board behind
  the bar and spares in a key box in the storeroom. A room is let by handing its key over
  (`ShopSystem.TakeKey`); the guest goes to whatever room the key opens. Given a taken room's key
  they walk in on its guest (`WalkInJob`): both lose satisfaction, and the next key's free.
  Leaving, a guest hands the key in on the counter, leaves it in the room, or walks off with it.
  The AI keeper takes the right key down, hangs loose keys back up, and fetches a locked room's key
  to work in it. Room keys aren't the hauler's (`IsAvailable`). Lock seeds come from a hash of the
  door and the round's seed, so fitting them leaves `sim.Rand` where it was.
- **The book** (`Hospitality/Ledger`): kept on the key board and read through
  `PerceptionView.RoomBoard`. A guest won't take a key till they're written in (`SignInSession`).
  The F window at the board lists the rooms, their keys and the book, with Write in and Strike out.
- **Turnover** (`Hospitality/RoomChores`): a guest's going leaves the bed unmade (clean linen to
  make it, the soiled sheets washed at the tub), the chamber pot full (`SlopSession` into an empty
  bucket, off the beat spills; tipped at the new midden behind the inn) and the jug empty (filled
  from a bucket of water). Ready rooms are let first. Left-behind things (`LeftBy`, `Tucked` in
  the sheets till it's stripped) are handed back for satisfaction or boxed as lost property.
- **The bot keeper** signs guests in, takes the key down and hands it over, and unlocks doors it
  has the key to (`Locksmithing.KeyedDoor(..., player: true)`).
- Bench hashes move (a behaviour change): seed 1234 state `3813191A36FA5D2A`, full `FE98358F8BCA658B`;
  seed 99 state `4F30080EDB097CF0`, full `1ED584621E2BAC85`.

### The kitchen and the inn, drawn (2026-11-15)
- **Stations** (`tools/kitchen_art.py`): the spit, oven, prep table, butcher block, log basket and
  smoking rack have icons of their own, and the rooms' key board, key box, washstand, chamber pot,
  midden and lost property box too; linen, soiled linen and the slop bucket have item icons. Models
  wear new faces: a brick oven with its mouth, a scored board, the block's end grain, wicker.
- **Scenes** (`VillageGame.KitchenScenes`, from `ui/work/*` in `tools/work_ui_art.py`): stew, roast,
  knead, bake, butcher, pluck, crimp, chop, pack, sniff, pour, mix, signing in and the slop pot each
  have a sprite scene over their gauges, drawn from the session alone. `--work-scenes` shows them.
- **The world**: steam off a pot by its surface, flames in a flare, the spit's joint the colour of its
  turned-up crust, an oven's mouth aglow and smoking while there's bread in, all through the new
  `PerceptionView.KitchenLook` (the renderer no longer reads `StewPot` or `SpitRoast`). Room keys hang
  on their hooks, a slept-in bed has its sheets heaped on it, a used pot shows, water stands in a
  filled basin, and ale runs from the cask while someone pours. `Rendering/KitchenLook` holds the
  crust colours for both.
- Not yet: the spoon, knife and quill in hand.


### Playtest fixes: serving across the bar, a fairer quest board, the pot and tub (2026-11-15)
- **Serving drinks** (`Shops/BarService`): F at the bar counter with a poured drink hands it across
  to whoever's waiting for one in reach, without finding them and opening the inspector: a
  mixed-drink order gets the mixed drink, your own orders first, then the line's order. F pointed
  at a customer serves them. The ale lesson and the keeper's tips say so.
- **The quest board** (`ExpeditionSystem.Roll`): a round's three quests are spread from easy to hard
  (put in order of how hard their kind runs, each taking its own share of its range), and the
  broker's first guess is within a quarter either way (`BrokerGuess`, was 40%). The whole board had
  often read as deadly, and no party would take anything. A 16-seed `--season-sweep`: won 5 (was 2),
  quests cleared 1.2 a round (1.1), failed 0.4 (0.2). Replacing cleared quests was tried and left
  out: an open quest feeds Dread every hour, so clearing one has to empty the board.
- **The stew pot** is a cauldron on a hearth-step: a stepped belly, an open mouth dark inside, lugs and
  a bail handle, in near-black iron. The stew shows inside it only while there's some to ladle
  (`KnownKitchenLook.Full`). The stew game's pot sprite is redrawn the same way.
- **The wash tub** is built like the quench tub: grained pine staves bound with hoops around grey
  soapy water sunk below the rim, on a low stand, a washboard stood in it; suds fly while someone
  scrubs.
- **Doors**: a locked door no longer swings open for whoever stands at it (a key-holder unlocks it as
  they pass). Upstairs, the boards run right across a doorway, so there's floor under an open door.
- Bench: state hashes unchanged; full `9C057DC56CA7791F` (seed 1234), `0FC614E584126363` (seed 99).

### Playtest fixes: the room chores (2026-11-15)
- **The jug**: F at a washstand wanting water, with a bucket of it in hand, fills the jug. Throwing
  was checked first and always won (`RoomChores.TakesWater`).
- **Where the leavings go**: carrying slops, a marker stands over the midden ("Tip the slops here");
  carrying soiled sheets, over the wash tub. The messages say where, and what the tipped bucket's for.
- **Signing in** is one press: F, and the quill writes "Gorran, room 3" on today's page in a moment.
  The choice that matters was the room; there's no beat to catch.
- **Making a bed** is three ways of working it (`BedStep`): hold F to drag last night's sheet off;
  F as the clean one billows to the top lays it flat (off the top, it flops and is shaken out
  again); A and D in turn tuck the head and the foot. The scene follows each.
- Bench hashes unchanged.

### Workplaces by town role; the bar's stock and the bar itself (2026-11-15)
- **Town roles** (`Defs/TownRole`): trades (`workplace`), services (`at`) and parties (`haunt`) name a
  role (Tavern, Kitchen, Forge, Shrine, TinkerShop), and building defs list the roles they fill
  (`roles`). Who works where is found by `ShopSystem.BuildingFor` / `WorkplaceOf`, the first standing
  building with the role. Millbrook's inn is both the tavern and the kitchen, so it played the same
  (bench hashes unchanged); a town with a cookhouse gives the cook a building of their own. The
  carter, feasts, the rats, the bot's upkeep and the round's stocking go by role, not the inn's name.
- **The bar's stock** (from the playtest): villagers ate the berries behind the bar. Its shelf is a
  `BarShelf`, a `kept` piece: food on it is for mixing, never taken to eat, to feed someone or to
  put in a pot (`Simulation.InKept`). Tankards are fetched from it as ever.
- **Millbrook's bar** runs the length of the common room's back wall, two counters long, with room
  behind for the cask, the bar shelf and the key board; the storeroom opens onto the keeper's side.
  The serving hatch moved to the kitchen wall's left end, the long table to the front right, and
  the bar's line runs along the counter toward the door.
- Bench hashes move (the inn's layout): seed 1234 state `57F4CB524D2257FB`, full `4187CA2B619AA4D5`;
  seed 99 state `C1BB56A18110F5C7`, full `38AAD123E2D4EA7D`; `--round` 1234 state `A5FCA9767F06D3FA`.

### A cookhouse, and an inn without a kitchen (2026-11-15)
- **`Cookhouse`** (role Kitchen): the Inn's kitchen a little roomier behind, a small eating room in
  front with a hearth and two tables, meals ordered and collected at the hatch in the wall between.
  Eating there is its own thought (`AteAtTheCookhouse`). Its default name is The Ladle.
- **`LandingInn`** (role Tavern): the Inn's common room, long bar, storeroom and rooms upstairs, with a
  scullery (wash tub, crockery shelf) where the kitchen was. The Dry Boots.
- A town that puts both up gives the cook a building of their own: signs, work, stock and the carter
  follow the roles (`CookhouseTests`). Millbrook keeps its inn. Bench hashes unchanged.

### Building on the water (2026-11-15)
- **Piled floors** (`building.piled`): a floor on piles is held up by the bed (ground, or the
  water's) within that many levels straight below it, however far from a bank. A finished piled
  floor is ground to build on, for a building that lays no foundation (`PlacementProblem`); a
  variant's empty part name leaves one out, so the cookhouse and the landing inn have a `Piled`
  variant whose floor is the deck.
- **`Boardwalk`** and **`Causeway`**: planks on piles, drawn as a timber bridge (`BridgeLook.Arched`
  false: thin rails, slim piles, no vault). They burn like any floor.
- **Breakable spans** (`building.breakable`): what a raid brings down (`Repairs.BreakBridge`), the
  stone bridge or a causeway, never a town's own decks.
- **Water sources** (`building.waterSource`): buckets are filled at a well or at a `DippingPost` on a
  deck's edge, and fire's fought from either; buckets hang at both.
- `LakeTests`. Bench hashes unchanged.

### Reedmere, a town on stilts (2026-11-15)
- **The map** (`data/defs/maps/reedmere.json`, from `tools/lake_map.py`): a shore with the road, the
  barn, the forge, the shrine and the cookhouse; a causeway out to a square on piles; boardwalks to
  the tinker's, five houses and the landing inn. Chosen on the title screen ("Which town?", before
  the trade and before hosting), or `--map Reedmere`; `--season-sweep` and `--bench` take `--map` too.
- **Map format**: a building or festival prop can stand at a fixed `level` (on a deck), and a legend
  entry can stack a floor and what stands on it (`"Boardwalk@2+DippingPost@2"`).
- **`tools/mapkit.py`**: the helpers both maps' scripts share (a canvas of grids, plots, gardens,
  the writer, the preview). Millbrook's JSON comes out byte-identical.
- **Tuning**: decks' flammability 0.4 (was 0.9): the dragon burned the whole town down and nobody
  out on the water reached the festival. The shrine moved to the shore: the wounded bled out on the
  bank before they could be carried across. 8-seed `--season-sweep --map Reedmere`: Renown mean 29.5
  (Millbrook 51.9), won 0 (2), Dread peak 71 (68). The walk from the inn to the cookhouse for every
  meal is most of what's left; the cook supplying the inn is next.
- `ReedmereMapTests`; `VillageDayScenarioTests` runs on both maps. Bench hashes unchanged.

### Buckets thrown like a bow (2026-11-15)
- A full bucket in hand: hold the right button (Heavy) to swing it back and forth (`BucketSwing`, the
  same 1.6 s swing), walking as you like (at a drawn bow's pace), and click the left (Strike) to throw
  it the way you aim, as far as the swing has gone (`BucketCommand`, `Buckets.Prime`/`Throw`). Your
  character faces the mouse while it swings; a bar shows the swing and the landing patch is outlined.
  Clicking a cell no longer throws; `ThrowSession` stays only for a save made mid-throw.
- `BucketTests` rewritten for it. Bench hashes unchanged.

### The innkeeper's playtest (2026-11-15)
- **Travellers** (`PartyDef.Traveller`, `adventurers.json`): a pedlar, a pilgrim, a drover and a
  minstrel, one at a time, 14:00 to 19:00, about a day apart and no more than two in town, on their
  own schedule and random stream (`PartySystem.Travellers`), never holding a party back
  (`Present` ignores them). They leave the morning after their last night, never quest, count half at
  the reckoning (`Reckoning.WordOf`), and keep back a room's price from anything else till they've a
  bed (`ShopSystem.KeptForARoom`): thin purses spent on supper and a scrape seen to left them none.
- **Bedtimes**: a guest asks for a room at their own hour, 18:00 to 21:00, derived from who they are
  and the night (`ShopSystem.Bedtime`), not all at six.
- **Order cards**: a green tick to take an order or favour; a drink order you're on gets a button
  showing what they ordered, enabled once you've poured one, that walks you over and serves it
  (`SeeToCustomerJob`, `ShopSystem.Delivers`). Errands use the same icon button.
- **Markers** for every order you've taken, labelled with what's owed ("their key, room 3"), for a
  guest back down from the wrong room, and for a guest whose name you click in the room book.
- **Keys** off the board go into an empty hand.
- **Kegs**: a cask holds 24 (was 60), so a season wants one changed; the sweep counts servings drawn
  and kegs knocked in. The innkeeper bot spends long spells stuck mixing or mopping, so it draws
  about 15 a season and never changes one yet.
- **Mixing cues**: dark tokens with a hoop ring and a bright letter, the next one ringed green.
- **Cleaning with the mouse**: `WorkInputKind.Point` carries the pointer into a session. The wash
  brush goes where it points; the scrub is now a grid of blots rubbed out by moving over them, by
  mouse drag or A/D/W/S, not A/D mashing.
- **Upper floor**: boards run on along an upper wall line (`WorldMesher.UpperWallBoards`), so no
  strip shows between the wall and the cell's edge.
- 8-seed `--season-sweep`: Renown mean 46.5 (48.8 before), won 1 (2), served 10.0 (7.9).
- **The inn's look**: half-timbering drawn a bay to a storey (one frame per cell, not a 2x2 grid of
  little framed tiles) in four variants picked by cell, in a warmer limewash; thatch in ragged
  courses instead of ruled stripes; gables filled with studded plaster (`gable_fill_plaster`). The
  village's other timber houses share the walls.
- **The bar runs east to west** in Millbrook: two counters down the common room's right side from
  the storeroom door, the cask, bar shelf and key board along the wall behind them, the line along
  the counter's front; the long table moved to the front left. The bot keeper hangs a room key back
  before its next order (from the hand, where the board now puts it).
- Save format 42. With the inn rearranged every bench hash moved: 1234 state `D6A12824E1AB05B0`,
  full `EBA4B9A2A90ED5BF`; 99 state `545C1A14456A3FF3`, full `0B41A5B0EDD223B8`. (Before the bar
  moved, the state hashes were unchanged by the rest of this entry.)

### The mouse in the mini-games (2026-11-15)
- **Every work window takes the buttons**: the left is F (a click; held, F held), the right Space;
  the hints say "Click" and "Right-click" while the mouse is in use. A game marks the picture the
  mouse points over (`TakesPointer`), places a click works as a key (`ClickSpot`: Cast's runes)
  and whether the wheel turns as W/S. A click carries where it was (`WorkInputKind.Click`): F
  unless the game minds (a spoke picked on the wheel, a stone the level's carried to, a half of the
  dough or the bed). The mouse points nothing till it's moved, and nothing with a pad in use. Mixing
  stays on its three keys; the old bucket throw is left alone.
- **Pulled, never jumped** (`WorkSession.Toward`): the probe along the seam, the loupe, the empath's
  hands, the pin lifted, the pour's tilt and the walk inward follow the mouse at their own pace, so
  what's felt in passing is still felt and a trigger swept with weight on it still springs. The
  whetstone's angle and a part over its seat turn as far as the mouse moves, so the wander still
  has to be met. A tool that finishes at its end stops short of it; the other action runs it on.
- **The cleaver is carried** (`Work/Aim`): over a joint (A/D, or the mouse) and struck where it's
  held, wider for an unsteady hand and wider still struck before the hand's settled.
- **The hammer is raised and let go**: hold F (or W, or the mouse's button) to raise it, let go to
  bring it down, drawn as it's swung with a ghost of the blow to aim for; the middle of its face is
  what lands, with a faint line down to where it would fall. Raised to the mark it's a full blow
  (1.6 times the old one's shaping); short, it moves the metal less (and grades poorly); past it,
  only a little, and it never undoes any. Raised, it wanders across the piece (more for an
  unsteady arm): A/D, or the mouse moved side to side, keep it over a marker that moves after
  every blow, and a blow off the marker glances. The bar's drawn as long as the marker can range,
  and lengthens (the marker ranging further) as it's shaped. An unsteady arm wavers as it rises. The bot smith
  keeps it over the marker, raises it to the mark and lets go.
- **Chopping by the fingers**: the beat's gone. Draw the guiding fingers back a slice (A, or the
  mouse moved left) and chop (F, or a click); an even slice is clean, a sliver or a slab poor, and
  a chop with the fingers still by the blade cuts them.
- **The cook's pictures**: stew, butchery, plucking and chopping draw one picture each (the little
  scene over them is gone); the pot's bigger; the cleaver's drawn edge on, a gash where it lands.
- **Plucking by drag**: a press anywhere grips the bird, and the pull is as hard as it's dragged,
  no quicker than a held F tightens; a grip gives out after a handful. Feathers fly as it comes
  away. **Stirring by circling**: pressed in the pot, the mouse stirs only as it goes round; the
  wheel works the fire.
- Save format 47. Bench hashes unchanged. 8-seed `--season-sweep`: Renown mean 46.0 (50.2 before),
  won 1 (3), quests cleared 1.4 (1.4); the smith's blows take their own time now, which moves
  every season after.

### Bots who play like people, and a town that talks back (2026-11-15)
For a solo round with bots to play like a multiplayer one: every trade's game, human slips, and
the trouble that makes rounds funny.
- **`BotPlayer` in partials** by area (`Bots/BotPlayer.*.cs`); its trades' orders come from the
  defs' `serves`; the solo table seats a cook; `--season-sweep` reports what each trade's bot
  played, took on, said and slipped at (`BotStats`).
- **Pitching in** (`BotPlayer.Helping`): water on a fire it sees, smells or was told of, and on the
  dragon once it's down (bucket from the well, swung and let go at the distance); the downed
  carried to a bed, a neighbour's bleeding bound, the dead called back by an empath bot or carried
  to a shrine pallet. A dead bot's ghost spooks raiders, rides a beast at them and whispers "call me
  back" to a sleeping empath.
- **Tempers** (`BotTemperament`, `bots.json`): care, haste, greed, nerve, chatter. Slips are real
  inputs (a blade lifted hot or left till it's cold, cold iron hammered, a pinch past right, a cue
  or rune muddled, a bandage too tight, a pour over the line, the key off the next hook). Greed
  prices, nerve decides who throws water and who hides. `--bot-skill F`.
- **Barks** (`Speech/Barks`, `barks*.json`): what's said when something happens, by the speaker's
  tags (kind, trade, temper, traits, drink), townsfolk's picked by a stable hash so the sim's dice
  are untouched. Bots shout alarms, greet, answer, ask for what they're out of, blame someone for
  a wrong key; adventurers pay, give up, complain (one voice a room, not a chorus) in their own words.
- **Every trade's game**: the cook's whole kitchen (fires, lids, chopping, the spit, the oven, pies,
  the block, sniffing, feasts, rations, the house dish, rats); the innkeeper's kegs, slops, jug and
  book; the smith's blades, the healer's charms and the tinker's contraptions on show and sold; the
  tinker's locks and curio haggling; the bot labourer's errands, gathering and festival sites.
- **Adventurers' tempers** (`Adventurers/Tempers`, `adventurers.json` "tempers"): easy, rowdy,
  picky, stingy, boastful, timid, by a hash of who they are. Worth, patience, thirst and taste go by
  them. A party says hello with its first order and goodbye as it found the town; loud drunks
  sing, the out-cold sleep where they fall, rowdy ones start fistfights (`Hospitality/Brawls`);
  the poisoned shout about it, a sprung trap gets a word from its owner, a timid one who feels the
  dead leaves within the hour. Gear is bought only from what's left after a quest and a box's opening.
- **The labourer's living** (`Favours/Wants`, trades.json "wants"): AI trades pin errands for what
  their workplace runs short of; an errand for what isn't lying about marks trees, bushes or rocks.
- A roast or loaf taken out with full hands no longer crashes on the pack it went into.

### The inn's look, its furniture, drinking (2026-11-15)
- **Behind the bar**: anywhere beside a counter on the keeper's side reaches the shelves behind it
  (`Vessels.BehindTheBar`), not only the cells next to the shelf.
- **Players drink**: Drink on a drink you carry (`DrinkCommand`, `DrinkHeldJob`); it goes to your
  head as a customer's does. For now: a line under Feels, a slow camera sway and the edges darkening
  and blurring, fading as you sober; out cold, you sleep where you stand.
- **Furniture**: warm oak faces (`tools/inn_furniture_art.py`, per wood), long faces repeating their
  art a cell at a time, trestle and turned-leg tables, braced stools, a barrel cask on its cradle
  (the pour from its tap, `spoutAt`), a panelled bar with a foot rail, backed shelves. Three-cell
  models (the bar, wagons) now stand on their own cells (`PropModel.Placed`): the bar came through
  the inn's wall.
- **The inn's outside** (art/reference/inn*.webp): a fieldstone ground storey with quoins and an
  arched door, a warm oak upper storey with braces, leaded windows, shutters and a flower box,
  jettied out over the street on a sole beam and brackets, a deep thatch with a ragged rolled eave
  and a spar-pinned ridge, oak-studded gables, two stone chimneys with their smoke. Kits gained
  `upper`, `facesOut` and `jetty`; buildings `chimneys` (docs/ART.md, "Building kits"). In front:
  steps between lantern posts, the sign on a scrolled iron bracket (a bear over a tankard), a bench
  and trestle table, barrels and flower tubs, all decorative.
- Bench baselines (the town's new props): 1234 state `E33D1E2E4B14E165`, full `C2D19A435FD2BAA2`;
  99 state `3E0C3971ABFE93F7`, full `FB7CFA1FEDC07CB6`.

### The cook supplies the inn (2026-11-15)
Where the inn and the kitchen stand apart (Reedmere), the cookhouse supplies the inn
(`Hospitality/Provisions`). On Millbrook, whose inn has its own kitchen, none of it applies
(`Provisions.Split`).
- **Provisions**: a new service, a hamper for the inn, on the cookhouse's sign only (12 silver,
  six servings). Only the inn's keeper orders one: F at the cookhouse hatch, from their own purse.
  It lines up at the hatch like a meal.
- **The hamper**: a container item carried in both arms. The cook packs it at the prep table from
  the dishes on them (bowls, bread, pies, a roast, rations), with the rations game
  (`PackSession.ForHamper`). A lid tied slack is hidden, and its dishes come out further gone.
  It's put up on the hatch and taken by the keeper at the front of the line, which completes and
  pays the order.
- **The warming shelf** behind the inn's bar: F with the hamper unpacks it. Nobody's job takes
  from it. **Lodgers' board**: at breakfast or supper a lodger asks at the inn's own bar for a dish
  off the shelf, at no charge, served across the bar by the keeper (`BarService`, `ServeJob`).
  **The road**: a party member setting out without rations takes a bundle off it. **A bare shelf**
  makes a room worth 0.7 of itself to an adventurer.
- **The AI**: the innkeeper sends for a hamper when the shelf's under two servings, waits in the
  hatch's line, carries it back and unpacks it (`OrderProvisionsJob`, `UnpackHamperJob`). The cook
  ladles bowls (else takes something cooked), packs without the game and puts it up
  (`PackHamperJob`). The bot does the same as either trade, by commands, and now plays the packing
  game. `SeasonScenarioTests` plays Reedmere too and wants at least one hamper delivered.
- **The carter**, with a span down: on Millbrook, nothing as before. Where the kitchen stands
  apart, the carter calls at it if a cart can still reach it from the map's edge, and at the inn
  only if it's still joined to the kitchen. The message says what's down.
- **Feasts** are called coming into the inn too (where parties loiter on a split map), and the party
  goes over to the cookhouse to eat.
- **The festival**: a party stranded out on the water missing it is accepted as the lake town's
  risk. Falling in, Reedmere's art and its balance are on the roadmap.
- **New round**: the town choice (Millbrook, Reedmere) was greyed out on the title screen. Items
  that lead on to a further choice now count as live.
- Save format 48 (a hamper's packing). Bench hashes unchanged.
- **Merged with the bots' work**: the hamper bot moved into its own partial (`BotPlayer.Hampers`); the
  bot cook takes a hamper order only when it can get on with it (dishes on it, or a cooked pot it
  knows of), so the kitchen's own work isn't starved. The bot chops by the fingers now (draw back a
  slice, then the knife), as the game does since "chopping by the fingers". `CookPassTests` picks a
  diner and calls off their other orders, now every adventurer orders on arrival. A lodger thanks
  for their board in their own words (`adv_board`). Bench: 1234 and 99 unchanged; `--round` 1234
  state `F47242A57089C29B`.

### Reedmere, the town on the long lake (2026-11-16)
- **The map, redrawn** (`tools/lake_map.py`, after `art/reference/laketown_map_by_tipexleloup*.jpg`
  and the esgaroth and laketown pictures): a crowded rectangle of houses on piles round an open
  harbour, a water-gate canal out to the lake, one long causeway north from a rocky spit to the gate
  quay; the shore only a headland in the south-west (the road, woods, garden, an old barn, the
  woodshop). Shrine, forge (on a `StonePier`) and storehouse (the stockpile) at the gate; the inn and
  cookhouse across the water-gate from each other; the tinker's on the west quay; nine cottages and
  two warehouses; the **moot hall** (`MootHall`, no role yet) on the far quay with the notice board,
  muster rack and a **great bow** (only seen) on its stone forecourt. Shingled `PiledShingle`
  variants for the cottage, inn and cookhouse, `Piled` for the smithy (stone, on its pier) and barn.
  Dressing: rowboats moored in the water (`floats`: a structure placed on water), drying racks, net
  frames, lanterns lit all night along the quays (`alwaysLit`, `LightingSystem.StandingLamps`), reeds
  in the shallows (`things/reeds`, `chibi_nature.py`).
- **The festival on the boats**: the boats lashed in the harbour (`BoatDeck`, a hull look drawn as
  boats lying east-west) round a mast that is the town's gathering place (`BuildingProps.Gathering`:
  `RoundSystem.FindSquare` prefers it to a well; the messages say where). The bonfire burns on a
  `FireBarge` that doesn't, the butts are shot along the raft.
- **Decks drawn on every open side**: rails on posts, with a gap now and then, wherever a deck on
  piles meets the water, and none between one floor and another (a pier and a boardwalk) or where
  it meets the shore (`WorldMesher.OpenEdges`).
- **The landing on a deck**: `MapDef.SurfaceZ` puts a map's center, stockpile and livestock on a
  floor it lays at a fixed level.
- **The carter** with the causeway down reaches neither the inn nor the cookhouse, both out on the
  water, nor the forge's ore (`Deliveries`: a smithy the road can't reach waits).
- **8-seed `--season-sweep --map Reedmere`**: Renown mean 34.4 to 52.0 of 60, won 1 to 4, Dread peak
  73.8 to 64.2, deaths 2.2 to 0.9 a round (the cookhouse a footbridge from the bar). Millbrook, with
  the rare companies in its pool: Renown mean 68.0, won 5 of 8.
- **The rare companies** (`adventurers.json`): `PartyDef.Rare` with a `chance` a round and days
  (`fromDay`, `toDay`), rolled at the round's start on the party system's own stream, at most one, sent
  on its day in place of the next party. `extraMembers` join as the round has more players
  (`SizeFor`: the core of four at `coreAt` 4 or fewer, all at `fullAt` 12); `fixedNames`. A kind's
  `folk` (Dwarf, Halfling, Elf, Human) is a `Kin` component the derived look honours (`HumanLook.Derived`;
  everyone else's look unchanged). A quest with a `party` is the company's own errand: never in a
  round's draw, posted when they come (`ExpeditionSystem.PostOwn`), the only one they'll take, at
  whatever danger (the broker's AI and the bot offer it first). **The Greyhorn Company** (a dwarf
  heir, a graybeard, axe-hands and Bramble the halfling; fourteen at twelve players) for Greyhorn
  Hall, and **the Company of the Long Road** (a grey wanderer, halflings, a ranger, an elf archer, a
  dwarf; nine at twelve) for the Cinder Mount. Our own names throughout.
- Save format 49 (whether a rare company comes, and when). `ReedmereMapTests` rewritten,
  `FestivalTests` on Reedmere, `CompanyTests`. Bench state hashes unchanged for 1234 and 99 (the
  full hash moves with the save format).

### Camera mode, for filming (2026-11-16)
- **Camera mode** (`` ` `` or R3, `VillageGame.CameraMode.cs`): the camera comes off your character
  and flies free on Narsil's `CinematicCamera`, which glides (speeds ease in and out) and is driven by
  intent, not keys. Fly with the walk keys or left stick, orbit Q/E, tilt R/F (20–70°; the right
  stick or a right-drag does both), zoom as ever, Shift faster and Ctrl slower. G (or a click) follows
  whoever you point at, or yourself; T turns slowly by itself; H hides the HUD and names; F12 as
  ever. Its keys are their own group (`Controls.Cinema`), rebindable. Leaving eases back onto your
  character, the short way round and back to the view's tilt.
- **Honest in a round:** drawn through your character's or ghost's Perception as ever, heard from
  where they stand, not the camera, zoomed no further out, and following only someone they can see.
  Free and all-seeing outside a round.
- **The clock:** pause (Space) and slow motion (M: 0.5x, 0.25x) in solo only, as a time scale on
  `RunSimulation`, so ticks stay whole. Co-op's clock never waits.
- `--camera-mode`, `--pitch N` for staged shots. The fire drill finds the tavern by role (works on
  Reedmere) and `--fire-spread N` starts a blaze.

### Round replays (2026-11-16)
- **Recording** (`Sim/Replay`): `ReplayRecorder` on `Simulation.Recorder` keeps a snapshot from where
  no job is running (before a new sim's first tick, after a load or a joiner's snapshot) and every
  command and co-op event since, by tick, with a state hash every 600 ticks. It only reads: bench
  hashes unchanged. Co-op's joins, leaves, away and barriers go through `PeerEvents`, so a session
  and its replay do the same; every peer records the same log, and a resync carries it on. The dev
  tools that change the world without a command spoil it.
- **Playback** (`ReplayPlayer`): the snapshot loaded, then ticked on with each event at its tick; back
  means from the start (a whole season replays in about four seconds). A hash that doesn't match is
  shown as where the replay drifted.
- **Kept at the reveal** in `replays/` in the config folder (the newest 20; a 1.5 h round is ~60 KB),
  with the crises, the festival and players' deaths marked. **Watch the replay** at the reveal (in
  co-op it leaves the game), **Replays** on the title, `--replay PATH|newest`. Replays carry their
  build and play only on it.
- **The viewer** (`Replay/ReplayGame`): all-seeing (the round's over), the cinematic camera, a timeline
  to scrub, 0.25x to 8x, jump to a crisis, click someone to follow, H hides it all. Dev: the Debug
  window's "Keep a replay now", `--keep-replay-at N`.
- `ReplayTests`: bot rounds on both maps play back hash for hash, seeking back lands the same, co-op
  events replay, files round-trip, recording changes nothing.

### Precision games, tightened for real hands (2026-11-16)
Playtest: the hammer "over every time" letting go on the ghost; the lock's wrench slipping, and pins
falling while the needle was green. A window a game tells you to hit now spans at least ~5 steps at
level 0 after ~3 steps of screen and input lag, and no cue is looser than its window
(`PrecisionTests` plays each game a few steps late, as a person does).
- **Hammer** (`HammerSession`): the arm slows to 0.4 of its pace within 0.12 of the mark and past it
  (`WindRate`); a wind up to two slowed steps past the mark is still on it (`BandLow`/`BandHigh`, drawn
  as the gauge's band); a touch further past is a heavy blow (grade fading 0.7 to 0.2,
  `LastWindWas` 1), not the old cliff to 0.1, which is kept for well past (`LastWindWas` 2). The ghost
  and the band go green while the hammer's in the band.
  The mouse held at (or past) either edge of the panel keeps the hammer going that way at A/D's pace
  (`EdgeU`; the anvil reads the mouse off the picture, held to its edge: `TakesPointer(edges:)`), so a
  spot far out is reachable after the hammer's drifted.
- **Lock** (`PickSession`): the cue to set a pin was shown at Feel 0.6 while a novice's window began
  near 0.83, so pressing on it missed. Now `SetFeel` gates the "set it" words and the bright shear
  line, and Feel is felt after the pin moves. The window's wider (`SetHalfWidth` 0.05), the pin stays
  stiff past its set point, too little tension makes the pins chatter for `SlackGrace` (24 steps)
  before they drop, the spring-back is gentler (0.0035 a step against W's 0.006), and the plug pushes
  less and less often.
- **Crimp** (`CrimpSession`): two steps late on a mark still counts (`LateSteps`).
- Audited and left alone: butchery (placed, not timed), quench (0.7–1.1 s by the steam), the spring
  latch (taps against a wide catch), truing, the span, the probe, the whetstone.
- Save format 50 (a lock's chattering). Bench hashes change: a gameplay change.

### The tutorial (2026-11-16)
Playtest: confusing, not clear what to do. The practice yard's lessons were a list of buttons in a
busy panel, nothing led from one to the next, and a paged "Getting started" box under the portrait
was most new players' only guide.
- **One guided run per trade** (`VillageGame.Tutorial`; the title's "Tutorial", was "Practice yard"):
  what the trade's about (`tutorialIntro`), the basics every trade shares (walk, the workplace, the
  pack, the shop, the controls card: `BasicsLesson`, trades.json's `basics`; skipped once done), then
  the trade's `core` chapters back to back (smith: sharpen, mend; inn: ale; cook: stew; healer: wound;
  tinker: box). Then a round in the trade, the extra chapters, or the yard to practise in.
- **A card at the top centre**: the step in a bigger face; the thing it's about pointed at with a
  named tag, pinned to the screen's edge with an arrow when it's off it; the button for it glowed (a
  step's `ui`, plus take, collect, take back and Mend), whatever the guide-highlights setting says.
  45 seconds on one step brings its `more` hint and "Set it up again".
- **Tips folded in**: every trade's `tips` line went into a step, a hint or a chapter's `notes` ("worth
  knowing", shown when it's done); the field, the box, its station rings and the Tips setting are gone.
- The title marks Tutorial "start here" while no saved character has a lesson or a round to its name;
  the trade picker says which trades are learned.
- `TutorialTests`, `TutorialWordsTests` (every key mark known, every glowed button an action).

### Pour, probe and the draw retuned (2026-11-16)
Playtest: the pour's tilt "off and weird"; Space in the probe's green set the trap off, the mouse's
reach varied, and a good flick showed nothing; drawing a wound hard with the mouse, easy with keys.
- **Pour** (`PourSession`): tipped, the tankard's low lip sits lower (`Lip`, `LipDrop`), and what rises
  past it runs over (`Lost`: a poorer drink; past `SpillLimit` a wasted serving). So: tip it early (less
  foam), upright near the top. One picture, held by the mouth under the tap, the stream down the inner
  wall when tipped, the line marked on the walls (`PourPicture`, also in `--work-scenes`).
- **Probe** (`ProbeSession`): stopped on a catch, the fingers take a read (`SpotRead`) that sets how wide
  the green is; hold F to press in, Space in the green flicks the spring out and ends the search (a snap).
  Out of the green, it fires; where nothing's there, nothing gives. A flick is judged on the pressure as
  pressed, with `LateSteps` of grace; pressing in, the mouse no longer drags the probe; the mouse is
  read off the seam bar, where the probe's drawn.
- **Laying on hands, drawing**: the mouse points where composure should be (the gauge is the pointer's
  while drawing) and steers it there, gentler as it nears; keys steer a little slower (`SteerPerStep`).
- A mini-game in hand takes the mouse as it takes the keys: no click-to-walk off it.
- The title menu shows every hint, always: no entry grows as the pick moves.
- The tutorial: one customer for a drink or a quest, two otherwise; the ale's wait lasts till the
  customer's in the tavern; a finished step stays on the card a few seconds; no tankard step.
- Save format 51. Bench hashes change: a gameplay change.

### Pausing (2026-11-16)
- **Alone**: the game menu (Esc, Start) and the windows it opens hold time; the pause key (`Act.Pause`:
  P, Pause) holds it till pressed again. Game-side (`VillageGame.Pause`): your character stands still
  and does nothing while it holds; god mode's speed buttons are as they were.
- **Co-op, by agreement** (`Round/PauseVote`): the pause key (or the menu's line) asks everyone; it holds
  once every connected player agrees, any one "carry on" turns it down, and an unanswered call lapses
  after 30 s. While it holds, everyone says they're ready (a joiner too) and time runs again. Who's
  connected comes from `PeerEvents`, so someone who drops can't hold the town. `CallPauseCommand`,
  `PauseAnswerCommand`; `PauseVoteTests`.
- `PauseVote` adds saved system state, so full bench hashes move: seed 1234 full `62C0D498662C6D4F`,
  seed 99 full `B60421361A01CE94`.

### Emotes and quick-say (2026-11-17)
- **Emotes** (`Speech/Emotes`): wave, bow, cheer, laugh, point, shrug, weep, angry, through
  `EmoteCommand`. An `Emoting` component lasts a few seconds and ends if you step off the cell or
  are knocked down, set to work, or draw a blade. Seen, not heard: `PerceptionView.EmoteOf` shows it
  only to whoever can see you. A point keeps the cell it points at (within 40 cells); the renderer
  rings it. Glyphs `ui/emote_*` (`tools/norse_ui.py --emotes`) in a bubble over the head. The
  animator asks for `emote_<kind>[_view]` tags while idle and stands idle where a sheet has none
  (the people sheets don't yet).
- **Quick lines** (`barks_quick.json`): help, over here, thanks, aye, nay, coming, fire, raiders,
  said as ordinary `SayCommand`s (the alarms shouted), so every hearing rule holds.
- **The wheel**: hold U and the mouse picks, with emotes inside and quick lines round them. On a pad,
  the RB wheel's LB turns its pages: Actions, Show, Say. `--emote KIND [--emote-at S]` shows one, for
  screenshots.
- Bots wave as they greet a new face.
- Bench hashes unchanged.

### The carpenter (2026-11-17)
Millbrook's sixth trade, so a six-player round has a game for everyone (DESIGN.md, "Carpenter").
- **The woodshop** comes furnished: sawhorse, carpenter's bench, chest, and a sign (`props/sign_saw`).
  Stocked at the deal with logs, planks and feathers; the carter leaves feathers each morning
  (`season.json` `woodshopDelivery`, `OreCarterSystem.DeliverToWoodshop`).
- **Sawing** (`Carpentry/SawSession`) and **fletching** (`Carpentry/FletchSession`) at the woodshop,
  started through `Crafting.WorkAt` (`Carpenters.Applies`). A quiver (`Fletched`) carries a hidden
  warp; quality shows as `Crafted`.
- **Quivers on quests** (`Carpentry/Fletching`, `MemberRecord.Quivers`): sold as goods, bought by
  archers only (`Goods.IsUpgrade`), shot away on the road (`ExpeditionSystem.Resolve`). Save format 52.
- **The town's work:** `mendsTown` (the bridge, with the tinker) and a new `builds` flag on the trade
  (`FestivalSystem.PutsUp`: the carpenter puts up the festival with the labourers).
- **AI carpenter** (`SawJob`, `FletchJob` in the Craft chain); **bot** (`BotPlayer.Woodshop`); the
  sweep's table is now the six trades (`SeasonSweep.Trades`), `--trades` picks another.
- **Tutorial:** `saw` and `quiver` (`Scenarios/Lessons/CarpenterLessons`); practice customers are archers.
- `StartingVillagers` 6 to 7, so a town with every trade taken still has an AI labourer.
- Fixed on the way: errands no longer take from the kitchen's own stores (the log basket, the larder):
  `ErrandJob` skips `WorkGivers.KitchenStock`.
- Art: `tools/carpenter_art.py` (quiver, feathers), `inn_art.py` (the sign). Sound: `work.saw`
  (`sfx_synth.py`).
- **Sweep** (24 seeds): `main` before this was Won 8, FellShort 7, Dread 8, TownFell 1 (Renown mean
  56); with the carpenter bot, Won 9, FellShort 3, Dread 12 (Renown 47). The old `season-baseline.txt`
  was 61 commits stale (8 seeds, Won 6); replaced with the 24-seed run.
- Bench hashes change (new behaviour).

### Traits taken out (2026-11-17)
DragonWorld's character traits (optimist, gloomy, hardy, kind, abrasive...) are gone: no Traits tab
in the creator, none rolled for villagers, none on saved characters or in the inspector, and none
weighing on mood, opinion or who starts a chat. `JoinLobbyCommand`, `CreateCharacterCommand`,
`Hero` and `RoundSystem.Sit` lost their traits argument. Save format 53: an older save's traits store
is read past (`ComponentCodecs.SkipTraits`, `SaveReader.SkipDef`); an old `characters.json` with
traits still loads. Bench hashes change (mood no longer scaled by traits).

### Fixed: the woodshop's logs (2026-11-17)
The woodshop now keeps its stock (`keepsStock`), as the smithy and the tinker's do: villagers
refuelling the town's fires had emptied its chest of logs in the round's first minute. The saw lesson
sends the player to the chest for six logs first (the chest is out of reach of the sawhorse).

### The carpenter's games, deeper; the woodshop dressed (2026-11-17)
- **One arrow, in depth** (`Carpentry/FletchSession`, `ArrowStage`): in place of three shafts sighted
  and feathered alike, one arrow taken through sight, shave (the knife along the shaft, its bite on
  W/S), the head bound on with sinew (pull judged as each wrap lays; too hard snaps it) and fletch.
  Each stage scores once; the hidden warp stays. Lesson `quiver` gains `shave` and `head` steps.
- **Bows tillered by hand** (`Carpentry/BowSession`): `MakeBow` at the carpenter's bench is a game of
  its own now, not hold-F. Lesson `bow`. Bot plays both (`BotPlayer.Woodshop`).
- **The woodshop dressed** (`workshop.json`): `BowRack`, `LogPile`, `PlankStack`, `ArrowBarrel`,
  `Shavings`; a timber sill under a plank floor (it had none: grass inside); torches. Its own
  furniture and the sawhorse and bench drawn in oak (`furniture/oak_*`) rather than the neutral planks
  that come out grey with no material. Faces from `tools/carpenter_art.py`.
- Tests leaning on a seed's timing given room: `AnAIBrokerSendsAPartyOut` (a party in hurt waits on
  the shrine) and the tinker's strongbox test (a party back late sleeps before it asks).

### The carpenter's games, reworked from play (2026-11-17)
- **Sawing** is a push and pull: A/D in turn (or the mouse side to side) work the saw, only a moving
  saw cuts, a rushed stroke wanders more, W/S lean it back. Drawn side on: the blade's teeth in the
  kerf sliding with the stroke, the kerf's path as it wandered, sawdust. Two cuts a batch.
- **Shaving** moves the knife freely, like the mop: it shaves while held, takes the lumps down to its
  edge, gouges below the shaft's line, and is done when the player says (Space).
- **The head** is pushed on and kept in line (it tips as it goes) in place of the sinew wraps,
  which didn't read.
- **Feathers** are placed where the player points round the still shaft; no spinning.
- **Esc** closes the newest closable window (a container, the pack over it, a station) before it
  reaches the game menu, as the pad's B already did (`VillageGame.UpdatePadUi`).
- **Sawing from above**: the plank top-down, the chalk line along it, the saw lying along the cut edge
  on (so the kerf shows under it) sliding with the stroke; W leans up and S down as seen, and an arrow
  at the cut's front points back to the line.
- **The bow, clearer**: a bar per section of the wood still to come off, down to its mark; the scraper
  drawn working, shavings flying (`BowSession.Scraping`); A/D and the mouse go by the sections as
  drawn (`ColumnOf`). And **strung** after tillering: bend it into the green and slip the loop on;
  too soon misses, too far strains (`Stringing`, `Flex`, `StringScore`). Lesson step `string`.
- **Planks the old way** (`SawSession`): the round log **hewn** square with the axe (raise and let
  go, a blow as deep as it was raised, four sides to the chalk), then the beam **pit-sawn** end on
  as the top sawyer sees it (W/S work the saw, only the downstroke cuts; A/D steer), with the beam
  from above beside it. Replaces the push-and-pull view, which didn't read. Lesson step `hew`.
  Riving (splitting with wedges) noted in DESIGN.md as the other old method, for later.
- **Hewing from the side**: the face being hewn turned to the right and upright, the broad axe coming
  straight down along it from above, its edge on the dashed cut line; chips fly off the face.
- **The pit saw on the mouse, smoothed**: the saw eased toward the pointer (`MouseEase`), not jumped to
  it, and no faster than `MostPace`; steering over a wider span with a dead zone in the middle, so
  working it up and down doesn't steer by accident. The hand's shake on the blade is a slow waver
  rather than a jolt a step, the kerf kept at twice the points and drawn lightly smoothed.
- **The log turned between sides**: a side hewn flat, the log comes a quarter round to the next over
  a moment (`SawSession.Turning`, `TurnSteps`), the axe resting meanwhile (a hold doesn't raise it),
  drawn eased round in the window.
- **Turning the log is the hewer's call**: Space (or a right-click) turns it to the next side, so how
  near the chalk to take a side is theirs to judge; wood left on now costs (an untouched side scores
  nothing), as a bite past the line does. The grain's drift on the saw a little stronger
  (`DriftPerStroke` 0.07), so an unsteered cut still costs planks with the smoother wobble.

### The theme: behind every hero, and two pairs of hands (2026-11-17)
- **Behind every hero** (`Expeditions/QuestTales`): each quest a party goes on keeps a `QuestTale`
  of whose work went with them, as `Deed`s: armed, armoured, fed or sickened, warded, a life spared,
  jammed, shot true or wide, healed, sent, struck at the sledge. Names are kept as words, so the
  tale outlives the people in it. Gear now keeps its maker on a quest (`GearRecord.Maker`, lost before
  on `Remake`). Who healed an adventurer is remembered (`TendedBy`, set in `ShopSystem.Treated`).
- **The reveal** has a "Behind every hero" section (`VillageGame.Reveal`): each quest, how it went,
  and the hands behind it, hidden truths included (`Deed.Hidden`).
- **The festival:** a party back in triumph names someone in its toast (`FestivalSystem.ThanksLine`,
  only `Deed.Public` work: never a quiver, so a warped one's silence can't give it away), and the
  ceremony calls up "the hands behind the heroes" (`QuestTale.Hands`).
- **Two pairs of hands** (`Work/Pairs`): the lead's and the helper's sessions are each driven only
  by their own player, and pass `Beat`s through the sim between steps (`Pairs.Exchange`). A session
  the sim hands a beat to bumps its `Revision`, and the `WorkPredictor` takes it again from the sim
  and replays its unplayed inputs. A helper's session follows the lead from one session to the next
  (`HelperSession.Follows`, `Pairs.Relink`) and puts its tool down when the work's done.
- **The striker** (`SledgeSession`): a `twoHanded` recipe (the **greatsword**, Millbrook's forge) is
  striker's work. The smith's blow is a tap (`HammerSession.TapShare`) that calls the sledge; the
  striker heaves it up and brings it down on the call, as good as the heave and how soon. Off the beat
  it can crack the piece (`OffbeatCrack`). The striker's named on the blade (`Crafted.Helper`,
  `WorkPiece.Helper`) and in the quest's tale, and learns a little Crafting. Joined with the work key at
  the anvil (`AvatarActionKind.LendAHand`).
- **A hand from a villager:** "Help!" from the quick-say wheel, at work for two, is a call for a hand
  (`AskForHandCommand`): of the AI villagers who hear it clearly, the one who thinks best of you
  comes (`HelpJob`, `Helping`), unless nobody thinks well enough of you (`Pairs.MinOpinion`).
- Saves to format 54. `QuestTaleTests`, `PairTests`.

### Wares shelves, and the inn's supplies from the cook (2026-11-17)
- **Wares shelves** (`Shops/Goods`): a playtester couldn't find how the healer sold charms (drop it
  on the shrine's floor, click it). Gear is now sold only from a shop's shelf (`display` furniture):
  a `WaresShelf` in the smithy, shrine, tinker's shop and woodshop, a `MerchandiseShelf` behind each
  inn's bar (`displayFor` the innkeeper, so the cook can't sell from it at Millbrook). Put on it by its
  seller (F with it in hand, from the pack, or the inventory's "Put on the wares shelf",
  `ShelveCommand`), a piece is for sale at once at a fair price (`Goods.Shelved`, `DefaultPrice`); the
  shelf's window sets prices (`DrawShelfPrices`). Only its seller takes a piece down (it comes off
  sale, unless someone's buying it). The shelf shows what's on it. The AI empath and carpenter, the
  bots and `SellJob` all work from the shelf; floor items are no longer on show.
- **The inn's supplies from the cook** (`Hospitality/Provisions`, now both towns, `Provisions.Active`):
  the cook doesn't sell to parties; the innkeeper sends for a hamper from the bar (the bar card's
  button, `OrderProvisionsCommand`), and holds no place in the hatch's line till it's up. The cook
  packs rations (an AI cook ties them itself, `PackHamperJob.RoadFood`) and, where the kitchen's apart,
  dishes for the board. A hamper is paid for by what's in it (`Provisions.Charge`: the sign's price is
  a full one's). Unpacked at the inn, its rations go on sale on the merchandise shelf; parties no longer
  take rations free as they set out. Millbrook's inn takes hampers at its own hatch.
- **The labourer's run** (Reedmere): a labourer takes the hamper off the cookhouse hatch and carries
  it over (`HamperRunJob`, F at the hatch for a player's), paid `Provisions.RunFee` by the innkeeper;
  the innkeeper fetches it only with no AI labourer about (`Provisions.Waits`, `Collect`).
- Practice customers bring silver to spare for what they came for (`Practice.CustomerPurse`): a
  tinker's curio went unasked for, its bearer broke on meals and drinks.
- `ABandAtYourElbowTakesItInTurns` averages three towns: on one seed it swung from 6.8 s to 5.0 s with
  the new furniture, and seeds 5, 9 and 11 were under 6 s before it.

### The empath, and the trip-bell (2026-11-17)
- **Healer is now Empath**, in the trade's name, its words and the docs, for what sets it apart: it
  feels what it takes on. Old names still resolve (`TradeDef.Formerly`, `DefDatabase.TryTrade`), so
  saves, replays, `--trade Healer` and `--practice Healer` still work, and a saved character's rounds
  and lessons as a healer carry over (`CharacterRoster`). Bench hashes unchanged.
- **The trip-bell** (`Tinkering/TripBells`, DESIGN.md "Tinker"): the first contraption for the town
  rather than the road (`ContraptionUse.Alarm`, `ForTheRoad` false: never on a shelf). Fitted at the
  bench, set on open ground out of doors with F (`AvatarActionKind.TripBell`), rewound or taken up the
  same way; the haulers leave a set one be. A raider crossing it rings the alarm and is shaken
  (`Unnerved`); a binding one may jam silent (`TripSet.Jammed`, hidden). The bell's heard where it
  is (`SoundDirector`).
- **A raid's alarm waits till it's seen** (`RaidCrisis.Watch`): a waking villager within 14 cells, a
  sleeper within 5 (woken by them), or half an hour in any case; then the shout, the town's bell and
  the muster, as before. The raid drill still rings at once (`RaiseNow`). A warned raid now comes by
  the road the shepherd named (`byRoad`), and the Director says which (`WarnedSide`).
- The AI tinker fits and sets a bell on the warned road (`TripBellJob`); so does the bot.
- Saves to format 57. `TripBellTests`.

### Two pairs of hands, the rest (2026-11-17)
Three more helpers on the striker's pattern (`HelperSession`, beats through the sim), so each pair
can be played in co-op.
- **The keeper at the Calling back** (`Afterlife/HoldSession`): begun at the body, the weave stays
  locked till someone holds them (`CastSession.WantsKeeper`); F at the body offers "Hold them". Each
  rune is a pull to brace through (`BeatKind.Held`, `Slip`, appended); slips cost the weave
  (`KeptQuality`), and below `Revival.FailsBelow` the calling fails. The empath can't let go till
  the last pull's through (`StillPulling`). Weaving the Calling's runes from the Gift alone no
  longer calls anyone back. The AI empath asks a villager to hold the body (`KeepVigilJob`) or gives
  up; the reveal names who called them back and who held them (`Revived.CalledBy`, `HeldBy`).
- **The pitman** (`Carpentry/PitSession`): `SawGreatLog` is `twoHanded`; the top sawyer's downstroke
  calls, the pitman's pull cuts (`SawSession.Great`, `GuideShare` alone).
- **The lifter** (`Carpentry/LiftSession`): a new axle's hubs go on with the wagon lifted level
  (`JoinSession.TakesHelper` for an axle, `GroundShare` alone); `Wagon.LiftedBy`.
- **The plumbing:** a lead session says how an AI helper answers it (`AiAnswer`) and what they say
  coming (`HelpLine`); joining bumps the lead's `Revision` so the other player's copy learns of its
  partner; `Pairs.WantsHandAt` by cell (a body, a wagon).
- **Bots** play every helper's game, call for a hand when they lead, and walk over to lend one when
  they see someone at work for two (`BotPlayer.LendAHand`).
- **The pairs drill** (`--pairs-test`, `Scenarios/PairsDrill`, `DevPairsDrillCommand`): every job for
  two staged at once once the round's dealt, for a co-op host and everyone joining.
- Saves to format 58.

### Well rested, well fed (2026-11-17)
Eating and sleeping turned from chores into bonuses (`Systems/WellBeing`).
- **No meters, no penalties:** nobody starves (Food's `damagePerDayAtZero` is gone), healing no longer
  waits on food, the hungry and tired thoughts are gone, and a hungry party no longer fights worse.
  `Needs` stays as the AI's appetite and drowsiness, never shown to players.
- **Well rested** (`WellRested`): two hours asleep gives eight hours from waking. A player's nap lasts
  at least two hours.
- **Well fed** (`WellFed`): four hours from a meal. Its strength comes from the dish (`ThingDef.WellFed`
  in the defs) × the cook's grade, + the house dish, less its rot (`WellBeing.FedStrength`). Only a
  better meal replaces it; a player is told "still full" rather than wasting food. Food poisoning
  clears it.
- **What they do:** work (`Job.Work`, hand crafting) up to +10% rested and +15% fed, walking +5% each,
  mood thoughts (`WellRested`, `WellFed`, `HeartyMeal`, `SplendidMeal`). A party's strength on a
  quest gets +6% fed and +4% rested (`MemberRecord.FedAtLeave`, `RestedAtLeave`).
- **Players:** the HUD card shows the buffs and their hours left in place of the bars; the Eat tooltip
  says what a meal would do, judged without its hidden rot. The food pointer shows only at
  mealtimes. Bots eat at supper when they've nothing better to do.
- Saves to format 59. `WellBeingTests`.

### The carpenter in the woods, and firewood (2026-11-17)
Trees felled, bucked and split by hand; the town's fires burn firewood, not timber.
- **Firewood** (`Firewood`, `core.json`): every wood-burning `fuel` (hearth, fireplaces, bread oven,
  kiln, the festival's fires) takes it. The barn starts with `barnFirewood` and the carter stacks
  `firewoodDelivery` there each morning, a little short of what the town burns on purpose, and none
  in a shortage (`OreCarterSystem.DeliverWood`). Timber (`Wood`) is for building and recipes only;
  its delivery fell from 120 to 30 a day. The kitchen's basket and the cook's want are firewood.
- **Felling** (`Carpentry/FellSession`, `Felling`): the carpenter (`TradeDef.Fells`, `RoundSystem.MayFell`)
  fells by hand: aim (A/D, a line on the ground; each tree's lean from `LeanOf`), notch to the mark,
  back cut thinning the hinge. `FallDir` is the pure rule: a shallow notch drifts toward the lean, a
  hinge cut through goes with it. A `Falling` tree creaks 1.5 s, then lands: creatures on its line hurt
  (`Felling.Crush`), a wall breaks it short and loses logs, and a `FelledTrunk` (`Trunk`) lies there.
  The renderer topples it; the sound director plays the creak and crash.
- **Bucking** (`BuckSession`): crown first, a log per cut, the pull cuts; past halfway the kerf
  pinches (Space drives a wedge). An oak's (`greatButt`) takes a `CrosscutSession` on the other end and
  may keep its butt whole as a `GreatLog`. Stopped early, the cut logs stay.
- **Logs** (`Log`, `GreatLog`): bulky, one in the arms, slowing whoever carries them (`ThingDef.CarrySpeed`).
  `SawLog` (7 planks) and `SawGreatLog` (16, with a pitman: the pit saw's great log is a real log now).
  The plain chop (`harvest`: the labourer, building sites, outside rounds) still gives timber; logs
  come from felling (`FellingProps`), by hand or by the AI carpenter's `FellJob`.
- **Splitting** (`SplitSession`, at the woodshop's new `ChoppingBlock`): the edge over a check, raised
  past the mark; off the check the axe sticks. A log gives 4 firewood, a great log 8. The AI carpenter
  splits what logs it can find when the barn runs low, felling a tree for some if there are none
  (`Carpenters.Split`, `SplitJob`, `FellJob`); the bot fells,
  bucks, carries and splits.
- **Lessons**: "Fell a tree" and "Split firewood" (`CarpenterLessons`). `--fell-test` (`FellDrill`).
  Panels for work out in the world sit to the side (`VillageGame._workAside`).
- Saves to format 62. `FellingTests`.

### A Director that reacts (2026-11-17)
The Director was a fixed script with dice: Dread pulled beats earlier and made them stronger, which
piled on a losing town. Now it paces like a game master.
- **`Director/Tension`**: a reading refreshed every 10 game minutes from the sim's own state (harm:
  deaths, the downed, open injuries, fire, breaches; load: orders, lines, quests; pace: Renown against
  the expected curve, Dread against its limit; calm: hours since the last crisis).
- **`Director/RuleMind`**, pure functions: a beat waits for calm (but still fires by its `toHour`),
  eases off a struggling town and pushes a coasting one within the `pacing` band in `season.json`,
  and settles a beat's kind a warning ahead from what the players can answer.
- A beat can follow a quest (`followsQuest`). None in the shipped season does yet; the Reedmere
  dragon should be the first.
- **Incidents** (`data/defs/incidents.json`, `Director/Incidents`) fill lulls: a fever brought in,
  rats, a traveller's errand. A wrecked wagon was left out: wagons already arrive broken most of the time.
- **`DirectorChoiceCommand`** (a host command) is the only door for a mind outside the sim: it can
  settle the next beat's kind or call an incident. There's no swappable mind interface, since the
  sim runs the same on every peer; an outside mind (an LLM on the host, ROADMAP backlog) would act
  only through that command.
- Saves to format 60. `DirectorPacingTests`.

### The carpenter's games, from play (2026-11-18)
- **Felling** starts aimed the way the tree leans (the easy fell), the lean a broad arrow under the
  aim's, and the hints say the first blow (F) sets the aim.
- **Felling's cuts**: the notch a V inside the round, the back cut a slim cut in from the far side
  that stops at the hinge (a box that ran out past the bark, and past the notch, before). The axe
  swings round from the hands into the cut, as a chop does, not pushed in and out along it.
- **Bucking**: side on, only the saw's handle shows at the cut, sinking as it goes down (the blade
  lay along the trunk before, as if slicing it lengthwise), each length its own shade of bark; beside
  it, the cut seen along the trunk, the saw going back and forth across the round, hidden inside it,
  sawdust spat out either side.
- **Planks from a log**: `SawLog` (one log, four planks) is the sawhorse's first recipe; six timber
  still make ten (`SawPlanks`, "planks from timber"), and a great log ten long ones. The woodshop's
  chest starts with three logs and a little timber for mending. The AI carpenter saws a log from the
  chest or one lying about, and fells for more when it's out (`SawJob`, `Carpenters.Saw`); the bot
  fetches, bucks or fells for one (`GetALog`).
- **A room's price kept back** (`ShopSystem.KeptFromGear`): from 16:00 an adventurer staying the night
  with no room yet doesn't spend a bed's price on shelf gear (a quiver off the woodshop's shelf left
  one with too little for a room, asleep in the street).
- **Joinery**: the tenon is cut to the mark's length, so home its shoulder sits on the face (it sank
  into the block before); the chisel stops at the hole as drawn.
- **A wagon's sprung board** is nailed, not pegged (`NailSession`, the carpenter's new `ClawHammer`
  in their kit): a tap to set, square blows to drive, a bent nail drawn with the claw.
- **The hew** is shown from the side too, the axe swung as the smith's hammer is at the anvil
  (`DrawToolSwing`, new `ui/work/axe` and `ui/work/log_side` in `tools/work_ui_art.py`).
- **Lifting a wagon** (`LiftSession`): A/D leaned it the wrong way (toward the tip, not against it);
  the bots, the tests and the hint all meant against it. Fixed in the session.

### From a Deck playtest: menus, the pad's words, the tutorial (2026-11-18)
- **Title**: Characters' Back returns to More (`ChooseCharacter` had cleared the submenu). The creator
  has a name-only Random, the folk tooltip is the hovered folk's (it was checked after the loop: the
  last button, the chosen folk's words), and "Start from" is "Starting look", clothes and features only.
- **The pad**: Start closes the game menu too (`ActionMap.PadPressedRaw`: the menu's pad navigation
  captured the pad, so the second press never reached the action). Narsil names View and Menu "Select"
  and "Start". A pair of one stick's directions is named by its axis ("L↑↓", "L←→"), so the lock pick's
  tension and pin aren't both "LS". In prose, actions with no pad button are "on the wheel, RB"
  (`Controls.Said`), and `~Pointer~`, `~Click~`, `~rightclick~` give the pad's right stick, RT and LT.
- **Tutorial**: alone, time holds while the card is open (`TutorialHold`, a setting, on by default);
  Got it, or B, lets it run. Not during hands-on work. The Shop button is "on the bar at the bottom".
- **The practice yard's customers** wait however long a learner takes (`Practice.StillWanted`; orders
  in practice never lapse), and a lesson set up again sends the earlier ones away with what they'd
  handed over (`Practice.SendAway`): a tester kept a strongbox that was never theirs.
- **Minimap**: a pin or rim arrow says what it is on hover; home's arrow carries a little house.
- **Trade pace** (`TradeDef.Pace`: easygoing, steady, busy) beside the name wherever a trade is chosen.
- **The coin stance's coin** has its own sheet (`person_<body>_coin`, front views only), drawn over
  everything: a halfling's braided beard hid it, and from behind it showed on the back of the head.
- Bench hashes unchanged for seeds 1234 and 99. Window moving and folding: ROADMAP, From testers.

### Planks for what's made, charcoal from the kiln, cloth for bandages (2026-11-19)
The town keeps itself in the makings the carter used to bring.
- **Planks** (`Stock.PlanksDef`) are what's crafted from: every forge, tinker and carpenter recipe that took
  timber, and furniture, stations and the festival's tables. Timber (`Stock.TimberDef`) is for building and
  mending only (walls, roofs, the bridge, a wagon's axle), and stone tools knapped by hand. The settlers'
  pile is half planks; the barn starts with `barnPlanks`; the smithy's chest, the tinker's, both kits and
  the carter's morning load carry planks where they carried timber. The AI's trip-bell takes a plank.
- **Charcoal** is baked from firewood (`BakeCharcoal`: 6 billets to 5) at a kiln standing out of doors by
  each smithy (Millbrook's south wall, Reedmere's pier: a map feature, its fire kept out of the smithy). The
  carter brings no more. `TradeDef.Keeps` is the door: an AI holder makes a kept recipe without a bill while
  it's short (`Crafting.KeepOf`, `Short`, `Kept`, which also lights an on-demand fire); the smith keeps 20.
  A bot smith bakes it once the bin's half that, or with nothing else to do (`BotPlayer.KeepIn`).
- **Cloth** for the empath: 4 in the shrine's basket at the deal and 2 each morning (season `shrineDelivery`,
  `OreCarterSystem.DeliverToShrine`; not where a cut road keeps the carter off). Torn into bandages by hand
  (`TearBandages`); a bot empath out of them does (`BotPlayer.TearBandages`).
- `tools/lake_map.py` reads the mods' building plans too (the woodshop had moved into the carpenter mod).
- Tests: `MaterialSupplyTests`. The raid drill passes once someone's on their way to the rack (who's caught
  on the road is down to where they stood); the party loop plays seed 1234 (99's dice moved with the kilns).
- Bench: seed 1234 state `5B644617887619E8`, full `D019E1AAC033B504`; seed 99 state `82049F2FF8ABCE34`,
  full `CC486CC08DBF190A`. Over 64 bot seasons, as before (won 36 to 35, dread 20 to 18).

### Readying the multiplayer test: the musician and the miner on, a desync fixed (2026-11-19)
- **On by default:** the musician and the miner (`defaultOn` in their `mod.json`), both player-only, so a round
  without them is as before. The test content (`TestContent.Standard`) has them too.
- **A trade only players take, late:** a late joiner can ask for one (`TakeTrade`); a newcomer comes to town for it
  under the joiner's own name and look (`TakeTradeCommand` carries them now; a villager taken over keeps its face),
  one to a trade, as the deal gives.
- **A trade the town can't hold** (`RoundSystem.InTown`: its workplace isn't on the map, as Reedmere has no mine)
  is neither dealt nor offered in the lobby or to a late joiner.
- **The lobby** counts a player-only trade toward "more players than trades" only once someone wants it, and marks
  it "only if played".
- **A leaver's character:** a musician stops playing (nobody stands about for it) and keeps the instrument; a miner
  keeps the trade and the pick.
- **Fixed, a save and join desync:** an AI keeper mopping up chose between equally near spots by the mess
  dictionary's order, which a load needn't keep (`CleanJob.For`: ties go to the lower cell index). It showed since
  the bigger Millbrook as `GoodsTests.AnAskForAPieceSurvivesASave`; a join snapshot is a save, so it would have
  split a co-op round.
- **The miner's first lesson** (`MinerLessons`, words in `miner.json`): into the mine, a face hewn, what came away
  carried to the shed's chest. The mine shed has its kit (`log_turf`).
- **The sweep watches the town's makings** (`SeasonSweep.Stores`): each one's low, mean low, end and hours out a
  season (charcoal, planks, timber, firewood, cloth, bandages, iron ore).
- Bench: unchanged with the new mods off (`--mods -musician,-miner`); with the miner's shed standing, seed 1234
  state `4053A2BDB5210BF2`, full `CBF62B88FA85A9C4`; seed 99 state `FD8F333AB780C411`, full `5B4A1B9322768C1A`.

### The makings, from the sweep; the triggers at work; the drum on small speakers (2026-11-19)
- **Charcoal at the deal** (`season.json` `smithyCharcoal`, was a constant of 30): 45, a day and a half's firing, so
  a smith new to the trade finds the kiln before the forge goes cold. Over 16 bot seasons the bin never ran out (it
  had, 0.8 hours a season; mean low 19 to 32). **Cloth at the deal** (`shrineCloth`): 6, was 4.
- **Cloth and bandages running out** (about 4 hours a season) didn't move with more cloth coming: each time it began
  with a crisis late in the season (the dragon or a raid at about hour 48, a fire once) and lasted to the end, past
  the next morning's delivery: the shrine's store burned. Left as a crisis's cost. Planks, timber and iron ore never
  ran short; firewood once, after a fire.
- **The sweep** shows per seed what ran out, for how long, and from what hour.
- **Fixed, from a Deck playtest:** RT and LT are the musician's notes (the sharp, a chord, the fifth note), and
  Strike and Heavy too: with a blade drawn, playing swung it (a swish, and wind spent). No blow is swung or bow drawn
  from hands-on work now (`AttackCommand`, `BowCommand`; and the game doesn't send them while `Working`).
- **The drum's doum** has its harmonics (2x to 5x, a little drive): a Deck's speakers can't sound its 82 Hz, and from
  these the ear hears the low note. About 12 dB more between 150 and 400 Hz; the body's still under it for headphones.
- Bench: seed 1234 state `B0538FAED4A548A7`, full `C86F4C60F6685905`; seed 99 state `4B5E9182A811FD84`, full
  `13FC3F3C779163C9` (the bin's charcoal). Over 16 bot seasons: won 8, fell short 2, Dread 6, as before.

### The tune book: a tune for each instrument (2026-11-19)
- **Drunken Sailor is out** (a sea shanty didn't suit the town). In: three traditional tunes, public domain,
  written out in our own notation: **"The Kesh Jig"** for the pipe (a jig in G, a third of a beat to a quaver),
  **"Greensleeves"** for the lute (E minor, called up by its chorus), **"Scarborough Fair"** for the fiddle (E dorian,
  its long notes for the bow). Our own three stay.
- **`for` on a tune** (`SongDef.For`): the instrument it's written for comes first in that instrument's book
  (`PlaySession.BookFor`); any tune still plays on any instrument with a book (the drum has none).
- `--drill sailor` is `--drill carry`: the pipe's own tune, the hands already carrying it.
- Tests: each instrument's own tune plays through minded (`EachInstrumentsOwnTunePlaysThroughMinded`); Greensleeves takes
  the shanty's place in the book tests (its form, call, breaths and long notes); the dance and the draw use the jig,
  the ring a reel.

### The musician's lessons and bot; the drum louder (2026-11-19)
- **Lessons** (`MusicianLessons`, words in `musician.json`, and a `tutorialIntro`): core, "Play a scale", "Strike up a
  tune" (the book, an opening, the beat and the breath kept to the end) and "Tune the lute" (put out of tune for it);
  after, "Busking" (a tip in the hat).
- **`ITradeModule.SetUpLesson`**: a trade's own setup for a lesson, after the core's stock and customers.
- **`~@id~` in the tutorial's words** (`Controls.Keys`): any action by its id, a mod's own included
  (`~@musician:note1~`), shown as bound now. `TutorialWordsTests` registers the musician's game half to check them.
- **The bot** (`MusicianBot`): of an evening at the inn, the book opened, its tune's opening played and carried
  through, a rest, again. **`BotPlayer.Custom`** carries a trade bot's own inputs (notes, breaths). Over 16 bot
  seasons with a musician seated: the same outcomes, Renown 66.5 against 67.8. The sweep lists every seated bot's stats.
- **Rune 9 on the pad** (R3): a lute's or fiddle's tuning couldn't be opened from a Deck. Camera mode waits while
  you work, so they share the button.
- **The drum louder** (a Deck playtest): the tek and the ka driven and let ring (their played level about 0.27
  from 0.05), the doum's body longer; gains near 1.
- `docs/RECORDING-THE-LUTE.md`: how to sample a guitar for the lute.
- Bench unchanged (seed 1234 `B0538FAED4A548A7`, 99 `4B5E9182A811FD84`).

### Work for two, played as the helper alone (2026-11-19)
- **`--help-as KIND`** (striker, keeper, pitman, lifter): a solo round where one bot takes the lead's trade and the
  player another, both stood by the work (`PairsLead`, `DevPairsLeadCommand`; a trade's module adds its own kinds,
  `ITradeModule.PairsKinds` and `PairsLead`, the carpenter's pitman and lifter). The lead's work is begun as F
  begins it (`CraftCommand` with the materials on them); whatever either was about is put down first.
- **`BotPlayer.WaitsForHand`**: a lead that never calls for a hand (no villager comes in the player's place).
- **The bot smith leads a greatsword:** a two-handed piece is heated only once someone's at the anvil to strike
  (meanwhile it calls for a hand); its hammer doesn't tap with no striker linked. Before, it went back and forth to
  the fire with nobody there, and the smithy went from 4 to 44 degrees in half a minute, past the bot's step-back
  point (`Safety`): the stall on the smithy's heat in the roadmap, reached at once.
- **Fixed, from a co-op try:** F at a lead waiting for a hand (the smith at the anvil) lends it when the work's in
  your reach; it offered "Trade with" them, the striker's sledge only from F aimed at the anvil itself.
- Tests: `PairsLeadTests`, each of the four led by a bot, waited on, and done with a hand; F at the smith lends it.

### The tune book from play: our own tunes, fairer time (2026-11-19)
From the user's playing on a Deck:
- **Our own tunes, longer** (the traditional three out): "The Miller's Wheel" for the pipe (a jig, about 40 s),
  "Lantern Home" for the lute (an air, about 50 s), "Hay in the Loft" for the fiddle (a reel in A, B, A, about 50 s):
  between their old length and a ballad's, which suits a round.
- **An opening with a long note strikes up** played in its time or a little slow (`StrikeUp`: the gap allowed after
  a note grows with its length). Greensleeves' call began on a long note, and played in time it never struck up.
- **A beat in a breath's gap is the breath's**: no miss while one hand takes the air at a mark.
- **Faltering, a beat kept steadies the mind twice as fast** (`PlaySession.RallyFactor`), so a slip doesn't snowball.
- **The opening's guide on a pad:** each key cap is at least as wide as its button's name (the jig's quick notes
  were caps too narrow to read).
- **The musician carries a dagger** (its kit): something to defend themselves with in a raid, R to draw it.
- **The tutorial lists the musician** (and every trade with a core chapter, not only those with a workplace); the
  basics skip the shop step, as they skip the workplace, for a trade with neither.
- **The musician comes wandering in from the road** (`TradeDef.ArrivesByRoad`, a line of its own; `Players.ArriveByRoad`):
  set 30 cells down the way a party would come in and walking into the square, at the deal and for a late joiner. And
  it's an easygoing trade on the pickers.
- **Tuning, quicker to land** (from play): the peg turns faster the further the string's out (`TuneSession.RateAt`:
  about four times as fast at 40 cents, the old slow turn near the note), and let go within 5 cents a string seats
  on its note (`SeatCents`).
- **Free play's rhythm meter, fairer** (from play: "doing awesome, and it shows middling"): a gap within 4 steps
  (about a fifteenth of a second) of its pulse is dead on (`Performance.Grace`, never more than a quarter of the
  pulse), and no pulse finer than half the usual gap is tried (`FinestShare`: against one that fine any jumble fits).
  A good hand at four notes a second now reads about 0.9, steady as a heartbeat (it read 0.6); a sloppy one about 0.8;
  a jumble about 0.35, ragged.
- **No breath to mind** (from play): F taps every beat, long notes' too, and the swell key (Shift, LB) held through a
  long note shapes it. Air isn't spent and breath marks ask nothing (the note before one still lifts early, as
  phrasing). The breath input is the swell's now (`PlaySession.SwellBit`, `SwellHeld`), saved where it was.
- **The hat stays put** (from play: the labourers carried it off to the barn): a thing set down on purpose is
  `SetDownBy` its owner, and no job takes it (`Simulation.IsAvailable`, `AvailableItems`); the silver tossed by it too.
- **F plays only an instrument in hand**, and never on furniture: on the shrine's pallet, F lay the musician down
  only after it opened the music. The bot takes its pipe in hand (`HoldCommand`) and steps off any furniture first.
- **The smithy off the west road** (from play: it stood across it): north of the road, its open front to the south
  onto it (`tools/village_map.py`), the kiln by its west wall. Tests that stood the smith at "the first cell beside
  the forge" stand them where the forge and the chest are both in reach; the empath bot's calling-back test sets the
  empath down in the smithy with the body (`Near(inSight)`: a bot acts on what it sees). Bench: seed 1234 state
  `94839B9CB447ECBB`, full `AEE7BD3D7563D150`; seed 99 state `F0A4C7903E7B0120`, full `20F3C8D80398D4BB`.
- **Fixed: no "Raid!" banner when a trip-bell rang** (from play): the alarm went out as the bell's message, placed at the
  bell, and a placed message reaches only who can see the place. In bot seasons the AI tinker's bell rang in every
  raid, so nobody away from the road got a banner. The town's bell is now unplaced ("Raid!", heard by all), and the
  trip-bell that set it off is noted where it is.
- **Bronze at the smithy** (from play: pieces asked for in bronze, and none to make them): 4 bronze bars, 8 copper ore
  and 4 tin ore in its chest at the deal (`season.json` `smithyStock`), and 6 copper and 2 tin in the carter's morning
  load. Copper's smelted into bars at the kiln, then 3 bars and 2 tin alloy into 4 bronze (charcoal in the kiln makes
  it hot enough). The smith's smelting lesson says so.
- **Fixed: a piece worked cooler than iron (bronze) went back and forth to the fire.** "Hot" was by degree, and a
  bronze window starts partway into dull red: drawn out at the right colour, it read as cold. Hot and the hammer's full
  strength are now by the colour shown (`Smithing.HotEnough`, `HammerSession.Workable`), and a piece looks ready at the
  colour at the top of its window (`HeatSession.LooksReady`). The bot smith over 8 seasons: heats 47 to 7, hammerings
  2.5 to 6.5, mends 4.1 to 5.1 (the roadmap's stall on the smithy's heat was this, mostly).
- **A bot keeper counts who's waiting by the orders it knows** (on what its sign sells), not bodies on the line's cells:
  a long line spills off them, and seven waiting read as two.
- Bench: seed 1234 state `D07ABAFA413F0DA9`, full `E5C4F7BF87167692`; seed 99 state `76B9A4BDB726594C`, full `A3CAC15B8861C2E5`.
- **The festival, from play:** a little shorter (`festivalHours` 7.5 to 6.5, the revels the shorter; the crowd gathers in
  15 minutes, was 25), the crier's tale told briskly (`talePace` 0.75 of a line's time), and each honour may be
  cheered once, as each toast is (with one party in town there was one toast to cheer, and the rest were honours).

### Work for two, from play: scenes for each helper (2026-11-19)
- **Every helper's panel is a scene of the work, and the lead sees the helper at it.** The striker sees the anvil with
  the bar at the smith's heat, the smith's hammer winding up ("Here it comes!", then "Now!") and their own sledge; the
  smith sees the sledge over their anvil. The keeper sees the body on the pallet lifting toward the grave's glow and
  their own hands pressing it down; the empath sees the same under the runes. The pitman looks down on the great log,
  the kerf and the saw between the two of them; the top sawyer sees the pitman under the beam. The lifter sees the
  wagon tipping on its far wheel, the level, and the carpenter's mallet at the hub. Characters at a helper's work move
  their hands only while working (`WorkSession.HandsMoving`).
- **F at or beside work for two wanting a hand lends it** before the stacks, benches and seats round it
  (`Pairs.WantsHandNear`): in the woodshop, F near the great log kept offering everything else.
- **The keeper's pull is slower** (`HoldSession.PeakAt` 36 to 54 steps, the peak 9 to 12 wide); **the lift is easier**
  (a wider level band, a slower sway). **Hanging on a moment at the bottom of a pull doesn't bind the saw**
  (`PitSession.HoldGrace`, 24 steps): it read as "With the stroke!" then "it binds!" at once. The panel says let go.
- `--auto-help` (with `--help-as`) joins the bot lead whenever it wants a hand, for screenshots.
- **The miner is off by default** for the multiplayer test (`--mods miner` to play it). The bench moves with it:
  seed 1234 state `30C197C34996F68D`, full `C632C5BF0983A867`; seed 99 state `C532130C046348AB`, full `7EE308EA7A0BC59C`
  (with `--mods miner`, unchanged from before).
- **The musician is comped a room at the inn** (from play): a trade's `lodgesAtInn` makes its home a room there,
  kept for them and never let, while one's free (a cottage if not; `Homes.Give`). Bench unchanged.

### Raiders rob the downed; raids sized by the players (2026-11-19)
- **A raider who puts a player down cuts half their purse and runs for it** (`Combat/Plunders`, `RobJob`, `Plunder`):
  it isn't `Leaving`, so the town may still chase it, and downed or killed before it's off the map it drops the silver
  where it falls ("Caught!"). Off the map, it's gone. Only players' characters are robbed.
- **A round's raid grows with its players more than its town** (`season.json` `raidAiShare` 0.5, `Raids.Points`): an
  AI villager counts half of a character a player's playing. The six-bot sweep is unchanged by it (bots are players);
  the combat sweep's trials size raids as for players (`Director.RaidAiShare`). Solo, one bot, 16 seeds: as the
  musician won 8 to 13, deaths 2.4 to 0.6 a round; as the smith won 8 to 10, deaths 1.2 to 1.1.
- From a solo round lost on day 2: the first evening's raid was two, an archer soon down and a skilled spearman
  (melee 9, bronze helm) who took one blow in the whole fight and put down all seven AI villagers one at a time, then
  the player. Everyone down, nobody was left to bind anyone, and all bled out by morning.
- **Fixed: a sweep crashed when an AI empath was downed by a wound it drew in.** The job ended inside its own step,
  and the AI then ended it again (`Job.Tick`, `AiSystem.RunJob`).
- **Anyone can toss a coin in the musician's hat** (from the co-op list): F at or beside a playing musician's hat puts in
  a silver, as often as you like (`Takings.Toss`); the musician's window shows who gave it. Not their own hat.
- **An untended wound stops bleeding by itself** (`HealthSystem.ClotHours`, 6 game hours, easing off evenly from when
  it was made; it still wants a dressing). A town all put down by a raid wakes hurt rather than bled out; the deepest
  wounds still kill. Six bots, 16 seeds: deaths 1.4 to 0.5 a round, won 11 to 13; solo musician deaths 0.6 to 0.4.
  The panels show bleeding as it is now.
- **The musician's hat is worn** (from play): a felt hat with a wide brim and a feather (`tools/chibi_people.py`'s
  `musician_hat`, written into mods/musician with its dwarf and halfling builds; `chibi_builds.py` now builds a mod's
  garments where they are). Put on from the kit (a kit's garment goes on if the slot's empty), taken off and set down
  upturned to play, and back on the head after. The playing panel draws it upside down, the coins in its opening.
- **The musician hears things** (from play): an adventurer settled in at the music may talk of a quest (`Rumours`,
  half the time, nearer the truth than the tavern's idle talk), and the playing panel lists the talk you've heard.
  F at the innkeeper passes it on, said aloud: whoever hears it clearly takes it in. Only the musician, once in two
  game hours.
- **The playing panel shows the instrument in your hands** (a first pass, to be played and changed): the pipe's holes
  fingered for the note and breath leaving its end, the lute's and fiddle's strings fretted and ringing (the fiddle's
  bow moving), the drum's head struck where the stroke is played.

### The rare companies dressed as themselves (2026-11-19)
The companies borrowed outfits that didn't fit them (the grey wanderer in the seer's kaftan and skirt, the
dwarves in the blue-haired wanderer's poncho), and their looks were the hash's, so the grey wanderer could
come young, beardless and blue-haired. From docs/COMPANY-GEAR-PLAN.md, its first four parts.
- **New garments** (`outfits.json`): the wanderer's robe (Tunic slot), a pointed hat and a dwarf's tasselled
  hood (cloth `garment`s in the Head slot, dyed). Art in `tools/chibi_people.py` (`g_wizard_robe`, `g_hat`), with
  the dwarf and halfling builds, and icons per dye (`tools/norse_items.py`). A Head item's `_back` layers are
  drawn behind the body now, as a cloak's are (the hood's tail, seen from the front).
- **An outfit per company kind**: `GreyWizard` (robe and hat in ash-grey), `DwarfLord`, `DwarfElder`,
  `DwarfKin` (a hood in a rolled colour; the axe-hands put their helms away to show them), `Burrowfolk`
  (barefoot), `Ranger`, `ElfWarden`.
- **A fixed look per adventurer kind** (`look` in `adventurers.json`, on `Kin`): the grey wanderer is always a man
  with long grey hair and a full beard; the dwarves always bearded, the graybeard grey. `Clothing.BodyOf` fits
  clothes to that body, `HumanLook.Fixed` draws it.
- **Fixed: a party back from a quest left a set of clothes in the road.** `Respawn` dressed them in their outfit,
  then put back what they'd worn over it, dropping each piece it replaced. They come back in what they wore.
  A returning company keeps its folk now, too (it lost its `Kin`).
- Saves to format 64. `CompanyTests`. Bench hashes unchanged.

### The brawl at the inn (2026-11-20)
A crisis of its own, from the combat tutorial's sparring: a rowdy crowd tears up the inn, and the town settles it
before it boils over. Plan and decisions: the brawl plan, slices 0 to 4.
- **Crisis kinds are a registry** (`SimRegistry.Crisis`, `Director/CrisisTypes`): `CrisisKind` is a name, the
  Director's send and load, the festival's lines and the replay's labels read the registration. A mod's crisis is
  `modId:Name`, weighed in `season.json` like the game's.
- **No steel in the inn** (`Combat/HouseRules`, a building def's `noSteel`): fists only there for the town and its
  guests, raiders and beasts aside, lifted while a raid's on. Weapons stay sheathed on screen too.
- **The brawl** (`Hospitality/BrawlCrisis`, the `brawl` block of `season.json`): a `crowd` party (the Bent Nail crew)
  comes in loud with drink, its ringleader throws the first punch, guests struck join in if rowdy or in their
  cups. Brawlers fight with the defenders' footwork, pulling their punches among themselves
  (`HouseRules.Pulled`). Heat climbs with how many are at it and boils over into the street (and maybe a lamp
  into a fire). Knocked down by someone not in it, a brawler's settled and sleeps it off; dropped by a mate, they
  get back up. Settle the ringleader and those who aren't rowdy lose heart.
- **The inn's furniture**: once it's hot, tables and stools are smashed, shoved into, and stools hurled
  (`breaksInBrawls`, `thrownInBrawls`), the carpenter's to make again (`ITradeModule.Burned` is now `Lost`).
- **Players hurl stools** (`Combat/Hurling`, `HurlCommand`): fists up, F picks one up; the right button draws it
  back over a power gauge, the left throws, a little off line and with no landing shown.
- **Other ways to settle it**: a bucket of well water sobers a brawler out of it; a ghost's sign sends one running;
  the musician calms them with a tender air, makes dancers of them with a reel, riles them playing sour
  (`ITradeModule.Soothe`); quick lines while it's on (`SayCommand`'s quick id): "Take it outside!" (only the
  ringleader can lead them out), "The watch is coming!" (once, on the sobering), "Drinks on the house!" (the
  keeper's, so much a head).
- **Bots**: a bot fights brawlers in sight with fists, a keeper bot stands the drinks first, a ghost bot spooks them.
- `--brawl-test` (`--drill brawl`). Fixed on the way: the carpenter's join split under the gauge's mark (the
  hand's wobble is in how far a blow drives now, not whether it splits). Saves to format 65. `BrawlCrisisTests`,
  `HouseRulesTests`, `HurlingTests`. Bench hashes unchanged.
