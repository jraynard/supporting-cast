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

## Known gaps

- Placing buildings and area tools need a trackpad drag.
- Not yet played on a Deck: the stick deadzone, the wheel and each window's navigation want a
  session on the device.
