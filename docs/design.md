# DragonWorld (working title: Embersong Vale)

The original design notes and idea backlog. What's actually built, and what's next, is in
[ROADMAP.md](ROADMAP.md); this file is the direction and the ideas that haven't been picked up yet.

Isometric view a la Timberborn (done). Dragons fly, and breath weapons have volume, like a dragon breathing fire down into a forest and splashing out everywhere.

Earlier name ideas: Dragonborn, Scalesong Banner, Ember Kin

## Synopsis

- The player directly controls one character they create (name, body type, hair, colours). They start at a low rank in an existing village; a story beat forces them out with a single dragon egg and a wagon of supplies, and protecting that egg becomes their goal. Others join them over time: companions act on their own and take direction, but the player only directly controls their own character (and, once the bond is high enough, their dragon).
- Gameplay target: a blend of Stardew Valley (direct WASD control, hold F to chop/mine/craft, a personal character) and RimWorld (autonomous companions, emergent systems, village building).
- their goal is to hatch the dragon(s) and start helping the dragons thrive, while taking care of the human's needs as well. 
- the dragons are intelligent creatures that have their own wants and needs. 
- once a human raises their bond with a dragon high enough they can also potentially ride them or command them in battle.

## General Overview

- Inspired by games like RimWorld and Dwarf Fortress, with heavily detailed systems interacting with each other and causing emergent gameplay and story telling. 
- Graphics will be simple and hand drawn (eventually, placeholder art is fine for now). 
- there should also be a story narrative to push the player forward though, perhaps with branching choices / options depending on the type of place the player wants to build.

## Mechanics

- Heat will be very important to model correctly when dealing with dragons, along with possibly electricity and atmospheric effects (e.g. poison cloud). 
- not sure if i want to do a full atmopsheric simulation (a la Oxygen Not Included or other games) or not yet. 
- social behavior ai will be important to focus on as well to make believable reactions and behaviors. 
- villagers and dragons should have random or inherited traits that make them different from each other. 
- multiplayer: lockstep co-op, one avatar per player (in alpha now; the deterministic sim made it possible)
- fire and electricity simulation - as the first dragon types these will be important. fire especially should be modelled accurately (but in a fun way of course). 
    i'm thinking similar to the way timberborn puts a strong emphasis on fluid mechanics which makes sense as a game about beavers, a game about dragons should have a strong emphasis on their breath weapons and the effects they cause.

## Systems

- social system - moods, relationships, feelings, etc
- health system - injuries needing both short and long term care, lasting scars and consequences of injuries
- combat system - i want to do a turn based combat system rather than the real time one games like rimworld use, with more control over each individual action.
- storyteller / crisis system - ala the storyteller in rimworld, randomized events that throw a wrench in things or provide a lucky boon when its needed
- bigger world - relationships with other towns / societies, both human and dragon, that can cause friction or aid, and give a sense of things happening beyond your town
- endgame? either starting a new town and building up an empire, or something to do with the narrative that requires shifting focus - not sure yet.
- bonding system - flesh this out a bit and make options for progressing it faster
- different types of dragons and their eggs have different requirements, start with two dragon types (maybe fire / electric ?) and build out from there

## further roadmap ideas

- further UX refinement if needed
- add second dragon type
- trading / economy
- (done since: inventory, crafting, co-op multiplayer in alpha)

## Crafting

- place blueprints for buildings / walls / furniture (similar to existing - but do we keep custom buildings, or have pre-set buildings of specific purpose with custom models?)
- building the blueprint requires refined materials in most cases, not just raw logs and chunks of stone. can build basic walls still with raw materials but they will not be as good at keeping heat in or as strong.
- materials can be refined at specific buildings / furniture - carpentry / masonry tools might be required for higher level crafts
- player crafts from a recipe book that starts with basic stuff and gets filled in with known recipes as they level up their craftings skills (maybe add books / trading for knowledge but that might be further down the road)

## Ideas

### Adventure mode backlog (formerly Bearer mode) (agreed direction, 2026-11)

- **Rank progression:** rise from egg-bearer to village head to ruler of a larger region as the town grows (long-term).
- **The egg remembers:** its incubation history (temperature swings, being carried, near-deaths, the bearer's attention) shapes the hatchling's traits and starting affinity.
- **Pursuers with memory:** the village or order you fled keeps a nemesis faction that sends trackers, bounty hunters or an old friend, scaled to the dragon's growth.
- **Nomad phase:** nightly camps (campfire for the egg, watch rotation) before choosing where to settle; founding a second settlement as an endgame.
- **Old ties:** people from the prologue village remember how you treated them; some defect to you, some sell you out.
- **Legacy, not game over:** if your character dies, continue as the companion with the strongest bond, or as the dragon.
- **Bond-sight:** at high affinity, see through or briefly steer the dragon, leading up to playing as it.
- **Asymmetric co-op:** one player as the dragon, one as the rider (`RidingSystem` already links them as two units).
- **Fire as a road tool:** burn the bridge or grove behind you to lose pursuers.
- **Multiplayer:** lockstep co-op (built: every player action goes through the sim command queue, the host sets the speed). Still needs deterministic trig.
- **Steward mode:** keep the current overseer controls as a sandbox or late-game mode so the RimWorld side isn't lost.



### Older notes

- Adventure mdoe wheere you control a single character ala dwarf fortress - or should we make this the main focus to help differentiate a bit too?
- When controlling, you could start as a human, but then choose to switch to being a dragon at some point
- fantasy first setting, lets have elves, dwarves, orcs, etc as well as humans.
- also thinking of a possible steampunk vibe with the possibility of different dragons providing inventions and advancements for human tech or something... 
    maybe this is the evil faction, and they exploit the dragons instead of living in harmony with them