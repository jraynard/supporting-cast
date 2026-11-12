# Heroes Not Included

A multiplayer fantasy village where the players are the people a tabletop party usually walks
past: the innkeeper, the blacksmith, the healer, the quest broker. Everyone has their own trade to
master, the village has to keep running, and someone may be working against it.

It's an early prototype. What runs today is the scaffold: a generated village with an AI population
that you can walk into alone or with friends. Trades, adventurers and traitors are next. See
[docs/DESIGN.md](docs/DESIGN.md) for where it's going and [docs/ROADMAP.md](docs/ROADMAP.md) for the plan.

Built in C#/.NET 10 on the [Narsil](external/narsil) engine, which runs on BRUTAL (GLFW + Vulkan +
Dear ImGui). Forked from DragonWorld.

## Running

Requires the .NET 10 SDK and a Vulkan 1.3 GPU.

```sh
git submodule update --init
dotnet run --project src/HeroesNotIncluded
dotnet test
```

Two players on one machine:

```sh
dotnet run --project src/HeroesNotIncluded -- --host --name Arin
dotnet run --project src/HeroesNotIncluded -- --join 127.0.0.1 --player-id bea --name Bea
```

| Input | Action |
| --- | --- |
| WASD, left click | Walk, walk to |
| F (hold) | Use, work |
| Z (hold) | Dig |
| X, N | Put down, sleep |
| Tab, 1–4 | Inventory, pack to hand |
| Q/E, wheel | Turn, zoom |
| `[` `]`, Home | Cutaway level, all levels |
| `\` | Show roofs |
| F5, F7, F8 | Reload art, shaders, defs |

## Layout

```
external/narsil/          Narsil engine (git submodule)
src/HeroesNotIncluded.Sim/   headless, deterministic simulation
src/HeroesNotIncluded/       the game: rendering, UI, networking
tests/                    sim and game tests (headless)
data/defs/                all content, as JSON
data/textures/            sprites
tools/                    art generators, packaging
docs/                     design, roadmap, art pipeline
```

## Credits & licensing

Built with BRUTAL, a game development framework by Ahwoo Limited, used under the BRUTAL License
v1.0: the logo splash, the copyright notice in the Credits window, `NOTICE.txt` and the `licenses/`
folder ship with every build (`external/narsil/docs/LICENSING.md`). The interface font is Fira Sans
(SIL Open Font License 1.1).
