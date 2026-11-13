# Heroes Not Included

*Somebody has to keep the lights on.*

A multiplayer fantasy village where the players are the people a tabletop party usually walks
past: the innkeeper, the cook, the blacksmith, the healer, the tinker. Parties of adventurers
come through town with silver, demands and wounds, and treat everyone like scenery. The village
has to keep them alive and paying long enough to see out the season, and, before long, someone
in it will be working against everyone else.

## Where it stands

An early prototype, played in closed playtests. A round today is a whole season:

- **Five trades, each its own game.** Pouring and mixing at the bar, a stew pot in the kitchen,
  heat and hammer at the forge, an empath who draws wounds into themselves and weaves runes, a
  tinker with lockwork, wagons and curios. AI villagers take the trades nobody picks.
- **Adventurers** come in parties, read your price sign, line up, order aloud and go without if
  it's too dear. Quests have a hidden truth, and how well the town kitted a party out decides
  whether it comes back.
- **Three days** of a season, judged on Renown against Dread: raids (and a bridge they pull
  down), fire, a shortage, a fever and a dragon, then the festival in the square and a reveal.
- **Millbrook**, a hand-made village with fireplaces, a stone bridge and an inn that lets rooms.
  Players and villagers come as humans or elves.
- **Solo or together:** alone with AI villagers, or co-op over a network or a relay. Bots stand in
  for players at every trade, which is how the scenario tests run a season unattended. Plays on a Steam Deck.

Traitors are next (M4, "Whispers"). [docs/DESIGN.md](docs/DESIGN.md) is where it's going,
[docs/ROADMAP.md](docs/ROADMAP.md) what's next and [docs/HISTORY.md](docs/HISTORY.md) what's
been built.

Built in C#/.NET 10 on the [Narsil](external/narsil) engine, which runs on BRUTAL (GLFW + Vulkan +
Dear ImGui). Forked from DragonWorld.

## Running

Requires the .NET 10 SDK and a Vulkan 1.3 GPU.

```sh
git submodule update --init
dotnet run --project src/HeroesNotIncluded                                   # the title screen
dotnet run --project src/HeroesNotIncluded -- --new-game --name Arin --trade Blacksmith   # straight into a round
dotnet test -c Release
```

Two players on one machine:

```sh
dotnet run --project src/HeroesNotIncluded -- --host --name Arin
dotnet run --project src/HeroesNotIncluded -- --join 127.0.0.1 --player-id bea --name Bea
```

More flags (`--bots N`, `--folk Elf`, `--screenshot`, `--bench`, `--check-art`...) are in
[CLAUDE.md](CLAUDE.md). Playtesters get packaged builds: see [docs/alpha](docs/alpha).

## Playing

| Input | Action |
| --- | --- |
| WASD, left click | Walk, walk to and use |
| F (hold) | Use or work on what's in reach; your stations start their games |
| X, N | Put down, sleep |
| Tab, 1–4 | Inventory, a stack from your pack to hand |
| J | Your shop's price sign |
| Q/E, wheel | Turn, zoom |
| `[` `]`, `\` | Look up or down a floor, show or hide roofs |
| H | Every other key |

Hungry? The inn serves meals at its serving hatch: F there to order, and wait in its line.

## Layout

```
external/narsil/             Narsil engine (git submodule)
src/HeroesNotIncluded.Sim/   headless, deterministic simulation
src/HeroesNotIncluded/       the game: rendering, UI, networking
tests/                       sim, game and scenario tests (headless)
data/defs/                   all content, as JSON (maps, buildings, trades, the season)
data/textures/               sprites, drawn by the generators in tools/
tools/                       art and map generators, packaging
art/                         the brand (logo, guide), map references
docs/                        design, roadmap, history, art pipeline, Steam Deck, playtest notes
```

## Credits & licensing

Built with BRUTAL, a game development framework by Ahwoo Limited, used under the BRUTAL License
v1.0: the logo splash, the copyright notice in the Credits window, `NOTICE.txt` and the `licenses/`
folder ship with every build (`external/narsil/docs/LICENSING.md`). The interface font is Fira
Sans and the title lettering Cinzel, both under the SIL Open Font License 1.1.
