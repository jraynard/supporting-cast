# Supporting Cast: design

Working title. Living document: the decisions so far and the intended depth of each system.
What's built and what's next is in [ROADMAP.md](ROADMAP.md).

## Pitch

A multiplayer fantasy village where the players are the people a tabletop adventuring party usually
walks past. One player runs the inn, another the forge, another the clinic. Heroes arrive wanting
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

- 30 to 40 minutes at fixed speed with no pause: 3 to 4 in-game days at 10 real minutes a day.
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

**Errands are the shared spine.** Every demand is an `Errand`: who wants what, where, for how much
and how long they'll wait. Adventurers post them and players post them to each other.

## Trades

Called `Trade` in code, because `Ai/Job` is already the AI coroutine.

| Trade | The game it plays | Built from | Betrayal it enables |
| --- | --- | --- | --- |
| Innkeeper | Order juggling, drink mixing, hearing gossip | Larder, cooking, Social | Spiked drinks, selling secrets |
| Blacksmith | Heat, hammer and quench timing | Forge, bellows, tiers, `Crafted` quality | A hidden flaw in a blade |
| Healer | Examine, diagnose, treat by hand | Lifeline's `Assessment` and `Work/` | Wrong treatment, "they didn't make it" |
| Quest Broker | Match parties to quests on partial intel | New `Expedition` | Sending a party to its death |
| Constable | Patrol, evidence, arrests, jail | `Prisoners`, locks | Framing, abuse of authority |
| Apothecary | Discover reagent reactions, reshuffled each round | New `Reagents` | Poisons, explosives |
| Merchant | Haggling, appraisal, spotting cursed loot | Inventory, `HoardValue` | Fencing, forged goods |
| Undertaker | Autopsy, burial before the dead rise | Health, `Scars`, carcasses, the pyre | Destroying evidence |
| Forager, Miner | Risky trips beyond the walls | Harvest, Mining, Hunting, Fishing | Alone out there, in both directions |
| Carpenter | Build, repair, fit locks | Construction, Rooms, doors | A spare key |
| Bard | Rhythm performance, morale, rumours | Mood, Social | Spreading lies |
| Mayor | Treasury, laws, chairs town meetings | New | Corruption |

## Pressure and traitors

A `Director` grown from `Storyteller` always runs: raids, fire, shortages, plague, and a late-round
dragon that burns the town. Traitors are a round option layered on top:

- **Honest:** none, and stronger crises.
- **Whispers:** one or two.
- **Unknown:** nobody is told whether there are any.

A traitor serves the Thing Below. They get secret objectives and buy tools from a hidden shop (the
Whisper) with favour earned by completing them. Everything leaves evidence: maker's marks, residue,
wounds, witnesses. A limited town bell calls a meeting to accuse, vote and exile.

**Death.** Ghosts watch and can be revived. After a delay a dead player may return as a new
arrival, without their old knowledge being confirmed to anyone.

## Technical decisions

- **Lockstep, for friends.** Narsil's netcode gives every peer the whole game state, including who
  the traitor is. The UI hides it, but a modified client could read it. That's acceptable among
  people who trust each other, and it is why playing with strangers is its own milestone (M7).
- **One knowledge layer (`Perception`).** The game may show other characters, allegiances and
  hidden item properties only through it, as Lifeline does with `Assessment`. If the game goes
  public, this layer becomes the server's replication filter.
- **Real-time violence only.** DragonWorld's turn-based fights freeze the whole map, so one stabbing
  would stop every player's game. Attacks go through `RealtimeAttacks`.
- **Speech is sim state.** `SayCommand` goes through the sim rather than Narsil's global chat, so
  hearing can depend on room and distance, with whispers, shouts, eavesdropping and disguised
  speakers. Text only for now.
- **Mini-games run ahead locally.** A timing game can't wait on a network round trip. The client
  runs the same deterministic `WorkSession` for instant feedback and sends batches of timestamped
  inputs, which the sim replays, like `AvatarPredictor` does for walking. Interruptions still come
  from the sim.
- **Bots.** A `BotPlayer` plays through commands, for scenario tests and solo playtests.

## Planned sim folders

`Round/`, `Perception/`, `Speech/`, `Work/`, `Adventurers/`, `Errands/`, `Director/`, `Secrets/`
and `Reagents/`, all in `src/SupportingCast.Sim`, driven only by commands and headless-testable.

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
| Mini-games, diagnosis (to copy in M1 and M2) | Lifeline `Work/`, `People/Assessment.cs` |
