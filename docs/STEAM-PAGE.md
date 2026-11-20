# Steam page draft

Copy for the store page, in the brand's voice (`art/brand/brand-guide.html`): warm, dry, plain
words with fantasy furniture. The plan around it is [MARKETING.md](MARKETING.md).

**Before the page goes up:** keep every claim true of the build. Traitors are written in below
because the page goes up during M4 and launch waits for them; until they play, keep the "in
development" line under the features. Player counts: 1 to 12 (`CoopSession.MaxPlayers`),
villagers fill the empty trades; Millbrook plays best at 4 to 6 for now.

## Name

Heroes Not Included

## Short description

Steam shows this beside the capsule. 300 characters at most.

> Every legend needs someone to pour the drinks. Run the inn, the forge, the kitchen or the
> empath's shrine in a fantasy village with your friends. Keep the heroes fed, mended and paying
> long enough to reach the festival. Some of you may have other plans.

(253 characters.)

## About this game

> **Somebody has to keep the lights on.**
>
> Every adventure starts in a tavern. Ours starts behind the bar. You and your friends are the
> village the heroes walk past on the way to glory: the innkeeper, the smith, the cook, the empath,
> the tinker and the carpenter. They get the glory. You get the tab.
>
> **Your trade is your game.** Pour ales and mix drinks while the line grows. Heat, hammer and
> quench a blade by the colour of the steel. Taste the stew, sniff the larder, and hope nobody
> notices the cabbage. Draw a stranger's wounds into yourself and weave the right mending before
> they do you in. Pick the lock on the strongbox a party dragged home, carefully. Fell a pine
> where you meant it to fall, and fletch the arrows the archers will lose by Thursday.
>
> **Nobody can do it alone.** The cook feeds the inn, the smith arms the party, the empath patches
> up whatever comes back. Set your own prices, pin favours to the notice board, and talk to each
> other, because the heroes certainly won't talk to you.
>
> **The season fights back.** Bandits come down the north road and take out the bridge. The inn
> catches fire. The larder runs low and the rats move in. A fever goes round. And one evening,
> something very large lands by the lake. Keep Renown up and Dread down until the festival, and
> the town's still standing. Nobody will say thank you.
>
> **Some of you may have other plans.** Turn on traitors and a few villagers are working against
> the rest: a flaw in a blade, a turned ingredient in the pot, a party sent somewhere they won't
> come back from. Gather evidence, call a meeting, and find out who it was at the reveal.
>
> **Death is a career change.** Die and you come back as a ghost: frost on a window, a dream, a
> beast that won't behave. The empath can call you home, if they notice.

### Features

> - Co-op for 1 to 12 players, online or on your local network. Villagers fill any trade nobody
>   takes, so a round works with two of you or a full town.
> - Six trades, each its own game: innkeeper, cook, blacksmith, empath, tinker and carpenter.
>   The musician and the miner are on their way.
> - Two towns: Millbrook by the river and Reedmere, on stilts over the long lake.
> - Seasons of 30 to 40 minutes: parties to serve, crises to weather, and a festival at the end.
> - Optional traitors, and rounds without them for groups who'd rather not.
> - A practice yard with lessons for every trade.
> - Full controller support, and plays on Steam Deck.

In development line, until traitors ship:

> Heroes Not Included is in active development. Traitors are on their way; co-op seasons are
> playable now. Try a season in the free demo.

The last sentence goes in once the demo is public (February 2027).

## The demo's page

The demo is an app of its own, with its own small page (NEXT-FEST.md). It takes the game's capsules
with a "Demo" mark, and links back to the game's page for wishlists.

**Short description:**

> One season in Millbrook, with the bar, the forge, the kitchen, the shrine, the tinker's bench
> and the woodshop. Play alone with the villagers or bring friends. Keep the heroes happy until
> the festival. They won't thank you.

**What's in it** (keep it true of the demo build):

> - A shortened season in Millbrook, from the deal to the festival and the reveal.
> - Six trades, and the practice yard with lessons for each.
> - Play alone, villagers taking the other trades, or invite friends through Steam.
> - Your characters carry over to the full game.

## Tags

Steam lets you pick up to 20, the first few weighted most. In order:

Multiplayer, Online Co-Op, Social Deduction, Simulation, Fantasy, Funny, Colony Sim, Management,
Local Co-Op (LAN), Cozy, Medieval, Comedy, Sandbox, Life Sim, Indie, Early Access, Controller,
3D, Real-Time, Character Customization.

Drop Social Deduction until traitors are in.

## Early Access questions

Steam asks these of every Early Access game. Draft answers:

**Why Early Access?**
> It's a game about a town full of people, and it's only fun because of the people playing it.
> We'd rather shape the trades, the crises and the traitors with the groups who play it than
> guess at it alone.

**How long will it be in Early Access?**
> Around a year, perhaps longer; we'll say more as we go. Full release when the town has its full
> set of trades and the traitors are properly sneaky.

**How is the full version planned to differ?**
> More trades (the musician and the miner first, then the constable, the apothecary and others), more towns, deeper betrayals
> and the evidence to catch them, and something of your character to carry between rounds.

**What is the current state of the Early Access version?**
> A complete season with friends: six trades or more, two towns, crises, the festival, death and
> ghosts, and traitors. Expect rough edges and balance changes.

(Write this at launch from what's actually in: the musician and the miner may be in by then.)

**Will the price change after Early Access?**
> Probably a little, as the game grows. Anyone who buys in Early Access gets every update.

**How are you planning to involve the community?**
> Through our Discord: playtests, feedback threads and the stories from your rounds. Plenty of
> what's in the game came from watching friends and family play.

## Capsules and media

Made in-house for Next Fest, and commissioned from an artist before Early Access (MARKETING.md,
Phase 2). Steam's required sizes, in pixels (check Steamworks' current list when uploading):

| Asset | Size |
|---|---|
| Header capsule | 920x430 |
| Small capsule | 462x174 |
| Main capsule | 1232x706 |
| Vertical capsule | 748x896 |
| Page background (optional) | 1438x810 |
| Library capsule | 600x900 |
| Library hero | 3840x1240 |
| Library logo | 1280 wide or 720 tall, transparent |
| Screenshots | 1920x1080, at least 5 |

The demo needs its own set: the same art with a "Demo" mark.

`tools/capsules.py KEYART.png --demo` makes the header, small and main capsules (`--all`: the rest, untuned)
and the library logo into `art/steam/`, with a contact sheet for judging candidates at real size.

- **Main capsule (and the small one):** the emblem and logo on a lit common room at dusk. Readable
  at the small capsule's size (231x87); test it shrunk. No heroes in front: the brand is the
  world around them. One ember thing.
- **Library hero and logo:** the horizontal logo over a wide shot of Millbrook.
- **Screenshots (5 or more, 1920x1080):** a full bar with a line out the door; the forge mid-strike;
  the inn on fire with a bucket chain; the dragon over the lake; the festival; a ghost's eye view.
  `--screenshot shot.png --screenshot-at N --quit` with `--fire-test`, `--dragon-test`,
  `--festival-at` sets most of these up; `--screenshot-no-ui` for the clean ones.
- **Trailer (30 to 60 s):** first 5 seconds is the hook (a hero walks past you without a word), then
  one shot per trade, a crisis, the festival, and (once in) a traitor moment. End on the tagline
  and "Wishlist now" (for the fest, "Play the demo"). Cut from real play; the stories in MARKETING.md are the shot list.
  Ready by late January (NEXT-FEST.md). Score it with a theme of our own, not the placeholder
  tracks, so Content ID leaves our videos and streamers' alone.

## Pricing

Leave the number for nearer launch. Friend-group games usually sit low (around $10 to $15) so
a whole group buys in; watch for regional pricing and a launch discount when the time comes.
