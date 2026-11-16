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
