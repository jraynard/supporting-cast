# Supporting Cast: design

Working title. Living document: the decisions so far and the intended depth of each system.
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

- 30 to 40 minutes at fixed speed with no pause: 3 to 4 in-game days at 10 real minutes a day.
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
Players ask each other for things as **favours**, for silver they name.

## Trades

Called `Trade` in code, because `Ai/Job` is already the AI coroutine.

| Trade | The game it plays | Built from | Betrayal it enables |
| --- | --- | --- | --- |
| Innkeeper | Order juggling, drink mixing, hearing gossip; matching parties to quests on partial intel | Larder, cooking, Social, `Expeditions/` | Spiked drinks, selling secrets, sending a party to its death |
| Blacksmith | Heat, hammer and quench timing | Forge, bellows, tiers, `Crafted` quality | A hidden flaw in a blade |
| Healer | Take a patient's wounds into yourself with focus, cast the right mending on yourself, meditate back your mana | Health, `Work/`, new mana | Taking only part of a wound, or handing one back |
| Constable | Patrol, evidence, arrests, jail | `Prisoners`, locks | Framing, abuse of authority |
| Apothecary | Discover reagent reactions, reshuffled each round | New `Reagents` | Poisons, explosives |
| Merchant | Haggling, appraisal, spotting cursed loot | Inventory, `HoardValue` | Fencing, forged goods |
| Undertaker | Autopsy, burial before the dead rise | Health, `Scars`, carcasses, the pyre | Destroying evidence |
| Forager, Miner | Risky trips beyond the walls | Harvest, Mining, Hunting, Fishing | Alone out there, in both directions |
| Carpenter | Build, repair, fit locks; pick the locks and disarm the traps on chests adventurers bring back | Construction, Rooms, doors, locks, `Work/`, expedition loot | A spare key, a chest opened early, a trap left armed |
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
  - **The smithy:** an anvil, a quench tub, a grindstone, bellows, a tool rack and a fuel bin.
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

**Innkeeper.**
- M2 pours ale at the cask. Hold to pour; the tilt controls the head. Customers taste the quality.
- cooking: cooking meals for customers
- rooms: renting out rooms
- cleaning up: cleaning up tables, the bar, the floor, doing dishes
- Later: brewing, and refilling casks from kegs.
- Mixed drinks: a press-on-cue sequence. maybe allow for cusomt concotions that can have different effects.
- Brokering quests (since M2, when the Quest Broker folded into the Innkeeper): the keeper hears
  every rumour over the bar and points parties at work on whatever it's worth.

**Healer (empathic).** Magical in flavour rather than clinical, but grounded in the health
model: wounds sit on body parts, have a kind, and bleed, hurt and fester.
- *Laying on hands.* Touching a patient, the healer feels their wounds, including the ones
  that sight misses, like an infection under a dressing. That touch is the diagnosis. They draw
  a wound across, holding their composure against its pain; lose it and the link snaps, with a
  backlash. The wound lands on the same part of the healer, lighter the more skilled they are.
  An infection comes with a fully drawn wound.
- *Carrying it.* What the healer takes on is real. It bleeds and hurts, and a healer who takes
  too much collapses.
- *Mending.* They cast the mending that fits each wound they've taken. One heals cuts and bites,
  another bruising, and so on, each cast by playing its runes on cue. Absorbed wounds read as
  sensations, not labels: a clean bright line of pain, a deep swelling throb, skin tight and
  stinging. Telling which mending is needed is a skill the player learns. The wrong one costs
  its mana and heals nothing.
- *Meditating.* Out of mana, they meditate it back with a breathing rhythm. Pain they still
  carry makes the rhythm stutter and flinch, so a healer who couldn't mend themselves fully
  struggles to recover.
- *The shrine.* The healer works from a shrine: pallets where the wounded come to lie, a
  meditation stone that steadies the breath against pain and gathers more mana, and a chest of
  bandages and other supplies for when the mana runs dry. Later, magical supplies can live there
  too.
- Betrayal later (M4): drawing only part of a wound, or quietly pushing one back.
- Maybe we should make the spell casting more freeform, so you input a set of runes and that decides what spell is cast (kinda like Magicka, i'm sure there are other examples too). they can look at their spellbook to see the correct runes for which spell they want to cast along with any description of what it does.

**Carpenter's locksmithing.** Adventurers come back with locked and trapped chests. Picking the
lock and finding and disarming the traps are each a hands-on game. They're the Carpenter's for
now; if the trade gets too crowded, the lock work splits off into a Locksmith trade of its own.
- Since M2: a party that clears a quest brings back a strongbox holding part of the reward, and
  asks at the woodshop to have it opened. The harder the place, the more pins and the likelier a
  trap (a poisoned needle, a spring blade, burning powder).
- *Feeling for the trap.* Run a probe along the lid's seam, light. The feel twitches as it goes
  and catches hard near a trigger. Dragging weight across a trigger sets it off; stopped over
  one, press in and hold the pressure in the band to ease the spring out, too hard and it fires.
  Reaching the hinge ends the search, so finishing never tells you whether there was one.
- *Picking the lock.* Lift each pin until it gives most and set it there. A slip drops a pin,
  wears the picks and can jar an armed trap; enough slips snap the pick. Opening a box with its
  trap still armed sets it off. A sprung trap wounds the hand, which is work for the healer.
- Betrayal later (M4): a spare key, a box opened early and some silver missing, a trap left armed.

## Pressure and traitors

A `Director` grown from `Storyteller` always runs: raids, fire, shortages, plague, and a late-round
dragon that burns the town. Traitors are a round option layered on top:

- **Honest:** none, and stronger crises.
- **Whispers:** one or two.
- **Unknown:** nobody is told whether there are any.

A traitor serves the Thing Below. They get secret objectives and buy tools from a hidden shop (the
Whisper) with favour earned by completing them. Everything leaves evidence: maker's marks, residue,
wounds, witnesses. A limited town bell calls a meeting to accuse, vote and exile.

**Death.** Ghosts watch and can be revived. They can only re-enter the round by being revived somehow,
or perhaps by the traitor if they are a necromancer. they can come back in the next round as a new character.

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
- **Hand-made villages.** A round is played on an authored map (`MapDef`, starting with
  Millbrook), not a generated valley. The town's layout is part of the social game: who can see the
  forge from the inn, which lane the parties come in by, and where a body would go unnoticed. A
  map is data with exact building anchors. The seed still varies what's underground, what grows,
  and who lives there.

## Sim folders

Built, all in `src/SupportingCast.Sim`, driven only by commands and headless-testable:
`Round/`, `Perception/`, `Speech/`, `Work/`, `Adventurers/`, `Shops/`, `Favours/`, `Healing/`, `Expeditions/`.
Still to come: `Director/`, `Secrets/`, `Reagents/`.

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
