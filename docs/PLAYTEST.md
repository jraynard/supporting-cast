# The tuning playtest

How to run the playtest in [ROADMAP.md](ROADMAP.md) ("Now, 2"): what to check alone first, what to
try with friends, and what to bring back. The goal is a round that holds up over the internet, and
numbers that have had a pass.

## Before

- [x] **The build:** cut a playtest build for itch, and make sure everyone runs the same one (the co-op
  handshake refuses a mismatch).
- [x] **The relay:** check it's current. Redeploy with
  `external/narsil/deploy/relay/publish.sh ubuntu@129.153.122.49 linux-arm64`.
- [ ] **Who:** a host at home, a joiner on another network (a phone hotspot will do), and a Steam Deck.
  The Deck through the relay on the same network already works.
- [x] **The baseline:** run `dotnet run --project src/HeroesNotIncluded -c Release -- --season-sweep`
  and keep its output. It's how bot-played seasons come out with today's `season.json`, the "before"
  for the season pass.
- [x] **Logs:** Settings, "Playtest logs", is on by default. Each round leaves a folder under the logs
  folder's `rounds/`.

## Alone first

Each one is a command away. Press the note key (F11) whenever something's worth remembering.

- [x] **Laying on hands:** `--practice Empath --lesson wound`. Does reading by the pulse teach the
  kinds? Is the flinch fair?
- [x] **The other empath games:** `--practice Empath --lesson many`, `festering`, `shrine`. The bandage
  band, the weave's beat window, the labyrinth's size.
- [x] **A Veteran:** `--arena Veteran` (`--weapon IronSpear` to change arms). Fair to a player? (About
  6% for a skill-6 villager with a sword.)
- [x] **The dragon in the ring:** `--arena Dragon`. Can a player hurt it and live?
- [x] **The dragon in a round:** `--dragon-test`. What water does against it; the town hiding; whether
  it leaves (it comes at the deal, before any party's in town).
- [x] **Bracing the bridge:** `--bridge-test`. The bracing window, as the tinker with wood in hand.

### The lessons

The whole run: `--practice TRADE` (the intro, the basics, then the core chapters; a character
that's done the basics skips them). One chapter: `--practice TRADE --lesson ID` (ids from
`data/defs/trades.json`, `basics` too). Check the words, whether the step shown is the one you're on,
that the arrow and the glowed button are the right ones, and the "worth knowing" notes at the end.
Sit on a step for 45 seconds: the fuller hint and "Set it up again" should come up.

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
- **Empath**
  - [ ] `wound`
  - [ ] `many`
  - [ ] `festering`
  - [ ] `shrine`
- **Tinker**
  - [ ] `box`
  - [ ] `curio`
  - [ ] `lock`
  - [ ] `crossbow`
- **Carpenter**
  - [ ] `saw`
  - [ ] `quiver`
  - [ ] `bow`
  - [ ] `woods`
  - [ ] `firewood`
- **Musician**
  - [ ] `scale`
  - [ ] `tune`
  - [ ] `strings`
  - [ ] `busk`

## With friends

A whole round, from the deal to the reveal, everyone from their own home.

### This round, try (2026-11-21)

New since the last round with friends. The miner is off for this one.

- [ ] **The musician** (only if someone plays it): wander in by the road, play at the inn of an evening,
  tunes from the book, the hat and requests, tuning the lute. Do others hear it from where they stand, and
  does a listener's dance keep the beat? Is the hat's take worth an evening?
- [ ] **Work for two, with a real partner.** Call Help! from the quick-say wheel and have a player come,
  then F at the work (or at the one asking):
  - [ ] the smith's greatsword, a player on the sledge (watch for "Here it comes!", then "Now!");
  - [ ] a Calling back, a player holding the body (the pull rising, "Hold them down!");
  - [ ] a great log at the woodshop, a player in the pit;
  - [ ] a wagon's axle, a player lifting.

  For each: could the helper tell what the lead was doing, and could the lead tell they were being
  helped? Over the internet, did the timing still feel fair?
- [ ] **Materials:** watch the smithy's charcoal (the kiln), planks at the woodshop, and the shrine's
  cloth and bandages in a raid. Note when any runs out, and for how long.
- [ ] **Bronze at the smithy:** a piece asked for in bronze, made from the chest's bars or smelted from
  copper and tin.
- [ ] **The festival:** shorter, a brisker tale; cheer each honour as well as each toast.
- [ ] **A late joiner** takes a trade nobody's playing, the musician included.

- [ ] **The link.** On a client, watch the Players window (turns queued, jitter, ran dry). Mid-round:
  - [ ] someone leaves and joins again;
  - [ ] the host pulls their network for a few seconds (the session should carry on, not split).
- [ ] **The empath:** if nobody plays the empath, watch what the AI one does (meditating, charms on show
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
