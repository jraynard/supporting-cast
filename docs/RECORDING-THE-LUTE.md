# Recording the lute from a guitar

The musician's lute is synthesized for now (`mods/musician/tools/musician_assets.py`). A real acoustic guitar, played
lute-like and recorded note by note, will sound far better. This is how to record it so the samples drop straight
into the game. Your part is steps 1 to 5; trimming, levelling and wiring them in is mine.

## What the game needs

The lute plays the D major scale over two octaves, **D4 to D6** (the keys are D E F# G A B C# D, and the octave key
goes up from there). Sharps and flats are a semitone off a scale note, so the best set is **every semitone** in that
range: 25 notes. Each is played back at its own pitch, so nothing is stretched and every note keeps its own tone.

| | Notes | Where on a guitar in standard tuning |
|---|---|---|
| Low octave | D4 D#4 E4 F4 F#4 G4 G#4 A4 A#4 B4 C5 C#5 | B string, frets 3 to 14, or the G string from fret 7 up |
| High octave | D5 D#5 E5 F5 F#5 G5 G#5 A5 A#5 B5 C6 C#6 | high E string, frets 10 to 21, or the B string from fret 15 up |
| Top | D6 | high E string, fret 22 (or the 10th-fret harmonic at a pinch) |

Pick one string for a run of notes and stay on it, so the tone doesn't jump halfway up the scale. The B and high E
strings suit a lute best: they're thinner and brighter than the wound strings.

Nice to have, if there's time: the five open strings of the lute's tuning (D4, G4, B4, E5, A5) played open or as
near open as you can, and one strummed chord, down and up (D major), for comparison. The game builds its chords from
the single notes, so these aren't needed.

## 1. The guitar

- **Acoustic, not electric.** A clean electric through an amp sounds like an electric guitar; an acoustic's body is
  what passes for a lute's.
- **Fresh-ish strings help, old ones are fine.** Lutes are gut-strung and mellow: slightly dead strings are closer
  than brand-new zingy ones.
- **Tune it well.** Use a tuner at A = 440 Hz and check it again halfway through. The game tunes and detunes the
  lute itself (`Tuning`), so a sample that's off throws every note built on it.

## 2. The microphone

In order of preference:

1. **A USB microphone or an audio interface with a condenser mic**, if you have one.
2. **A phone**, with a voice-memo or recorder app set to its highest quality, uncompressed WAV if it offers it.
   Turn off any "enhance", noise reduction or automatic gain: those pump the level and smear the attack.
3. **A laptop's built-in mic** only if nothing else is to hand.

**Placement:** about 20 to 30 cm (a hand's span or so) from the guitar, pointing where the neck meets the body,
around the 12th to 14th fret. That's a warm, even sound. Pointing straight into the soundhole gives a boomy one, so
avoid it.

## 3. The room

Quiet, and not echoey: a carpeted room, a bedroom with a bed and curtains, or a closet full of clothes. Turn off
fans, the fridge's hum if it's near, and anything ticking. A small room's echo is hard to take out later, but the
game adds its own room sound (the inn's reverb), so the drier the better.

## 4. How to play each note

This is what makes it sound like a lute and not a guitar.

- **Pluck with a pick or a fingernail, near the bridge,** about a thumb's width from it. Near the bridge is thinner
  and more nasal, like a lute; over the soundhole is round and guitar-like.
- **One even strength for all of them:** medium, not soft and not hard. A few loud and a few soft make an uneven
  instrument, and evening them out afterwards never quite fixes it.
- **Let each note ring until it's gone, or for about 4 seconds,** then damp it with your palm and leave a second of
  quiet before the next.
- **No vibrato, no slides, no bends.** The game bends and slides the notes itself.
- **Mute the other strings** with your fretting hand's spare fingers, so only the one note rings.
- **Two takes of each note** is plenty: I'll pick the cleaner one.

Easiest is one long recording per octave: play the 12 or 13 notes in order, two takes each, with the gaps. I'll cut
it up. Say the note name aloud before each if it helps you keep your place: I'll trim the speech off.

## 5. Levels and files

- **Set the level on the hardest note you'll play,** so it peaks around three-quarters of the meter, never at the top.
  Clipping can't be undone; quiet can be turned up.
- **WAV, 44.1 or 48 kHz, 16 or 24 bit,** mono or stereo. Not MP3: it smears the start of each note.
- **Names:** anything you'll recognise later, for example `lute_low_octave.wav` and `lute_high_octave.wav`, or one file
  per note (`lute_d4.wav`, `lute_f#4.wav`).
- **Where to put them:** `art/source/lute/` in the repo, and tell me which guitar and string you used.

## What I'll do with them

- Cut each note out, trim the silence before the attack, and fade its tail.
- Even out the loudness across the range, and check each note's pitch (a few cents off gets corrected).
- Make the lute play one sample per key (`octaves` in `mods/musician/defs/musician.json` takes a list, as the fiddle's
  `held` does), retire the synthesized lute, and keep `musician_assets.py` from overwriting the recordings.
- Credit them in the Credits window and `NOTICE.txt`: they're yours, recorded for the game.
- Check them on the Deck's speakers as well as headphones.

## A quick test first

Before recording everything, record three notes (D4, A4, D5) and send them. That's enough to hear whether the
placement and the pluck sound like a lute in the game, before you spend an hour on all 25.
