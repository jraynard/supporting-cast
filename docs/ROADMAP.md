# Roadmap

What's next, in order. Design intent is in [DESIGN.md](DESIGN.md); everything built so far, slice
by slice, is in [HISTORY.md](HISTORY.md). When something's done, move it to HISTORY.md and trim it
here. Ideas for long after this roadmap are in [VISION.md](VISION.md).

## Where we are

- **M0 to M2:** forked from DragonWorld; being someone (perception, speech, trades as games of
  their own); the first party (adventurers, shops and the line, quests with hidden truth).
- **Millbrook**, the hand-made village; UX and control passes; the blacksmith, innkeeper, cook,
  empath and tinker each played through, with lessons in the practice yard.
- **M3, a whole season:** the reckoning (Renown and Dread), nights and suppers, the Director's
  crises (raid, the bridge, fire, shortage, fever, the dragon), the festival and the notice board.
  Bots play a round from the deal to the reveal (`SeasonScenarioTests`).
- **Since M3:** the Steam Deck and control passes, free-movement combat, death and ghosts, the
  cook pass, the festival as an event, internet play readied, playtest builds on itch.

**The M3 gate, "is serving heroes fun with friends, without traitors?", is a tentative yes.**
Playtests say it's becoming quite fun, and rounds without traitors are a real mode for players who
want no PvP, not just a stepping stone. What's left before M4 is making that mode solid over the
internet, with the weakest trade brought up and the numbers tuned.

## Now: the Next Fest demo

Until 1 Mar 2027 the plan is [NEXT-FEST.md](NEXT-FEST.md): a demo in Steam Next Fest (22 Feb to
1 Mar). It's one shortened Millbrook season with six trades, solo-first with bots, co-op through
Steam invites, and no traitors. The store page goes public by mid-December, registration closes
10 Jan, and the demo goes in for review by 25 Jan. Below, **(demo)** marks the items the demo
needs. The rest waits until after the fest unless there's time.

## Now: the co-op round, solid

What outside testers have shown so far (playtests 18 to 22): **getting in** was rough from an
itch.io link but smooth since testers were given the app and a key, so the trouble was getting
the build, not the netcode. **The trades confused people**: what their work is and what to do
first. Visual issues and requests came along the way. So the order is: newcomers first, then the
numbers. Tuning a season with players who don't yet know their work only measures the confusion.

### 1. The first ten minutes
The tutorial (playtest.20), F to take an order and the "waiting" tags (playtest.22) are the answer
so far. Nobody new has tried them yet.
- [ ] **(demo) The tutorial, played by someone new:** one trade at a time, the whole run (`--practice
  TRADE`): the words, the step shown, the arrow and the glowed button, the stuck hint
  (PLAYTEST.md, "The lessons"). Check it alone first, then watch a non-gamer (and a child, if
  one's handy) play their first ten minutes without help.
- [ ] **(demo) The tutorial's gaps:** handing over a drink, a dish or a box from the order (as "Take it
  back" does for a mend), and a lesson for commissions off the notice board (the maker's pointer to the asker comes
  with taking it, `VillageGame.Errands`).
- [ ] **(demo) For you to decide, now: a first-round hint.** The tutorial is optional, so anyone who skips it
  starts a round confused. Either a one-line "your work: ..." on the trade's card until that trade
  has a finished round (`CharacterProgress`), or a nudge on the trade picker toward the tutorial
  for a trade never played. The first goes against "no in-round tips" (CLAUDE.md).
- [ ] Maybe mark a trade or two on the picker as a good first one (the smith's loop is the plainest).
- **Done:** the labourer is gone. The musician is what a player gets when every trade's taken (any
  number may play, `TradeDef.Unlimited`), and the lobby says so; the town keeps one villager with no trade,
  a hand when called. No villager hauls, runs errands or the hamper over, and the AI takes only from its
  own workplace and the stockpile (`Simulation.AiMayTake`), so a new trade's chest is safe by default.
- **Done:** supplies ordered from the carter (`Shops/CarterOrders`, `supplies.json`): the tradesman orders at
  the shop window (From the carter), paid in silver, in their store next morning; standing orders keep an
  AI-held shop going, and bots order by them. Replaces the fixed daily drop at the smithy, woodshop, shrine
  and inn. The basics teach it (a "carter" step).
- [ ] **Tune the carter's prices** (`supplies.json`) in a playtest: is a day's ordering a fair bite of a
  day's takings? Does anyone run short, or drown in overstock?
- [x] **The carpenter, played by someone:** `--practice Carpenter`. Do the saw and the fletching
  read? A first playtest found it quiet (no orders at all), so it now has them: worn bows, cudgels
  and staves mended, archers asking for arrows, a wagon's woodwork (the tinker keeps the wheel), and
  furniture the fire took set back. Is it busy enough now, without being the smith twice? In the bots'
  sweep it takes about 3 orders a season to the smith's 4. Try the joinery (`JoinSession`): does the
  split read before it happens? No lessons for the new work yet (below).

### 2. From testers
Visual issues and requests from playtests, so they don't get lost between HISTORY.md and the Backlog.
- [x] **(demo) Flicker** at wall corners and on furniture, and the upper floors when walking past the inn.
- [x] **(demo) Asked-for pieces, by bots** (Deck playtest, Nov 2026): bot players now make a piece asked for by name
  and hand it over (HISTORY.md, 20 Nov). Still to do: AI keepers never stock a shelf, so nobody asks them.
- [ ] **For you to decide: asks a shop can't answer.** Adventurers ask for any piece a shop's stations make
  (`Goods.Wishes`), so the smith is asked for a longsword with no steel in town, and the empath for a mending it
  doesn't know. Fine as flavour, or noise? Wishes could be limited to what the town can make.
- [ ] **HUD layout: move, fold, choose** (Deck playtest, Nov 2026): testers want windows they can drag about and
  fold away like an OS's (the character block top left most of all), and to choose what it always shows (rest
  and food at a glance, not only when low; a setting, not more on screen). Every HUD panel is placed by Narsil's
  `Hud` each frame (`Hud.Flags`: `NoMove`, `NoSavedSettings`), so: an opt-in per panel that skips placing it once
  it's been dragged, its position and folded state kept in `Settings`, a fold button like the tutorial card's
  (`_tutorialFolded`), `_hud.Took()` reserving no room for a panel that's moved, and "Reset layout" in Settings.
  The character block's lines are in `VillageGame.Needs` (`DrawNeedsPanel`: `all = !SimpleHud || hovered`).
- [ ] **Combat feedback: a better hit stop** (brawl playtest, Nov 2026): a blow landing doesn't read strongly
  enough yet. Improve the hit stop (the freeze, the shake and the flash on impact) so you can feel each hit
  land, given or taken. Part of a combat refinement pass to plan separately.

### 3. The tuning playtest
**(demo)** The connection checks and a pass on the season's numbers, for the demo's shorter season.
Rounds with friends over the internet, to prove the round and tune its numbers, then a pass on
what they show, once players know their work (1). Play on **Millbrook**; Reedmere is untuned and
still being built (Later, "Reedmere, in progress"). How to run it: [PLAYTEST.md](PLAYTEST.md).
Each round leaves its numbers and the testers' notes (F11) under the logs folder's `rounds/`;
`--season-sweep` gives the bots' baseline. Mini-game timings come after, trade by trade (Backlog,
"Tuning the mini-games"). What's been answered so far is in HISTORY.md, "Solo playtest notes" and
"From two multiplayer rounds".

**Settled, leave alone**
- **The dragon:** in a multiplayer round, two or three players throwing water drove it off easily
  enough and it felt good. Left as it is, and so is the dragon with no party in town (the town
  alone almost never sees it off: it's meant for players). Villagers with buckets stays in the
  Backlog.
- **The Veteran** (`--arena Veteran`): fair, a little easy for a practised action-game player.
  Left unless friends say the same.
- **Join snapshots:** 54 KB, built in 3 to 48 ms, with no stall seen. No need to chunk them.

**Next round with friends.** Fixed after the last rounds and not played since:
- [ ] **Feasts:** does the cook see the call and lay one on in its 6 hours?
- [x] **The downed:** do players and the AI keep them alive now? AI first aid, bandaging and rescue
  now reach downed adventurers, and blows go over the downed. A downed empath weaves on themselves.
- [x] **Wood:** the barn should hold out and the fires stay lit. In round two the inn's fireplaces,
  the oven and the forge kept burning out.
- [x] **Truing a wheel** (new controls), **tinker games finishing in co-op**, and **a second party
  sent on a quest**.
- [x] **Inn rooms:** keys, the book and turning rooms round are new (HISTORY.md, "Inn rooms").
  Is letting a room busy in a good way next to the bar's line? Watch for rooms stuck unready, keys
  lost for good, and guests walking in on each other more than a careless keeper earns.
  Playtest feedback (rooms feast or famine, order buttons, keys, markers, the cleaning and mixing
  games, the keg, the upper floor) and its fixes are built: [HISTORY.md](HISTORY.md), "The innkeeper's playtest".
- [ ] **The rare companies** (HISTORY.md, "Reedmere, the town on the long lake"): do the dwarves
  and the long road's company feel like an event? Their chances (0.3 and 0.25 a round) and their
  errands' danger are first guesses.
- [ ] **Net notices:** if someone drops, it now shows up in `net.log`.

**The connection, in those same rounds.** Getting in is fine now; what's left is staying in. No
session of its own: watch for these in ordinary rounds. (The relay: `CoopRelay.Default`,
129.153.122.49:47050; redeploy with `external/narsil/deploy/relay/publish.sh ubuntu@129.153.122.49 linux-arm64`)
- [ ] A joiner on another network (a phone hotspot will do).
- [ ] Over a whole round, watch the Players window's buffer, jitter and starvations.
- [ ] Join mid-round.
- [ ] Pull the host's network for a few seconds: the session should carry on, not split.

**Also new since:** adventurers dying in town now count as lost (Renown and Dread, and the
reveal), feasts give the kitchen 6 hours, and there are six parties, each coming once before any
comes again (HISTORY.md, "From two multiplayer rounds"). In the bots' sweep, Renown rose with them
(mean 43 to 58 of 60 over 8 seeds, mostly feasts now laid on). Tune `renownTarget` against a round
with friends, not before.

**For you to decide: the numbers** (defaults not yet tuned in play)
- [ ] **The season** (`season.json`): the Renown target and Dread rates (Dread per death included),
  the day-two crisis weights (equal for now), raid size, the raid warning's lead, the town purse,
  the price of arms, the carter's load.
- [ ] **Heroes die often:** about one raid in two with a party in town costs one. Watch what that
  does to Renown, now that deaths in town cost some, before making raids gentler.
- [ ] **Night speed and the need rates.**
- [ ] **The empath in a round:** the AI empath's meditation (`MeditateJob.Gathers`) and the charms
  it puts out (`LayOnHands.CharmsOnShow`, price).
- [ ] **The festival** (`festival.json`, `season.json`): how long the tale and the revels run, the
  activity weights, what the show table and the games are worth to a guest, and whether the toasts
  swing a season enough to matter.
- [ ] **Death and ghosts:** the Calling's cost and windows (`Revival`), presence and sign costs
  (`Hauntings`), errand times, reaches, grace and Renown (`afterlife.json`); what a beast costs to
  ride (`Possession.DrainPerHour`, by mass) and whether a ridden boar is too strong a fighter; how
  hard the dead's unnerving bites (`Hauntings.Unnerved*`).

### 4. Alongside: Steam (now the demo's)
The Steamworks account, the **Coming soon** page, uploads from the release workflow, and Steam
invites and lobbies are all demo work now, dated in [NEXT-FEST.md](NEXT-FEST.md). Invites and lobbies
were left for Early Access prep before. A **Steam Playtest** is optional: testers can play the demo's
release candidate from a private branch instead, and Valve advises against running one during the
fest. Keep itch keys as the fallback until Steam builds are running. Datagram Relay stays with Early
Access prep.

*Exit: newcomers get through their first ten minutes, friends play a whole round from their own
homes, and the numbers have had a pass.*

### 5. Two pairs of hands: playtest them in co-op
The striker, the keeper, the pitman and the lifter are built (HISTORY.md, "Two pairs of hands, the
rest"). Each wants two players over the internet, one as the lead and one as the helper.
`--host --pairs-test` stages them all at once (`Scenarios/PairsDrill`): a greatsword's iron by the
forge, a great log by the sawhorse, a wagon with a cracked axle by the woodshop, and a villager
dead on a shrine pallet with the empath's mana full. Take the four trades between you and swap.
- **Alone, as the helper:** `--new-game --help-as striker|keeper|pitman|lifter`: a bot takes the lead's trade and waits
  at the work for you (`PairsLead`, `BotPlayer.WaitsForHand`); F at the work or at the lead lends the hand.
- [ ] **The striker:** a greatsword at the anvil. A co-op tester had trouble as the striker: F beside the smith
  offered "Trade with" them, not the sledge (fixed: F at a lead waiting for a hand lends it). Play it again with
  `--help-as striker`. Is the call's window (`SledgeSession.CallSteps`)
  wide enough at a real round trip? Does the smith wait for the striker's heave? Tune `TapShare`,
  `StrikeShaping`, `OffbeatCrack`.
- [ ] **The keeper:** `--die-at 5` with an empath and a third player. Does the empath see the
  weave's locked till someone holds them? Is the pull's peak (`HoldSession.PeakAt`, `PeakHalf`)
  readable, and the grip's tiring fair? Is `Revival.FailsBelow` too harsh?
- [ ] **The pitman:** `SawGreatLog` at the woodshop. Does the pitman see the stroke start in time
  (`PitSession.CallSteps`)? Is `SawSession.GuideShare` low enough that it feels like two people's work?
- [ ] **The lifter:** a party wagon with a cracked axle. Is the sway (`LiftSession.SwayPerStep`) a
  game, and the arms' tiring a reason to set it down between blows?
- [ ] A "Lend a hand" lesson in the practice yard (`trades.json` `basics`), with an AI lead.
- [ ] AI smiths never make a great piece (`Crafting.NextForVillager` skips `twoHanded`), nor AI
  carpenters a great log. The greatsword borrows the longsword's sprite; the sledge, the pit saw's
  pull, the keeper's hold and the lifter have no sounds of their own.
- [ ] Working beside someone could be a good memory of them (`mood.json` opinion).

### 6. The trades, next
Testers' favourites so far are the tinker and the empath: both read a hidden truth by feel (a trap's
catch, a curio's tells, a wound known only as a sensation). Lean on that. The gap across the trades is
the crises: each falls to one or two trades (raid: the smith and the fighters; fire and dragon: anyone
with a bucket; shortage: the cook; fever: the empath; the bridge: the tinker and carpenter), and the
tinker and innkeeper have nothing to do before one comes. In order:
- [ ] **Playtest the trip-bell** (HISTORY.md, "The empath, and the trip-bell"): does a tinker think to
  set one when the shepherd comes, and is a shaken band worth it? On Millbrook the town's near the
  map's edges, so the warning it buys by day is seconds; the night raid and the shaking are its worth.
  Watch the alarm now waiting till someone sees them (`RaidCrisis.Watch`): a sleeping town is
  slower to wake.
- [ ] **The carpenter played by someone** (1, above), then its slice 2: furniture broken in raids,
  mended (Backlog, "The trades, deeper"); the brawl at the inn breaks it already (`BrawlCrisis.Smash`).
- [ ] **More town contraptions** (DESIGN.md, "Tinker"): a smoke bell for a fire, rat traps for the
  cook in a shortage, then something for the dragon. Each a `ContraptionUse` that's not `ForTheRoad`,
  set like the trip-bell.
- [ ] **New trades:** the Constable comes with M4 (traitors need catching); the Musician
  ([MUSICIAN-PLAN.md](MUSICIAN-PLAN.md)) as the seventh player's seat in place of the labourer.

### 7. Speaking aloud: playtest it
Built ([VOICE.md](VOICE.md)): hold Talk and speak; the words are made out on the machine
(whisper.cpp), shown with the volume they were spoken at, and said as an ordinary `SayCommand`.
- [ ] **On a Deck:** `--dictate-test mic` and a round with L4 on Talk. How long does the Quick
  model take for a short line, and does the frame hold while it works? Is Careful affordable?
- [ ] **The volume:** calibrate, then whisper, talk and shout near another player. Do whispers
  stay whispers (the fence goes to the quieter side), and does the Deck's microphone level a
  shout (`VoiceCalibration.Flattened`)?
- [ ] **The card:** is two seconds the right wait? Do testers change the volume on it, or take
  lines back?
- [ ] **Names:** does the prompt (`VillageGame.VoicePrompt`) spell the town's names right?

### 8. Modding, built in
A new trade, mini-game, map or game mode should be a mod, and the core game the first of them
(the plan, phase by phase: [MODDING.md](MODDING.md)). Before many more trades are added, so each
new one is built the moddable way.
- [x] **Guard rails:** the Moddability rule (CLAUDE.md); banned non-deterministic APIs in the sim.
- [x] **Data mods:** `mods/<id>/` folders with `mod.json`, several def, texture and sound roots,
  `$patch` merges, the mod set in co-op's build tag, saves and replays; a Mods panel; `--check-mod`.
- [ ] **A third town as a data mod**, played in co-op, a mismatched joiner refused.
- [x] **Mod assemblies** loaded, sim and game halves; sessions, commands, codecs, systems, work views
  and sounds registered.
- [ ] **The rest of the registries:** sessions, work views, codecs, commands, systems, work givers,
  roles, crises, lessons and bot hooks registered, not switched on; mod DLLs loaded. Hashes unchanged.
- [x] **The carpenter moves out** to `mods/carpenter`: its sim half, defs and game half are a mod (on by
  default, off with `--mods -carpenter`); the core and the game never name it.
- [x] **Art and sound tools write a mod's files into the mod** (`tools/artroot.py`); the carpenter's sprites and saw moved.
- [x] **Workplaces and services of a mod's own**, named in its defs.
- [x] **Game modes:** `IGameMode`, the season and the practice yard the game's own, Siege Night (`mods/siege`) the example.
- [x] **The musician, built as a mod** from [MUSICIAN-PLAN.md](MUSICIAN-PLAN.md): first pass in (a player-only trade,
  the tin pipe played free, `mods/musician`), then the tune book's two modes, full songs (traditional, public
  domain: a tune for each instrument), and minding the beat, the breath and the long notes while the hands carry a tune (`--drill carry`); then the
  lute, fiddle and hand drum (`--drill lute`, `fiddle`, `drum`) with bends; then an audience (whoever's free and hears
  it comes to listen, or to dance to a lively steady tune: `IPastime`); playtest, then originals of our own, the drum's
  own tunes, the ring dance, the player's dance and the art.
  - [ ] **A band:** more than one musician in co-op (the deal lets several seats take the trade), playing together:
    one strikes up a tune and the others join on it, the drum keeping its beat, each minding their own part; a band
    draws a bigger crowd than any one of them.
- [x] **The ModKit's templates and the coding-agent skills** (`modkit/`, `.claude/skills/new-trade` and friends).

## Then: M4, Whispers

The traitors: the game's hook, and what makes it a game people tell others about.
- Allegiance, objectives, the hidden shop, evidence, player violence, Constable and jail, the bell,
  meetings and exile.
- Ghosts in it: what the dead can tell, and what a traitor does about a Calling. Frost and dream
  whispers are kept to a few words so a ghost can't name its killer at length; decide whether
  even that's too much once there are killers to name.
- The festival ceremony's `Accusation` step (`CeremonyStep`, a no-op for now): the town can name a
  traitor before the verdict.
- **The lobby's Traitors toggle** (`RoundOptions.Traitors`, forced off by `RoundSystem.SetOptions` for
  now): let it through and deal allegiances from it.
- **The playtest logs keep to secrets** (`PlaytestRecorder`): they write every message as it comes,
  which is fine without traitors; with them, only what the player's entitled to until the reveal.
- **Friendly fire's consequences:** townsfolk you cut are only hurt for now; the town remembering
  it, shouting it, and it counting at the reveal.
- Discord integration for meetings and reveals, in the spirit of the bots Among Us groups use
  (look at what's out there before designing ours). Sooner and separately: Rich Presence (trade,
  town, day of the season, an invite to join), cheap and harmless without Discord running.
- Ghosts' own emotes (a wisp's flicker, a cold point), and body poses for the living's
  (`emote_<kind>_<view>` frames in `tools/chibi_people.py`; the bubble carries them meanwhile).

*Exit: a Whispers round.* **Playtest gate: can traitors act and be caught?**

## Toward Steam

The people side (playtests, clips, creators, Next Fest) is [MARKETING.md](MARKETING.md); the store
page's copy is drafted in [STEAM-PAGE.md](STEAM-PAGE.md).

### When
Don't wait for breadth (M6) to start; do wait for M4 to launch. The dates for the page and the
demo are in [NEXT-FEST.md](NEXT-FEST.md).

- **Store page: public by mid-December 2026.** Next Fest requires it. A "Coming soon" page costs
  little and is how wishlists accumulate, and wishlists at launch decide how much Steam shows the
  game. Put it up with a capsule and screenshots, and add the trailer in January (a theme for it,
  Backlog "Sound").
- **The demo: public early February 2027, in Next Fest (22 Feb to 1 Mar).** A game gets only one
  Next Fest, so this is it.
- **Steam Playtest: optional.** It's a way to grow beyond friends and family, and later to see
  whether traitors land with groups who don't know you. Not during the fest.
- **Early Access launch: spring or summer 2027, after M4, with a little breadth.** It can't come
  before the fest ends. The traitors are what make this
  different from other cosy co-op games, and what streamers and friend groups pick up, so launch
  with them in. Before launch, have: a round that's reliable over the internet with Steam invites,
  Whispers through its gate, a second map or a couple more trades so rounds don't feel the same
  by the fifth, and some cosmetic progression so there's something to come back for. M5 to M7
  belong in Early Access, shaped by its players; that's what Early Access is for.
- Launch is the one big moment of visibility, so launching too thin costs more than launching a
  few months later. Matchmaking with strangers (M7) isn't needed: a friends-and-invites game is a
  fine Early Access product.

### What
- **Store page:** Steamworks account and app fee, capsule art in the brand's style, a short
  trailer from real play, screenshots (`--screenshot` covers these), the description in the
  brand's voice. Put the page up as "Coming soon" to start gathering wishlists.
- **Steamworks:** invites and lobbies through Steam (friends join from the overlay; for the demo,
  NEXT-FEST.md), Steam Datagram Relay as a transport in place of (or beside) our own relay, rich presence, Steam Cloud
  for `characters.json`. Keep it behind an interface in the game, not in Narsil's sim.
- **Deck Verified:** the remaining pad gaps (a cursor on the right stick for placing buildings and
  dragging areas), sound on the Deck, text size.
- Licensing: the BRUTAL License allows commercial release; keep the splash, Credits, `NOTICE.txt`
  and the licence files (`external/narsil/docs/LICENSING.md`).

## Later

### Your character: customization and progression
Glamour is the true end game. All of it lives on the saved character (`CharacterRoster`,
`CharacterProgress`: rounds, survivals and silver per trade are already kept; `Titles`, `Title` and
`Unlocked` wait to be filled).
- More to choose in the creator: customization is a focus.
- Mastering a trade earns an outfit and a title for it.
- Shop customization: pick your sign's icon, and better looks with skill and Renown.
- Lighter, RPG-like ways to improve a character, kept small so a veteran and a newcomer can share a
  round.

### M5: The systemic layer
`Reagents`, Apothecary, Undertaker, Merchant, writable notes, keys, disguises, poison on anything.
The necromancer and the Undertaker build on `Death`, `Ghost` and the Calling.

### M6: Breadth
Remaining trades, more maps (authored like Millbrook), crisis variety, personal ambitions, an art
pass. Purely non-combat options (a round, or trades, that never need a blade) once there's the
variety to carry them.
- **A town for twelve:** Millbrook suits five or six players. Before the twelve-player goal, grow
  it or make a bigger one. The rare companies already come bigger with more players
  (`PartyDef.SizeFor`); the ordinary parties and the crises may want the same.
- **Bigger maps: one map a round, grown as needed.** Far places stay off the map, as quests are
  (`MemberRecord`); a different town is a different round. Millbrook is 128x96 now (the hill and the
  woods east of the old square); up to about twice a side the size is only data. Before going much past
  that (192 or more), in order of what breaks first:
  1. Every structure change rebuilds path costs, regions, rooms and heat conductance over the whole grid
     (`Simulation.RebuildDerivedIfNeeded`, one global `StructureVersion`): make it dirty-rect or per chunk.
  2. A* gives up past 60,000 expanded nodes (`GridPathfinder.MaxExpandedNodes`), with no hierarchy:
     portals between chunks, or region-level paths over `GridRegions`.
  3. `WorldMesher.Draw` draws every chunk every frame: cull to the camera, and budget the first build.
  4. The join snapshot is about 20 bytes a cell, mostly floats (`SaveGame.WriteMap`), against the
     relay's 32 MB frame: leave out or quantise temperature and light, run-length the blocks.
  5. Every system's side arrays come to about 100 bytes a cell, and depth multiplies them all.
- **The walk in counts against patience:** an adventurer's order is posted when they decide (often at the
  map's edge), so on a bigger map the walk in eats into it and the quick-service tip (`ShopSystem.TipFor`).
  Start an order's patience once they're in town or in the line.

### Reedmere, in progress
An untuned map still being built; the town picker says so. Playtests stay on Millbrook until the
first ten minutes and the numbers have settled. It may stay a little harder than Millbrook on purpose.
- **Redrawn** (HISTORY.md, "Reedmere, the town on the long lake"): does the crowded town read in
  play, is the long bridge worth its walk, and does the festival on the boats work with friends?
  The raft reads as one great barge from the usual angle; boats you can tell apart would be better.
- **The cook supplies the inn** (HISTORY.md, "The cook supplies the inn"): does the innkeeper keep
  the warming shelf stocked, and is the trek to the cookhouse worth the bar standing empty? Do the
  bowls that go over in hampers come back, or pile up dirty at the inn?
- **Still to do:** knockback off the walks, swimming and breath, fishing someone out (a body sunk
  in the lake is the traitors', M4); rowing the moored boats; a proper dipping post sprite;
  balance (`--season-sweep --seeds 8 --map Reedmere`), once the rest has settled.

### The dragon over the lake
Reedmere's set piece, after the battle the lake towns of its stories are known for. Not built yet;
a sketch to build from.
- **It follows from a quest.** The dwarves' errand to Greyhorn Hall wakes what sleeps there: a
  day or two after they set out (whether they come back or not), the dragon comes to Reedmere at
  dusk, in place of the season's own dragon. Needs a link from a quest's outcome to a crisis in the
  `Director`.
- **The town burns.** It strafes the timber town end to end (`DragonBrain`'s passes, along the
  walks), and the bucket lines from the dipping posts are the town's whole defence until someone
  hurts it.
- **The weak spot is a secret.** One part of it has no armour: a hidden property, known only
  through `Perception` (`Intel`), from what the halfling saw in the hall and tells whoever's in
  earshot at the inn on the way back. Called shots, or an armour gap on one body part that an aimed
  arrow can find (`HealthSystem.PickPart`).
- **The great bow** on the moot hall's forecourt is real: crewed by two (the tinker keeps it
  working), slow to turn, one bolt at a time, a turret's free aim from the stand.
- **One heirloom arrow**, kept in someone's house: an ammo item, fetched in time or not.
- **It falls into the lake**, breaking the decks under it, and the town counts what it lost.

### M7: Playing with strangers
An authoritative server filtered by `Perception`, matchmaking, moderation, voice. Likely built
together with persistent towns' dedicated server (VISION.md, "Shared with M7").
- **Voice** goes by the speech rules, or it undoes them: each client plays a voice only where
  `SpeechSystem` would let that listener hear it (the same ranges; muffled through walls; the dead
  only to the dead). Raw proximity voice would let the dead talk to the living and get round
  eavesdropping and the traitors. Steamworks voice for capture and the codec.
- **A word filter** on every line, typed or spoken (VOICE.md: a misheard word can come out rude).

### Steam Workshop
Uploading and subscribing to mods (Steamworks UGC) once modding is built (Now, 8; [MODDING.md](MODDING.md)):
a subscribed item is a mod folder in the user mods directory, with the code notice for one that runs code.

### Villagers who listen
No AI acts on what a player says, typed or spoken: orders go through F at the counter, and only bots
read a line's words. Spoken lines make this matter more. A deterministic `Speech/Listening` system,
in the sim (the words are already in the `SayCommand`), matching what an AI caught clearly against
word banks in the defs, and answering through what's already there:
- a villager or adventurer named turns and answers (a bark, `Barks`);
- "Help!" joins the ask for a hand (`Work/Pairs`); "Fire!" or "Bandits!" raises the alarm as a
  trip-bell or a sighting does (`RaidCrisis.Watch`), for those who believe the speaker;
- greetings and thanks answered, which a mood memory could follow (`mood.json`);
- a dish or a service on the sign, said at the front of a line, taken as the same `Order` F makes
  (`ShopSystem.PlayerOrder`);
- later, an optional host-side language model reading what was meant, acting only through commands,
  as the Director's outside mind would (Backlog, "Crises and the town").

## Backlog

Left behind along the way, plus wishes. Grouped by area; nothing here blocks a playtest. Pull
items into "Now" when a playtest says they matter.

**Tuning the mini-games**
- **Forge:** the heat and quench windows; the bloom's band, how long it takes and its yield.
- **Kitchen:** the pot, the seasoning; and the cook's list below.
- **Bar:** washing up, tapping a keg, mixing.
- **Healing:** after the tuning playtest (Now, 2).
- **Tinkering:** truing a wheel had its pass (a spoke at a time, a spin to chalk it); its pull and band in play.
- Some jobs easier and some harder, to suit different players and styles.

**The trades, deeper** (DESIGN.md, "Trade notes")
- **Blacksmith:** commissions (the buyer's hilt, runes and metal); molds, and the hilt and grip as
  their own step; lighting and banking the forge by hand; reforging. AI smiths still forge and
  smelt with a plain `CraftJob` and don't answer a call to arms (bots do). Only iron is smelted
  by hand; copper, tin and the Kiln aren't in Millbrook. The town purse is a number, not coin in a
  strongbox: it wants the Mayor.
- **Innkeeper:** the AI innkeeper doesn't mix (asked for a named drink, it pours an ale); mess on
  tables, and a rag; no brewing (casks refill only from the carter's kegs); keys for the guest
  rooms; villagers at supper sit but don't swap rumours.
- **Cook:** AI cooks never chop, roast, bake, make pies, salt, smoke or name a house dish (an AI
  cook never offers the roast, bread or pies); the cook bot does all but salting and smoking. Rations are sold at the bar's line; the hatch would suit them better. The empath sending
  the hurt to eat a broth; teas from shared herbs; pickling. To tune: the fold cue and the cord's
  band (`PackSession`), cravings and moods (`Tastes`), the fed and sick factors (`Rations`),
  seasoner weight and cap (`Cooking`), the house dish (`HouseDish`), tips (`ShopSystem.FineTip`),
  the spit (`Roasting`), mealtimes and small hours (`ShopSystem.Mealtime`, `SmallHours`), arrival
  hunger (`PartySystem.Send`), baking (`Baking`), the cleaver and the pluck (`Butchery`), the crimp
  (`Pies`), flares (`Cooking.FlarePerStep`), rats (`RatSystem`), feasts (`FeastSystem`), broths
  (`Broths`), word of the food (`ShopSystem.Fare`).
- **Empath:** adventurers never tear a charm in town.
- **Labourer:** removed (a crutch: the AI doing what players should). Split up rather than given a pass:
  the Carpenter came out of it, and the Miner (`mods/miner`, a first pass); the Forager is left, for the new
  woods east of Millbrook. Errands are the players'; nobody chops or forages in a round till the Forager.
- **Miner** (a beginner's trade, three games: hewing, sounding, the barrow; its bot works the whole round; off by
  default till played): an AI miner that hews (it only carries ore to the smithy now; it digs only what's marked,
  and could prefer chalked seams); the ore price on the shed's sign rather than the mod's list (`ores`); the smith sending
  for a rush load; a practice face in the yard; tipping the barrow out anywhere; the barrow drawn leaning as it leans
  (`BarrowLoad.Lean`), and the pusher's pose; later, timbering and cave-ins, bad air, and raiders in the tunnels.
- **Tinker:** no lockboxes or locked chests (locks go on doors only); AI tinkers don't buy curios;
  a wagon lesson (practice customers' wagons aren't damaged yet); a trip-bell lesson in the practice
  yard, and the trip-bell's own sprite (it borrows the tapping rod's).
- **Carpenter:** raids breaking furniture (fire and the brawl at the inn do, `ITradeModule.Lost`;
  the small one-on-one fights of `Hospitality/Brawls` don't); a hand game for mending a cudgel or a staff (a split haft spliced and bound:
  it's hold-F for now); lessons for mending a bow, a wagon's woodwork and setting back a piece (the
  practice yard has no worn bows, damaged wagons or burned furniture yet); the build and zone tools
  brought back as the carpenter's own (they are off for everyone, `VillageGame.Journal.BuildTools`,
  since playtesters found them useless); feathers from the cook's plucking; AI carpenters don't build
  bows for sale. Ideas for later: coffins for the round's dead; walking staves for travellers;
  barricades before a raid.
- **Tinker:** crossbows are mended at the tinker's (`CrossbowRepair`), but no adventurer kind carries
  one, so it only comes up when a bowman's bought one off the tinker's shelf. An arbalester in a party
  or two would give it work.
- **Gathering:** chopping and foraging are the labourer's until the Forager comes; digging and
  mining are the labourer's and the Miner's (`TradeDef.Mines`).

**Bots** (solo playtests and the scenario tests)
- The cook bot never offers porridge, matches a craving or an ask for the house dish by name, or
  salts, smokes or makes broths; a pot that smells off can't be emptied by anyone. Rations are
  sold only where an AI innkeeper keeps the bar (the innkeeper bot doesn't sell goods). Rats are
  chased at the think's pace, not every tick.
- The bot labourer doesn't dig (no pick); the porter's errands (unload a party's wagon) aren't in.
- Adventurers don't chat among themselves (`SocialSystem.CanTalk` leaves them out); drunks aren't
  refused service or thrown out after a fight.
- Sim: at the forge, the bot's step back from the heat (`Safety`) fights a piece in the tongs, and
  the work key at a forge with a piece in it offers Craft before Refuel, so a forge gone out can't
  be fed (by a player either). Charms are all labelled "charm", so a buyer can't tell two apart.
  The work key at an oven with dough in hand puts the dough in rather than refuelling.
- `SeasonScenarioTests` checks only that a round plays through, not how it comes out. The sweep
  swings a lot from seed to seed (crises met decide most of it): tune tempers over 24+ seeds.

**Crises and the town**
- **The Director's outside mind** (an experiment): a host-side language model that every game hour
  reads a compact summary (tension, who's doing what, quests, what's been said aloud near villagers)
  and answers with a `DirectorChoiceCommand`: an incident or a beat's kind, plus words in the brand's
  voice (a shepherd's warning, a stranger's request). It falls back to `RuleMind` offline; solo and
  bench always use the rules. It can only combine verbs the sim has, so a bigger incident menu is
  what makes it worth having (a fight at the inn, a curio someone's desperate to sell, a small fire at
  one forge, quest outcomes that bring trouble).
- The Reedmere dragon follows its quest (`followsQuest`), the first beat to.
- Raiders don't bash doors or gates; only the bridge breaks.
- AI carrying the dead to the shrine, so an AI empath gets a chance without a player's help.
- Time passes fast only while everyone sleeps, not when the town is otherwise quiet.
- Wood left out on a cold day goes on the fires before an errand can fetch it.
- **Brave villagers with buckets:** once the dragon's down, the bolder villagers (by temper or
  mood) fetch water and soak it (`DragonBrain.Douse`; `SoaksToDrive`), not just the fires it sets
  (`BucketJob`). The town without a party has a way to see it off (met 1/48 in the sweep).

**Map and movement**
- Lanes cost the same to walk as grass, so villagers don't prefer them.
- An AI re-planning a walk snaps back to the cell it left rather than turning mid-step (players
  turn at once).

**Art and look**
- **Dance and cheer frames** from the people pipeline (`tools/chibi_people.py`: new tags after the
  rest, every layer regenerated, then `chibi_builds.py`). Blows have their own (swing, thrust, punch:
  docs/ART.md). A dancer hops and sways (`WorldRenderer`), and a cheer is a hop. The dodge stays a
  tumble (testers like it), with a kick of dust each end.
- A proper ghost (a pale, see-through take on the character), and the Calling's glow. A ridden
  beast's look (a pale glint in its eyes), frost drawn on the ground rather than a tag, and a
  startle sound for the beasts (spook and frost borrow the chill's for now; docs/AUDIO.md).
- Co-op: a ridden beast isn't predicted on its rider's screen (`FreeMovePredictor` is the
  character's), so it lags a round trip behind the keys.
- The bucket's water drawn in flight.
- Placeholder art in the kitchen's items: rations are a bolt of cloth, roast slices and the side
  of pork a meat sprite, flour and oats the fiber, dough and the unbaked pie the clay, bread,
  porridge and pie the meal; the fowl is hide, salt is lime; rats are small hares. Nobody yet
  holds a spoon, knife or quill while they work (a held shape each in `tools/chibi_people.py`).
- The folk (elves, dwarves, halflings) are only looks; they could have something of their own (an
  elf's keener sight at dusk, a dwarf's ease at the forge, a halfling's quiet step). Halflings
  could go barefoot (villagers' clothes come from the sim, so it isn't only a look).
- real windows that can be opened, closed, and seen through.

**Sound** (first pass done: docs/AUDIO.md)
1. **Music, ours:** a theme (at least its start), then a score to replace the placeholder tracks
   mood by mood (`title`, `day`, `night`, `danger`, `festival`, `reveal`). Later, adaptive layers
   (danger drums over the day track), festival songs, and a musician. Needed before a Steam trailer.
2. **Coverage:** footsteps by surface, doors, animals and weather; hammering heard from other
   smithies and every trade seen from outside; the bucket's splash; room reverb (EFX) for the inn,
   cottages and caves; a mix pass from playtest notes.
3. **Recorded sounds:** still synthesized are the inn's murmur, a town bell, a burning building and
   an arrow's impact. CC0 freesound pages (login to download), brought in through
   `tools/import_sounds.py`:
   - murmur: freesound.org/people/ivolipa/sounds/326313, squareal/sounds/237398
   - bell: dsp9000/sounds/76405, Audeption/sounds/425172
   - house fire: 7778/sounds/196619
   - arrow hit: plantmonkey/sounds/410751
   - a babbling stream: BurghRecords/sounds/415151
   - a beer pour: Kinoton/sounds/347006
4. **Voices (text to speech):** NPC lines spoken aloud through local neural TTS (sherpa-onnx
   running Kokoro or Piper voices), a voice per character derived like their look; synthesized off
   the main thread, cached, opt-in in Settings, babble the fallback. Watch the model size (about
   100 MB) and the CPU on a Deck. Lines are made at runtime (players type theirs), so they can't
   be pre-recorded.

