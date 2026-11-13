# Publishing playtest builds

Builds go to a **restricted itch.io page** (testers install through the itch app, which keeps them
on the latest version), and a webhook announces each one in the playtest **Discord**'s #builds.
Discord can't host the builds themselves: they're about 50 MB each, and an unboosted server
takes 10 MB attachments. `.github/workflows/playtest-release.yml` does the work; `tools/package.sh`
builds the packages (it runs locally too, see its header).

## Releasing a build

1. Bump `<Version>` in `Directory.Build.props` (`0.1.0-playtest.2`, ...).
2. Add a `## 0.1.0-playtest.2` section at the top of `docs/alpha/CHANGELOG.md`, written for
   testers. It becomes the Discord post. The workflow refuses to publish without it.
3. Commit, push, then tag and push the tag:

   ```sh
   git tag v0.1.0-playtest.2 && git push origin v0.1.0-playtest.2
   ```

The run tests, packages, checks Windows and signs the Mac app, pushes the three channels to itch
and posts to #builds. Pushes to `main` without a tag only test and package (the packages are on
the run's page for a day or so, as artifacts). To republish from a branch: Actions -> Playtest
release -> Run workflow, tick "publish".

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
