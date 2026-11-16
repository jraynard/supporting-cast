# Social accounts

Setting up and running the game's accounts. Why and when we post is [MARKETING.md](MARKETING.md).

## The rules

- **Separate from personal accounts**, all signed up with the game's own Google account (the
  Gmail made for it). Keeps your own life apart, lets a helper in later without sharing your
  logins, and the lot can be handed over if it ever needs to be.
- **A password manager** (Bitwarden is free), a different password for each.
- **Two-factor login on every one.** Your own phone number for verification is fine.
- **The same handle everywhere:** `heroesnotincluded`, or one fallback used everywhere it won't fit.

## What to upload

| What | File |
|---|---|
| Profile picture (everywhere) | `art/brand/emblem.png` |
| YouTube banner | `art/brand/banner-youtube.png` |
| X and Bluesky header | `art/brand/banner-1500x500.png` |

All made by `tools/brand_logo.py`; if the logo changes, `--banners-only` rebuilds the banners.

## Bios

The Discord invite is `https://discord.gg/GxkCEQ8N2h` (also in `BuildInfo.cs`). Check in Discord
that it never expires (Server Settings → Invites).

**Short bio** (most profiles):

Every legend needs someone to pour the drinks. A co-op fantasy village game: run the inn, the
forge, the kitchen. Heroes not included. 🍺 Discord: https://discord.gg/GxkCEQ8N2h

**Shortest** (tight limits):

> The heroes get the glory. You get the tab. Co-op village game in development.

**YouTube description:**

> Every legend needs someone to pour the drinks.
>
> Heroes Not Included is a co-op fantasy village game in development. You and your friends are the
> people the heroes walk past on the way to glory: the innkeeper, the smith, the cook, the healer
> and the tinker. Keep the adventurers fed, mended and paying, weather the raids, fires and worse,
> and reach the festival with the town still standing. Nobody will say thank you.
>
> Here you'll find clips from our playtests, development updates, and whatever went wrong at the
> inn this week.
>
> Come say hi on Discord: https://discord.gg/GxkCEQ8N2h

Once the Steam page is live, add "Wishlist on Steam: [link]" near the top of each, and make it
the first link.

## The accounts

### Google (the game's email) — done

### YouTube — done
1. Signed in to YouTube with the game's Google account: profile picture → **Create a channel**,
   name "Heroes Not Included", handle `@heroesnotincluded`.
2. **studio.youtube.com → Customization → Profile:** banner, picture, the YouTube description,
   and the Discord invite under **Links**. **Publish**.

### TikTok — done
1. Signed up with **email** (the game's), not "Continue with Google".
2. Username `heroesnotincluded`, display name "Heroes Not Included", emblem, short bio.
3. Stay a **personal/creator account**. A "Business account" loses most of the popular music.

### Bluesky - done except for custom domain
1. Sign up with the game's email.
2. **Use the domain as the handle**, `@heroesnotincluded.com`:
   - Bluesky: **Settings → Account → Handle → "I have my own domain"**. It shows a DNS record
     (a `TXT` record named `_atproto`).
   - At the domain registrar, add that record to heroesnotincluded.com.
   - Back in Bluesky, **Verify**. It can take a few minutes.
3. Emblem, header, short bio.

### X
1. Sign up with the game's email.
2. Handles are 15 characters at most, so `heroesnotincluded` won't fit: `heroesnotinc` or
   `HNIgame`. Display name "Heroes Not Included".
3. Emblem, header, short bio.

### Reddit - user created, subreddit created
Reddit dislikes brand accounts that only promote, so:
1. **Reserve** `u/heroesnotincluded` so nobody else takes it, and leave it quiet.
2. **Post as yourself, the developer**, from a personal account or a new one (say
   `u/JustinMakesGames`): "I'm making a game where you're the shopkeepers the heroes ignore.
   Here's our inn on fire."
3. Start commenting in r/IndieDev and similar now: many subs want some account age and karma
   before you can post.

### Optional
- **Facebook page:** worth it if family and their friends live on Facebook. Made from your
  personal account, but it shows only its own name.
- **Instagram:** can share clips with TikTok. Skip unless you already use it.

## The domains

At the registrar, forward ("URL forwarding") heroesnotincluded.com and herosnotincluded.com to
the Discord invite: an address that's easy to say out loud. Later it points at the Steam page or
a small site.

- [ ] heroesnotincluded.com forwards to Discord
- [ ] herosnotincluded.com forwards to Discord

## Handles

| Site | Handle |
|---|---|
| YouTube | `@heroesnotincluded` |
| TikTok | `@heroesnotincluded` |
| Bluesky | |
| X | |
| Reddit (reserved) | |
| Reddit (dev) | |

## Posting

- Vertical clips, 15 to 30 seconds, one moment each. The same clip goes on YouTube Shorts and
  TikTok.
- One a week is plenty. Early posts get few views; that's normal.
- The clips come from recorded playtests: see MARKETING.md, Phase 1.
