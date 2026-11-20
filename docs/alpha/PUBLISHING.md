# Publishing playtest builds

Builds go to a **restricted itch.io page** (testers install through the itch app, which keeps them
on the latest version), and a webhook announces each one in the playtest **Discord**'s #builds.
Discord can't host the builds themselves: they're about 50 MB each, and an unboosted server
takes 10 MB attachments. `.github/workflows/playtest-release.yml` does the work; `tools/package.sh`
builds the packages (it runs locally too, see its header).

## Releasing a build

1. Bump `<Version>` in `Directory.Build.props` (`0.1.0-playtest.2`, ...). The minor number is
   the milestone and N the build: `0.2.0-playtest.1` when M4 opens, `0.3.0-playtest.1` at M5, N
   back to 1 each time; `1.0.0` is the Steam release. Compatibility isn't in the version: saves
   (`SaveGame.FormatVersion`, old formats read forward), the character roster
   (`CharacterRoster.Version`), replays (`ReplayFile.FormatVersion`) and the relay
   (`RelayProtocol.Version`) each carry their own, and co-op needs the identical build anyway.
2. Add a `## 0.1.0-playtest.2` section at the top of `docs/alpha/CHANGELOG.md`, written for
   testers. It becomes the Discord post. The workflow refuses to publish without it. If this
   build breaks something testers keep (older saves no longer load, characters reset, kept
   replays no longer play), say so in a **Compatibility** line at the top of the section.
3. Commit, push, then tag and push the tag:

   ```sh
   git tag v0.1.0-playtest.2 && git push origin v0.1.0-playtest.2
   ```

The run tests, packages, checks Windows and signs the Mac app, pushes the three channels to itch
and posts to #builds. Pushes to `main` without a tag only test and package (the packages are on
the run's page for a day or so, as artifacts). To republish from a branch: Actions -> Playtest
release -> Run workflow, tick "publish".

The Mac app keeps only native code in `Contents/MacOS` and everything else in `Contents/Resources`
behind symlinks (`tools/package.sh`, `make_app`). Anything else there would be signed through
extended attributes, which the trip through Linux and butler drops; the `macos-sign` job checks a
copy without them.

## One-time setup

### itch.io
1. Make an itch.io account if you haven't. Create a new project:
   - Title "Heroes Not Included", URL `heroes-not-included`.
   - Kind of project: **Downloadable**.
   - Uploads: none (butler adds them).
   - Cover image: `packaging/icon-512.png` for now.
   - Visibility: **Restricted**. Under it, pick **password**: simplest for testers, and for kids.
     You can also hand out download keys per tester instead.
   - Save as a draft, then publish it (Restricted keeps it private).
2. Account settings -> API keys -> Generate new API key.
3. On GitHub (repo Settings -> Secrets and variables -> Actions):
   - Secret `BUTLER_API_KEY`: that key.
   - Variable `ITCH_TARGET`: `<your-itch-username>/heroes-not-included`.
4. After the first publish, edit the project's uploads once: each of the three (windows, linux,
   mac) should be marked for its platform. itch guesses from the channel name, so check.

### GitHub
- Secret `SUBMODULE_TOKEN`: only if `jraynard/narsil` is private. Use a fine-grained token with
  read-only Contents access to it.
- Secret `DISCORD_WEBHOOK_URL`: see below.
- Actions minutes: a private repo has a monthly free allowance, and macOS minutes count ten times.
  That's why the Mac job runs only for a publish.

### Discord
**Server icon:** `packaging/icon-512.png`.

**Roles:**
- `Dev` (you): everything.
- `Playtester`: can see the playtest channels. Give it through an invite link, or through
  Onboarding (Server Settings -> Onboarding) with a "Here to playtest?" question that grants it.

**Channels** (make the Info ones read-only for `@everyone`, posting for `Dev` only):

| Category | Channel | What for |
| --- | --- | --- |
| Info | `#welcome-and-rules` | What the game is, how to behave, that builds are private. |
| Info | `#how-to-install` | The itch page link and its **password** (only `Playtester` sees it), plus a pinned copy of README-ALPHA. |
| Info | `#builds` | The webhook posts each build here. |
| Info | `#announcements` | Playtest sessions, news. |
| Playtest | `#bug-reports` | A **Forum** channel. Tags: Innkeeper, Cook, Blacksmith, Healer, Tinker, Multiplayer, Crash, Looks wrong. Post guidelines: version line from the title screen, what you did, what happened, attach the newest log. |
| Playtest | `#feedback` | What's fun, what's confusing, what's a chore. |
| Playtest | `#looking-for-game` | Arranging sessions; who's hosting. |
| Playtest | `#screenshots` | F12 in game. |
| Voice | The Common Room, The Back Room | Playing together. |

**The webhook:** `#builds` -> Edit Channel -> Integrations -> Webhooks -> New Webhook. Name it
"Heroes Not Included", give it the icon, Copy Webhook URL, and paste that into the
`DISCORD_WEBHOOK_URL` secret. Anyone with the URL can post as it: keep it only in the secret.

**Invite:** make a non-expiring invite (to `#welcome-and-rules`), and put it in `BuildInfo.Community`
(`src/HeroesNotIncluded/BuildInfo.cs`) so the title screen's Report a Bug shows it.

**Kids and safety:**
- Discord's minimum age is 13: younger testers play beside a parent who holds the account.
- Server Settings -> Safety Setup:
  - Explicit image filter on for all members.
  - Verification level Medium or higher.
  - DM spam filter on.
- **Events:** schedule each playtest session as a Discord Event, so it shows at the top of the
  server, with a reminder.

## Steam

Every published build also goes to Steam once `STEAM_APP_ID` is set: the `steam` job in
`playtest-release.yml` turns the same three packages into depots (`tools/steam_package.sh`) and
uploads them with `steamcmd` to the **testing** branch. Going live on the default branch is always
a click in Steamworks, by hand. The game's files are the itch build's bytes; only a `steam.json`
beside the game (on the Mac, beside the `.app`) makes a copy Steam's
(`src/HeroesNotIncluded/Platforms/SteamConfig.cs`), so itch and Steam players of one version can
play together. Valve's libraries are in `external/steamworks/` (its README says how to update).

### Trying it from a checkout

With the Steam client running:

```sh
dotnet run --project src/HeroesNotIncluded -- --steam 480 --new-game --name Arin
```

480 is Valve's public test app (Spacewar). The log says `Steam: started for app 480 (dev), as
<your name>`, and a few seconds later whether the overlay hooked the game.

Steam only draws its overlay (Shift+Tab) over games it launches itself, which is also what the
invite dialog and the Wishlist button open. To have it from a terminal on Linux, switch on its
Vulkan layer and preload its renderer, as Steam would:

```sh
ENABLE_VK_LAYER_VALVE_steam_overlay_1=1 LD_PRELOAD=$HOME/.local/share/Steam/ubuntu12_64/gameoverlayrenderer.so \
  dotnet run --project src/HeroesNotIncluded -- --steam 480 --new-game --name Arin
```

The log then says `Steam: the overlay is on (Shift+Tab)`. A Steam copy always opens its window
through X11 (XWayland on a Wayland desktop, `GameHostOptions.PreferX11`): the overlay hooks X11
input, and over a native Wayland window it loads but never sees Shift+Tab, which then reaches the
game instead. Elsewhere (Windows, the Deck): Steam →
Add a Game → Add a Non-Steam Game, pick the built `HeroesNotIncluded`, and set its launch options to
`--steam 480`.

### One-time setup

1. **The apps.** In Steamworks, the game's app and its demo (a demo is an app of its own, linked to
   the game). Give each three depots, Windows, Linux and macOS, with matching OS settings. Depot IDs
   are usually the app ID plus 1, 2 and 3; if not, set them below.
2. **Launch options**, for each app:
   - Windows: `HeroesNotIncluded.exe`.
   - Linux: `HeroesNotIncluded`, with the Steam Linux Runtime 3.0 ("sniper") chosen under
     Installation → Linux Runtime.
   - macOS: `HeroesNotIncluded.app`.
3. **A testing branch** on each app (Builds → Branches), password-protected if testers share it.
4. **Rich presence:** upload `packaging/steam/rich_presence.vdf` under Community → Rich Presence
   Localization, for the game and the demo. Upload it again whenever a line changes.
5. **A builder account:** a separate Steam account with only Edit App Metadata and Publish App
   Changes To Steam permissions on these apps, Steam Guard on. On a machine of your own, run
   `steamcmd +login <builder> +quit` once and enter the Steam Guard code. Then base64-encode its
   `config/config.vdf` (under `~/.steam/steamcmd/` or wherever steamcmd lives), which keeps that login.
6. **On GitHub:**
   - Variables: `STEAM_APP_ID`, `STEAM_DEMO_APP_ID`, and only if they aren't app + 1, 2, 3:
     `STEAM_DEPOT_WINDOWS`, `STEAM_DEPOT_LINUX` and `STEAM_DEPOT_MAC`, and the same with `STEAM_DEMO_`.
   - Secrets: `STEAM_USERNAME` (the builder) and `STEAM_CONFIG_VDF` (the base64 above). If Steam
     asks for a code again later, redo the login and replace the secret.

### Releasing

The same tag as for itch. When the run finishes, the build is live on the testing branch. To put it
in front of everyone, in Steamworks: Builds → set it live on **default**.
