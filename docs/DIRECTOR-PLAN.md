# A Director that reacts: options, and a first step

_Saved 2026-11-17 for later; not started._

## Context

The `Director` ([Director.cs](../src/HeroesNotIncluded.Sim/Director/Director.cs)) is a **fixed script with dice**.
At the start of the round it rolls each beat in [season.json](../data/defs/season.json) (one raid on night 1,
one raid/fire/shortage/plague on day 2, the dragon on day 3), a time in its window and a kind.
The only things it reacts to are:
- **Dread**: higher Dread pulls each beat earlier and makes it up to 1.5× stronger.
- **Heroes present**: a beat waits in its window for a party to be in town.

It never looks at what the players are doing, which trades are filled, who's hurt, whether the
town is coasting or drowning, or how the quests turned out. Fights are bigger when the town is losing,
which piles on, the opposite of a good DM. Nothing happens between the three beats except the
systems' own rhythms. The user wants a "dungeon master" that reacts, adjusts difficulty as it goes,
and lets unusual player plans happen.

## What's realistic: three layers, each built on the one before

**1. A drama manager (rule-based, deterministic).** This is the proven approach (the Left 4 Dead AI Director,
RimWorld's storytellers): the director keeps a **tension reading** built from sim state, and decides
*when* and *what* from it, not only from the dice.
- It waits for a lull and doesn't stack trouble on a town that's still bleeding. When the town is
  coasting it pushes harder; when it's drowning it eases off. This replaces Dread's straight scaling.
- It picks a kind the players can answer and haven't seen yet: no second fire if the first went badly
  and nobody's on buckets; plague means more when a player is the empath.
- Pace is set against an expected Renown curve (the season sweep gives the baseline), not a single
  target at the end.
- Fits every rule already in place: determinism, replays, saves, `--season-sweep` for tuning.

**2. An incident menu (content in defs).** The DM's "verbs": small, data-defined happenings between
the big beats, each with **preconditions**, a **target** (often one player's trade), and
**consequences**. Most can be built from systems that already exist:
- a traveller with an odd request (Favours board)
- rats at the larder (`Hospitality/Rats`)
- a fever brought in (`FeverSystem`)
- a wagon wrecked on the road (`Wagons`)
- a fight breaking out at the inn
- a curio someone's desperate to sell (`Curio`)
- a small fire at one forge
- and **quest outcome → consequence** (ROADMAP's "dwarves wake the dragon" needs exactly this link)

Layer 1 picks from this menu, and so would an LLM. This layer is where "spicier" actually comes from.

**3. An LLM "DM" (optional, host-side, experimental).** This *can* work, within limits:
- **How it fits determinism:** the model never runs inside the sim. It's a host-side advisor that, every game
  hour or so, reads a compact summary (tension, who's doing what, quests, what's been said aloud near
  NPCs) and answers with a **choice from the incident menu plus parameters plus flavour text**. That
  choice goes in as an `IHostCommand` (e.g. `DirectorChoice`), checked by `CommandRules`, recorded in
  the replay, and applied the same way on every peer. So the model is just another player issuing commands.
- **Latency is fine:** a game hour is about 40 real seconds, and a few seconds' delay doesn't matter for pacing decisions.
- **What it's good at:** choosing *which* thing fits the story so far, and writing it in the brand's voice
  (the shepherd's warning, a stranger's request, the festival tale). Hand-written text gets repetitive fast, and this is where a model helps most.
- **"Let the player do unique things":** this is the hard part. The model can only **combine verbs the sim already
  has**: post a quest, send a traveller, set a price, grant a favour, move a crisis. If a player
  says aloud to an NPC "we'll pay the bandits off", it could turn that into an existing verb (call the raid
  off for silver, made into a Favour). Anything truly new still needs a system written for it. The bigger and
  more composable the incident menu (layer 2), the more of this the DM can do.
- **Costs:** needs an API key (the host's, or a service you run), some cost per round, and it must fall
  back to layer 1 when offline or when the model fails. Solo and bench runs always use layer 1.

**Recommendation:** build layers 1 and 2 behind an interface, so a later LLM experiment only swaps the part that
decides. Layers 1 and 2 are worth having regardless, and an LLM is useless without them.

## First step (what this plan would implement)

1. **`Director/Tension.cs`**: a deterministic reading, refreshed every 10 game minutes, covering:
   - harm: recent deaths, downed, open injuries, burning cells, breaches
   - load: open orders and lines, open quests
   - pace: Renown now vs the expected curve for this hour, and Dread as a share of the limit
   - calm: hours since the last crisis ended
   Read through the sim's own state, never `Map.Light` or perception.
2. **`IDirectorMind`** with `RuleMind` as the default. Each beat asks it to (a) fire now or wait
   (within the window; it must still fire by `toHour`), (b) pick a kind from the beat's weights,
   re-weighted by tension and which trades players hold, (c) pick a strength. Change only the
   decision parts of `Director.cs` (`PickKind`, `StrengthOf`, `Due`), not how crises are sent.
3. **Turn Dread's role around:** high harm means smaller and later; low harm with Renown ahead of the curve
   means sooner and stronger, within a `[min,max]` strength band in `season.json`.
4. **Quest → crisis link:** an optional `followsQuest` on a beat in `season.json` (the Reedmere dragon).
5. **Incidents (small):** an `incidents.json` + `IncidentSystem` with 3–4 incidents built from what exists
   (a fever brought in, rats, a wrecked wagon, a traveller's favour). The mind can call one in a lull.
6. **Save codec** for the new state (format bump), and a **`DirectorChoice` host command** stubbed only as
   the seam for a later LLM mind (not wired to any model in this step).

Files: `Director/Director.cs`, new `Director/Tension.cs`, `Director/Mind.cs`, `Director/Incidents.cs`,
`Defs/SeasonDefs.cs`, `data/defs/season.json`, new `data/defs/incidents.json`,
`Saving` (Director's `Save`/`Load`), `DirectorTests`.

## Verification

- `dotnet test -c Release`, plus new `DirectorTests`:
  - a beat waits while harm is high and still fires by `toHour`
  - a coasting town gets a stronger beat
  - the quest-linked beat follows its quest
- `--season-sweep --seeds 8` before and after: compare the Renown/Dread spread. We want fewer runaway losses
  and fewer stomps, with the means about where they were.
- `--bench --seed 1234` / `99`: hashes will change (this isn't a refactor); no allocation or cost spike.
- A solo round (`--new-game --bots 3`): read the message log for when beats land relative to the fights.
