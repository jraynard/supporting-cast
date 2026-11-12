# Roadmap

Milestones toward a playable prototype, then beyond. Design intent is in [DESIGN.md](DESIGN.md).

## Done

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

Known leftovers:
- Build and zone tools are open to every player; the Carpenter should own them. The Carpenter's
  building and mending have no game of their own yet. (The lockwork has since gone to the Tinker,
  and the Carpenter is benched: see "The Tinker".)
- Players can hunt animals but can't yet attack people (M4).
- Villagers don't fight back against raiders, they only suffer them (M3).
- The dragon creature has no brain or breath job of its own yet (M3's late-round crisis).
- Some dragon-era art, tools and def fields are still in the tree. Prune with the M6 art pass;
  the whetstone, ale cask and meditation stone borrow art too.
- AI healers only bandage. Casks don't refill and there's no brewing. Mixed drinks, lighting the
  forge by hand and commission details are still to come (docs/DESIGN.md, "Trade notes").
- `Adventurer.Satisfaction` and the expedition tallies are collected, waiting for M3's Renown and Dread.
- The new mini-games haven't been tuned in play yet.

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

Known leftovers:
- Lanes cost the same to walk as grass, so villagers don't prefer them.
- `--building-demo` now lays its showcase over the village.
- The pig is the boar's sheet recoloured and shaven.
- The start message still mentions a dragon egg.

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

Known leftovers: the other mini-games are still drawn plainly; the title screen keeps its old
look; time doesn't yet advance on its own when there's nothing to do.

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

Known leftovers: no mid-step turns (a turn waits for the step under way); the forge can heat
the smith's spot to 80°C+ and kill a bot that stands there for hours (also before this pass).

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

Known leftovers: commissions (the buyer's hilt, runes and metal); molds, and the hilt and grip
as their own step; lighting and banking the forge by hand; reforging; AI smiths still forge with
a plain `CraftJob`. Tune the heat and quench timing in play. Save format 3.

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

Known leftovers:
- The AI innkeeper doesn't mix: asked for a named drink, it pours an ale.
- Mess goes on floors only, not on tables, so there's no rag. Rooms have no keys yet.
- The new stations borrow existing textures and the chest's toolbar sprite. The mop and the mess
  decals have their own art (`tools/inn_art.py`).
- Tune the pot, seasoning, wash, tap and mix timings in play.

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

Known leftovers:
- The meditation stone still borrows the mason's bench icon.
- AI healers still only bandage.
- A wound stays felt once found, however it changes.
- Pallets aren't kept from villagers who sleep there.
- Tune in play: the draw feels, the heartbeat window and the wrap band.
- Bots don't weave charms or put goods on show.
- Adventurers never tear a charm in town.
- Tune the labyrinth in play: its size, the sitting's length and the centre's burst.

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
- Save format 7. Bench baselines: seed 1234 state `5DCB26EC7CD4459D` full `AB09BAC6394EC6FC`; seed
  99 state `71272E53719B30F6` full `A09F9DA9676D2333`.

Known leftovers:
- No lockboxes or locked chests yet: locks go on doors only.
- Bots don't pin locks, make contraptions or haggle over curios; AI tinkers don't buy curios.
- Bridges, gates and the town's structures aren't damaged by anything yet: that's M3's crises.

### Heroes Not Included (2026-11-12)
- The game is **Heroes Not Included** throughout: the solution, projects and namespaces
  (`HeroesNotIncluded`, `HeroesNotIncluded.Sim`, their tests), the exe and the Mac `.app`, the
  packages, the Linux desktop id (`heroes-not-included`: run `tools/install-desktop-entry.sh` again),
  the Mac bundle id and the co-op game id (older builds can't join newer ones).
- Settings, saves, screenshots and logs live in `~/.config/Heroes Not Included` now. Nothing moves
  over from `Supporting Cast`: old saves stay there.
- The LAN share's default is `/srv/heroes-not-included` (`SC_SHARE_DIR` to change it). Still the
  old name: the repo folder. Bench hashes unchanged.

## Next

### Trade refinement
- refining the four initial trades to be fun. blacksmith, innkeeper, carpeneter (locksmith), and healer.
- blacksmith done, innkeeper done (some jobs split off to cook which will be handled later).
- healer done (see Healer polish), tinker done (see The Tinker): the starting trades are in. The
  Carpenter (building, furniture, fletching and bows) waits for later.

### M3: A whole season
- `Director`, Renown and Dread, crises including the late-round dragon, the festival, the
  end-of-round reveal. Villagers defend themselves.
- Crises damage the town's bridge, gate and buildings; the Tinker mends them (a game like truing
  a wheel), and a broken bridge cuts a road until they do.
- need to deal with downtime for the villagers as well during the night or when they should be sleeping, and how that might interact with the traitor system later. they shouldn't just have to watch their character sleep for 5 minutes though, that would be boring.
- needs probably will have to be tuned to not be too annoying, but it would be good for the townspeople to gather at the inn after the day to eat, and later when we have the miner and forager trades they will have to buy supplies from each other to replenish their stocks, so there will be some interaction pressure there.
- make the "favors" system a physical board that has to be interacted with in the town square so people looking for work can use it. or even use it to ask for help from a laborer or something.
*Exit: an Honest round is playable start to finish.*

### PLAYTEST PREP
- add icon for the game executable
- publish builds in a way we can easily distribute for playtesting (maybe use discord or something)
- UX / art polish pass (not a full art pass but just clean things up as needed to make sure its legible for people that are not hardcore gamers)

**Playtest gate: is serving heroes fun with friends, without traitors?** Tune before going further.

### M4: Whispers
- Allegiance, objectives, the hidden shop, evidence, player violence, Constable and jail, the bell,
  meetings and exile, ghosts.

*Exit: a Whispers round.* **Playtest gate: can traitors act and be caught?**

### M5: The systemic layer
`Reagents`, Apothecary, Undertaker, Merchant, writable notes, keys, disguises, poison on anything.

### M6: Breadth
Remaining trades, more maps (authored like Millbrook), crisis variety, personal ambitions, art pass.

### M7: Playing with strangers
An authoritative server filtered by `Perception`, moderation, voice.

### polish pass - timing uncertain
- ~~get rid of the sprite wagon and do a box prop for it~~ (done with the Tinker); check to see if there are any others that need updating
- make the bridge over the river nicer - half height walls, maybe an arch to it or supports?
- title screen polish
- doc cleanup pass
- character creation - more traditional DnD fantasy options. elves, dwarves, hobbits, etc
- make the hearth into a fireplace
- wall torches
- difficulty tuning - some jobs should be easier, some harder, to accomodate different players and styles.