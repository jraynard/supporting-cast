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


### Death and ghosts
- need to move this up most likely as people can and should die when the dragon attacks, or other dangers added in the crisis system.

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

### Polish pass (timing uncertain)
What's been left behind along the way, gathered from each milestone's leftovers, plus wishes.
Grouped by area; nothing here blocks a playtest.

**Tuning, once there's play to tune from**
- The season (`season.json`):
  - the Renown target and the Dread rates;
  - the weights of the day-two crises (equal for now);
  - raid size;
  - the raid warning's lead, the town purse and the price of arms, the carter's load;
  - Difficulty doesn't scale the target or the limit yet.
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
- The forge can heat the smith's spot past 80°C. Bots step back from it now; AI smiths and
  players don't.

**The trades, deeper** (docs/DESIGN.md, "Trade notes")
- **Blacksmith:**
  - commissions (the buyer's hilt, runes and metal);
  - molds, and the hilt and grip as their own step;
  - lighting and banking the forge by hand;
  - reforging;
  - AI smiths still forge and smelt with a plain `CraftJob`, and don't answer a call to arms
    (bots do);
  - the town purse is a number, not coin in a strongbox: it wants the Mayor;
  - villagers keep the town's arms after a muster, and nobody hangs them back on the rack;
  - only iron is smelted by hand; copper, tin and the Kiln aren't in Millbrook.
- **Innkeeper and cook:**
  - the AI innkeeper doesn't mix (asked for a named drink, it pours an ale);
  - casks don't refill and there's no brewing;
  - mess on tables, and a rag;
  - keys for the guest rooms;
  - villagers at supper sit but don't swap rumours.
- **Healer:**
  - AI healers only bandage;
  - a wound stays felt once found, however it changes;
  - pallets aren't kept from villagers who sleep there;
  - adventurers never tear a charm in town;
  - a fever doesn't weaken a party on a quest.
- **Tinker:**
  - no lockboxes or locked chests (locks go on doors only);
  - AI tinkers don't buy curios.
- **Carpenter** (benched): the build and zone tools are open to every player and should be the
  carpenter's; building and mending have no game of their own.
- **Gathering:** digging, mining, chopping and foraging are the labourer's for now, until the
  Miner and Forager come.

**Bots** (solo playtests and the scenario tests)
- Bots don't:
  - weave charms or put goods on show;
  - pin locks, make contraptions or haggle over curios;
  - use the notice board;
  - bind their own wounds or go to the healer, so a hurt bot can bleed out.
- `SeasonScenarioTests` checks only that a round plays through, not how it comes out.

**Crises and the town**
- Raiders don't bash doors or gates; only the bridge breaks.
- The unarmed don't mob a raider together. Without adventurers in town, two raiders put every
  villager down.
- The wounded lie where they fell until someone thinks to help.
- A sleeping player is woken only by walking. A shout, a fire or a raid should rouse them.
- Time passes fast only while everyone sleeps, not when the town is otherwise quiet.
- The festival has no lanterns, tables or music, and the inn doesn't serve outside.
- Errands fetch only what's lying about or in common stores. Wood left out on a cold day goes on
  the fires first.
- A dev button to send a crisis (the bridge has one). The bracing window and the dragon haven't
  been seen in a running game yet.
- The bridge's parapets are drawn, not built: check nobody can step off its side.

**Art and look**
- A hands-on pass over each mini-game's window; some are still drawn plainly.
- More fantasy folk: our own takes on dwarves and halflings. Elves are in, as a look; they could
  have something of their own (keener sight at dusk, an aptitude).
- The title's loading line shows for a frame while a village is made ready; a real loading screen
  would let it breathe, and work for joining and hosting too.

**Map and movement**
- Lanes cost the same to walk as grass, so villagers don't prefer them.
- No turning mid-step: a turn waits for the step under way.
- `--building-demo` lays its showcase over the village.

**Steam Deck**
- Native gamepad support (an action layer over `Input`, ImGui gamepad navigation), so menus work
  from the d-pad and nothing needs the trackpad.
- Tooltips reachable without hovering.

**Words and docs**
- Watch a child and a non-gamer play the first ten minutes.
