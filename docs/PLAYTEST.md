# The tuning playtest

How to run the playtest in [ROADMAP.md](ROADMAP.md) ("Now, 2"): what to check alone first, what to
try with friends, and what to bring back. The goal is a round that holds up over the internet, and
numbers that have had a pass.

## Before

- [ ] **The build:** cut a playtest build for itch, and make sure everyone runs the same one (the co-op
  handshake refuses a mismatch).
- [ ] **The relay:** check it's current. Redeploy with
  `external/narsil/deploy/relay/publish.sh ubuntu@129.153.122.49 linux-arm64`.
- [ ] **Who:** a host at home, a joiner on another network (a phone hotspot will do), and a Steam Deck.
  The Deck through the relay on the same network already works.
- [ ] **The baseline:** run `dotnet run --project src/HeroesNotIncluded -c Release -- --season-sweep`
  and keep its output. It's how bot-played seasons come out with today's `season.json`, the "before"
  for the season pass.
- [ ] **Logs:** Settings, "Playtest logs", is on by default. Each round leaves a folder under the logs
  folder's `rounds/`.

## Alone first

Each one is a command away. Press the note key (F11) whenever something's worth remembering.

- [ ] **Laying on hands:** `--practice Healer --lesson wound`. Does reading by the pulse teach the
  kinds? Is the flinch fair?
- [ ] **The other healer games:** `--practice Healer --lesson fever`, `many`, `festering`, `shrine`,
  `meditate`. The bandage band, the weave's beat window, the labyrinth's size.
- [ ] **A Veteran:** `--arena Veteran` (`--weapon IronSpear` to change arms). Fair to a player? (About
  6% for a skill-6 villager with a sword.)
- [ ] **The dragon in the ring:** `--arena Dragon`. Can a player hurt it and live?
- [ ] **The dragon in a round:** `--dragon-test`. What water does against it; the town hiding; whether
  it leaves (it comes at the deal, before any party's in town).
- [ ] **Bracing the bridge:** `--bridge-test`. The bracing window, as the tinker with wood in hand.

### The lessons

`--practice TRADE --lesson ID` (ids from `data/defs/trades.json`). Check the words, and whether the
step shown is the one you're on.

- **Innkeeper**
  - [ ] `ale`
  - [ ] `mixed`
  - [ ] `quest`
- **Cook**
  - [ ] `stew`
  - [ ] `roast`
  - [ ] `bread`
- **Blacksmith**
  - [ ] `sharpen`
  - [ ] `mend`
  - [ ] `forge`
  - [ ] `smelt`
- **Healer**
  - [ ] `wound`
  - [ ] `fever`
  - [ ] `many`
  - [ ] `festering`
  - [ ] `shrine`
  - [ ] `meditate`
- **Tinker**
  - [ ] `box`
  - [ ] `curio`
  - [ ] `lock`
  - [ ] `crossbow`

## With friends

A whole round, from the deal to the reveal, everyone from their own home.

- [ ] **The link.** On a client, watch the Players window (turns queued, jitter, ran dry). Mid-round:
  - [ ] someone leaves and joins again;
  - [ ] the host pulls their network for a few seconds (the session should carry on, not split).
- [ ] **The healer:** if nobody plays the empath, watch what the AI one does (meditating, charms on show
  and their price).
- [ ] **Heroes dying:** about one in two raids with a party in town costs a hero. Watch what that does to
  Renown.
- [ ] **The dragon with no party in town:** can the players drive it off? If the season hasn't brought it
  by the last day, the host sends it: the Debug window, Dragon, Send crisis.
- [ ] **The numbers:** keep an eye on how the season feels against ROADMAP's "The numbers": the Renown
  target, Dread, raid size and warning, night speed and needs, the festival, death and ghosts.
- [ ] **Notes:** everyone presses **F11** when something feels off, too hard, too easy, broken, or
  good. It takes a screenshot and asks for a line. On the Deck, bind it to a back button in Steam Input,
  or rebind it in Settings, Controls.

## After

- [ ] **Everyone sends their folder.** The reveal has "Open playtest folder". Zip it and send it.
  The folder holds:
  - `summary.md` (and `summary.json`): outcome, Renown and Dread hour by hour, the crises and
    whether they were met, the festival, everyone's fate;
  - `messages.log`, `crises.log`: the round as it happened;
  - `net.log`: the link each game hour, on a co-op peer;
  - `notes.md` and `shot-NNN.png`: the notes.
  The session's log file (named in the summary) has the `net: snapshot … KB` lines.
- [ ] **Read them** against ROADMAP's "Now, 2". Join stalls point to chunking snapshots; a season out of
  shape points to `season.json`.
- [ ] **The season pass:** change `season.json`, run `--season-sweep` again, and compare with the
  baseline. Put the before and after numbers in HISTORY.md.
- [ ] Then the mini-game timings, trade by trade (ROADMAP Backlog, "Tuning the mini-games").
