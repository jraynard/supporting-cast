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
  neutral, wait where their errands are served, and move on.
- `Errands/`: every demand is an errand asked aloud, so only those who hear it know of it. Coin is
  real silver that changes hands. Players post, take, finish and call off errands for each other.
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
  building and mending have no game of their own yet, only the lockwork.
- Players can hunt animals but can't yet attack people (M4).
- Villagers don't fight back against raiders, they only suffer them (M3).
- The dragon creature has no brain or breath job of its own yet (M3's late-round crisis).
- Some dragon-era art, tools and def fields are still in the tree. Prune with the M6 art pass;
  the whetstone, ale cask and meditation stone borrow art too.
- AI healers only bandage. Casks don't refill and there's no brewing. Mixed drinks, forge
  management, the quench and commission details are still to come (docs/DESIGN.md, "Trade notes").
- `Adventurer.Satisfaction` and the expedition tallies are collected, waiting for M3's Renown and Dread.
- The new mini-games haven't been tuned in play yet.

## Next

### UX Pass
- UX needs a lot of work. let's clean things up and make it look a nicer visually. we can leave the job minigame art fairly basic for now until we playtest those a bit more to make sure everything is good, but lets do one as an example as well, we can use the blacksmith's hammering to start with. if possible design UX upgrades so they can be carried back to dragonworld / narsil as well.

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
