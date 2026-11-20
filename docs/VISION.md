# Vision

Where Heroes Not Included might go, long after the roadmap. None of this is planned or scheduled.
The Next Fest demo ([NEXT-FEST.md](NEXT-FEST.md)) and the roadmap ([ROADMAP.md](ROADMAP.md)) come
first. These are directions worth keeping in mind so today's choices don't close them off.

What runs through all of them is what makes the game now: **hidden quality in someone's work, paid
off in someone else's story.** Each idea is worth it to the extent it puts a real person on the
receiving end of that.

Nearest first.

## Players as the adventurers

Some players are dealt the party: questing, fighting, an RPG's progression, with a flavour and a
world grown out of this one. The interesting part isn't the dungeon. It's coming back to a town run
by people, and swapping stories with other parties at the inn.

**What we have toward it.** Adventurers are already characters with their own brain
(`AdventurerBrain`), parties (`PartySystem`), quests with hidden truth, and a return. Each quest
keeps a `QuestTale` of whose work went with the party. Combat is real time, and there's a practice
ring (`--arena`). DragonWorld is where the fighting started.

**Why it's the strongest idea.** The trades' hidden work becomes something a human feels: the
warped arrow nobody saw, the contraption whose binding jams, the loose ration tie, the trap the
tinker missed. Today those land in a resolver and the reveal. With player adventurers they land
mid-fight, and the party comes back to the inn to complain to the smith, or to toast them at the
festival.

**The hard parts.**
- Time scale. A town day is 16 real minutes, and quests happen off the map. A played quest needs a
  map or instance of its own that runs while the town carries on, then a return.
- Two audiences. Fighters want pace and loot, tradespeople want depth and time. Making both fun in
  one session is a design problem, not a technical one.

**First step.** A mode (`IGameMode`), not a second game: one to four players dealt as the party, a
quest played out on a dungeon map, the rest the town. If that's fun, it's the expansion.

## A world in 3D, work done in it

A full 3D world with simple models and a look of its own (cel shading and bold outlines), and no
mini-game panels: the work happens in the world, at the anvil and the pot, where everyone can see
it. More immersive, and it reads better on a stream.

**What we have toward it.** The world is already a 3D cell grid with levels, drawn in Vulkan, with
models on defs and a free camera. A cel-shading pass is a shader and an art direction, not a new
engine. And because `WorkSession.Advance` is pure and kept apart from its view, a trade's panel can
be replaced by a view drawn in the world without touching the sim.

**The hard parts.**
- Animation: characters visibly doing the work is where the cost is, and a simple, stylized look is
  how a small team keeps it affordable.
- The mini-games are each trade's feel and skill ceiling. Carry that into the world; don't flatten it
  into holding a key.

**First step.** A cel-shading test, which is cheap and good for marketing either way. Then one
trade's work drawn in the world (the smith's piece glowing on the actual anvil), then the next.

## A cyberpunk total conversion

The same game in a neon city with magic in it: street docs, deckers, fixers, a bar owner keeping the
crews fed, a corporate plant as the traitor. The trades map over almost one to one: the empath to a
street doc, the tinker's lockwork to a decker's security programs, the smith to a gunsmith or
cyberware fitter, the labourer to a courier.

**What we have toward it.** The core is meant to be the first mod (docs/MODDING.md): trades, maps,
modes, art and sound roots all come through mods. A full reskin is the best test of whether that's
true.

**The hard parts.** Art and writing, not code. And no borrowed setting: our own world, not a
well-known tabletop one's names or its exact mix of magic and cyberware.

**First step.** Once the mod system can carry a whole town on its own, a single trade and a map as
a total-conversion mod.

## Persistent towns and rival cities

The furthest: a whole city, or rival cities, running on players' industry, where the PvP is
economic, espionage and subterfuge, and fighting is forbidden.

**Why not an MMO.** Co-op is deterministic lockstep: every peer runs the sim and the host seals the
turns. That's right for a handful of players and can't stretch to hundreds. An MMO would need a
server-authoritative rewrite of networking and saving, which is a different engine. And the game
is built on rounds: the season, the hidden traitor, the reveal and the festival all need an ending.
A world that never ends loses the reveal. An economy with spies is also a live service, with
balancing and moderation to match.

**What we have toward it.** Players inhabit AI villagers, and a player who leaves becomes one. A
city that's mostly AI, with players dropping in, is the architecture we already have.

**The realistic version.**
- Persistent towns: a dedicated server keeps a town's buildings, debts, renown and grudges from
  season to season, and each season still ends in its reveal.
- Rival towns on a trade road: two or three towns, each its own session, exchanging caravans, spies
  and favours asynchronously rather than in shared real time.

Together with player adventurers, that's most of the dream.

## Order

A suggestion, not a commitment:

1. The Next Fest demo.
2. A cel-shading test.
3. Player adventurers, as a mode.
4. Persistent towns, linked by a trade road.
5. Work done in the world, one trade at a time, ongoing.
6. The cyberpunk conversion, whenever the mod system can carry it.
