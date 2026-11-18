# Speaking aloud

Hold a key, speak, let go: your words become your character's line, whispered, said or shouted as
you spoke them. It's the Steam Deck's answer to typing, and works for anyone with a microphone.
It's off until a player turns it on (Settings, **Voice**).

## How it fits

- **The sim never knows.** Speech is made out game side and reaches the sim as the same
  `SayCommand(actor, text, volume)` a typed line is. Who hears it, muffling through walls, the dead
  hearing only the dead, rate limits, replays and co-op all work as they do for typing. The audio
  never leaves the machine and is never written to disk: only the words are sent.
- **Push to talk.** Talk (`Act.Talk`, default O) is held while speaking. A clip stops at 28 s
  (Whisper hears 30 at a time). Let go and it's made out in the background.
- **Shown, then said.** The words come up on a card at the bottom of the screen, with the volume
  they'll be said at, counting down (`VoicePreviewSeconds`, 2 s by default; 0 says them at once,
  "Wait for me" holds them). In that time: Talk again says it now (and listens for the next),
  Unsay (`Act.Unsay`, Backspace) takes it back, a click or a tap on **Whisper / Say / Shout**
  changes the volume (and holds the line for you), and **Edit** puts it in the speech box to type
  over, its volume kept as `/w` or `/s`.
- **Long speech** is split at sentence ends into lines the sim takes (`Transcript.Lines`, 300
  characters each).

## The volume

`Narsil.Speech.VoiceLoudness` measures a clip in 20 ms frames: the room's quiet (the quietest
tenth), the voice's level (the 80th percentile of the frames 9 dB above that), and the share of
the voice with a pitch, found by autocorrelation over 70–400 Hz. A clip with nothing standing out
above its quiet is the room alone (a hum, the key pressed by mistake) and is never transcribed:
Whisper makes up words for silence.

`VoiceCalibration` reads that against the player's own voice. Calibrating (Settings, Voice) is
three lines read aloud: whispered, as you'd talk, and called out. The thresholds sit halfway
between, nudged up 1.5 dB, so a reading on the fence takes the **quieter** volume: a word read
as a whisper costs a repeat, but a whisper read as a word gives away a secret to the room.

- **Whispers are breath.** A real whisper has no pitch, so one that's unvoiced reads as a whisper
  however close to the microphone it was. This is used once the calibration's whisper and talking
  voice differ in voicing (`UsesVoicing`).
- **Automatic gain** (PipeWire, Windows "enhancements", some headsets) levels the microphone, so a
  shout comes in barely louder than a word. Calibration notices (`Flattened`: under 6 dB between
  them), warns, and never reads a shout from level then; Shout on the card still works.
- Uncalibrated, it judges against a typical headset (`VoiceCalibration.Default`).

## The recognizer

whisper.cpp through Whisper.net (both MIT), on the CPU, English only (`Narsil.Speech`,
`WhisperRecognizer`, behind `ISpeechRecognizer`). The models are OpenAI's Whisper (MIT), quantised
by the whisper.cpp project, fetched once from Hugging Face into the settings folder's `models/`
when the player asks, and checked against their SHA-256 (`SpeechModels`):

| Model | Size | 11 s clip, desktop (4 threads) | Use |
|-------|------|------|-----|
| Quick (`base.en`, q5_1) | 60 MB | 0.75 s | the default, and the Deck's |
| Careful (`small.en`, q5_1) | 190 MB | 2.7 s | better with names and mumbling |

Each clip's **prompt** is the names the player might say, nearest first: the people in sight, who's
been heard lately, and the shops' signs (`VillageGame.VoicePrompt`, all of it what the player
perceives). That's what gets "Arin" and "the Gilded Goose" spelled right.

The recognizer gets half the machine's threads, up to four (`WhisperRecognizer.DefaultThreads`), and
runs only after the key's let go, so it never competes with the frame while you talk. While the key
is held the game turns itself down (`DuckWhileTalking`), so a Deck's speakers aren't heard back.

## Trying it

```sh
dotnet run --project src/HeroesNotIncluded -c Release -- --dictate-test clip.wav   # loudness, the reading, and the words, timed (--model small.en, --prompt "Arin, Bea", --threads N)
dotnet run --project src/HeroesNotIncluded -c Release -- --dictate-test mic        # four seconds from the microphone in the settings
dotnet run --project src/HeroesNotIncluded -- --new-game --voice-clip clip.wav     # a clip spoken into a round as if through the microphone
dotnet run --project src/HeroesNotIncluded -- --settings-tab Voice                 # the title screen with the settings open on a tab
```

Tests: `SpeechTests` (Narsil: loudness, voicing, calibration and transcripts on made-up signals;
the real recognizer with `NARSIL_SPEECH_MODEL`, `NARSIL_SPEECH_CLIP` and `NARSIL_SPEECH_WORDS`) and
`VoiceTests` (the game's push to talk with a recognizer stood in, the calibrator, the settings).

## On a Steam Deck

Talk and its take-back are rebindable on the Voice tab itself (and on Controls). No pad button is
free for Talk by default; with none set, prompts say so rather than naming a key the pad can't
press, and holding to speak in Settings counts a pad button even though the window has the pad. A Deck's back grips aren't seen by the game as a non-Steam
install, but Steam Input can put a key on one: see [STEAM-DECK.md](STEAM-DECK.md), "Speaking
aloud". The touchscreen taps the card's buttons. Measure on the Deck with
`--dictate-test` before changing the default model.

## Not yet

- **Villagers don't listen to players.** No AI acts on what a player says, typed or spoken: orders
  go through F at the counter, and only bots read the words. See ROADMAP.md, "Villagers who listen".
- English only. Multilingual models are larger and less accurate in English.
- A word filter, before playing with strangers (M7): a misheard word can come out as a rude one.
