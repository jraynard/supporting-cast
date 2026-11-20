# Next Fest, February 2027

The road from here (19 Nov 2026) to a demo in **Steam Next Fest, 22 Feb to 1 Mar 2027**. Until
the fest ends this is the "Now", ahead of [ROADMAP.md](ROADMAP.md). The people side follows
[MARKETING.md](MARKETING.md), and the page copy is [STEAM-PAGE.md](STEAM-PAGE.md). When a line here is
done, tick it. After 1 Mar, move what's left into ROADMAP.md and archive this file.

## The decisions

- **The demo is one season in Millbrook, shortened (20 to 25 minutes).** It's built for one
  player: bots fill the empty trades. Co-op works too, and friends join through a Steam invite.
  Most Next Fest players come alone and give a demo half an hour, and the groups come later if it
  hooks them.
- **Six trades:** innkeeper, cook, smith, empath, tinker and carpenter. The musician and the miner
  stay out, and the page names them as coming.
- **No traitors.** They come with M4, after the fest. The demo shows the co-op round, the mode we
  already know is fun.
- **Early Access in spring or summer 2027, with traitors.** The fest gathers wishlists, M4 lands,
  and launch follows a few months later. The date gets chosen once we've read the fest's recap.
- **Capsule art made in-house for the fest**, with an artist commissioned before Early Access.

## Valve's rules and dates

From partner.steamgames.com/doc/marketing/upcoming_events/nextfest:

- A game gets **one** Next Fest, ever. It has to be this one, and done properly.
- The game's own store page must be **published and public**. The demo also needs its own page,
  and it must be live and playable by the start of the fest.
- The game must not release before the fest ends. Early Access counts as a release.
- Running a Steam Playtest during the fest isn't recommended.

| Date | What |
|---|---|
| **Sun 10 Jan** | Registration closes |
| **Mon 18 Jan** | Developer Preview opens (5 weeks before) |
| **Mon 1 Feb** | Last day to submit the demo build for review (3 weeks before) |
| **Thu 11 Feb** | Press Preview. Press can play the demo only if it's already public. |
| **Thu 18 Feb** | Valve's fest trailer |
| **Mon 22 Feb** | Next Fest opens. Put the demo out at least 30 minutes before. |
| **Mon 1 Mar** | Next Fest ends; the recap follows |

For **14 days after the demo goes public**, Steam lets you email everyone who wishlisted the game.
Time the demo's release so that email lands before the Press Preview.

## The timeline

Three dates can't move: the page public before the holidays, registration by 10 Jan, and the demo
submitted by 1 Feb. Everything else can slip a little.

### Week of 23 Nov: accounts and the start of Steam
- [ ] **The Steamworks account:** the $100 app fee, the tax interview, bank details and the
      identity check. Each can take days, so start them first. *Applied, and identity check under
      way, 19 Nov.*
- [x] Check the name: Steam, itch, USPTO and EUIPO for "Heroes Not Included" and near matches.
      *Clear, 19 Nov.*
- [ ] Set up the game's app, and a **demo app** beside it (demos are an app of their own, with
      their own page and depots).
- [x] The Steam integration, tried with the test app (below, "Steamworks").
- [ ] Capsule art, part 1 (below).
- [ ] A `Demo` build property, and the `mods/demo` content gate (below).

### Week of 30 Nov: the page's pieces
- [ ] The in-house capsule set, in every size Steam requires (STEAM-PAGE.md, "Capsules and media").
- [ ] Five or more screenshots: `--screenshot-no-ui` with `--fire-test`, `--dragon-test`,
      `--festival-at`, a full bar, the forge, the woodshop.
- [ ] The page copy brought up to date (STEAM-PAGE.md).
- [ ] Steam init and shutdown, the overlay, rich presence.

### Week of 7 Dec: the page in for review
- [ ] **Submit the store page for review by Fri 11 Dec.** Review takes 3 to 5 business days, and
      Valve slows over the holidays.
- [ ] The Steam setup once the app IDs exist (below, "Steamworks"; PUBLISHING.md, "Steam").
- [ ] **Newcomers play the tutorial,** one trade at a time (ROADMAP, Now 1).

### Week of 14 Dec: the page goes up
- [ ] **The page public as "Coming soon" by about Fri 18 Dec.**
- [ ] "Wishlist on Steam" first in every bio, the Discord's welcome and the YouTube links
      (SOCIAL.md). The first devlog post goes to the Discord and the page's news.
- [ ] Bots: the smith's heat stall, and answering asks (ROADMAP, Now 2).
- [ ] Flicker at wall corners and on the inn's upper floors.

### 21 Dec to 3 Jan: the holidays
- [ ] Rounds with family and friends: tuning, stories and recordings (MARKETING, Phase 1).
- [ ] Short clips at one a week, which we can keep up.

### Week of 4 Jan: register and freeze
- [ ] **Register for Next Fest by Wed 6 Jan** (it closes the 10th). Read the criteria for
      Valve's fest trailer while you're there.
- [ ] **Demo feature freeze, Fri 8 Jan.** After that, only fixes and words.

### Weeks of 11 and 18 Jan: the release candidate
- [ ] A demo build on a private Steam branch, played by the Discord's testers.
- [ ] The trailer, 30 to 60 seconds (STEAM-PAGE.md, "Trailer").
- [ ] The demo's store page ready.
- [ ] A press kit page on itch: logo, emblem, screenshots, trailer, a paragraph, contact.
- [ ] A list of 30 to 50 small creators (MARKETING, Phase 3).

### Week of 25 Jan: the demo in for review
- [ ] **Submit the demo build and its page by Mon 25 Jan** (the deadline is 1 Feb, which leaves a
      week for a rejection).
- [ ] The trailer on the game's page.

### Weeks of 1 and 8 Feb: the demo out
- [ ] **The demo public by about Mon 8 Feb**, so press can play it at the Press Preview.
- [ ] Email the wishlisters (the 14-day window).
- [ ] Demo access to the creators on the list, with an offer of keys for their whole group.
- [ ] Posts on Reddit and Shorts (MARKETING, Phase 3).
- [ ] The press kit to outlets for the Press Preview (11 Feb).
- [ ] Plan the fest's streams: when, who from the Discord, which trades.

### 22 Feb to 1 Mar: the fest
- [ ] Streams of our own with the Discord, as many days as we can keep up.
- [ ] Answer the Steam discussions and the Discord every day.
- [ ] Triage daily, and put out a fix build every other day at most. Don't change the game under
      players mid-fest.
- [ ] Clip creators' rounds (ask first) for the page and the socials.

### After
- [ ] Read the recap: wishlists, demo players, median playtime, where people stopped.
- [ ] Commission the capsule.
- [ ] M4, Whispers. Then choose the Early Access date.

## The demo

**Content, as a mod.** A `mods/demo`, on only in demo builds, so the gate goes through the same
doors as every other mod (CLAUDE.md, Moddability):
- `$patch`es `season.json` to a shorter season.
- Limits the towns offered to Millbrook.
- Limits the trades dealt and offered to the six. The miner and musician mods aren't shipped in
  the demo's depots at all.
- Keeps the practice yard and every lesson, which matter more in a demo than anywhere.

**The build.**
- A `Demo` MSBuild property sets `BuildInfo.IsDemo`. It turns `mods/demo` on, and hides the Mods
  panel, the arena and the dev modes (`--raid-test` and friends).
- A **Wishlist** button on the title screen and at the reveal, opening the Steam overlay to the
  store page, with the Discord link beside it. The reveal is where a good round ends, so ask there.
- Saved characters carry over into the full game for free: it uses the same config folder and the
  same `characters.json`.

**Solo has to be good.** In a demo, bots are most of the town:
- [ ] The bot smith's heat stall (`BotPlayer.Forge.TooHotToForge`).
- [ ] Bot keepers answering asked-for pieces (`Order.Wants`).
- [ ] `SeasonScenario` and `--season-sweep` run on the demo's season, to check it plays through and
      lands about right.

**The first ten minutes** are where the demo is won or lost (ROADMAP, Now 1):
- [ ] A newcomer plays each trade's tutorial, a non-gamer among them.
- [ ] The tutorial's gaps: handing over from an order, and the pointer for errands.
- [ ] **The first-round hint has to be decided now.** A demo player who skips the tutorial has
      nothing else to go on. Recommended: one line, "your work: ...", on the trade's card until
      that trade has a finished round (`CharacterProgress`).
- [ ] The smith marked on the picker as a good first trade.

**Co-op.**
- Steam's Join game and Invite carry the relay's join code, so friends join over our own relay
  (`CoopRelay.Default`) with no lobby (below, "Steamworks"). Lobbies and Steam Datagram
  Relay wait for Early Access.
- [ ] A joiner on another network, a join mid-round, and the host's network pulled for a few
      seconds (ROADMAP, Now 3).
- [ ] The relay box under a fest's load: many small sessions at once. Check its limits before 8 Feb.

**Sturdiness.**
- [ ] No Vulkan: a plain message, not a crash (`--check-gpu`).
- [ ] The log folder reachable from the menu, so bug reports come with logs.
- [ ] Graphics settings at the usual resolutions, windowed and full screen.
- [ ] Windows smoke tests on a machine that isn't ours; the Deck through `tools/deck.sh`; the Mac
      build, if it ships.

**Music.** The placeholder tracks aren't in git, but `tools/package.sh` already fetches them
(`tools/fetch_music.py`), so a Steam job built on it ships with music. The CC-BY tracks are fine
with their credits. The trailer wants a short theme of our own anyway (ROADMAP, Backlog "Sound"):
it keeps Content ID claims off our videos and streamers'.

## Steamworks

Built (19 Nov), game side only, never in Narsil or the sim. How it works is in CLAUDE.md's
architecture notes; setting up and releasing are in [alpha/PUBLISHING.md](alpha/PUBLISHING.md),
"Steam".

- **Valve's flat C API, bound by hand** (`Platforms/SteamNative.cs`, SDK 1.65 in
  `external/steamworks/`), not Steamworks.NET: we need about fifteen functions, and the bindings
  match the libraries we commit.
- **One build for itch and Steam.** The libraries ship in every package. A copy is Steam's only
  if a `steam.json` sits beside it (`tools/steam_package.sh` adds one), so builds stay the same
  bytes and itch and Steam players can still play together. `--steam 480` tries it from a checkout.
- **Joining without lobbies.** While you host online, rich presence `connect` is `--join CODE`
  with the relay's join code. Friends see **Join game** on you, **Invite friends** sits beside the
  code in the Players window, and an invite accepted with the game closed launches it with that
  `--join`, which was already a way in. Accepting one mid-round asks before leaving.
- **Rich presence** ("Innkeeper in Millbrook, day 2"): its words are
  `packaging/steam/rich_presence.vdf`, uploaded in Steamworks.
- **Wishlist on Steam** on the title screen and at the reveal, only in the demo.
- **The Steam name** is the default name for a first character.
- **Uploads:** the `steam` job in `playtest-release.yml` puts each published build on the
  **testing** branch. It stays dormant until the `STEAM_APP_ID` variable is set.

Still to do:
- [x] Init, callbacks and shutdown with the test app (480), from a checkout and from the packaged
      Linux build.
- [ ] **The overlay over Vulkan:** does Shift+Tab draw over the game? Add the build to Steam as a
      non-Steam game with `--steam 480` (PUBLISHING.md). Then Windows, then the Deck. If it won't
      draw, Join game from the friends list still works, and the Wishlist button opens the browser.
- [ ] Once Valve makes the apps: the one-time setup in PUBLISHING.md (depots, launch options, the
      Linux runtime, the testing branch, rich presence, the builder account, the GitHub variables and
      secrets), then a tagged build onto the testing branch.
- [ ] Two Steam accounts on two machines: Join game, Invite, an invite with the game closed, an
      invite mid-round, Wishlist.
- [ ] The Deck: the overlay, an invite, the on-screen keyboard in text fields.
- [ ] The demo's own content (`mods/demo`, above). Until then, a demo depot is the game's.

Later, for Early Access: lobbies, Steam Datagram Relay, Steam Cloud for `characters.json`,
achievements, the Workshop.

## Capsule art, part 1: the key art and the first three sizes

The page's review needs every capsule. Part 1 gets the three that matter most (header, small and
main) far enough along to judge at real size. Part 2 does the rest (vertical, library, the demo's
set) from the same key art. Made in-house for the fest (MARKETING.md, Phase 2), and replaced by an
artist's version before Early Access.

**The brief** (MARKETING.md and STEAM-PAGE.md): the brand's colours, a busy common room or square
at dusk, the logo, no heroes in front, one ember thing. Above all, it has to read at the small
capsule's size.

- [ ] **Three candidate key-art shots**, captured clean and large. The screenshot flags
      (`--screenshot-no-ui`, `--size 3840x2160`, `--skip-hours`, `--camera-mode`, `--look-at`,
      `--yaw`, `--pitch`) set up a still scene. Shift+F12 in camera mode during a real round catches
      a lived-in one. Candidates:
  1. The inn's common room at dusk: a full bar, the fire lit, a line at the counter.
  2. Millbrook's square at dusk from above: the inn's windows lit, smoke from the smithy, the
     notice board.
  3. The smithy mid-strike at night, the forge the ember thing, the inn lit behind it.
- [ ] **`tools/capsules.py`:** composites a key-art image and the brand's logo
      (`art/brand/logo-horizontal.svg`, `logo-stacked.svg`, rasterized the way `tools/brand_logo.py`
      does) into each Steam size, with a crop and a logo position per size, and a "Demo" mark in
      the brand's ember for the demo's set. It writes to `art/steam/` and builds a contact sheet:
      every size at 100%, plus the small capsule at the half size a store list shows it.
- [ ] **Judge the three together** on the contact sheet, at small size first. Pick one, then tweak
      the framing and do part 2.
- [ ] Add the musician video from 18 Nov (`~/2026-11-18 19-02-22.mp4`) and the weekend's recordings
      to the trailer's footage. Keep the stories that go with them in MARKETING.md.

## If we're late

Cut in this order:
1. Steam invites and Join game. Join by typed code over the relay, as now.
2. A cut trailer. A 30-second montage from kept rounds (`--replay`) does.
3. The carpenter. The demo goes out with the five.

The page, registration and the demo's submission don't move.
