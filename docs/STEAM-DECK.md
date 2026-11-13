# Playing on a Steam Deck

The Linux build runs on the Deck as a non-Steam game. There's no native controller support yet:
you play through a Steam Input layout that turns the Deck's controls into keys and a mouse. Every
mini-game uses only F, W/A/S/D, Space and 1–9, so they map cleanly to buttons.

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

Change any of them in **Options**. 40 and 45 fps are offered for battery life. Once you've saved
settings, the game never changes them again.

Clicking into a text field (your name, the shop's name, chat, a join address) asks Steam for its
on-screen keyboard. If it doesn't appear, press **Steam + X**.

To preview the Deck layout from a desktop, run:

```sh
dotnet run --project src/HeroesNotIncluded -- --new-game --deck   # also --size 1280x800 --ui-scale 1.35
```

These flags never touch your settings file.

## The controller layout

Open the game's controller settings (the controller icon on its library page) and build this,
starting from the **Keyboard (WASD) and Mouse** template:

| Deck control | Sends | Does |
|---|---|---|
| Left stick | W A S D | walk |
| Right trackpad | mouse (as a trackpad) | point |
| Right trackpad click, or R2 | left click | walk there or act; select |
| L2 | right click | an item's menu |
| A | F | use, talk, work; hold for the mini-games |
| B | Esc | stop, close, put the work down |
| X | Space | the mini-game's other action |
| Y | Tab | inventory |
| L1 / R1 | Q / E | turn the camera |
| D-pad up / down | = / − | zoom in / out |
| D-pad left / right | [ / ] | level down / up |
| L4 | Left Shift | held: Shift-click looks at someone |
| R4 | Left Ctrl | held: one level up while building |
| L5 / R5 | J / T | shop / talk |
| View button | H | the controls card |
| Menu button | Esc | |
| Left trackpad | radial menu: 1 2 3 4 5 6, K, L, G, N, Z, X | hotbar and runes, quests, log, gift, sleep, dig, drop |

When you walk with the stick, F acts on what's in front of you, not on the spot where the
trackpad cursor was left. Touch the trackpad again and F goes back to what's under the pointer.

Once the layout plays well, export it from the controller settings and commit it to
`tools/steamdeck/`. We can't produce a working file without a Deck.

## Known gaps

- There's no native gamepad support or menu navigation with the d-pad. Menus need the trackpad.
- Tooltips only show on hover. With the trackpad that works, but it's slow.
- Placing buildings and area tools need a trackpad drag (hold R2).
