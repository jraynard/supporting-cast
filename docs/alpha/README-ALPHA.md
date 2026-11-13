HEROES NOT INCLUDED: PLAYTEST BUILD
===================================

Thanks for testing! This is an early build: expect rough edges, placeholder art and bugs. You
play one of the village's trades (innkeeper, cook, blacksmith, healer, tinker) while parties of
adventurers come through town wanting things. We most want to hear what's confusing, what's
fun, what's a chore, and anything that breaks.


WHAT YOU NEED
-------------
- 64-bit Windows 10/11, 64-bit Linux, or a Mac with Apple Silicon (M1 or later) on macOS 13+.
  Intel Macs aren't supported.
- A graphics card with Vulkan 1.3 drivers (most cards from 2016 on). If the game won't start,
  update your graphics driver first.
- Nothing else: .NET is bundled.


INSTALLING
----------
Easiest: the itch.io app (https://itch.io/app). Sign in, open the game's page with the link and
password from the Discord's #how-to-install channel, and click Install. The app keeps the game
up to date by itself, which matters: everyone playing together needs the same version.

Or download the zip/tarball for your system from the same page:
Windows: unzip anywhere and run HeroesNotIncluded.exe. Windows SmartScreen will warn that the
  app is unrecognised (it isn't code-signed yet): click "More info", then "Run anyway". A console
  window opens alongside the game; it shows the log, and you can ignore it.
Linux: extract the tarball and run ./HeroesNotIncluded from that folder.
Steam Deck: the Linux build, added to Steam as a non-Steam game. The controller layout and
  setup are in STEAM-DECK.md (docs/STEAM-DECK.md in the source).
macOS: unpack it and drag HeroesNotIncluded.app wherever you like. It isn't notarized by Apple
  yet, so the first launch of a downloaded copy is blocked: open Terminal and run
      xattr -dr com.apple.quarantine /path/to/HeroesNotIncluded.app
  (type the first part, then drag the app onto the Terminal window for its path), then open it
  as usual. Or try to open it once, then go to System Settings -> Privacy & Security and click
  "Open Anyway".

If the game won't start, run it from a terminal with --check-gpu and send us what it prints:
  Windows: HeroesNotIncluded.exe --check-gpu
  macOS:   /path/to/HeroesNotIncluded.app/Contents/MacOS/HeroesNotIncluded --check-gpu

Your version is at the bottom of the title screen ("Playtest build v0.1.0-playtest.N"). The host
refuses a different build and says which one it runs.


PLAYING TOGETHER
----------------
Host game -> New round opens a lobby: friends join, say which trades they want, and the host
deals the round. Host game -> A saved village opens one of the host's saves. Friends pick
Join game and either pick the village from the list (same network) or type the host's address.

Over the internet, the host's computer has to be reachable. Two ways:

1. Easiest: a virtual LAN. Everyone installs Tailscale (https://tailscale.com, free for small
   groups) and joins the same network. Joiners then type the host's Tailscale address
   (100.x.y.z, shown in the Tailscale app). No router settings needed.

2. Port forwarding: the host forwards TCP port 47060 on their router to their computer, and
   joiners type the host's public IP address. (UDP 47061 only lists games on a local network;
   it doesn't need forwarding.)

The first time you host on Windows, the firewall asks whether to allow Heroes Not Included: allow
it (at least on private networks), or nobody can connect.

Good to know:
- Only the host saves. If the host leaves, the session ends.
- A player who leaves becomes an AI villager and carries on their trade.
- Saves from one playtest build may not load in the next.


CONTROLS
--------
WASD or left-click: walk. Click something to walk over and use it.
F (hold): use or work on what's in reach. Your trade's stations start their own little games;
  each one shows its keys while it's open. In them, A/D steer and Space does the other thing
  (taste, latch, draw it out); Esc steps away.
X: put down what you hold. N: sleep.
Tab: inventory. 1-4: take a stack from your pack.
Q/E or the mouse wheel: turn and zoom.  [ ]: look up or down a floor.  \: show or hide roofs.
J: your shop's price sign. Hungry: F at the inn's serving hatch orders a meal; wait in its line.  F12: screenshot (Shift+F12 without the interface).


REPORTING BUGS
--------------
Post in the Discord's #bug-reports forum (the title screen's Report a Bug says the same). Say:
- what you were doing and what went wrong,
- your version (bottom of the title screen), your trade, and whether you were hosting or joining,
- and attach the newest log file, plus any crash-*.txt or desync-* files from the same folder.

The files are in the game's folder in your user profile (Report a Bug -> Open logs folder, or
Settings -> Interface -> Open logs folder, opens it):
  Windows: %APPDATA%\Heroes_Not_Included\logs
  Linux:   ~/.config/Heroes_Not_Included/logs
  macOS:   ~/.config/Heroes_Not_Included/logs (a hidden folder: in Finder, Go -> Go to Folder...)
Screenshots are next to it, in "screenshots", and saves in "saves".
