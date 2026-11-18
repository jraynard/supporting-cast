# Playing on a Steam Deck

The Linux build runs on the Deck as a non-Steam game. The game reads the Deck's controls as a
gamepad, and the right trackpad stays a mouse. Every control can be rebound in **Settings →
Controls**, for the keyboard and the pad alike, and every prompt shows what you've bound for the
device you last touched.

## Installing

1. In Desktop Mode, extract the Linux tarball somewhere under your home folder, say
   `~/Games/HeroesNotIncluded`.
2. In Steam: **Games → Add a Non-Steam Game to My Library → Browse**, pick
   `~/Games/HeroesNotIncluded/HeroesNotIncluded`, and add it.
3. Leave the compatibility tool off: it's a native Linux build, so don't run it through Proton.
4. Go back to Game Mode and launch it from your library.

## Quick deploys from a dev machine

For testing builds, skip the tarball: `tools/deck.sh` publishes the Linux build and copies only
what changed straight into the Deck's install folder over ssh, so a push after a small change
takes seconds and the library shortcut runs it.

Once, on the Deck in Desktop Mode (Konsole):

```sh
passwd                                  # give the deck user a password
sudo systemctl enable --now sshd
```

Once, on your machine: `ssh-copy-id deck@steamdeck.local` (or the Deck's IP). Then:

```sh
tools/deck.sh push            # publish (Release) and send it; --debug for a Debug build
tools/deck.sh tail            # follow the game's log on the Deck while you play
tools/deck.sh logs            # fetch its logs (playtest rounds, crashes) and screenshots into dist/deck-pulled/
tools/deck.sh ssh             # a shell on the Deck
```

`DECK_HOST` (default `deck@steamdeck.local`) and `DECK_DIR` (default `~/Games/HeroesNotIncluded`,
where the shortcut points) choose where it goes. Launch from the library as usual: a game started
over ssh wouldn't get Game Mode's display or Steam Input. Co-op only connects identical builds, so
to host from your machine for a Deck you just pushed, run `dist/deck/HeroesNotIncluded` rather
than `dotnet run`.

## What the game does on a Deck

When the game finds no settings file yet (`~/.config/Heroes_Not_Included/settings.json`) and
spots a Deck, it starts with defaults that suit the screen. It spots a Deck by `SteamDeck=1`,
which Steam sets in Game Mode, or by the board's name in Desktop Mode. The defaults are:

- UI scale 1.2, which makes body text about 20 px tall
- borderless at 1280×800
- a 45 fps cap

Change any of them in **Settings**. 40 and 45 fps are offered for battery life. Once you've saved
settings, the game never changes them again.

Clicking into a text field (your name, the shop's name, chat, a join address) asks Steam for its
on-screen keyboard. If it doesn't appear, press **Steam + X**.

To preview the Deck layout from a desktop, run:

```sh
dotnet run --project src/HeroesNotIncluded -- --new-game --deck   # also --size 1280x800 --ui-scale 1.35
```

These flags never touch your settings file.

## The controller layout

In the game's controller settings (the controller icon on its library page), start from Steam's
**Gamepad with Mouse Trackpad** template, so the Deck is a gamepad and the right trackpad is a mouse.
The game does the rest:

| Deck control | Does |
|---|---|
| Left stick | walk (any direction); at work: steer |
| Right stick | aim (weapon drawn); pick from the wheel |
| A | use, talk, work; hold for the mini-games |
| B | dash clear (weapon drawn); otherwise back, stop, close |
| X | dig; at work: the mini-game's other action |
| Y | draw or sheathe your weapon |
| R2 (RT) | strike; loose a drawn bow |
| L2 (LT) | swing heavy; held, draw a bow |
| L1 (LB) | guard (hold); raised as a blow lands, a parry |
| R1 (RB) | held: the action wheel (panels, your stacks, put down, sleep, build) |
| D-pad left / right | turn the camera |
| D-pad up / down | zoom in / out |
| At work: d-pad, L1, R1 | the empath's six runes |
| View | inventory |
| Menu | the game menu (save, load, settings, controls, back to title) |
| L3 | the controls card |
| Right trackpad | the mouse: point, click to walk there or act |

With a window open (inventory, a chest, a bench, the sign, settings, the menu), the d-pad moves
through it, A picks and B closes it; tooltips show on whatever's picked. The title screen works
the same way.

With the pad, A acts on what's in front of you; touch the trackpad and the pointer counts again.
The back buttons are free: put anything else on them in Steam Input, as keys (Left Shift for
Shift-click looking, say; the controls card lists the rest). Once the layout plays well, export it from the controller settings and commit it to
`tools/steamdeck/`. We can't produce a working file without a Deck.

## Speaking aloud

The Deck's microphones let you talk instead of typing ([VOICE.md](VOICE.md)). Turn it on in
**Settings → Voice**, download the Quick model (60 MB, once), and read the three calibration lines.
Then give Talk a button. Either set a pad button for it on the Voice tab (every pad button already
does something, so it'll share one), or, better, put it on a back grip:

1. In Game Mode, press the Steam button with the game running: **Controller Settings → Edit
   Layout → Back Grip Buttons**.
2. **L4 → Keyboard → O** (Talk: hold it while you speak).
3. **R4 → Keyboard → Backspace** (take back what you just spoke).

The words show on a card at the bottom of the screen for a moment before they're said: tap
**Whisper**, **Say** or **Shout** on the touchscreen to change how loudly, or press L4 again to say
it at once. If your shouts come out as plain words, the calibration will say whether the
microphone is levelling itself; a headset with a microphone does better than the Deck's own in a
noisy room. `tools/deck.sh tail` shows how long each line took to make out ("Heard in … ms").

## Known gaps

- Placing buildings and area tools need a trackpad drag.
- Not yet played on a Deck: the stick deadzone, the wheel and each window's navigation want a
  session on the device.
