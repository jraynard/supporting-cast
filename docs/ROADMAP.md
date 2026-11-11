# Roadmap

Milestones toward a playable prototype, then beyond. Design intent is in [DESIGN.md](DESIGN.md).

## Done

### M0: Fork and strip (2026-11-10)
- Imported DragonWorld at `cf61dfc` without its history; Narsil at `7b6ac5a`.
- Removed dragon raising, bonding, breeding, riding, elements, the story starts and turn-based
  combat, with their UI, defs and tests. Saves reset to format 1.
- `Scenarios/VillageRound`: a generated town with a longhouse, smithy, clinic and dwellings, six AI
  villagers, and players walking in as avatars, alone or in co-op.
- Raiders now strike in real time (`AssaultJob`); before, turn-based combat did it for them.
- `VillageDayScenarioTests` replaces the dragon slice.
- Bench baselines: seed 1234 state `274EE1FDFF381E51`, full `3117DAE617562E37`; seed 99 state
  `5984BD546BB34042`, full `C3F30EEDDF3BEE6F`.

Known leftovers, each to be picked up by the milestone that needs it:
- Spare beds still trigger `SettlerSystem`, so newcomers arrive and the population grows. A round
  wants a fixed cast (M1).
- Build and zone tools are open to every player; the Carpenter should own them (M2 onward).
- Players can hunt animals but can't yet attack people (M4).
- Villagers don't fight back against raiders, they only suffer them (M3).
- The dragon creature has no brain or breath job of its own yet (M3's late-round crisis).
- Some dragon-era art, tools and def fields are still in the tree. Prune with the M6 art pass.

## Next

### M1: Being someone
- Lobby and trade deal from player preferences; late joiners take an open trade from an AI villager.
- `Perception`, with sight-limited drawing of creatures and items.
- `Speech`: say, whisper, shout, heard by room and distance.
- `Work/`: Lifeline's mini-game framework with networked prediction.
- `BotPlayer`, for scenario tests and solo playtests.

*Exit: 4 players take trades and can only see and hear what their characters can.*

### M2: The first party
- Adventurers (`Party`, `AdventurerBrain`), `Errands`, coin, and the off-map `Expedition`.
- Four deliberately different trades: Innkeeper, Blacksmith, Healer, Quest Broker.

*Exit: a party arrives, is served, goes out and comes back changed by what the players did.*

### M3: A whole season
- `Director`, Renown and Dread, crises including the late-round dragon, the festival, the
  end-of-round reveal. Villagers defend themselves.

*Exit: an Honest round is playable start to finish.*
**Playtest gate: is serving heroes fun with friends, without traitors?** Tune before going further.

### M4: Whispers
- Allegiance, objectives, the hidden shop, evidence, player violence, Constable and jail, the bell,
  meetings and exile, ghosts.

*Exit: a Whispers round.* **Playtest gate: can traitors act and be caught?**

### M5: The systemic layer
`Reagents`, Apothecary, Undertaker, Merchant, writable notes, keys, disguises, poison on anything.

### M6: Breadth
Remaining trades, more maps, crisis variety, personal ambitions, art pass.

### M7: Playing with strangers
An authoritative server filtered by `Perception`, moderation, voice.
