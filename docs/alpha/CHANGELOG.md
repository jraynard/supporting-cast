# Playtest changelog

What changed in each playtest build, newest first. The release workflow posts the newest
section to Discord's #builds with the build, and the packages ship this file as CHANGELOG.txt.
Write it for testers: what they'll notice, not how it's done.

## 0.1.0-playtest.9

The cook gets a real kitchen, and the inn keeps the town's hours.

**The pot**
- Ingredients taste of something now: savoury, sweet, hot or fresh, and a stew tastes of what went
  in. A pot of one thing is flat; meat with something green and a little heat is a stew.
- Emberroot, herbs and honey are seasoners: up to three in a pot, for flavour, not more bowls.
- Diners say what they fancy when they order ("Something with a bit of heat to it"), and are
  pleased when the bowl matches.
- Served a poor bowl, a diner tells you what was wrong with it: too salty, bland, caught on the
  bottom, not cooked through, or all one taste.
- Smell food before it goes in (the Smell button in the larder and your pack, or on the pot). A
  practised nose can tell fresh from turned. A pot only ever smells "not quite right".
- Name a fine pot the house dish. It goes on the sign, and any pot that tastes like it is the
  same dish. Some diners ask for it by name.

**The kitchen**
- A prep table: chop raw meat, fish or cabbage on the knife's beat (it cooks quicker and goes a
  little further; mind your fingers), and knead flour into dough.
- The spit by the fire: put meat on, turn it (F) as each side goes golden, and turn it away when
  the fat flares. Lift it off (Space) golden all round. Left alone, it chars.
- The bread oven in the corner: let your dough prove an hour or two, then bake it by smell alone.
  Out on the golden crust; left in, it burns.
- Roast and bread go on the sign at their own prices. Tick them on once you can make them, and
  some diners will ask for them by name.
- Rations for the road: three portions of food, folded and tied at the prep table, sold like
  gear. Good rations make a party stronger on its quest; turned ones make them sick, and they'll
  say so when they're back. Rations with bread in them keep longer.

**The inn's day**
- Guests come in hungry and want feeding at breakfast and supper: expect a rush at the hatch.
- Nobody rides into town or comes back from a quest after dark. Guests turn in for the night and
  ask for nothing between eleven and six but a room or a bad wound seen to, and nobody's patience
  runs out while the town's asleep.
- Customers idling about the inn keep out of the kitchen.
- Quick, fine service earns a tip, at the bar and the hatch.

**Steam Deck**
- Under Steam's gamescope the game no longer thinks it's in the background, so the pad isn't
  ignored and the frame rate isn't held down.
- An input log for chasing controller trouble: add NARSIL_INPUT_LOG=1 to Steam's launch options.

## 0.1.0-playtest.8

The festival, made a night to remember, and death that sticks.

**The festival**
- The season's last evening is a real festival now, from five in the afternoon to ten at night.
  The bell rings, and the square fills with lanterns, bunting, two long feast tables and a
  bonfire.
- The town and its guests eat at the tables (the inn serves out to the square), warm themselves
  at the bonfire and dance round it. Press F at the bonfire to join the dance.
- The show table: lay your best work on it (F with it in hand, or put it in from your pack).
  Guests still in town look it over and think better of the town for good work, and worse for
  shoddy work.
- Two games: the striker (swing the mallet as the needle tops the gauge; ring the bell) and the
  archery butts (hold the swaying aim on the gold and loose). Three goes each, and the best score
  of the night wins a purse of silver. Guests enjoy watching.
- The ceremony: the bell calls everyone to the well. Each party still in town stands and gives
  its toast, saying what it makes of the town, and that's the moment their opinion counts toward
  Renown. So keep your guests happy right to the end. Then the honours (the games' winners, the
  finest work on the show table, the busiest counter, anyone called back from the dead) and the
  verdict. The reveal lists the honours.
- Workers with orders waiting keep serving through the revels, and the night never fast-forwards
  during the festival.

**Death and ghosts**
- Death sticks in a round. Your character leaves a ghost that drifts through walls, overhears the
  living and talks with the other dead.
- The empath can call you back from your body (the "Calling back" mending). Answer it at your
  body with F.
- Meanwhile you can haunt (a chill, a guttering light, a knock) and see to unfinished business for
  a little Renown. The reveal says who died, on which day, and who came back.

**Controls**
- Every control can be rebound (Settings > Controls), keyboard and gamepad.
- A gamepad plays the whole game: the left stick walks, the right aims, the triggers strike.
- Hold RB for a quick wheel, and Start opens the menu. Menus and the title screen work from the
  pad.

**Trades and gear**
- Each trade starts with its own weapon: the cook's knife, the healer's staff, the tinker's dagger
  and the smith's hammer. The innkeeper's cudgel, Lucky Lou, lives under the bar. Reach for it
  when trouble comes and put it back after; only the innkeeper knows it's there.
- The tinker makes a crossbow at the bench, fitted by hand. Hold right click to crank it; once it's
  loaded it stays loaded until you fire.
- Feeling a strongbox for traps reads more clearly: a line saying what to do, a trace of what the
  probe has felt, and the gauge's zones named.
- Shop signs: an anvil for the smithy, a cogwheel for the tinker, a bindrune for the shrine. A
  marker floats over your home.
- Anyone but the healer gives only first aid: a bandage holds the bleeding off a while but heals
  nothing.
- Parties that hand over gear for mending wait for it before they set out on their quest.

**Fixes and smaller changes**
- The innkeeper's F at the bar no longer offers to "reach under the counter for stone bridge" once
  the cudgel's out (pressing it threw an error).
- Stairs: stepping off the top takes you to the floor above instead of dropping you back down.
- Your character turns at once when you click behind them.
- You wake to a shout nearby, smoke, raiders or the call to arms.
- The difficulty scales the Renown you need and the Dread the valley will bear.
- The town's arms go back on the muster rack after a raid. The healer's pallets are kept for the
  hurt, not for a night's sleep.
- The dragon lands somewhere new each pass, and not half inside a house.
- Raiders go after whoever just hurt them.
- A fever weakens a party on its quest. A wound that worsens must be felt again before it's drawn.
- A warning the moment the forge's heat you feel turns dangerous.
- The dead keep what they wear, and a ghost's knock creaks first.
- An inventory window crash is fixed.

## 0.1.0-playtest.7

One way to fight, everywhere.

- You always move freely now: the Free movement setting is gone, because it's simply how you move.
  Old saves load and pick it up.
- F no longer starts a fight (or a hunt). Draw your weapon with R and use the mouse: aim, left
  click to strike, right click to swing heavy, hold Q to guard, Space to dash clear. Hunting is the
  same: draw your bow, aim at the hare, click. F still butchers what you bring down.
- Raiders swing at you at their full pace now, not held back for you as before.
- Blows land where they reach, for everyone: no more dice for whether a hit connects. Townsfolk
  and raiders slip some blows (the more skilled, the more often), and armour takes its share of
  every hit that lands.
- In co-op a swing is judged against where everyone was when you pressed it, so what looked in
  reach on your screen is.
- Walking diagonally onto stairs climbs them.
- The parry window is fairer: from a fifth of a second before a blow's flash to a moment after it,
  so throwing your guard up as you see the flash counts, and the result shows at once rather than a
  beat later.
- Bows: hold right click to draw and aim (a line on the ground shows where the arrow will fly,
  turning ember once the bow's fully drawn, grey if you're too winded), then left click to loose.
  Click a moment early and it looses as soon as it's drawn. Hold on and the next arrow's drawn.
  Every arrow is seen flying now, including the ones that hit nothing.
- Your wind (stamina) comes back slower, and only once you've stopped spending it for a moment, so
  a flurry of swings, dodges and blocks can run you dry.
- Raiders with nobody left to fight give up and go, instead of standing in the square for hours
  while the hurt bleed out indoors.
- Mac: the app opens once you've cleared the quarantine flag (README-ALPHA). Its signature was
  broken in every earlier build, so a Mac copy from before this one needs a fresh download.

## 0.1.0-playtest.6

Fighting, reworked: you can see a blow coming now, and do something about it.

- Raiders take you on one at a time while the rest circle, instead of all hacking at once.
- Every blow is wound up first: a ring closes on whoever it's aimed at (amber, ember for a heavy
  one) and flashes as it lands. Hold Q (or V) to guard; raise it just as it lands to parry, which
  leaves them open to a harder blow back. Space dodges. Shift+F swings heavy and knocks aside a
  tired guard. A Wind bar under your needs shows what you've left to fight with.
- Arrows can be blocked or dodged, never parried, and archers keep their distance and shoot. A
  dragon's jaws and fire can only be partly blocked, and dodged only late; it draws breath where
  you can see it first.
- The practice ring (`--arena`, or `--arena Archer`, `--arena Dragon`): one foe a bout, Enter for
  the next, a panel to pick foe and weapon. In the ring you move freely and fight with the mouse: aim
  with it, left click to strike, right click to swing heavy. Hits land by where your weapon reaches,
  not by chance: a sword sweeps wide, a spear reaches far and narrow.
- Try it in the village: Settings, Interface, Free movement. It applies to the next game you start,
  host or load, and whoever joins you gets it too. You walk freely with WASD and still click to walk
  to things; R draws your weapon, and then the mouse fights. A blow landing on you draws it for you.
  Swings hit whoever's in the way, friend or foe, so mind the neighbours. Raiders move freely too:
  they circle while they wait their turn, step in to strike and back off, and you can see each
  blow's sweep on the ground and step out of it. Archers keep their distance.
- Blows feel like blows: a heavy hit, a parry or a broken guard holds the picture for an instant and
  shakes the camera a little (Settings, Screen shake). Parries ring, blocks knock, heavy swings and a
  spear's jab sound like themselves. A sheathed weapon hangs across your back.
- The camera turns on Z and X now (Q and E without a character). Dig is C, put down is G, and the
  empath's gift panel is Y. The controls card (H) has a Fighting section.
- `--raid-test`: play as the smith with bandits coming down the north road at once.

## 0.1.0-playtest.5

The art pass: the village looks more like itself, and so do its people.

- Dwarves and halflings: two more folk to play (Look, then Folk in the character creator), each
  with a build of their own. Dwarves are short, broad and bearded; halflings small and slight. You'll
  spot a few of each about town. Every outfit and tool fits them.
- Hair, redrawn: every style is made of locks now, with braids woven strand by strand, a bun wound
  round itself and proper back views. The curly styles are new: a big shoulder-length mop of curls,
  and long loose waves to the waist.
- Beards, redrawn: the short beard follows the jaw, the full one hangs to the chest under a
  moustache that curls down, and the braided one ends in a plait with a bead.
- The dragon is redrawn: our own, upright, with ember veins that glow brighter as it draws breath.
- The mini-games look like what they are: the pot and its salt, the bed being made, the arm being
  bound, the hurt drawn into the healer's hand, the blade on the whetstone, the probe along the
  strongbox's seam, the loupe on a curio, the patient on the pallet, the wheel on its stand, the
  cask, its tap and the mallet.
- A proper loading screen while the village gets ready, instead of a frozen title.
- Stairs are real steps now, gentler to look at, and you walk up them instead of through them.
- Torches on the walls of the inn and the smithy. The bellows stand beside the forge instead of
  lying flat on the floor.
- The inn's cask, keg, shelves, larder, stew pot, wash tub, counter, hatch, fireplace and lantern,
  the barrel, crate, barn, whetstone, meditation stone, herbs and pyre all have pictures of their own.
  The pig looks like a pig.
- A compass in the corner, so north is where you think it is.
- Raiders come in by the road, and the shepherd's warning says the right side of the valley.
- Labourers leave the smith's chest alone: its wood and ore are the smith's.
- Picking something up puts it in your pack first, and only into your hands if it won't fit. Ore and
  charcoal fetched for a bloom no longer go astray one at a time.
- A campfire doesn't offer stew any more (it needs a building's fire and its pot).

## 0.1.0-playtest.4

A whole season: a round has a beginning, a middle and an end now. And the town has sound.

- Sound and music. You hear what your character would: fights and fire (muffled through walls),
  people talking, wind, birds, the river, the hearth, the anvil and the pour. Music follows the
  mood: day, night, danger, the festival. Volumes are under Settings > Sound.
- Characters: before any game you pick or make a character, and they're kept. The new
  Characters screen on the main menu lets you change them or let them go, and shows the trades
  each has played. Joining a game goes by your character's name.

- A round is three days. On the last evening the town holds its festival in the square, and the
  season is reckoned: how well travellers speak of the town (Renown) against how much the dungeon
  weighs on the valley (Dread). A screen at the end shows how it went, with everyone's takings.
- Under the clock: the season's day, when the festival is, and a word for each of Renown and Dread.
- Nights pass quickly once everyone's in bed (N). Anyone still up keeps the clock at normal speed.
  You get up at dawn.
- Trouble comes: raiders on the first night, then a fire, a shortage at the inn, raiders or a fever
  on the second day, and a dragon on the third. Villagers with a weapon fight; the rest get indoors.
  You can strike at raiders with F.
- Before a raid a shepherd brings word of how many are coming and when. The town calls for
  weapons and pays the blacksmith for each one hung on the muster rack by the well. When the
  bell rings, anyone without a weapon can take one from the rack (F) and stand with the town.
- Raids are bigger: left to itself, the town loses. It needs you.
- Raiders may pull down the bridge, cutting the road. The tinker mends it: two wood to the gap, F,
  and brace the span level.
- The healer can feel a fever ("a heat in the blood") and quell it with Rime and Still.
- The blacksmith smelts their own iron: ore and charcoal in the forge, the bellows (hold F)
  keeping the fire orange while the bloom comes together, then the slag beaten out on the anvil.
  A good bloom gives three bars. You start with two bars and a chest of ore, and a carter brings
  more each morning.
- Favours are pinned to the notice board in the square: F to read it or pin your own. You can ask
  for so many of something, brought to you, and the town's labourer will take it on.
- Hunger and tiredness go down slower. Everyone starts with 12 silver, and villagers buy their
  supper at the inn of an evening.
- No pausing once you're in town: the town doesn't wait.
- Digging, mining, chopping and foraging are the labourer's work for now.
- Every shop has a sign out front, like the inn's.
- Everyone has a home: a cottage of their own near their work, and nobody else sleeps in your
  bed. The tinker's shop has a little more room round it.
- The bridge looks like one now: low walls, piers and arches over a straighter river. When
  raiders bring it down, you'll see the gap.
- Houses and the inn have fireplaces in their walls. Stand in front to warm up, cook or feed it.
- The inn has lanterns hanging from its beams, so it isn't so gloomy after dark.
- Hungry? Order a meal at the inn's serving hatch (F there), wait in its line, and it's yours
  when the cook puts a bowl up.
- Elves: pick one in the character creator (Look, then Folk), and you'll spot a few about town.
- A fresh title screen: Host game and Join game have their own entries, each line tells you what
  it does underneath, and Credits shows who made the game.
- Steam Deck: the Linux build fits the Deck's screen and plays with its controls (see
  STEAM-DECK.md).
- Old saves won't carry over into a season.

## 0.1.0-playtest.2

Easier to read, with less reading to do.

- Customers show what they want in a bubble over their head: a tankard, a bowl, a hammer, a
  bandage. The bubble turns red as they run out of patience, and a ! means it's yours to serve.
- New in a trade? Tips for your first day sit under your portrait, and your workstations have a
  ring round them until you've been to each. Turn them off in Options > Interface > Tips.
- In the hands-on games, Space does the other thing: draw the piece out of the fire, taste the
  stew, set a pin, latch a spring. A and D only ever steer now. Done seasoning? Just step away
  (Esc).
- A tidier screen: only your shop panel is open at the start, and the others show a dot when
  there's something new. Save, Load and Options live in the Menu.
- The title screen has the new logo.
- Saves from playtest 1 still load.

## 0.1.0-playtest.1

The first playtest build.

- Five trades to take: innkeeper, cook, blacksmith, healer and tinker. Each has its own
  hands-on work (pouring, cooking, forging, mending, lockwork), and AI villagers fill the ones
  nobody picks.
- Parties of adventurers come through town with silver to spend. Set your prices on your shop's
  sign (J), serve them at the front of the line, and watch them head off on quests.
- Play alone (New Round) or with friends (Multiplayer).
- Title screen: Report a Bug says where reports go and opens the logs folder.
