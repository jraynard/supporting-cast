# Playtest legibility audit (2026-11-12)

The last two PLAYTEST PREP items: make the game legible for people who aren't hardcore gamers,
and lean less on reading so kids can play.

**Status (2026-11-12):** everything below marked **Done** shipped for `0.1.0-playtest.2` (see
ROADMAP, "Playtest prep"). What's left: the inn's lighting, the two art leftovers, and the
hands-on pass.

**How it was audited:**
- Solo rounds as each trade, from the packaged Release build (what testers get: no debug
  tooltip coordinates, frame times or Debug menu).
- `--new-game --trade X --screenshot ... --screenshot-at N`, at the start and four hours in.
- The title screen.
- Every mini-game's `BeginWork` hints, read in code. No flag opens a mini-game directly, so
  those still want a hands-on pass.

Sizes: **S** about an hour, **M** a session, **L** several. Pick the ones to do; they become the
work under PLAYTEST PREP.

## What's already fine
- `[F] Use the Forge` and similar prompts appear over the station you're next to.
- Mini-game hints are short key + phrase pairs, and Esc always leaves.
- The shop sign reads well: a tick, a price and a plain name per service.
- The time-of-day dial, and the food/rest bars.

## Biggest wins

1. **Done.** **What customers want, over their heads (M, every trade).** Osric's "wants a drink" is only a
   line in the Shop panel; nothing in the world shows who's waiting or for what.
   - Proposal: a speech bubble over a waiting customer with the service's icon (tankard, bowl,
     sword, bandage, lockbox), and a patience ring that empties.
   - Only for orders you heard, read through `Perception` (the order is spoken; only those who
     heard know it).
   - The single change that most reduces reading.
2. **Done** (tips under the portrait, rings on the stations). **"Go here next" (M, every trade).** A new player at 8:37 in an empty common room has no idea
   what to do. Proposal: a short first-day goal line in the HUD per trade ("Customers will come to
   the bar. Set your prices (J) while you wait."), and the trade's own stations outlined softly in
   the world until each has been used once.
3. **Done.** **Fewer panels open at the start (S).**
   - The innkeeper starts with Shop, Quests and Heard all open; the Quests list runs off the
     bottom of a 900px screen.
   - The Heard chat box is a large empty black rectangle until someone speaks.
   - Proposal: open only Shop. Heard opens itself (or flashes its tab) when there's something
     to read, and Quests opens when a party member asks for work.
4. **Done** (the hints already drew caps; the prompt over your head now does). **Key caps instead of letters (M, all mini-games).** Draw F / A / D / W / S / WASD / Esc as
   little key icons in `BeginWork` hints and in the `[F]` world prompt. Kids find keys by shape
   faster than by reading "A/D".
5. **Done:** Space is the other action (`WorkInputKind.Act`); A/D only steer or mean less/more. **The same key, the same kind of thing (M, design).** Letters mean different things in
   different games:
   - D: "draw it out" (bellows), "taste it" (seasoning), "set the pin, take up the next" (pinning),
     "latch it" (ratchet).
   - A: "that'll do" (seasoning), "slacken it" (truing).

   Proposal: F is always the main action, A/D always a left/right or less/more, and "I'm done"
   always Space or Enter, with the hint shown as a ✓ icon. That's a decision for you; it
   touches muscle memory from earlier playtests.

## Smaller fixes (S each)

**HUD** (all done)
- The hover tooltip ("Packed earth, 13°C outdoors") follows the cursor everywhere and says
  nothing a player needs. Show it only over things you can use or inspect, or only in dev mode.
- Mood shows a bare number (48) where Food and Rest show percentages; the number also sits on the
  bar's edge. Use a face icon (happy/neutral/sad) instead of a number.
- The quest-giver hint is cut off at the panel's edge ("...who's asked for work to sen"). Wrap it.
- Time left on an order reads "0.2h left": show only the bar, or "a few minutes".

**Top bar** (done)
- The top menu (Menu, Save, Load, Options, Recipes, Screenshot, Controls, Credits) is a lot for a
  child to scan. Fold Save/Load/Screenshot/Credits into Menu; keep Recipes and Controls.

**Inventory** (done)
- Hotbar slots show tiny item sprites. They already had tooltips (this audit missed them); the
  sprites are larger now.

**Inn**
- The inn's interior is dark, and the cask, shelf and larder are hard to tell apart from above.
  Brighten interiors a step (or add a lamp over the bar), and the station outlines in (2) cover
  the rest.

**Title screen** (done, with the stacked logo)
- The menu labels are centred in fixed-width buttons under a left-aligned title, so they look
  ragged. Left-align them.
- "Alpha build": change to "Playtest build", to match the itch page.

**Art leftovers** (from ROADMAP)
- The meditation stone borrows the mason's bench icon.
- The carter's wagon on the street is still a sprite where the tinker's wagon became a box prop.

## Needs a hands-on pass
- Each mini-game's window at play speed: is the target band visible enough, does success/failure
  read without text, and is the window large enough at 1080p and default UI scale?
- First join in Multiplayer: the lobby and the deal, as a newcomer.
- Ideally, watch one child and one non-gamer play the first ten minutes without help, and note
  every time they stop to ask.

## Suggested order
(3) panels, then the small HUD fixes, then (1) order bubbles, then (2) first-day goals and
outlines, then (4) key caps. Decide (5) before (4), since the icons would show the new mapping.
