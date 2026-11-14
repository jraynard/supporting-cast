# Heroes Not Included: design

Living document: the decisions so far and the intended depth of each system.
What's built and what's next is in [ROADMAP.md](ROADMAP.md).

## Pitch

A multiplayer fantasy village where the players are the people a tabletop adventuring party usually
walks past. One player runs the inn, another the forge, another the healer's shrine. Heroes arrive wanting
food, repairs, healing and work, and treat everyone like scenery. The village has to keep them
alive and paying long enough to survive the season, and someone in it may be working against
everyone else.

The inspiration is the social, systemic play of Space Station 13 and Among Us. The setting, names
and art are our own: generic fantasy, with no trademarked tabletop terms or monsters.

Tone: warm, comic and a little put-upon, until someone finds the blacksmith in the river.

## Pillars

1. **Your trade is your game.** Each one has its own interface, knowledge and way to be good at it.
   A bartender and a healer shouldn't feel like the same game with different labels.
2. **Nobody can do it alone.** Trades depend on each other's output, so people have to talk.
3. **Properties, not scripts.** Poison is a substance on a thing, a lock works for whoever holds
   the key, and fire burns what burns. A plan nobody anticipated should simply work.
4. **You know only what your character knows.** Sight, hearing and rumour are the information game.

## A round

- 30 to 40 minutes at fixed speed with no pause: 2 to 2.5 in-game days at 16 real minutes a day (40 s an hour).
  Players get no speed controls. When there's nothing left to do (everyone asleep, say, or the
  town quiet between parties), the game advances time on its own.
- 4 to 12 players. AI villagers fill the empty trades at a basic level, so any headcount works.
- Self-contained: no progression between rounds yet.
- Lobby, trade deal from player preferences, the season, end-of-round reveal.

## The loop

Parties of entitled AI adventurers arrive and make demands. They spend coin, cause trouble, head
to the dungeon, and return with loot and wounds, or don't return. How well the town equipped and
informed them decides how the expedition goes.

- **Renown** rises with satisfied, surviving parties.
- **Dread** rises as the dungeon goes unanswered.
- The town wins by reaching the end-of-season festival with Renown high enough. It loses if Dread
  overflows or the town falls.

**Shops and the line.** Players set the prices for their own goods and services on a sign at
their shop, which anyone can read. An adventurer who wants something (a drink, a mend, a healer, a
room, a quest lead) weighs the price against what it's worth to them and what's in their purse. If
it fits, they go to the shop and line up at its counter: the bar for a drink, the smithy's counter
for a mend. If it doesn't, they say so aloud and go without. Each want is an `Order` in code. Price
is a lever: too high and nobody comes and Satisfaction falls; too low and the purse fills slowly.
Players ask each other for things as **favours**, for silver they name, pinned to the notice board
in the square: only those who read it know what's wanted. An errand (bring me so many of a thing)
is done by bringing it, and the town's labourer takes those on.

## Trades

Called `Trade` in code, because `Ai/Job` is already the AI coroutine.

| Trade | The game it plays | Built from | Betrayal it enables |
| --- | --- | --- | --- |
| Innkeeper | Order juggling at the bar, drink mixing, letting rooms, keeping the common room, hearing gossip; matching parties to quests on partial intel | Social, `Expeditions/` | Spiked drinks, selling secrets, sending a party to its death |
| Cook | The inn's kitchen: the stew pot by taste, the larder, the bowls | Larder, cooking | A turned ingredient left in the pot, or something worse |
| Blacksmith | Heat, hammer and quench timing | Forge, bellows, tiers, `Crafted` quality | A hidden flaw in a blade |
| Healer | Take a patient's wounds into yourself with focus, cast the right mending on yourself, meditate back your mana | Health, `Work/`, new mana | Taking only part of a wound, or handing one back |
| Constable | Patrol, evidence, arrests, jail | `Prisoners`, locks | Framing, abuse of authority |
| Apothecary | Discover reagent reactions, reshuffled each round | New `Reagents` | Poisons, explosives |
| Merchant | Haggling, spotting cursed loot | Inventory, `HoardValue` | Fencing, forged goods |
| Undertaker | Autopsy, burial before the dead rise | Health, `Scars`, carcasses, the pyre | Destroying evidence |
| Forager, Miner | Risky trips beyond the walls | Harvest, Mining, Hunting, Fishing | Alone out there, in both directions |
| Tinker | Pick the locks and disarm the traps on chests adventurers bring back; pin locks and cut keys; fit contraptions together for the road; mend wagons; appraise curios and buy them for a collector | `Tinkering/`, doors, `Work/`, expeditions, loot | A spare key, a chest opened early, a trap left armed, a contraption that jams, a curio talked down |
| Carpenter | *Benched for now.* Building, mending furniture, fletching and bows | Construction, Rooms, the woodshop | Comes back as a trade of its own later |
| Bard | Rhythm performance, morale, rumours | Mood, Social | Spreading lies |
| Mayor | Treasury, laws, chairs town meetings | New | Corruption |

### Trade notes

Ideas for depth, to build when each trade's milestone comes round.

**Blacksmith.**
- *Built: forging a heat at a time* (`Work/Smithing`).
  - **Begin:** a piece is begun at the forge, its materials staged there, with the billet in the
    smith's tongs.
  - **The fire:** hold F to work the bellows, and draw the piece out (D) when its colour is
    right. Heat is shown only as colour, black through cherry and orange to white.
  - **The anvil:** it shapes while it holds that colour; as it fades it goes back to the fire.
    Hammering black iron can crack it.
  - **The quench:** for weapons and tools. Lift it as the steam dies to a hiss: too soon and
    it's soft, too long and it's brittle. Armour is finished off the anvil.
  - **The edge:** edged pieces come out blunt, for the grindstone.
  - **Flaws:** they don't show; they tell in use (`Flawed`, `WeaponWear.Struck`), the hook for
    the hidden-flaw betrayal.
  - **The smithy:** an anvil, a quench tub, a grindstone, bellows, a tool rack, a fuel bin and
    the chest the ore comes in.
- *Built: smelting a bloom* (`Work/BloomSession`). The smith starts a round with two bars and a
  chest of iron ore; a carter brings more each morning (`Shops/Deliveries`). Ore and charcoal go
  in the forge, and the bellows keep the fire orange while the bloom comes together: let it sink
  and it stalls, blow it yellow and the iron runs to slag. Then the slag is beaten out on the
  anvil. A fat, clean, well-beaten bloom gives three bars, a poor one one or none. It's shown as
  colour and words, never a number.
- *Built: the call to arms* (`Militia/`). Some hours before a raid a shepherd brings word: how
  many, by which road, and when (the raid is then fixed as told). The town calls for weapons for
  its unarmed folk and pays the smith from a town purse for each new one hung on the muster rack
  by the well. When the raiders actually arrive the bell rings and the town musters: villagers
  without a weapon take one from the rack (players by hand), and the armed fight. The rack is left alone
  otherwise. The town's arms don't count toward a raid's size. Each trade starts with a makeshift
  weapon of its own (the cook's knife, the smith's hammer, the innkeeper's Lucky Lou under the bar):
  enough to hold off a raider when cornered, not enough to skip the muster.
- Running the forge is play in its own right: the bellows are, so far. Still to come: lighting
  and banking it by hand. It lights itself for the work now, and the smith feeds it from the bin.
- Repair starts from what's actually wrong with the piece. A dull edge goes to the sharpening
  wheel or stone; a chipped or bent blade goes on the anvil, and the worst may need reforging, or
  other real techniques. The smith has to judge the right approach. M2 starts with an abstract
  version: an edge (dull or keen) and chips (HP). Hammering out chips flattens the edge, so a
  chipped blade is hammered first and then sharpened. Mending now uses the forge's stations:
  heat it, hammer out the chips, then the grindstone. Reforging the worst is still to come.
- Commissions have details the buyer chooses: the hilt, runes engraved on it, the materials.
- more detail - heating the billet to the correct temp based on how hot it looks, using molds or shaping the item, making the hilt, handle and grip, thinking of like forged in fire style with realistic detailed forging here (as long as its fun).

**Innkeeper and Cook.** The inn takes two. The innkeeper keeps the bar (drinks, mixing, the
kegs, the tankards), the rooms upstairs and the quest board; the cook keeps the kitchen (the pot,
the larder, the bowls, the meals out). Each washes their own crockery and mops their own floor:
the kitchen's the cook's, the common room's the keeper's. After the first playtest the whole
inn was too much for one person. Even split, the bar's line, a room to show up and a keg to change
all want the keeper at once. The skill is in the juggling, and in what they overhear while they
do it.
- *Built (Innkeeper polish): all of the below, but brokering's unchanged and the "Later" list
  is still to come.* The inn has a kitchen and a storeroom behind the common room, and six rooms
  upstairs.
- *The bar.* Since M2: pour ale at the cask. Hold to pour, and the tilt controls the head.
  Customers taste the quality.
  - **Vessels:** a drink goes in a clean tankard and a meal in a clean bowl, taken from the shelf
    behind the bar. They come back dirty, left where the customer drank, and are scrubbed at the
    tub, brush moved over the grime until the piece shines. Run out of clean ones and the line stalls.
  - **Casks and kegs:** a cask runs dry. Tap a new keg from the store by knocking the tap in on
    cue; a miss sprays foam and wastes servings.
- *The pot.* Cooking is one stew pot by the kitchen's fire, cooked by taste (`Work/Cooking`).
  - **The ingredients:** whatever's in the larder.
  - **The heat:** keep it at a simmer, read only from the surface (still, a simmer, a rolling
    boil), as the forge is read from its colour. Stir so it doesn't catch.
  - **The seasoning:** season a pinch at a time, tasting as you go. It can't be taken back out.
  - **The result:** a pot gives many bowls, ladled out, each with a quality the diner tastes. A
    spoiled ingredient spoils the pot, and nobody can taste it until the diner is sick.
- *The hatch.* Meals are ordered at a serving hatch in the kitchen wall, not the bar (`Shops/Hatch`).
  Diners line up at it from the common room and say what they want through it. The cook puts
  bowls up on its counter, no more than are waiting for one, and whoever's at the front takes one,
  pays the cook and goes to sit with it. First come, first served: a bowl up on the hatch is
  anyone's at the front, and nobody else can take it.
- *Rooms.* Let by the night: the guest pays each evening, keeps the same room while they're in
  town, and checks out when they move on or let a night go unpaid. A slept-in bed is stripped
  and remade before it can be let again, and a guest shown to an unmade one says so.
- *Mess.* Spilled ale, mud off the road, crumbs and blood are things on the floor
  (`Hospitality/MessSystem`), and anyone can see them; dirty dishes lie where they were left.
  Customers think less of a dirty room. Scrub it up, faster with the mop in the keeper's kit.
  Scrubbed blood is gone, which will matter once there are bodies. Later: mess on the tables.
- *Mixed drinks.* A press-on-cue sequence at the bar.
  - **Recipes:** named ones are what adventurers ask for.
  - **House mixes:** any other combination makes one, its effect coming from the properties of
    what's in it (warming, rousing, numbing, loosening). The same plumbing later carries reagents
    and poison.
- *Sitting over it.* Served a drink or a meal, a customer pays and tastes it at once, then takes it
  to a free stool and sits with it a while (about half a game hour for a drink, more for a meal).
  It takes hold, or fills them, when it's finished, and the empty is left where they sat. Sat
  together, they talk: a third and two thirds of the way through, each may tell a rumour, likelier
  with drink in them. So a full common room is where things are overheard.
- *Drink takes hold.* Drinkers get merry.
  - **Tongues loosen:** a tipsy adventurer volunteers rumours, and truer ones, so a busy bar is
    the keeper's best intel.
  - **They get loud:** their speech carries further.
  - **They get clumsy:** they spill. Too much, and they're asleep on the floor.
- *Directions.* Adventurers new to town know only the inn, and the shops their party has walked
  past. Wanting a smith, they ask aloud where one is. Whoever answers (keeper or anyone) points
  them there, and everyone in earshot learns it; a villager answers if no player does. Unanswered,
  they think less of the town.
- *Brokering quests* (since M2, when the Quest Broker folded into the Innkeeper): the keeper
  hears every rumour over the bar and points parties at work on whatever it's worth.
- Later:
  - **Brewing:** the kegs from the keeper's own barley and hops (Forager), not a delivery.
  - **Money:** a tab for parties good for it, and tips for quick, fine service.
  - **Bar fights:** drunk adventurers start brawls that break furniture (work for the
    Carpenter, when it's back). The keeper throws them out, or calls the Constable.
  - **Rooms:** keys (the Tinker fits locks to the room doors; built), and a chest per room for a
    guest's things while they're out.
  - **A notice board:** at the inn, for shop signs, favours and quests posted where anyone can
    read them.
  - **Supplies:** bought from the Forager, the Miner's hunting trips and the farms by favour,
    so an empty larder is everyone's problem.
- Cook, later (ideas to give the kitchen more to do):
  - **A menu beyond stew:** a roast on the spit (turned on cue), bread and pies from an oven
    (knead, prove, bake by smell), porridge in the morning. Each dish is its own small game, and
    the sign prices them apart.
  - **Prep:** chopping on rhythm, butchering what the hunters bring in, plucking. Done ahead in
    a quiet hour, it makes the rush go faster.
  - **Rushes:** breakfast and supper come all at once. Two pots on the go, the fire kept fed
    (the Woodcutter's wood), the hatch line growing.
  - **Diners who want something:** "something hot", "no meat", a party's own tastes. Get it
    right and they tip; a rich party asks for something special.
  - **Rations for the road:** a party setting out orders provisions. How well they're packed,
    and whether they've turned, tells on the expedition (`Expeditions/`): fed well, they fare
    better; spoiled, they sicken on the road.
  - **Feasts:** a party back in triumph orders a feast for the whole room. A big, many-dish job
    that lifts everyone's mood and loosens tongues.
  - **Keeping the larder:** rot runs faster in a warm kitchen. Salt, smoke or pickle to keep
    meat, and keep the cold store cold.
  - **With the Healer:** broths that speed healing and teas from shared herbs; the Healer sends
    patients to eat.
  - **Trouble in the kitchen:** a grease fire if the pot's left, rats in the larder.
  - **Word of the food:** a good table gets talked about, and more parties come to eat.
- Betrayal later (M4/M5):
  - one bowl poisoned for one diner (the hatch is first come, so a target has to be handed it);
  - a party's rations drugged or spoiled to weaken them on the road;
  - a drink spiked or watered, or a short measure;
  - a guest's room rifled while they're out;
  - what was overheard sold to the highest bidder;
  - directions to the wrong place;
  - a party sent to its death;
  - a floor scrubbed clean of evidence.

**Healer (empathic).** Magical in flavour rather than clinical, but grounded in the health
model: wounds sit on body parts, have a kind, and bleed, hurt and fester.
- *Feeling them over.* Touch is the diagnosis. The healer runs their hands from head to feet;
  the feel rises near a wound, and lingering there makes it out. Only a wound they've felt can
  be drawn. Pressing in is slower, but it's the only way to find festering under a dressing.
- *Laying on hands.* They draw a wound across, holding their composure against its pain; lose
  it and the link snaps, with a backlash. Each kind of wound pulls on the composure its own way:
  a cut jolts, a bite tears twice, a bruise swells and ebbs, a burn pulls steadily one way, frost
  numbs the hands so they answer late, venom grows worse the more comes across. So the draw
  teaches what each feeling is. A patient lying on a pallet is calmer to draw from. The wound
  lands on the same part of the healer, lighter the more skilled they are. An infection comes
  with a fully drawn wound.
- *Carrying it.* What the healer takes on is real. It bleeds and hurts, and a healer who takes
  too much collapses.
- *Weaving.* Mendings are woven from six runes (Thread, Flesh, Ember, Rime, Salt, Still), keyed
  1 to 6 on the beat of the healer's own heart, then let go. The mix decides the mending, in any
  order: Thread and Flesh knit a cut; Still and Flesh ease a bruise. Extra copies of a mending's
  lead rune make it stronger, for more mana. A mix that makes nothing fizzles for a little mana.
  Absorbed wounds read as sensations, not labels (a clean bright line of pain, a deep swelling
  throb), so telling which mending a wound wants is a skill the player learns. Pain makes the
  heart race and stumble, and the weave with it.
- *The book.* Every empath starts with the common mendings written in their book: each one's
  runes, what it does, and the feelings it answers. Others are found by trying new mixes, and
  written in when they work: searing a wound shut, steadying your own hands, grounding lightning.
- *Charms.* Before anyone's hurt, the healer weaves ahead: a mending woven into a strip of linen
  keeps. Torn, it lands on whoever tears it, gift or no. Put out in the shrine with a price,
  charms sell to parties as wards for the road, a couple each and no two alike. On a quest a charm
  spends itself on the first wound it fits, and a healing one may turn a death into a bad wound.
  Anyone can see which mending a charm holds; only its maker knows how well it was woven. Which
  to weave is a guess at what the parties will face.
- *Meditating.* Out of mana, they find their way inward: each sitting is a labyrinth grown fresh,
  entered at a corner, with the still centre of the self at its heart. Only what's near is clear;
  what's been seen stays dimly remembered, and holding still lets them see further. Each new place
  reached gathers a little mana, and the centre a great deal, the sooner the more. Pain they still
  carry narrows the sight, makes the steps heavy and scatters the thoughts, throwing them back
  along their path, so a healer who couldn't mend themselves fully struggles to recover.
- *Binding.* Anyone can bandage, the healer best: each turn of cloth is pulled tight by hand
  and let go in the band. Too slack slips; too tight bites. It stops bleeding without mana, so a
  healer can bind what they've drawn while their mana comes back. Bandages are woven from fiber
  or torn from cloth.
- *The shrine.* The healer works from a shrine: pallets where the wounded come to lie (the
  front of its line), a meditation stone that steadies the breath against pain and gathers more
  mana, and a chest of bandages and other supplies for when the mana runs dry. Later, magical
  supplies can live there too.
- Later:
  - **Herbs and poultices**, from the Apothecary (M5).
  - **Reviving** the dead, somehow.
- Betrayal later (M4): drawing only part of a wound, or quietly pushing one back.

**Tinker.** Locks and clockwork, the last of the starting trades, split off the Carpenter (who's
benched until building and fletching make a trade of their own). Works from the tinker's shop on
Millbrook, south of the square, with a bench, a stock chest and the collector's crate
(`Tinkering/`, `data/defs/tinkering.json`).
- *Strongboxes (since M2).* A party that clears a quest brings back a strongbox holding part of
  the reward, and asks at the tinker's to have it opened. The harder the place, the more pins and
  the likelier a trap (a poisoned needle, a spring blade, burning powder).
  - *Feeling for the trap.* Run a probe along the lid's seam, light. The feel twitches as it goes
    and catches hard near a trigger. Dragging weight across a trigger sets it off; stopped over
    one, press in and hold the pressure in the band to ease the spring out, too hard and it fires.
    Reaching the hinge ends the search, so finishing never tells you whether there was one.
  - *Picking the lock.* Lift each pin until it gives most and set it there. A slip drops a pin,
    wears the picks and can jar an armed trap; enough slips snap the pick. Opening a box with its
    trap still armed sets it off. A sprung trap wounds the hand, which is work for the healer.
- *Locks and keys.* A lock is pinned at the bench from an iron bar: each pin is filed down to its
  mark (hold F; the longer the stroke, the harder the file bites) and set with D. Pins set
  crisply make a tight lock, hard to pick (a pick has less room to set a pin), and a key that
  turns sweetly; filed past the mark, it's loose, and a poor key sticks. The lock is fitted to any
  door, which then locks and unlocks for its keys alone (or a good pick). Keys are copied at the
  bench from an iron bar. AI villagers never lock a door; the Innkeeper is the obvious customer
  (room doors), through favours.
- *Contraptions for the road.* A tapping rod (fewer wounds from traps), a grapnel and winch (a
  fatal fall becomes a bad wound, once), a clockwork lantern (stronger in dark places), a spring
  snare (stronger against beasts), a signal rocket (when a quest goes wrong, some who'd have
  died are found). Made by fitting parts: hold F to file a part while it binds, D to seat it at
  the fit; then tap F to wind the spring and latch it with D while the pawl catches. Loose parts
  cost quality; a part forced home while it still binds leaves a hidden *binding*, and a binding
  contraption may jam on the road and do nothing. They go on show and sell like the smith's gear;
  adventurers carry two, not two alike, and judge them only by what shows.
- *Wagons.* Some parties ride in with a wagon, often the worse for the road: a buckled wheel or a
  cracked axle. It's parked outside near their haunt, and its owner asks for it mended. Truing a
  wheel: it turns past a gauge; F tightens the spoke at the gauge where the rim bulges out, A
  slackens it where it dips in. A new axle is fitted like a contraption's parts, from two wood. A
  party that sets out with its wagon still broken brings nothing home: no strongbox, no curios.
- *Curios and the collector.* A cleared quest sometimes sends a curio home (an idol, a clockwork
  bird, an old seal), on a sound wagon. What it's really worth is hidden, and some are fakes. The
  party asks what it's worth. Under the loupe, A/D move the glass along it and the eye catches
  near a tell; held still over one, it's read. Each tell read narrows the reckoning, and on a fake
  one of them gives it away. The tinker says what they'd put it at (aloud: anyone by hears) and
  hands it back; the owner believes them. Or offers to buy it: the owner takes a price near what
  they believe. Curios in the collector's crate go off with the morning carrier, and the silver
  comes back the morning after: what it was really worth, less a cut, nearly nothing for a fake.
- Later: bridges, gates and the town's structures damaged in the M3 crises (the Director), mended
  by the Tinker; town devices (alarms, a bell trip) when there are thieves to catch (M4).
- Betrayal later (M4): a spare key, a box opened early and some silver missing, a trap left armed,
  a contraption that jams, a curio talked down to nothing and sold on.

## Pressure and traitors

A `Director` grown from `Storyteller` always runs: raids, fire, shortages, plague, and a late-round
dragon that burns the town. Traitors are a round option layered on top:

- **Honest:** none, and stronger crises.
- **Whispers:** one or two.
- **Unknown:** nobody is told whether there are any.

A traitor serves the Thing Below. They get secret objectives and buy tools from a hidden shop (the
Whisper) with favour earned by completing them. Everything leaves evidence: maker's marks, residue,
wounds, witnesses. A limited town bell calls a meeting to accuse, vote and exile.

**Death.** It sticks. A player whose character dies is a ghost for the rest of the round unless
someone brings them back; they can come back in the next round as a new character. Taking over a
villager mid-round is for late joiners only.
- *Dying.* The downed bleed out unless someone binds them or carries them to bed, and can still
  speak, weakly, to call for help.
- *Ghosts* see only what's around them (no allegiances, no hidden properties), through walls and
  in the dark, and talk among themselves where the living can't hear. A ghost knows where its body
  lies and who's at it.
- *Haunting.* A ghost can spend its presence on small signs: a sudden chill, a light that gutters
  (and a weak one goes out), a knock. The living perceive them but not who made them.
- *Unfinished business.* Small errands for the dead (sit with the grieving, watch over a sleeper,
  show a lost party the way), each worth a little Renown.
- *Coming back.* The empath's rite, "Calling back", woven at a fresh body: most of their mana, and
  the grave's chill taken on. A body keeps longer on a shrine pallet. The ghost must come and
  answer. The revived come back downed and weak, once a round. Later, a traitor necromancer
  (M4), and the Undertaker (M5) for the bodies nobody brings back.

## Technical decisions

- **Lockstep, for friends.** Narsil's netcode gives every peer the whole game state, including who
  the traitor is. The UI hides it, but a modified client could read it. That's acceptable among
  people who trust each other, and it is why playing with strangers is its own milestone (M7).
- **One knowledge layer (`Perception`).** The game may show other characters, allegiances and
  hidden item properties only through it, as Lifeline does with `Assessment`. If the game goes
  public, this layer becomes the server's replication filter.
- **Real-time violence, seen coming.** DragonWorld's turn-based fights freeze the whole map, so one
  stabbing would stop every player's game, and even turns for the fighters alone would leave the
  day's clock running for minutes. Fights are real time but readable: you move freely and aim
  with the mouse; each blow is wound up where it's seen coming and lands on whoever's in its sweep,
  no roll to hit, met with a guard, a parry or a dash (`Combat/Blows`, `Combat/Reach`); only so
  many press one person at a time (`Combat/Engagement`).
- **Speech is sim state.** `SayCommand` goes through the sim rather than Narsil's global chat, so
  hearing can depend on room and distance, with whispers, shouts, eavesdropping and disguised
  speakers. Text only for now.
- **Mini-games run ahead locally.** A timing game can't wait on a network round trip. The client
  runs the same deterministic `WorkSession` for instant feedback and sends batches of timestamped
  inputs, which the sim replays, like `AvatarPredictor` does for walking. Interruptions still come
  from the sim.
- **Bots.** A `BotPlayer` plays through commands, for scenario tests and solo playtests.
- **Hand-made villages.** A round is played on an authored map (`MapDef`, starting with
  Millbrook), not a generated valley. The town's layout is part of the social game: who can see the
  forge from the inn, which lane the parties come in by, and where a body would go unnoticed. A
  map is data with exact building anchors. The seed still varies what's underground, what grows,
  and who lives there.

## Art direction

- **The brand** (logo, palette, type, voice, word bank): [art/brand/brand-guide.html](../art/brand/brand-guide.html).
  The art's own rules and pipelines: [ART.md](ART.md).
- **Where it's going:** the world's art came over from DragonWorld, which is Norse-inspired; the
  hand-made town added more traditional half-timbered houses, thatch and a stone bridge. The aim
  is a more Tolkien-inspired look in general, keeping the Norse pieces where they fit.
- **Fantasy folk** are our own takes, not anyone else's. Elves came first: long-lived and a little
  fey, out of step with the village's hurry, with tall swept-back ears; for now a look only, with
  no rules of their own. Dwarves are short and broad, bearded (the women too, some of them) and
  ruddy; halflings are small, slight and quick, in warm browns. Each has a build of its own, not a
  shrunk human. All three are looks for now.

## Sim folders

Built, all in `src/HeroesNotIncluded.Sim`, driven only by commands and headless-testable:
`Round/`, `Perception/`, `Speech/`, `Work/`, `Adventurers/`, `Shops/`, `Favours/`, `Healing/`, `Expeditions/`, `Hospitality/`,
`Tinkering/`, `Director/` (Renown and Dread so far), `Afterlife/` (death, ghosts, the Calling back).
Still to come: `Secrets/`, `Reagents/`.

## Reuse

Forked from DragonWorld at `cf61dfc` and left to diverge. What it brought, and what comes from
Lifeline when its milestone starts:

| Need | Source |
| --- | --- |
| Lockstep, relay, reconnect, host migration | Narsil `Narsil.Net`, `Net/CoopSession`, `Net/CoopRelay` |
| Players becoming AI when they leave, permissions | `Systems/Players.cs`, `Core/CommandRules.cs` |
| Avatar control, walk prediction | `Systems/AvatarSystem.cs`, `Net/AvatarPredictor.cs` |
| Crafting, forge heat, tools, quality | `Systems/Crafting.cs`, `Ai/CraftJobs.cs`, `Toolbelt`, `Mining` |
| Injuries, downed, rescue, jail | `HealthSystem`, `Rescues`, `Prisoners`, `Locked` doors |
| Mood, gossip, traits | `MoodSystem`, `SocialSystem`, `TraitRules` |
| Crises | `Storyteller`, `WildEvents`, `Raids`, `FireSystem`, `FoodPoisoning` |
| Look, creator, rendering | `Ui/CharacterCreator`, `HumanLook`, `WorldRenderer`, `WorldMesher`, chibi tools |
| Mini-games | Lifeline `Work/` (reworked to run ahead; the empath's touch replaced `Assessment`-style diagnosis) |
