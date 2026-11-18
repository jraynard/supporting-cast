# The ModKit

Templates for Heroes Not Included mods (docs/MODDING.md), for `dotnet new`:

```sh
dotnet new install modkit/templates/hni-trade      # and modkit/templates/hni-content
dotnet new hni-trade -n Glassblower -o mods/glassblower
dotnet new hni-content -n LongNights -o mods/longnights
```

- `hni-trade`: a trade, whole: its def, a tool, a hand mini-game (the sim half), how it's drawn and sounds (the
  game half), a bot that plays it and a drill (`--drill <id>`). Builds and passes `--check-mod` as it stands.
- `hni-content`: defs alone (and art, sounds): new things, a patched price, a town.

Each carries an `AGENTS.md` for a coding agent working in the mod's own folder. In this repository the
skills in `.claude/skills/` (`new-trade`, `new-minigame`, `content-mod`, `new-map`) go further.
`modkit/check.sh` makes a mod from every template, builds it and checks it: run it after changing the mod API.
