# Sound

How the game sounds, and how to add to it. The engine side is Narsil's `Narsil.Audio`
(`external/narsil/README.md`); this is the game's half.

## How it fits together

- **`GameAudio`** (`src/HeroesNotIncluded/Audio/GameAudio.cs`) holds the one `AudioEngine`. It
  opens at start-up over OpenAL Soft, and runs silent with no device or with `--mute`. It applies
  the volumes from Settings, the Sound tab.
- **`data/sounds/sounds.json`** maps each **sound id** to its files and how they play. Game code
  names ids only, as constants in `Audio/Sounds.cs`, so files can change under them freely.
- **`SoundDirector`** (in the village) plays the world as the local player perceives it:
  - fights and dragon fire you can see, or hear through a wall (muffled)
  - lines your character heard, as babble
  - the beds around the camera: wind, birds or crickets, a full room
  - the nearest hearths, a blaze and the river, placed where they are
  - a bell for danger and coin when your purse changes
  - the music's mood
- **`WorkSounds`** plays the mini-games from the session in hand: the anvil's ring graded by the
  blow, the forge and bellows, the quench, the pot, pins clicking, runes.
- **Secrets:** sound obeys the same rule as the screen. Other characters are only heard through
  `PerceptionView`: `Heard()` for speech, and `CanSeeCell` or `SpeechSystem.Hear` for where a
  sound comes from. Hidden state never makes a noise: a strongbox's trap, a binding, the Director's
  crises before anyone knows of them. The danger music starts from what a player can know: a
  Danger message, a fight seen or heard, a dragon in sight, a blaze.
- **Positions** are in cells, with each level `SoundDirector.LevelHeight` up. The listener hangs
  over the camera's target, higher when zoomed out, and is turned to match the screen so left is
  left.

## Adding a sound

1. Put the file(s) in `data/sounds/sfx` (one-shots), `amb` (loops) or `voice`: `.ogg` (Vorbis)
   or `.wav`, mono for anything placed in the world. Several takes of one sound are variants:
   name them `thing_1.ogg`, `thing_2.ogg` and list `"sfx/thing_*.ogg"`.
2. Give it an entry in `sounds.json`:

   | field      | meaning                                                                  |
   |------------|--------------------------------------------------------------------------|
   | `files`    | paths relative to `data/sounds`; wildcards take every match               |
   | `bus`      | `sfx` (default), `ui`, `ambience`, `voice`, `music`: which slider          |
   | `gain`     | 0..1 (a little over is fine)                                              |
   | `pitch`    | `[low, high]`: a random pitch each play, so repeats don't grate            |
   | `range`    | cells: placed in the world, fading out by this distance (0: in your ears) |
   | `near`     | full volume within this (default a fifth of the range)                    |
   | `max`      | most copies at once; a new one cuts the oldest (default 8)                |
   | `cooldown` | seconds before it can play again                                          |
   | `priority` | when the 48 voices run out, higher takes from lower (default 1)           |
   | `stream`   | decode while playing: long loops                                          |

3. Name it in `Audio/Sounds.cs` (and its `All` list), then play it:
   - `GameAudio.Engine.Play(id, at, gain, pitch, muffle, delay)` for a one-shot
   - `Hold(key, id, gain, at)` every frame for a loop that lasts while you hold it
4. `dotnet run --project src/HeroesNotIncluded -- --check-art` fails on an id with no entry, a
   missing file or one that won't decode (`SoundAudit`). `SoundTests` checks the same.

From a checkout, edits to `data/sounds` are heard at once (Debug watches the folder; F5 forces a
reload). `SC_SOUND_LOG=1` logs every sound as it starts: handy for checking a hook fires.

## Synthesized sounds

`python3 tools/sfx_synth.py` writes the first set from scratch with numpy and ffmpeg: anvil
strikes by modal synthesis, coins, the bell, hisses, the pot, clicks, chimes, the babble syllables
and the ambience beds. `--preview` adds `.cache/sounds/preview.ogg` with every sound in a row, and
`--only anvil` makes just those. Retune a sound there and run it again; it's seeded, so unchanged
sounds come out the same. These are ours outright.

## Recorded sounds

Synthesis is thin for some sounds, so CC0 recordings from OpenGameArt play these:
- clashes, swings, hits and the bow
- the dragon's roar, wings and breath
- wind, birds, crickets and the stream
- the fireplace, the steam of the quench and the boiling pot
- cloth, coins, glass, knocks and lock clicks
- the battle din

Their files are `*_rec*.ogg`, and the synthesized versions stay beside them to swap back to in
`sounds.json`. `tools/import_sounds.py --from DIR --ledger DIR/ledger.json` brings them in from
packs unpacked outside the repo. It trims, levels, loops and converts each file, and writes
`data/sounds/CREDITS.md`; its `SPEC` says which file becomes which. The rules:

- **Effects:** CC0 only (the importer refuses anything else). **Music:** CC0 or CC-BY 3.0/4.0,
  credited. Never CC-BY-SA, NC, or "free for personal use".
- No assets from, or named after, the games that inspired this one.
- Anything CC-BY is credited in `NOTICE.txt` and the Credits window (`Ui/GameCredits.cs`) as
  well as CREDITS.md.
- Downloaded packs are untrusted data: unpack them outside the repo, and only the converted
  files come in.

Still synthesized because nothing CC0 was good enough: the inn's murmur, the bell, a blaze, and
an arrow's impact. The roadmap lists freesound candidates to fetch by hand.

## Music

Each mood's playlist reads two folders:

- `data/sounds/music/<mood>_<name>.ogg`: **our own songs**, kept in git.
- `data/sounds/music/free/<mood>_<name>.ogg`: free placeholders that `python3
  tools/fetch_music.py` downloads (CC-BY ones by Kevin MacLeod and Tausdei, and CC0 ones by
  RandomMind). Run it once per checkout or build machine. These aren't kept in git; without them
  there's no music, and `--check-art` only warns.

The moods are `title`, `day`, `night`, `danger`, `festival` and `reveal`. Each mood shuffles its
tracks with a quiet `gap` (seconds, `[low, high]`) between them, and crossfades when the mood
changes. To replace the placeholders, drop your songs in `music/` with a mood prefix. Then
remove the free ones (the folder or the script's entries) and their credits: no code changes.

## Voices

Speech is babble for now (`Audio/Babble.cs`). Its syllables come from a small formant synthesizer
in `sfx_synth.py`: a breathy glottal buzz through four gliding resonators. They're made in three
registers (deep, middling, high), with seven onset consonants (none, b, d, g, m, s, l) and five
vowels, so each register keeps a natural throat instead of being a pitched-up deep voice.

- **The words:** each syllable is picked from the line's own letters, so different words sound
  different.
- **The speaker:** each speaker keeps their own register, pitch, loudness and pace all game.
- **The line:** each line drifts down in pitch, stresses the first syllable of longer words, and
  falls at the end, or rises for a question.
- **Placement:** voices come from where they were said, so near ones are loud and far ones soft.
  Shouts are louder, whispers soft, and lines through a wall muffled.
- **Crowds:** at most `SoundDirector.MaxTalking` lines babble at once, the nearest; the rest of
  a busy room is the inn's murmur.
- **Strangers:** a voice you didn't recognise takes its voice from the line, so babble never
  gives away who spoke.

Real voices (text to speech) are on the roadmap.
