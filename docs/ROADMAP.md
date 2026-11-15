# Roadmap

What's next, in order. Design intent is in [DESIGN.md](DESIGN.md); everything built so far, slice
by slice, is in [HISTORY.md](HISTORY.md). When something's done, move it to HISTORY.md and trim it
here.

## Where we are

- **M0 to M2:** forked from DragonWorld; being someone (perception, speech, trades as games of
  their own); the first party (adventurers, shops and the line, quests with hidden truth).
- **Millbrook**, the hand-made village; UX and control passes; the blacksmith, innkeeper, cook,
  healer and tinker each played through, with lessons in the practice yard.
- **M3, a whole season:** the reckoning (Renown and Dread), nights and suppers, the Director's
  crises (raid, the bridge, fire, shortage, fever, the dragon), the festival and the notice board.
  Bots play a round from the deal to the reveal (`SeasonScenarioTests`).
- **Since M3:** the Steam Deck and control passes, free-movement combat, death and ghosts, the
  cook pass, the festival as an event, internet play readied, playtest builds on itch.

**The M3 gate, "is serving heroes fun with friends, without traitors?", is a tentative yes.**
Playtests say it's becoming quite fun, and rounds without traitors are a real mode for players who
want no PvP, not just a stepping stone. What's left before M4 is making that mode solid over the
internet, with the weakest trade brought up and the numbers tuned.

## Now: the co-op round, solid

In priority order. Each is small enough to finish and playtest on its own.

### 1. Internet play, proven
Readied in code (Narsil relay protocol 3, keepalive, host reclaims its join code after a blip,
relay fan-out and limits; a 200 ms jitter buffer cap, pad commands throttled, a claim budget on
free movement). The relay runs on an Oracle Cloud free instance (`CoopRelay.Default`,
129.153.122.49:47050); redeploy with
`external/narsil/deploy/relay/publish.sh ubuntu@129.153.122.49 linux-arm64`.
- Test over a real internet connection: home host, a joiner on a phone hotspot and a Steam Deck.
  Watch the Players window's buffer, jitter and starvations over a whole round, join mid-round,
  and pull the host's network for a few seconds (the session should carry on, not split).
- Read the host's `net: snapshot … KB` log line: if joins visibly stall the others, chunk snapshots.

*Why first:* every other playtest depends on friends being able to play from their own homes.

### 2. The healer's games, playtested
The pass is in (HISTORY.md, "The healer's pass"): laying on hands is one sitting (skim, press to
read, keep pressing to draw), the shrine is rebuilt, and nobody goes hungry without a cook. Play
it through in the practice yard (`--practice Healer`) and in a round with friends:
- Does reading by the pulse teach the kinds? Is the flinch fair? Tune `HandsOnSession` (read and
  rough steps) and `DrawLink` (the band, the snap).
- Still to look at: the bandage band, the weave's beat window, the labyrinth's size.
- The AI empath's meditation (`MeditateJob.Gathers`) and the charms it puts out (`LayOnHands.CharmsOnShow`, price).

### 3. Combat balance
The town drives off fewer raids than it did (1 of 8 seeds in a sample against 3 of 8 before the
one model). Tune in the practice ring (`--arena`) and `--raid-test`: AI defenders, armour soak, AI
dodge odds, stamina (`Blows.Regen`, `RegenDelay`, costs) and the parry window (`ParryBefore`,
`ParryAfter`). The dragon: likely too easy with a party in town, impossible without one. Related:
- **AI footwork.** Fighters hold a blow while a friend's in its sweep but don't step round for a
  clearer angle, and don't back off when hurt beyond giving up the fight. The dragon still lands
  and bites from the grid rather than circling (`Ai/DragonBrain`).
- The unarmed don't mob a raider together; without adventurers in town, two raiders put every
  villager down. The wounded lie where they fell until someone thinks to help.

### 4. A tuning playtest
One round with friends aimed only at the numbers, then a pass on what it shows. Everything here
has defaults that haven't been tuned in play:
- **The season** (`season.json`): the Renown target and Dread rates (Dread per death included),
  the day-two crisis weights (equal for now), raid size, the raid warning's lead, the town purse,
  the price of arms, the carter's load.
- **Night speed and the need rates.**
- **The festival** (`festival.json`, `season.json`): the tale's and the revels' length, the
  activity weights, what the show table and the games are worth to a guest, whether the toasts
  swing a season enough to matter.
- **Death and ghosts:** the Calling's cost and windows (`Revival`), presence and sign costs
  (`Hauntings`), errand times and Renown (`afterlife.json`).
- **The lessons:** one trade at a time, the words and whether the step shown is the one you're on.

Mini-game timings come after this, trade by trade (Backlog, "Tuning the mini-games").

## Then: M4, Whispers

The traitors: the game's hook, and what makes it a game people tell others about.
- Allegiance, objectives, the hidden shop, evidence, player violence, Constable and jail, the bell,
  meetings and exile.
- Ghosts in it: what the dead can tell, and what a traitor does about a Calling.
- The festival ceremony's `Accusation` step (`CeremonyStep`, a no-op for now): the town can name a
  traitor before the verdict.
- **The lobby's Traitors toggle** (`RoundOptions.Traitors`, forced off by `RoundSystem.SetOptions` for
  now): let it through and deal allegiances from it.
- **Friendly fire's consequences:** townsfolk you cut are only hurt for now; the town remembering
  it, shouting it, and it counting at the reveal.
- Discord integration for meetings and reveals, in the spirit of the bots Among Us groups use
  (look at what's out there before designing ours).

*Exit: a Whispers round.* **Playtest gate: can traitors act and be caught?**

## Toward Steam

### When
Don't wait for breadth (M6) to start; do wait for M4 to launch.

- **Store page: now-ish, during M4.** A "Coming soon" page costs little and is how wishlists
  accumulate, and wishlists at launch decide how much Steam shows the game. Pages usually want
  six months or more of lead time. What it needs is a capsule, a trailer and screenshots that
  sell the pitch, so get the co-op round looking and sounding good first (Now, 1 to 5, and a theme
  for the trailer).
- **Steam Playtest: once Whispers plays.** Use it to grow beyond friends and family and to see
  whether traitors land with groups who don't know you.
- **Early Access launch: after M4, with a little breadth.** The traitors are what make this
  different from other cosy co-op games, and what streamers and friend groups pick up, so launch
  with them in. Before launch, have: a round that's reliable over the internet with Steam invites,
  Whispers through its gate, a second map or a couple more trades so rounds don't feel the same
  by the fifth, and some cosmetic progression so there's something to come back for. M5 to M7
  belong in Early Access, shaped by its players; that's what Early Access is for.
- Launch is the one big moment of visibility, so launching too thin costs more than launching a
  few months later. Matchmaking with strangers (M7) isn't needed: a friends-and-invites game is a
  fine Early Access product.

### What
- **Store page:** Steamworks account and app fee, capsule art in the brand's style, a short
  trailer from real play, screenshots (`--screenshot` covers these), the description in the
  brand's voice. Put the page up as "Coming soon" to start gathering wishlists.
- **Steamworks:** invites and lobbies through Steam (friends join from the overlay), Steam
  Datagram Relay as a transport in place of (or beside) our own relay, rich presence, Steam Cloud
  for `characters.json`. Keep it behind an interface in the game, not in Narsil's sim.
- **Steam Playtest** in place of the itch page once there's a page, to reach more testers.
- **Deck Verified:** the remaining pad gaps (a cursor on the right stick for placing buildings and
  dragging areas), sound on the Deck, text size.
- Licensing: the BRUTAL License allows commercial release; keep the splash, Credits, `NOTICE.txt`
  and the licence files (`external/narsil/docs/LICENSING.md`).

## Later

### Your character: customization and progression
Glamour is the true end game. All of it lives on the saved character (`CharacterRoster`,
`CharacterProgress`: rounds, survivals and silver per trade are already kept; `Titles`, `Title` and
`Unlocked` wait to be filled).
- More to choose in the creator: customization is a focus.
- Mastering a trade earns an outfit and a title for it.
- Shop customization: pick your sign's icon, and better looks with skill and Renown.
- Lighter, RPG-like ways to improve a character, kept small so a veteran and a newcomer can share a
  round.

### M5: The systemic layer
`Reagents`, Apothecary, Undertaker, Merchant, writable notes, keys, disguises, poison on anything.
The necromancer and the Undertaker build on `Death`, `Ghost` and the Calling.

### M6: Breadth
Remaining trades, more maps (authored like Millbrook), crisis variety, personal ambitions, an art
pass. Purely non-combat options (a round, or trades, that never need a blade) once there's the
variety to carry them.

### M7: Playing with strangers
An authoritative server filtered by `Perception`, matchmaking, moderation, voice.

### Mods
New maps, buildings, trades, game modes and character options, as far as the defs allow; Steam
Workshop eventually. Content is already all in `data/defs`, which is most of the way there.

## Backlog

Left behind along the way, plus wishes. Grouped by area; nothing here blocks a playtest. Pull
items into "Now" when a playtest says they matter.

**Tuning the mini-games**
- **Forge:** the heat and quench windows; the bloom's band, how long it takes and its yield.
- **Kitchen:** the pot, the seasoning; and the cook's list below.
- **Bar:** washing up, tapping a keg, mixing.
- **Healing:** after the healer's playtest (Now, 2).
- **Tinkering:** truing a wheel, bracing a span.
- Some jobs easier and some harder, to suit different players and styles.

**The trades, deeper** (DESIGN.md, "Trade notes")
- **Blacksmith:** commissions (the buyer's hilt, runes and metal); molds, and the hilt and grip as
  their own step; lighting and banking the forge by hand; reforging. AI smiths still forge and
  smelt with a plain `CraftJob` and don't answer a call to arms (bots do). Only iron is smelted
  by hand; copper, tin and the Kiln aren't in Millbrook. The town purse is a number, not coin in a
  strongbox: it wants the Mayor.
- **Innkeeper:** the AI innkeeper doesn't mix (asked for a named drink, it pours an ale); mess on
  tables, and a rag; no brewing (casks refill only from the carter's kegs); keys for the guest
  rooms; villagers at supper sit but don't swap rumours.
- **Cook:** bots and AI cooks never chop, roast, bake, make pies, salt, smoke or name a house dish
  (an AI cook never offers the roast, bread or pies); bots don't joint, pluck, lid a flare or chase
  rats. Rations are sold at the bar's line; the hatch would suit them better. The healer sending
  the hurt to eat a broth; teas from shared herbs; pickling. To tune: the fold cue and the cord's
  band (`PackSession`), cravings and moods (`Tastes`), the fed and sick factors (`Rations`),
  seasoner weight and cap (`Cooking`), the house dish (`HouseDish`), tips (`ShopSystem.FineTip`),
  the spit (`Roasting`), mealtimes and small hours (`ShopSystem.Mealtime`, `SmallHours`), arrival
  hunger (`PartySystem.Send`), baking (`Baking`), the cleaver and the pluck (`Butchery`), the crimp
  (`Pies`), flares (`Cooking.FlarePerStep`), rats (`RatSystem`), feasts (`FeastSystem`), broths
  (`Broths`), word of the food (`ShopSystem.Fare`).
- **Healer:** adventurers never tear a charm in town.
- **Tinker:** no lockboxes or locked chests (locks go on doors only); AI tinkers don't buy curios;
  a wagon lesson (practice customers' wagons aren't damaged yet).
- **Carpenter** (benched): the build and zone tools are open to every player and should be the
  carpenter's; building and mending have no game of their own.
- **Gathering:** digging, mining, chopping and foraging are the labourer's until the Miner and
  Forager come.

**Lessons**
- Handing over a drink, a dish or a box from the order (as "Take it back" does for a mend), and
  the customer pointer for notice-board errands (`VillageGame.Errands`).

**Bots** (solo playtests and the scenario tests)
- Bots don't weave charms, pack rations, put goods on show, pin locks, make contraptions, haggle
  over curios, use the notice board or throw water.
- `SeasonScenarioTests` checks only that a round plays through, not how it comes out.

**Crises and the town**
- Raiders don't bash doors or gates; only the bridge breaks.
- AI carrying the dead to the shrine, so an AI empath gets a chance without a player's help.
- The bracing window, buckets against the dragon and the dragon itself haven't been seen in a
  running game yet (the dev tools send either).
- Time passes fast only while everyone sleeps, not when the town is otherwise quiet.
- Errands fetch only what's lying about or in common stores. Wood left out on a cold day goes on
  the fires first.

**Map and movement**
- Lanes cost the same to walk as grass, so villagers don't prefer them.
- An AI re-planning a walk snaps back to the cell it left rather than turning mid-step (players
  turn at once).

**Art and look**
- **Swing, dodge, dance and cheer frames** from the people pipeline (`tools/chibi_people.py`: new
  tags after the rest, every layer regenerated, all four in one pass). For now a blow plays the
  work swing with the weapon drawn about the hand, a dodge is a tumble, a dancer hops and sways
  (`WorldRenderer`), and a cheer is a hop.
- A proper ghost (a pale, see-through take on the character), and the Calling's glow.
- The bucket's water drawn in flight.
- Placeholder art in the kitchen: rations are a bolt of cloth, the prep table and butcher block a
  table, the spit the butcher spot, roast slices and the side of pork a meat sprite; the oven
  wears the fireplace, flour and oats the fiber, dough and the unbaked pie the clay, bread,
  porridge and pie the meal; the fowl is hide, salt is lime, the log basket a crate, the smoking
  rack the tool rack; rats are small hares.
- The folk (elves, dwarves, halflings) are only looks; they could have something of their own (an
  elf's keener sight at dusk, a dwarf's ease at the forge, a halfling's quiet step). Halflings
  could go barefoot (villagers' clothes come from the sim, so it isn't only a look).

**Sound** (first pass done: docs/AUDIO.md)
1. **Music, ours:** a theme (at least its start), then a score to replace the placeholder tracks
   mood by mood (`title`, `day`, `night`, `danger`, `festival`, `reveal`). Later, adaptive layers
   (danger drums over the day track), festival songs, and a bard. Needed before a Steam trailer.
2. **Coverage:** footsteps by surface, doors, animals and weather; hammering heard from other
   smithies and every trade seen from outside; the bucket's splash; room reverb (EFX) for the inn,
   cottages and caves; a mix pass from playtest notes.
3. **Recorded sounds:** still synthesized are the inn's murmur, a town bell, a burning building and
   an arrow's impact. CC0 freesound pages (login to download), brought in through
   `tools/import_sounds.py`:
   - murmur: freesound.org/people/ivolipa/sounds/326313, squareal/sounds/237398
   - bell: dsp9000/sounds/76405, Audeption/sounds/425172
   - house fire: 7778/sounds/196619
   - arrow hit: plantmonkey/sounds/410751
   - a babbling stream: BurghRecords/sounds/415151
   - a beer pour: Kinoton/sounds/347006
4. **Voices (text to speech):** NPC lines spoken aloud through local neural TTS (sherpa-onnx
   running Kokoro or Piper voices), a voice per character derived like their look; synthesized off
   the main thread, cached, opt-in in Settings, babble the fallback. Watch the model size (about
   100 MB) and the CPU on a Deck. Lines are made at runtime (players type theirs), so they can't
   be pre-recorded.

**Words and docs**
- Watch a child and a non-gamer play the first ten minutes.
