# Building this content mod (for coding agents, and people)

Defs, art and sounds layered over Heroes Not Included's own; no code. Defs merge after the game's: a new name
adds, the game's name replaces (every field), `"$patch": "merge"` changes only the fields given (arrays
replaced whole), `"$patch": "remove"` drops it. Read the game's def before changing it. A PNG in `textures/`
at a game sprite's path replaces it; `sounds/sounds.json` adds or replaces sounds by id.

Check after every change: `<game> --check-mod <this folder>` (it lists what you add, change and remove, and
fails on a broken reference or a missing file), then `<game> --new-game --mods examplecontent`.
