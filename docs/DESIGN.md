# Heroes Not Included: design

Living document: the decisions so far and the intended depth of each system.
What's built and what's next is in [ROADMAP.md](ROADMAP.md).

## Pitch

A multiplayer fantasy village where the players are the people a tabletop adventuring party usually
walks past. One player runs the inn, another the forge, another the empath's shrine. Heroes arrive wanting
food, repairs, healing and work, and treat everyone like scenery. The village has to keep them
alive and paying long enough to survive the season, and someone in it may be working against
everyone else.

The inspiration is the social, systemic play of Space Station 13 and Among Us. The setting, names
and art are our own: generic fantasy, with no trademarked tabletop terms or monsters.

Tone: warm, comic and a little put-upon, until someone finds the blacksmith in the river.

## Pillars

1. **Your trade is your game.** Each one has its own interface, knowledge and way to be good at it.
   A bartender and an empath shouldn't feel like the same game with different labels.
2. **Nobody can do it alone.** Trades depend on each other's output, so people have to talk.
3. **Properties, not scripts.** Poison is a substance on a thing, a lock works for whoever holds
   the key, and fire burns what burns. A plan nobody anticipated should simply work.
4. **You know only what your character knows.** Sight, hearing and rumour are the information game.

**The theme under them:** working people, ordinary people, facing danger together. Two systems
play to it directly.
- **Behind every hero** (`Expeditions/QuestTales`). The heroes take the glory, but the town keeps
  its own count. Each quest a party goes on records whose work went with them: the smith's blade
  (and who struck for it), the cook's rations, the empath's charms, the tinker's grapnel, the empath
  who patched them up, and the broker who sent them. The reveal tells it all, the hidden truths
  too (a warped quiver, a contraption that bound). Before the reveal only what everyone could know
  is told: at the festival a party back in triumph toasts someone whose work saw them through, and
  "the hands behind the heroes" is called up.
- **Two pairs of hands** (`Work/Pairs`). Some work is too much for one person. Help never makes
  ordinary work easier: it opens up work one pair of hands can't do, the helper plays a game of
  their own with its own way to fail, and bad help is worse than none. The lead calls and the
  helper answers, so they have to keep time and talk. Anyone can lend a hand: another trade, the
  labourer, or an AI villager who heard the call and thinks well enough of you. The helper is named
  on the work and learns a little of the trade.
  - *Built: the striker.* A great blade (`twoHanded` recipes: the greatsword) is drawn out by a smith
    and a striker. The smith's hammer only taps where the sledge must fall; the striker heaves it
    up, holds it, and brings it down on the tap. Late, the call's gone; with no call, the sledge
    comes down off the beat and may crack the piece. Alone, a great piece barely moves.
  - *Built: the keeper* (`Afterlife/HoldSession`). The Calling back is a rite for two: the empath
    begins it at the body, but no rune takes till someone kneels and holds them. Each rune keyed is a
    pull of the grave that swells and peaks; the keeper braces (holds F) through the peak. The grip
    tires while it's held, so bracing early leaves nothing for the peak; a slipped hold costs that
    rune its share of the weave, and a calling held badly enough fails, the mana spent. An AI empath
    asks the nearest willing villager to hold the body (`KeepVigilJob`). The reveal says who called
    them back and who held them.
  - *Built: the pitman* (`Carpentry/PitSession`). A great log (`SawGreatLog`, ten logs to twenty
    planks) is hewn as any, then sawn with a pitman below: each downstroke the top sawyer starts is
    the call, and it's the pitman's pull (hold F) that cuts. Pull with no stroke, or hold on after
    it, and you're fighting the saw: it binds, knocking the blade off its line and the cut back.
  - *Built: the lifter* (`Carpentry/LiftSession`). A party wagon's new axle (the carpenter's
    joinery): the hub's driven on with the wagon lifted. The lifter holds it up (F) and leans
    against its sway (A/D); each blow on the hub is the call, answered at once with how level it's
    held. Level, the blow goes home; dropped or tipped, the hub's jolted back. The arms tire, and
    give if it's held too long. Alone, with its weight on the ground, a blow barely moves the hub.

## A round

- 30 to 40 minutes at fixed speed: 2 to 2.5 in-game days at 16 real minutes a day (40 s an hour).
  Players get no speed controls. Alone, the game menu and the pause key (P, Pause) hold time; in
  co-op the town never waits for one player, but holds once everyone agrees to a pause (someone
  needs a break), and runs again once everyone's ready. When there's nothing left to do (everyone asleep, say, or the
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
their shop, which anyone can read. An adventurer who wants something (a drink, a mend, an empath, a
room, a quest lead) weighs the price against what it's worth to them and what's in their purse. If
it fits, they go to the shop and line up at its counter: the bar for a drink, the smithy's counter
for a mend. If it doesn't, they say so aloud and go without. Each want is an `Order` in code. Price
is a lever: too high and nobody comes and Satisfaction falls; too low and the purse fills slowly.
Players ask each other for things as **favours**, for silver they name, pinned to the notice board
in the square: only those who read it know what's wanted. An errand (bring me so many of a thing)
is done by bringing it, and the town's labourer takes those on.

**Well rested, well fed** (`Systems/WellBeing`). Eating and sleeping are bonuses, never chores:
there's no meter to keep filled and going without costs nothing. Two hours' sleep leaves you
*well rested* for eight hours after you wake. A meal leaves you *fed* for four, more the better the
dish: bread or berries are a little, a stew more, a roast or pie more again, scaled by the cook's
grade, and the house dish more still (fed, well fed, hearty, splendid). Only a better meal tops a
fed character up, so nobody eats for nothing. Both make you a little quicker on your feet and at
your work, and lift your mood, and a party that leaves town fed and rested fights a little better
on the quest. That's how the cook's craft shows in everyone's day. Food poisoning takes the good
of a meal with it. Underneath, `Needs` still runs as the AI's appetite and drowsiness, which sends
villagers to supper and adventurers to the hatch, but nobody sees it and it never harms anyone.

## Trades

Called `Trade` in code, because `Ai/Job` is already the AI coroutine.

| Trade | The game it plays | Built from | Betrayal it enables |
| --- | --- | --- | --- |
| Innkeeper | Order juggling at the bar, drink mixing, letting rooms, keeping the common room, hearing gossip; matching parties to quests on partial intel | Social, `Expeditions/` | Spiked drinks, selling secrets, sending a party to its death |
| Cook | The inn's kitchen: the stew pot by taste and flavour, sniffing the larder, the bowls, rations for the road | Larder, cooking, `Expeditions/` | A turned ingredient left in the pot, rations that turn on the road, or something worse |
| Blacksmith | Heat, hammer and quench timing | Forge, bellows, tiers, `Crafted` quality | A hidden flaw in a blade |
| Empath | Take a patient's wounds into yourself with focus, cast the right mending on yourself, meditate back your mana | Health, `Work/`, new mana | Taking only part of a wound, or handing one back |
| Constable | Patrol, evidence, arrests, jail | `Prisoners`, locks | Framing, abuse of authority |
| Apothecary | Discover reagent reactions, reshuffled each round | New `Reagents` | Poisons, explosives |
| Merchant | Haggling, spotting cursed loot | Inventory, `HoardValue` | Fencing, forged goods |
| Undertaker | Autopsy, burial before the dead rise | Health, `Scars`, carcasses, the pyre | Destroying evidence |
| Forager, Miner | Risky trips beyond the walls | Harvest, Mining, Hunting, Fishing | Alone out there, in both directions |
| Tinker | Pick the locks and disarm the traps on chests adventurers bring back; pin locks and cut keys; fit contraptions together for the road; mend wagons; appraise curios and buy them for a collector; set trip-bells across the roads before a raid | `Tinkering/`, doors, `Work/`, expeditions, loot | A spare key, a chest opened early, a trap left armed, a contraption that jams, a curio talked down, a trip-bell that never rings |
| Carpenter | Sawing planks, fletching quivers for the parties' archers, bracing the bridge, putting up the festival; later mending furniture and building | Construction, `Carpentry/`, the woodshop | A warped shaft in a quiver that looks true |
| Bard | Rhythm performance, morale, rumours. | Mood, Social | Spreading lies |
| Mayor | Treasury, laws, chairs town meetings | New | Corruption |
| Animal Handler / Druid | taming wild animals, ancient forest rituals | new | forest spirts, "wild" animal attacks
| Farmer | grows crops, tends livestock, processes results | farm plots exist on map but need a barn or farmouse | rot, not sure |

### Trade notes

Ideas for depth, to build when each trade's milestone comes round.

**Bard**
- musical performance obviously - allow some instrutment customization if possible
- stretch goal would be full ocarina of time remake style playing where a real life musician could actually recreate songs in game
- insstrument care should be part of the trade, and instrument crafting.
- perhaps poetry and writing as well somehow? tough to make that into a mechanic though, but something symbolic perhaps.
- dealing with writers block maybe

**Animal Handler / Druid**
- gets a druid circle out in the forest as their work area
- can talk to wild animals and tame them
- maybe some limited shapeshifting with effort or some side effects
- perhaps they could provide an alternate route for dealing with the dragon

**Hunter**
- can collect like lizards and small animals that can be kept as pets and sold / given to other players as well
- skill shot based hunting - aim for the correct part for a sure kill, otherwise you'll have to chase
- skinning and prepping - don't make it too gruesome or anything but hunter should have to prep their kill.
- carrying it back should be cumbersome and perhaps a mini game while carrying it
- butchering and then restocking the larder for payment
- uses for the rest of the animal besides just the meat

**Foraging**
- identifying plants in the wild
- collecting useful herbs for cooking and medicine
- studying poisonous plants, mild and dangerous
- collecting seeds to grow your own herb garden
- packaging herbs and berries to sell

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
- *Rooms.* Let by the night: the guest pays each evening, keeps the same room while they're in
  town, and checks out when they move on or let a night go unpaid (`Hospitality/InnRooms`).
  Each guest turns in at an hour of their own, 18:00 to 21:00, so a party doesn't rush the board.
  - **Travellers:** lone passers-through (a pedlar, a pilgrim, a drover, a minstrel) come of an
    afternoon, about one a day, alongside whatever party's in town. They eat, drink, take a room
    for a night or two and move on the morning after; they never quest, keep a room's price back
    till they have one, and their word of the town counts half. The rooms aren't all or nothing.
  - **Keys and the book:** each room's door is locked; its key hangs on the board behind the bar,
    with a spare in the storeroom. The keeper writes the guest into the book on the board (a
    stroke on each beat), takes a room's key down and hands it over: whatever key they're given is
    the room they go to. The book holds what was written, not who's where. The wrong key sends the
    guest in on whoever has the room; both are put out, and they come back down for another.
  - **Checking out:** the key's left on the counter, or in the room, or now and then walks off with
    them, and the spare goes up on the board (or to the tinker to be copied).
  - **Turning a room round** (`Hospitality/RoomChores`): the bed stripped and made with clean linen
    (the sheets washed at the tub with the crockery), the chamber pot emptied into a bucket and
    tipped at the midden behind the inn, the jug filled from a bucket of water. A guest given a
    room that isn't ready says so. Now and then they leave something, on the floor or in the
    sheets: handed back, they're pleased; the AI keeper boxes it as lost property.
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
  - **Rooms:** a chest per room for a guest's things while they're out.
  - **A notice board:** at the inn, for shop signs, favours and quests posted where anyone can
    read them.
  - **Supplies:** bought from the Forager, the Miner's hunting trips and the farms by favour,
    so an empty larder is everyone's problem.
- Betrayal later (M4/M5), the keeper's (the cook's are under **Cook.**):
  - a drink spiked or watered, or a short measure;
  - a guest's room rifled while they're out;
  - what was overheard sold to the highest bidder;
  - directions to the wrong place;
  - a party sent to its death;
  - a floor scrubbed clean of evidence.

**Cook.** The inn's kitchen, behind the common room: the pot by its fire, the larder, the prep
table, the wash tub and the serving hatch. The cook's game is taste: what goes in, how it's kept,
and whether it's turned, none of which the diner sees until they eat. What's built comes first;
the rest follows in the order it's planned (docs/ROADMAP.md, "The cook pass").
- *Built: the pot.* One stew pot by the kitchen's fire, cooked by taste (`Work/Cooking`).
  - **The heat:** keep it at a simmer, read only from the surface (still, a simmer, a rolling
    boil), as the forge is read from its colour. Stir so it doesn't catch.
  - **The seasoning:** season a pinch at a time, tasting as you go. It can't be taken back out.
  - **The result:** a pot gives many bowls, ladled out, each with a quality the diner tastes. A
    spoiled ingredient spoils the pot, and nobody can taste it until the diner is sick.
- *Built: the hatch.* Meals are ordered at a serving hatch in the kitchen wall, not the bar
  (`Shops/Hatch`). Diners line up at it from the common room and say what they want through it.
  The cook puts bowls up on its counter, no more than are waiting for one, and whoever's at the
  front takes one, pays the cook and goes to sit with it. First come, first served: a bowl up on
  the hatch is anyone's at the front, and nobody else can take it.
- *Built: flavours* (`Work/Flavour`). Ingredients taste of something: savoury, sweet, hot or
  fresh. A pot tastes of the blend of what went in, by how much (food by its nutrition), as a
  drink is the sum of its adds. Emberroot (hot), herbs (fresh) and honey (sweet) are seasoners:
  three at most to a pot, they flavour what's there and add no bowls. A rounded pot (two or three
  flavours, none swamping it) is better than a pot of one thing. Anyone looking in, or eating, can
  tell what it tastes of: "savoury, with some heat".
- *Built: diners' tastes* (`Hospitality/Tastes`). Each kind of adventurer likes the taste of
  something. Ordering a meal, about half say what they fancy, in the same breath, so whoever hears
  the order hears the craving. A bowl that tastes of it pleases them; one that doesn't, a little
  less. So what goes in the pot is a guess at the room.
- *Built: sniffing* (`Work/SniffSession`). The cook's way into hidden rot, as feeling a wound over
  is the empath's: smell a stack (in the larder, in hand) and it's fresh, fine, on the turn or off,
  roughly, rougher the less practised the nose. A pot only ever smells good or "not quite right":
  what turned in it can't be picked out, so the time to smell is before it goes in.
- *Built: rations for the road* (`Work/Rations`, `Work/PackSession`). The cook's work that leaves
  town, as charms and contraptions do.
  - **Packing:** at the kitchen's prep table, three portions of food (raw or cooked). Fold the
    cloth's four corners on cue, each from its own side (A, D, A, D), then hold F to draw the cord
    and let go to tie it. The cord reads only as slack, snug or biting, and snug isn't all one
    thing: tied at its slack end it looks fine and lets the air in, turning twice as fast on the
    road. That never shows.
  - **Selling:** set down in the inn and put up for sale like any gear. An adventurer carries one.
  - **On the road:** eaten halfway out. Sound, the traveller is a little stronger for the quest and
    comes home less hungry (the AI's appetite); turned (it left half gone, or tied slack on a long road), they're
    weaker for it and come home sick. The party's tale says which: the cook's only word of it.
- *Built: a name for the kitchen.*
  - **The house dish** (`Work/HouseDish`): a cooked pot good enough to put a name to (Fine or
    better) can be named by the cook from the pot. The name and how it tastes go on the sign. The
    recipe is the flavour: any later pot that tastes near enough the same is the house dish again,
    so it has to be hit from the larder each time. About a third of diners ask for it by name, and
    know it when they get it (or don't).
  - **Diners say what was wrong:** a bowl remembers what most let its pot down (salty, bland,
    caught on the bottom, underdone, all one note), and a diner served a poor one says so. The cook
    learns from the room as well as the spoon.
  - **Tips:** a drink or meal served Fine or better, and quickly (within a third of their
    patience), earns its server a coin over the price; masterwork, two. The keeper's too.
- *Built: prep* (`Work/ChopSession`). Raw meat, fish or cabbage in hand at the prep table, cut on
  the knife's beat (F as it comes down), done ahead in a quiet hour. Chopped food is its own item:
  it goes a little further, turns a little sooner, and a pot of it cooks up to half again as fast.
  A cut off the beat does nothing; one wildly off may slip, and a slip is a real cut on the cook's
  arm, work for the empath.
- *Built: the spit* (`Work/Roasting`). Beside the kitchen fire. Raw meat goes on (up to four), F
  turns the next side to the fire, Space lifts it off. Each side's crust shows as colour (pale,
  colouring, golden, dark, charred); how far it's cooked through doesn't. Fat drips and the fire
  flares now and then, browning the side over it twice as fast: turn it away. Golden all round and
  cooked through is a fine roast; a charred side tastes burnt, a quick one comes off pink. Left on
  the spit, the side to the fire chars. It comes off as slices, no bowl needed.
- *Built: the menu* (`Shops/Menu`). Dishes beyond the plain meal go on the sign with their own
  price, once the cook ticks them on (they start off, so a kitchen nobody roasts in is never asked
  for a roast). A plain meal order takes whatever's up on the hatch at the meal's price; about a
  third of diners ask for a dish on the menu by name instead, at its price, and only it will do.
- *Built: the oven* (`Work/Baking`). A domed bread oven with its own small fire in the kitchen's
  corner, fed with wood like any hearth. Two flour at the prep table are kneaded on the beat, from
  one side then the other (A, D, ...): smooth and springy, or lumpy. The dough proves by itself,
  read by eye: slack, rising, well risen (an hour or two), puffed up tall, sunk back on itself. Into
  the oven, it's baked by smell alone, the door shut: raw dough, warm and yeasty, bread baking, a
  golden crust, toasty, burning. Take it out on the golden crust; left in, it burns. Two loaves a
  ball. Bread is on the menu, and it keeps: rations with a loaf in them turn half as fast.
- *Built: the rest of the menu.*
  - **Porridge** (`Work/Cooking`): oats in the stew pot; a pot that's mostly grain serves porridge,
    not stew. On the menu as the breakfast dish: asked for only before noon, and first at
    breakfast. The AI cook puts oats on of an early morning.
  - **Pies** (`Work/Pies`): a ball of dough and two portions of raw food at the prep table, the rim
    crimped round on the marks (`CrimpSession`). Baked in the oven by smell, as bread is, into three
    slices tasting of the filling. A rim crimped loose lets the filling run out (a leaked pie).
  - **The butcher block** (`Work/Butchery`): the carter brings pork by the side and birds in their
    feathers, no use to anyone till they're dressed. A side is jointed with the cleaver as it
    sweeps over the joints (`ButcherSession`): off a joint it hacks through bone and there's waste.
    A bird is plucked with short firm pulls (`PluckSession`): hang on and the skin tears, at a
    point a little different every bird and never shown. Dressed fowl roasts on the spit. An AI
    cook dresses what's whole in the larder without the games.
  - The fire's old recipes (simple meals, stew over the hearth, its cooking pot) are gone: the
    kitchen cooks.
- *Built: the rushes, and the town's hours* (from the first playtest). Adventurers want a meal at
  breakfast (7 to 9:30) and supper (6 to 9) whenever they're at all peckish, not only when
  starving, and come into town hungry from the road. They keep the town's hours: nobody rides in
  or comes back from a quest in the dark, nothing's asked between 11 and 6 but a room or a bad
  hurt seen to, they turn in at night unless well rested, and the small hours don't count against
  anyone's patience. A meal only half wanted that never comes is shrugged off. Customers idling
  about the inn keep out of the kitchen (`staffOnly` furniture marks a room so).
- *Built: pressure in the kitchen.*
  - **Two pots:** a second over the bread oven's fire.
  - **The fire fed by hand:** only the kitchen's cook feeds its fires, and what's kept in the
    kitchen (the larder, the log basket) is theirs alone. A pot's fire set roaring burns wood half
    again as fast. The carter brings the wood to a log basket by the door.
  - **A grease fire:** a fatty pot (mostly meat) left at a rolling boil spits into the fire and
    flares. Clap the lid on within five seconds (Space in the stew, F at the pot), or it's a real
    fire, through the fire the town already fights.
  - **Rats you chase** (`Hospitality/Rats`): the shortage crisis looses four into the larder (a
    quarter of it spoiled already, not half). They keep about it, dart off from anyone close, and
    each gnaws a portion every hour and a half while it lives. The cook goes after them with the
    kitchen knife, through free movement and blows; an AI cook does too. When the hard times pass,
    the rest scurry off.
- *Built: the kitchen and the town.*
  - **Feasts** (`Hospitality/Feasts`): a party back from a quest cleared with nobody lost calls for
    one as they come into the inn: a dish each at the hatch (a different one off the menu where
    there's a choice), paid over the odds, against a two-hour clock. Laid on, the room's merry, the
    party's pleased and Renown rises; left bare, they grumble.
  - **Broths** (`Work/Broths`): a bowl that tastes fresh enough (herbs, greens) is a broth, and the
    pot says so. Eaten, wounds close half again as fast for half a day, and an empath gets a little
    mana back.
  - **Keeping the larder** (`Work/Larder`): the larder keeps cool, so what's left out in a warm
    kitchen turns first. Salt at the prep table, a scoop to four portions: salt meat and fish keep
    for weeks and season a pot by themselves. The smoking rack by the oven cures meat and fish in
    six hours of smoke.
  - **Word of the food** (`ShopSystem.Fare`): fine meals and the house dish get the table talked
    of, and the sign says how much. Parties come up to a third sooner for it; the talk fades with
    each new party, so it has to be kept up.
- *Later:* teas from the herbs the empath uses, and the empath sending the hurt to eat; pickling.
- Betrayal later (M4/M5):
  - one bowl poisoned for one diner (the hatch is first come, so a target has to be handed it);
  - a party's rations drugged, or left to turn (the rot already rides out with them);
  - a turned ingredient left in the pot, knowing.

**Empath.** Called the healer once; renamed for what sets it apart, that it feels what it takes on. Magical in flavour rather than clinical, but grounded in the health
model: wounds sit on body parts, have a kind, and bleed, hurt and fester.
- *Laying on hands.* One sitting, from the first touch to the last wound drawn. Touch is the
  diagnosis: the empath runs their hands from head to feet, and near a wound there's a warmth.
  Stopping on it and pressing reads it: its kind comes through the hand the way it'll pull when
  drawn, and how bad it is. A dressed wound takes a longer press to find festering under it.
  Pressing while the hands move is rough, and the patient flinches. Keep pressing on a wound read
  and it's drawn across, the empath holding their composure against its pain; lose it and the link
  snaps, with a backlash. Each kind pulls on the composure its own way: a cut jolts, a bite tears
  twice, a bruise swells and ebbs, a burn pulls steadily one way, frost numbs the hands so they
  answer late, venom grows worse the more comes across. Composure and strain carry from one wound
  to the next. A patient lying on a pallet is calmer to draw from. The wound lands on the same part
  of the empath, lighter the more skilled they are. An infection comes with a fully drawn wound.
- *Carrying it.* What the empath takes on is real. It bleeds and hurts, and an empath who takes
  too much collapses.
- *Weaving.* Mendings are woven from six runes (Thread, Flesh, Ember, Rime, Salt, Still), keyed
  1 to 6 on the beat of the empath's own heart, then let go. The mix decides the mending, in any
  order: Thread and Flesh knit a cut; Still and Flesh ease a bruise. Extra copies of a mending's
  lead rune make it stronger, for more mana. A mix that makes nothing fizzles for a little mana.
  Absorbed wounds read as sensations, not labels (a clean bright line of pain, a deep swelling
  throb), so telling which mending a wound wants is a skill the player learns. Pain makes the
  heart race and stumble, and the weave with it.
- *The book.* Every empath starts with the common mendings written in their book: each one's
  runes, what it does, and the feelings it answers. Others are found by trying new mixes, and
  written in when they work: searing a wound shut, steadying your own hands, grounding lightning.
- *Charms.* Before anyone's hurt, the empath weaves ahead: a mending woven into a strip of linen
  keeps. Torn, it lands on whoever tears it, gift or no. Put on the shrine's wares shelf, priced,
  charms sell to parties as wards for the road, a couple each and no two alike. On a quest a charm
  spends itself on the first wound it fits, and a healing one may turn a death into a bad wound.
  Anyone can see which mending a charm holds; only its maker knows how well it was woven. Which
  to weave is a guess at what the parties will face.
- *Meditating.* Out of mana, they find their way inward: each sitting is a labyrinth grown fresh,
  entered at a corner, with the still centre of the self at its heart. Only what's near is clear;
  what's been seen stays dimly remembered, and holding still lets them see further. Each new place
  reached gathers a little mana, and the centre a great deal, the sooner the more. Pain they still
  carry narrows the sight, makes the steps heavy and scatters the thoughts, throwing them back
  along their path, so an empath who couldn't mend themselves fully struggles to recover.
- *Binding.* Anyone can bandage, the empath best: each turn of cloth is pulled tight by hand
  and let go in the band. Too slack slips; too tight bites. It stops bleeding without mana, so a
  empath can bind what they've drawn while their mana comes back. Bandages are woven from fiber
  or torn from cloth.
- *The shrine.* The empath works from a shrine: pallets where the wounded come to lie (the
  front of its line), a meditation stone that steadies the breath against pain and gathers more
  mana, and a chest of bandages and other supplies for when the mana runs dry. Later, magical
  supplies can live there too.
- Later:
  - **Herbs and poultices**, from the Apothecary (M5).
  - **Reviving** the dead, somehow.
- Betrayal later (M4): drawing only part of a wound, or quietly pushing one back.

**Tinker.** Locks and clockwork, split off the Carpenter (who came back as a trade of its own
with fletching; see **Carpenter.**). Works from the tinker's shop on
Millbrook, south of the square, with a bench, a stock chest and the collector's crate
(`Tinkering/`, `data/defs/tinkering.json`).
- *Strongboxes (since M2).* A party that clears a quest brings back a strongbox holding part of
  the reward, and asks at the tinker's to have it opened. The harder the place, the more pins and
  the likelier a trap (a poisoned needle, a spring blade, burning powder).
  - *Feeling for the trap.* Run a probe along the lid's seam, lightly to feel for explosive traps. 
    The feel twitches as it goes
    and catches hard near a trigger. Dragging weight across a trigger sets it off. Stopped over
    one, hold still for a read of the spot (the surer the read, the wider the green), press in, and
    flick the spring out while the pressure's in the green; flicked outside it, or pressed too hard, it fires.
    Reaching the hinge ends the search, so finishing never tells you whether there was one.
    don't notice (or ignore it) and it explodes in the adventurer's hand when they open it.
  - also should have to inspect the keyhole to see if there are any poison needle traps and disarm
    them too or suffer an unfortunate injury while lockpicking.
  - *Picking the lock.* Lift each pin until it gives most and set it there. A slip drops a pin,
    wears the picks and can jar an armed trap; enough slips snap the pick. Opening a box with its
    trap still armed sets it off. A sprung trap wounds the hand, which is work for the empath.
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
- *Crossbows* (`ServiceKind.CrossbowRepair`): the tinker makes them, so the tinker mends them: the
  lock taken apart, the nut and the trigger seated again and the spring wound (`FitSession.Mending`).
- *Wagons.* Some parties ride in with a wagon, often the worse for the road: a buckled wheel, a
  cracked axle, a sprung board. It's parked outside near their haunt, and its owner asks for each part
  mended where it's done: the wheel of the tinker (`WagonRepair`), the woodwork of the carpenter
  (`WagonJoinery`, see **Carpenter**), each its own order and paid once that part's sound. Truing a
  wheel: it stands on its stand; A/D turn it to a spoke, W tightens that spoke (pulling a bulge
  in), S slackens it (letting a dip out), and F spins it past a gauge, which chalks how far the rim
  runs out or in at every spoke. It's true when a spin runs within the band. A party that sets out
  with its wagon still broken in any part brings nothing home: no strongbox, no curios.
- *Curios and the collector.* A cleared quest sometimes sends a curio home (an idol, a clockwork
  bird, an old seal), on a sound wagon. What it's really worth is hidden, and some are fakes. The
  party asks what it's worth. Under the loupe, A/D move the glass along it and the eye catches
  near a tell; held still over one, it's read. Each tell read narrows the reckoning, and on a fake
  one of them gives it away. The tinker says what they'd put it at (aloud: anyone by hears) and
  hands it back; the owner believes them. Or offers to buy it: the owner takes a price near what
  they believe. Curios in the collector's crate go off with the morning carrier, and the silver
  comes back the morning after: what it was really worth, less a cut, nearly nothing for a fake.
- *Built: the trip-bell* (`Tinkering/TripBells`), the first of the town's own contraptions: the
  tinker's part in the crises before they come. Fitted at the bench like a road contraption (and
  binding the same hidden way), then carried out and set on open ground with F: a cord pegged low
  across a way in, a spring and a bell. A raid isn't known till someone sees it coming
  (`RaidCrisis.Watch`: a waking villager near them, a sleeper only close by, or soon in any case);
  a raider crossing a set bell rings the alarm there and then, and its clangor shakes them
  (`Unnerved`, as a ghost's sign does). The shepherd names the road a raid will take, so the tinker
  has the warning's hours to string one across it. A binding bell may jam and stay silent; it's
  tripped either way, so anyone can see it went off or didn't, never why. Rewound or taken up with
  F. Never sold. The AI tinker and the bot set one on the warned road (`TripBellJob`).
- Next town contraptions, one for each crisis the tinker sits out: a **smoke bell** in a building
  (a fire there rings at once), **rat traps** set in the larder for the cook in a shortage (the
  spring snare's cousin), and something for the dragon (a horn that draws it off a roof; the great
  bow on Reedmere).
- Later: gates and the town's other structures damaged in the crises, mended by the Tinker; bells on
  doors when there are thieves to catch (M4).
- Betrayal later (M4): a spare key, a box opened early and some silver missing, a trap left armed,
  a contraption that jams, a curio talked down to nothing and sold on.

**Carpenter.** Millbrook's sixth trade, so a six-player round has a game for everyone (the
labourer is what a seventh gets). Works from the woodshop by the tinker's: a sawhorse, a
carpenter's bench and a chest the carter leaves feathers in (`Carpentry/`).
- *Making planks* (`SawSession`), as before the sawmill (riving, splitting along the grain with
  wedges, is the other old way: a later alternative, not a step of this):
  - **Hew**: the round log end on, a square chalked on it. F held raises the axe, let go strikes as
    deep as it was raised (a dashed line shows where it'll cut to). Each of the four sides hewn
    flat to the chalk; past it is waste. The log turns to the next side on its own.
  - **Pit saw**: the squared beam end on, as the top sawyer sees it, the planks' lines chalked down
    its end. W/S (or the mouse up and down) work the long saw, and only the downstroke cuts; the
    blade wanders, more rushed, and A/D (or the mouse across) steer it back. Beside it, the beam
    from above with the cut creeping along it. Six logs make up to ten planks: a log hewn past its
    lines or a wandering cut wastes some.
- *Making a quiver* (`FletchSession`): a plank and a bundle of feathers make one fine arrow, the
  pattern the score in the quiver is cut to, through four stages scored once each:
  - **Sight**: bend the shaft straight against its warp (hold F, Space when the needle reads true).
    The needle is the carpenter's eye, a little off the truth (wider with shakier hands), so a shaft
    can look truer than it is. Left past the limit, the quiver's warped: hidden item state
    (`Fletched.Warped`), never shown.
  - **Shave**: the knife goes where the mouse (or WASD) puts it, as the mop does, and shaves while
    the button (or F) is down, taking the lumps down to its edge a bite at a time. Below the shaft's
    line it gouges. Space when it's smooth enough: wood taken off counts, gouges count against.
  - **Head**: hold F to push the iron head onto the shaft's end; it tips further off line as it
    goes, and A/D (or the mouse) keep it straight. Seated, it's bound on.
  - **Fletch**: the shaft end on, still; point round it (mouse or A/D) and set three feathers,
    best a third of a turn apart.
- *Tillering a bow* (`BowSession`, four planks): the stave on the tillering stick, each limb in four
  sections, every one too stiff. A/D (or the mouse) picks a section, F scrapes it (harder the longer
  the stroke), till the bend follows an even arc, both limbs alike; Space draws it to full and it's
  done. A section scraped past even is a hinge, worse than one left stiff. A bar per section shows
  the wood still to come off down to its mark; shavings fly while the scraper bites. Then it's
  strung: F bends it against the floor, Space slips the loop into the nock, which catches only bent
  far enough; bent too far, the limbs strain. Bows sell as gear.
- *Quivers on the road* (`Fletching`): sold as goods in the woodshop, one to an archer (only those
  with a bow or crossbow buy). Shot away on the quest: a true quiver makes the archer count for
  more, a warped one for a little less. Like rations and charms. An archer with no quiver who finds
  none on the shelf asks for one aloud (`ServiceKind.Arrows`, at the woodshop): handed one, they
  pay. Each quest shoots it away, so they ask again when they're back.
- *Mending wooden gear* (`ServiceKind.WoodRepair`): whoever makes a piece mends it
  (`WeaponWear.MendKind`): forged gear and dull edges are the smith's, a bow, a cudgel or a staff
  the carpenter's (made at the bench: `MakeBow`, `MakeCudgel`, `MakeStaff`). A worn bow is
  re-tillered where it's set stiff and restrung (`BowSession.Mending`); a cudgel or a staff is
  mended the plain way (hold F) for now. The plank it takes comes from what's to hand
  (`Crafting.RepairCost`), and a poor job leaves it short of what its owner will take back.
- *Arms for the muster:* a bow (or a cudgel) of the carpenter's own, hung on the muster rack in a
  call to arms, is paid for like the smith's spears, and the town shoots with it.
- *Joinery* (`JoinSession`): chisel and mallet, a joint at a time. F held raises the mallet, let go
  it strikes as hard as it was raised. **Cut** the mortise down to its chalk mark (Space when it's
  deep enough: short of it the tenon won't seat, past it the joint's loose); then **drive** the tenon
  home, lighter as it tightens: a blow past what the joint will take (the gauge's mark) splits it.
- *Wagons' woodwork* (`ServiceKind.WagonJoinery`): a cracked axle hewn new from a log, each end's arm
  trimmed and its hub driven on; a sprung board with a plank pegged across it. Joinery, where the
  wagon stands. The wheel stays the tinker's.
- *Furniture after the fire* (`Refurnishing`): a joined piece (a stool, a table, a shelf, a chest: a
  def with `joinery`) that burns in a town building is remembered where it stood. The carpenter
  joins a new one at the bench (the Rebuild tab, planks from what's to hand), carries it over in
  their arms, and sets it there (F); the town pays by the plank. The AI carpenter (and the bot) waits
  till the fire's out and no crisis is on.
- *The town's work:* braces the bridge when a raid breaks it (with the tinker), and puts up the
  festival with the labourers.
- AI carpenter: saws when the chest's short of planks and keeps two quivers on show
  (`SawJob`, `FletchJob`), answers an archer's ask (`ArrowsJob`) and mends what's brought
  (`MendJob`, as the smith's), without the games.
- The woodshop: a plank floor, a rack of bows on the back wall, logs and planks stacked along the
  front, a barrel of arrows by the chest, shavings by the saw, torches on the walls. Logs are
  fetched from the chest by hand, as the smith fetches ore.
- Next: bar fights and raids that break furniture (only fire does, so far); a hand game for a
  cudgel or a staff (a split haft spliced and bound); the build and zone tools as the carpenter's
  own; feathers from the cook's plucking.
- Betrayal later (M4): a quiver warped on purpose, sold to the party a traitor wants to fail.

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
  speak, weakly, to call for help. A downed empath can still weave a mending on themselves (and
  meditate for the mana), with a narrower beat: bound but in pain, they'd otherwise lie there for hours.
- *Ghosts* see only what's around them (no allegiances, no hidden properties), through walls and
  in the dark, and talk among themselves where the living can't hear. A ghost knows where its body
  lies and who's at it.
- *Haunting.* A ghost can spend its presence on small signs: a sudden chill, a light that gutters
  (and a weak one goes out), a knock. The living perceive them but not who made them.
- *Unfinished business.* Small errands for the dead (sit with the grieving, watch over a sleeper,
  show a lost party the way), each worth a little Renown. A ring round whoever needs you shows how
  near is near enough; they may step out of it a few seconds and the lingering only pauses.
- *The beasts* feel the dead as people don't: a ghost can startle them (a rat at a larder leaves
  it for good) or beckon the timid to graze about it.
- *Possession.* A ghost can get into a wild beast near it, a hare to a boar (never a raid's wolves
  or bears), and run, bite and gore with it while its presence lasts, a bigger beast spending it
  faster. To the town's people it's on their side (friendly fire off, it passes through them);
  to anyone looking, its eyes are wrong.
- *Frost and dreams.* A ghost can spend all its presence to write a few words in frost, there for
  anyone to read until it melts, or whisper a few into a sleeper's dream, remembered on waking.
  Never who wrote them, and kept short: the dead hint, they don't testify.
- *Unnerving.* A chill, knock or startle near raiders shakes them: their blows come slower and
  they break sooner. The dead help defend the town without striking a blow (unless they're in a boar).
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
  and who lives there. Who works where is the buildings' town roles (`roles`: Tavern, Kitchen,
  Forge, Shrine, TinkerShop), so a map decides who shares a roof.
- **Reedmere** (`tools/lake_map.py`): a town on stilts out in a long lake, after the lake towns
  in `art/reference/` (the tipexleloup map for the plan, the others for the mood). Houses crowd on
  piles round a pool of open harbour water, under steep shingles; one long bridge (the causeway)
  runs back to the shore, which is only a headland in the corner: the road in, the woods, an old
  barn and the woodshop. At the gate, where the bridge lands and the fighting comes in, stand the
  shrine, the forge on a stone pier (kept off the timber) and the storehouse with the stockpile. The
  main street runs up from the gate across the harbour on the festival's boats to the **moot hall**
  on the far quay, the town's hall (the Mayor's, one day), with the notice board, the muster rack and
  a great bow on its stone forecourt. The inn and the cookhouse face each other across the
  water-gate, the canal out of the harbour; the tinker's is on the west quay. Everything else is
  timber (it burns, though damp planks are slow to catch); buckets are let down from dipping
  posts; nets and fish dry along the outer walks and lanterns burn on the quays all night. A raid
  that brings the causeway down cuts the whole town off from the shore, the carter with it, till
  the tinker mends it.
  The cook **supplies the inn** (in every town; here the cookhouse stands apart): the innkeeper
  sends from the bar for a hamper, from their own purse, a labourer runs it over from the cookhouse
  hatch, and it's unpacked behind the bar. Lodgers have their board off the warming shelf at
  breakfast and supper, at no charge, and a bare one makes a room worth less; the rations go on the
  merchandise shelf, for the innkeeper to sell to parties setting out. So the two trades lean on each other, and there's something for a traitor to
  spoil. A feast is called at the inn and eaten at the cookhouse.
  **The festival is on the boats**: there's no square to spare, so the town gathers round the mast
  of the boats lashed together in the harbour (the `gathering` place, not the well), the bonfire on
  a stone-ballasted barge among them, the butts shot along the raft.
- **The rare companies** (`adventurers.json`, `rare`): now and then a company comes in place of a
  party, our own homage to the stories the town's setting comes from. A dwarf heir's company bound
  for the hall their grandfathers lost to a fire-wyrm, a halfling along to keep the books; and the
  company of the long road, halflings who never meant to leave home, a grey wanderer, a ranger, an
  elf and a dwarf, carrying something to a mountain that burns. They come as their own folk and by
  their own names, as big as the town can take (four always; all of them only at twelve players),
  and they'll take only their own errand, whatever the broker says of it.

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

## Other village ideas

- an underground dwarven fortress as nod to dwarf fortress
- a mountain town
- beach or island town
- howl's moving castle inspired moving city
- cyberpunk / bladerunner setting (maybe a different game) 

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
| Mood, gossip | `MoodSystem`, `SocialSystem` |
| Crises | `Storyteller`, `WildEvents`, `Raids`, `FireSystem`, `FoodPoisoning` |
| Look, creator, rendering | `Ui/CharacterCreator`, `HumanLook`, `WorldRenderer`, `WorldMesher`, chibi tools |
| Mini-games | Lifeline `Work/` (reworked to run ahead; the empath's touch replaced `Assessment`-style diagnosis) |
