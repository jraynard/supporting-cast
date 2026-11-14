# Roadmap

What's next, toward a playable prototype and beyond. Design intent is in [DESIGN.md](DESIGN.md);
everything built so far, slice by slice, is in [HISTORY.md](HISTORY.md).

## Where we are

- **M0 to M2:** forked from DragonWorld; being someone (perception, speech, trades as games of
  their own); the first party (adventurers, shops and the line, quests with hidden truth).
- **Millbrook**, the hand-made village; UX and control passes; the blacksmith, innkeeper, healer
  and tinker each played through and polished.
- **M3, a whole season:** the reckoning (Renown and Dread), nights and suppers, the Director's
  crises (raid, the bridge, fire, shortage, fever, the dragon), the festival and the notice board.
  Bots play a round from the deal to the reveal (`SeasonScenarioTests`).
- **Playtest prep** and the **Steam Deck pass** (docs/STEAM-DECK.md); then polish pulled forward:
  the bridge, fireplaces, elves, the title screen and the docs.

**Open gate, from M3: is serving heroes fun with friends, without traitors?** Tune before going
further: the season's numbers (`season.json`: the Renown target, the Dread rates, crisis weights,
raid size), the dragon's toughness, the night speed and the need rates.

## Next

### The cook pass
Bring the cook up to the other trades (docs/DESIGN.md, "Cook"). The smith, healer and tinker each
have five to seven hands-on games, a hidden state read by feel, and work that goes out on quests;
the cook had two games and nothing that left town. Phases 1 and 2 are built (HISTORY.md, "Cook
pass"): flavours, diners' tastes, sniffing, rations for the road; the house dish, diners saying
what was wrong, tips; and chopping at the prep table, the first of phase 3. Next, in order:

1. **A menu beyond stew:** the spit (a roast turned on cue, read by its crust); the oven (knead, prove, bake by smell; bread for better
   rations); porridge in the morning. Priced apart on the sign.
2. **Pressure in the kitchen:** rushes at breakfast and supper, a second pot, the kitchen fire fed
   by hand; a grease fire (a fatty pot at a boil; clap the lid on or it's a real fire); rats in the
   larder in the shortage crisis, as creatures the cook chases down.
3. **The kitchen and the town:** feasts for a triumphant party (several dishes against the clock,
   the room's mood and tongues); broths with the healer; keeping the larder (salt, smoke, the cold
   store); word of the food bringing parties.

Left from phases 1 and 2:
- Bots and AI cooks never chop or name a house dish; the house dish brings no more parties yet (word of the
  food, phase 3).
- Rations are sold at the bar's line like any gear in the inn; the hatch would suit them better.
- Placeholder art: rations are a bolt of cloth, the prep table is a table's sprite.
- Tune in play: the fold cue and the cord's band (`PackSession`), the craving chance and moods
  (`Tastes`), the fed and sick factors (`Rations`), seasoner weight and cap (`Cooking`), how near
  a pot must taste to be the house dish and how often it's asked for (`HouseDish`), the tips
  (`ShopSystem.FineTip`).

### SOUND

First pass done (docs/AUDIO.md, HISTORY.md). Next, as long-term items:

1. **Music, ours:** a theme song (at least the start of one), then a score to replace the free
   placeholder tracks mood by mood (`title`, `day`, `night`, `danger`, `festival`, `reveal`).
   Later, adaptive layers (danger drums over the day track), festival songs, and a bard.
2. **Coverage:** audio feedback for just about every part of the game:
   - footsteps by surface, doors, animals and weather
   - hammering heard from other smithies, every trade action seen from outside
   - room reverb (EFX) for the inn, cottages and caves
   - a mix pass from playtest notes
3. **Recorded sounds:** most thin spots now have CC0 recordings (AUDIO.md).
   - **Still synthesized:** the inn's murmur, a town bell, a burning building and an arrow's
     impact. There's nothing good on OpenGameArt; these CC0 freesound pages need a login to
     download:
     - murmur: freesound.org/people/ivolipa/sounds/326313, squareal/sounds/237398
     - bell: dsp9000/sounds/76405, Audeption/sounds/425172
     - house fire: 7778/sounds/196619
     - arrow hit: plantmonkey/sounds/410751
     - a babbling stream: BurghRecords/sounds/415151
     - a beer pour: Kinoton/sounds/347006
   - Bring them in through `tools/import_sounds.py`.
4. **Voices (text to speech):** NPC lines spoken aloud.
   - Local neural TTS through sherpa-onnx (NuGet) running Kokoro or Piper voices, a voice per
     character derived like their look.
   - Synthesized off the main thread, cached, opt-in in Settings; babble stays the fallback.
   - Watch the model size (about 100 MB) and the CPU on a Steam Deck. Lines are made at runtime
     (and players type theirs), so this can't be pre-recorded.
5. **Steam Deck:** check the sound there (output switching, volume, CPU).

### The festival
Built (HISTORY.md, "The festival, made an event"). What's left:

- **Playtest it:** the length, the activity weights, what the show table and the games are worth
  to a guest (`festival.json`), and whether the toasts swing a season enough to matter.
- **Art:** dance and cheer frames in the people pipeline (`tools/chibi_people.py`; the renderer
  hops for now), a proper bunting string between the lantern posts.
- **The butts with real arrows:** a target thing the ranged code can hit, shot from a mark.
- **Music from the square:** truly positional music needs Narsil's audio to place a track; for now
  the festival's music plays only near the well.
- **Putting it up:** an AI labourer carrying the tables and lanterns out that afternoon, instead of
  everything appearing at the bell.
- **M4:** the ceremony's `Accusation` step, where the town can name a traitor before the verdict.

### Death and ghosts
First pass done (HISTORY.md, "Death and ghosts"). What's left:

- **Playtest it:** the Calling's cost and windows (`Revival`), presence and sign costs
  (`Hauntings`), errand times and Renown (`afterlife.json`), and Dread per death (`season.json`).
- **Art:** a proper ghost (a pale, see-through take on the character), the Calling's glow.
- **AI carrying the dead** to the shrine, so an AI empath gets a chance without a player's help.
- Later: the necromancer (M4) and the Undertaker (M5) on `Death`, `Ghost` and the answer.

### Combat pass
Fights are free movement and blows by reach and timing, Souls-lite, everywhere (done: HISTORY.md,
"Combat: blows seen coming"). What's left:

- **Swing and dodge frames** from the people pipeline (`tools/chibi_people.py`: new tags after the
  rest, every layer regenerated). For now a blow plays the work swing and the weapon is drawn on
  its own about the hand; a dodge is a tumble.
- **Balance.** The town drives off fewer raids than it did (1 of 8 seeds in a sample against 3 of
  8 before the one model): AI defenders, armour soak, AI dodge odds, stamina (`Blows.Regen`,
  `RegenDelay`, costs) and the parry window (`ParryBefore`, `ParryAfter`) want tuning in the
  practice ring (`--arena`) and `--raid-test`.
- **AI footwork.** Fighters hold a blow while a friend's in its sweep but don't yet step round for a
  clearer angle, and don't back off when hurt beyond giving up the fight. The dragon still lands
  and bites from the grid rather than circling (`Ai/DragonBrain`).
- **Friendly fire's consequences** (with M4): townsfolk you cut are only hurt; the town remembering
  it, shouting it, and it counting at the reveal.

### Control Pass: on the Deck
- Built (HISTORY.md, "Control pass: rebinding and the gamepad"). Left for the device: play a round
  on a Deck from the pad alone; check every mini-game and window navigates (ImGui focus quirks show
  only in play); tune the stick deadzone and the wheel; export the Steam Input layout to
  `tools/steamdeck/`.
- Building and area tools still need the trackpad (a pad cursor would cover them).

### Internet play
- test over an actual internet connection to verify it works
- test relay servertaw

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

### Character customization
- The roster is one for every mode, with its own Characters screen (done: HISTORY.md). Next, more to
  choose in the creator: customization is a focus.

### Progression systems
- ways to improve your characters... rpg like systems, and, more importantly haha, cosmetic options. Glamour is the true end game! this should be tied to your saved character.
  - Builds on `CharacterProgress` (`Characters/CharacterRoster.cs`): rounds, survivals and silver per
    trade are already kept; `Titles`, `Title` and `Unlocked` wait to be filled.
- if you master a trade you get a cool outfit and title related to that trade
- shop customization - pick your icon, get upgraded looks based on your skill and renown.

### Mods
- ability to add mods (new maps, new buildings, new trades, new game modes, character customization options, whatever's possible)
- steam workshop integration eventually

### Polish pass (timing uncertain)
What's been left behind along the way, gathered from each milestone's leftovers, plus wishes.
Grouped by area; nothing here blocks a playtest.

**Tuning, once there's play to tune from**
- The season (`season.json`):
  - the Renown target and the Dread rates;
  - the weights of the day-two crises (equal for now);
  - raid size;
  - the raid warning's lead, the town purse and the price of arms, the carter's load;
- The dragon: likely too easy with a party in town, and impossible without one.
- Night speed and the need rates.
- Every mini-game's timing, none of it tuned in play yet:
  - **Forge:** the heat and quench windows; the bloom's band, how long it takes and its yield.
  - **Kitchen:** the pot, the seasoning.
  - **Bar:** washing up, tapping a keg, mixing.
  - **Healing:** the draw feels, the heartbeat window, the wrap band; the labyrinth's size,
    sitting and centre burst.
  - **Tinkering:** truing a wheel, bracing a span.
- Some jobs easier, some harder, to suit different players and styles.

**The trades, deeper** (docs/DESIGN.md, "Trade notes")
- **Blacksmith:**
  - commissions (the buyer's hilt, runes and metal);
  - molds, and the hilt and grip as their own step;
  - lighting and banking the forge by hand;
  - reforging;
  - AI smiths still forge and smelt with a plain `CraftJob`, and don't answer a call to arms
    (bots do);
  - the town purse is a number, not coin in a strongbox: it wants the Mayor;
  - only iron is smelted by hand; copper, tin and the Kiln aren't in Millbrook.
- **Innkeeper and cook:**
  - the AI innkeeper doesn't mix (asked for a named drink, it pours an ale);
  - mess on tables, and a rag;
  - no brewing: casks are refilled only from the carter's kegs;
  - keys for the guest rooms;
  - villagers at supper sit but don't swap rumours;
  - the cook's own list is "The cook pass" under Next.
- **Healer:**
  - AI healers only bandage;
  - adventurers never tear a charm in town;
- **Tinker:**
  - no lockboxes or locked chests (locks go on doors only);
  - AI tinkers don't buy curios.
- **Carpenter** (benched): the build and zone tools are open to every player and should be the
  carpenter's; building and mending have no game of their own.
- **Gathering:** digging, mining, chopping and foraging are the labourer's for now, until the
  Miner and Forager come.

**Bots** (solo playtests and the scenario tests)
- Bots don't:
  - weave charms, pack rations or put goods on show (AI cooks don't pack rations either);
  - pin locks, make contraptions or haggle over curios;
  - use the notice board;
  - go to the healer (they stanch their own bleeding with a bandage to hand, nothing more).
- `SeasonScenarioTests` checks only that a round plays through, not how it comes out.

**Crises and the town**
- Raiders don't bash doors or gates; only the bridge breaks.
- buckets to fight fire (throw should be like hold F to charge how far it goes then sprays out the water, then run back to well to refill)
- The unarmed don't mob a raider together. Without adventurers in town, two raiders put every
  villager down.
- The wounded lie where they fell until someone thinks to help.
- The bracing window and the dragon haven't been seen in a running game yet (the dev tools now
  send either).
- Time passes fast only while everyone sleeps, not when the town is otherwise quiet.
- Errands fetch only what's lying about or in common stores. Wood left out on a cold day goes on
  the fires first.

**Art and look**
- The folk (elves, dwarves, halflings) are looks; they could have something of their own (an elf's
  keener sight at dusk, a dwarf's ease at the forge, a halfling's quiet step). Halflings could go
  barefoot (villagers' clothes come from the sim, so it isn't only a look).

**Map and movement**
- Lanes cost the same to walk as grass, so villagers don't prefer them.
- An AI re-planning a walk snaps back to the cell it left rather than turning mid-step (players
  turn at once).

**Steam Deck**
- Placing buildings and dragging areas from the pad (a cursor on the right stick while a tool's out).

**Words and docs**
- Watch a child and a non-gamer play the first ten minutes.
