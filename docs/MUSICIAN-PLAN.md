# The Musician: playing, dancing, and a town that listens

_Saved 2026-11-17; being built as a mod (`mods/musician`, docs/MODDING.md). Renamed from the bard on 2026-11-18:
a bard reads as an adventurer's class, and this is a professional musician, the town's own._

## Context

The musician is in DESIGN.md's trade table ("rhythm performance, morale, rumours"), and its trade notes ask for instrument customisation and care, poetry in some symbolic form, writer's block, and a stretch goal: playing that a real musician could use to recreate songs. The user wants an **optional, social, support** trade that leaves the round entirely when no real player takes it. The heart of it is the music:
- the musician picks an instrument
- playing feels close to a real instrument, with a wide range of expression
- an Ocarina-of-Time-style **lead-in**: you play a tune's opening and your hands carry the rest, so non-musicians can perform too
- dancing, so people react to the music

Agreed scope:
- **Instruments:** lute, fiddle, pipe (tin whistle) and hand drum.
- **Playing:** free play plus a tune book, with the lead-in that takes over.
- **Also in this pass:** instrument care, performances that pay, the musician leading the festival, and lore and rumour songs.

**What's already there to build on:**
- **Dancing:** the festival already has a ring dance (`Ai/Festival.cs`: `Activity.Dance`, `Ring`, `FestivalJob.Dancing`), faked in the renderer as a sine hop (`WorldRenderer.cs:851`).
- **Satisfaction from watching:** `FestivalSystem.Played` (:750) already pays watchers' satisfaction up to a cap.
- **Notes as pitched samples:** the babble system (`Audio/Babble.cs`) already plays pre-rendered samples with pitch and delay. `AudioEngine.Play(id, at, gain, pitch, muffle, delay)` covers notes too, with `pitch = 2^(n/12)`.
- **Live input:** the Work pipeline (pure `Advance`, step-stamped `WorkInput(int Step, Kind, int Value)` batches, `WorkPredictor`) is exactly what live playing needs. It's deterministic, replayable, and every co-op peer runs the same session, so everyone can hear it.

## 0. A trade only players take

- **`TradeDef.PlayerOnly`** (`Defs/TradeDefs.cs`, `trades.json` entry `Musician` with `"playerOnly": true`).
- **`RoundSystem.Deal`** (`Round/RoundSystem.cs:364`):
  - Only a seat that put the musician among its `Wanted` gets it.
  - Leftover seats never fall to it (:392).
- **`RoundSystem.Start`:** AI villagers never draw it from `open` (:427). The musician's seat spawns a newcomer through `Players.SpawnNewcomer` instead of taking over an AI villager.
- **`TakeTrade`** (:459): a late joiner may still take the musician; it spawns a newcomer the same way.
- **When the musician's player leaves:** the character becomes an ordinary AI villager. It keeps the instrument but doesn't perform. Its brain treats it as a labourer.
- **Everything musician-related checks for a performer, not for the trade.** Rounds without a musician play exactly as today. This is what keeps `VillageDayScenario`, the `--bench` hashes and the season sweep baseline untouched.
- **Lobby** (`VillageGame.Lobby.cs:112,177`):
  - Player-only trades are left out of the "trades vs players" count.
  - The musician is marked as optional ("comes only if someone plays it").
- **`SeasonSweep.Trades`** doesn't include the musician unless asked (`--bots` with the musician wanted), so the baseline stays comparable.

## 1. The instrument and how it's played (`Sim/Music/`)

**Data.** `data/defs/instruments.json` describes each instrument:

| Field | What it sets |
|---|---|
| range | lowest and highest note |
| voice | plucked, bowed, breath or struck |
| polyphony | how many notes can sound at once |
| sustain | does a note decay, or last while held |
| ornaments | which ones it allows |
| care model | what drifts and what breaks (§5) |

Instruments are items with an `Instrument` component, saved through a codec in `ComponentCodecs.cs`. It holds:
- tuning per course or string, in cents
- strings intact
- wear
- for the drum, the head's tension

**What each instrument is like:**
- **Lute:**
  - plucked, so notes decay; two voices
  - Space strums the current chord of the mode
  - notes ring on until damped
- **Fiddle:**
  - bowed: a note sounds while held, and its loudness follows the bow
  - slides between notes and vibrato
  - double stops on neighbouring strings
- **Pipe:**
  - one note at a time, legato while held
  - ornaments: cuts and rolls (a grace note played quickly before a note)
  - a breath meter, so long phrases need a breath
  - Shift overblows the second octave, as on a real whistle
- **Hand drum:**
  - strokes, not pitches: low open, high and rim
  - pressing the head (W/S) bends the pitch
  - it sets and holds the tempo

**The session: `PerformSession : WorkSession`.** It's started from the instrument held in hand, not from a station, so the musician can play anywhere.
- Before starting, pick a key and mode: major, minor, dorian or mixolydian.
- **Note input.** Add a `WorkInputKind.Note` whose `int Value` packs four things:
  - the scale degree, 0–15
  - a chromatic offset of −1, 0 or +1
  - the velocity, 0–7
  - an on/off bit

  Note-off matters for sustained instruments. One step is 1/60 s, so timing is preserved exactly.
- **Expression** comes in on the existing `Axis` and `Axis2`:
  - bend or vibrato
  - dynamics, or bow pressure
- **Purity:** `Advance` stays pure. It turns inputs into a ring buffer of `NoteEvent`s (step, pitch, velocity, length), the thing everyone hears.

**Controls** (new `Act` ids through `Controls`, so they rebind and work on the Deck; named in text with `Controls.Glyph`):

| Control | Keyboard | Pad (OoT-style) |
|---|---|---|
| Notes | A row of eight keys for the scale degrees (default the home row) | Face buttons and d-pad for eight degrees |
| Octave and semitone | Shift is the octave up, Ctrl the semitone | LB/RB for the octave and semitone |
| Expression | Mouse wheel or W/S for dynamics; A/D for bend and vibrato | Right stick for bend and vibrato; triggers for dynamics |

Because notes are relative to the chosen key, a real musician can play any diatonic tune, and accidentals reach the rest.

**Sound** (game side, `Audio/MusicVoices.cs`):
- **Samples:** `tools/sfx_synth.py` gains instruments, each with a handful of root samples per octave plus loop and release tails for the sustained ones:
  - the lute by Karplus–Strong
  - the fiddle by additive bowed tone with bow noise
  - the pipe by breath and a sine stack
  - the drum by a membrane mode
- Each instrument gets its entries in `sounds.json`, with the random pitch range fixed at `[1,1]`.
- **Playing a note:** `Play(id, pitch: 2^(n/12) * tuningCents)`.
- **Sustained notes:** played with `Hold(key, …, fade)`. If `Hold` can't retune a voice while it plays, add a small `AudioEngine.SetPitch(key, pitch)` to Narsil for bends and vibrato, and commit it in the submodule first.
- **What you hear of your own playing:** your own client plays notes at the keypress, from the predicted session (`WorkSounds`), so there's no lag.
- **Other people's playing** is heard through `SoundDirector`:
  - positionally
  - gated by `PerceptionView`/hearing like speech
  - read from the sim's replayed session

  It trails by batch latency, which is fine for listeners.
- **The festival band:** the `festival.band` loop ducks under a performing musician.

**Out of tune is audible.** An instrument's tuning offsets go straight into the pitch. Nothing hidden makes a noise, which is consistent with the secrets rule.

## 2. The tune book and the lead-in

- **`data/defs/songs.json`:** original tunes only (no traditional or copyrighted melodies we didn't write). Each tune gives:
  - its notes as scale degree, octave and beats
  - its tempo and mode
  - its kind: jig, reel, air, march or ballad, which sets the dance it calls for
  - its mood: lively, tender, stirring or sombre
  - optional verse slots for lore (§7)
- **The musician's book.** It starts with a few tunes, and more come through the tutorial, from adventurers' requests and by composing. It's kept per character in `CharacterProgress`, and the sim gets the list sent from the character at the deal, like traits.
- **Three ways to play, in the same session:**
  1. **Free play.** Anything at all.
  2. **Guided.** The tune scrolls as a lane or staff over the notes (`VillageGame.Music.cs`). Accuracy and expression are judged as you go.
  3. **Lead-in.** A `TuneMatcher` (pure, inside `Advance`) watches what you play for a tune's opening phrase, the first 4–8 notes:
     - It matches the interval contour, so any key works.
     - It's tolerant of rhythm.
     - The required accuracy is scaled by `Hands`.

     On a match, "your hands take over". The session plays the rest from the song def deterministically, so no extra inputs are sent. You keep shaping it live:
     - tempo nudge (A/D)
     - dynamics (W/S)
     - ornaments on prompts
     - stop (Space)
     - break back into free play by playing any note
- **Known tunes are recognised.** Listeners recognise a known tune whichever way it's played ("they know this one!"), a small bonus.
- **Quality** comes from three things:
  - lead-in accuracy, or guided accuracy
  - the instrument's state
  - how much expression is kept up

  An auto-played tune with no shaping is pleasant but flat. A shaped one moves people more.

## 3. How a performance reads: `Performance` (sim)

A `Performance` component on the musician while playing. `PerformanceSystem` updates it from the session's `NoteEvent`s, deterministically, with no float trig outside `DetMath`. It holds:
- **Tempo:** a beat estimate from note onsets.
- **Steadiness:** the variance of onsets against the beat.
- **Energy:** note density times velocity.
- **Mood:** from the mode and the tune's kind, or in free play from the mode and energy.
- **Sourness:** off-key notes against the mode, plus the instrument's tuning.
- **Tune:** which tune, if one is recognised.
- **Beat phase:** what dancers step to.

**Free play is judged on these, never on "correct notes"**, so improvising is real expression. Players read the performance through `PerceptionView.Music(entity)`: the beat, the mood words and the tune name if their character knows it.

## 4. The audience: reactions and dancing

**`AudienceSystem`** finds hearers using the same room and range rule speech uses. Each picks a reaction from the performance and their own state:

| The music is… | Their reaction |
|---|---|
| Lively and steady | Dance if there's floor, else clap or tap on the beat |
| Tender or sombre | Sit, sway, listen |
| Stirring | Cheer |
| Sour or ragged | Wince or drift away; a drunk heckles |

**Brains:**
- **Villagers** (`HumanBrain.Think`, next to `Festival.Gathering`, `Ai/Brains.cs:303`): listening goes after work in the evening and in place of idle wandering otherwise. Work is never abandoned for it, except at the festival.
- **Adventurers** (`AdventurerBrain`, :79): listening goes before the haunt-wander.

**`DanceJob`** generalises the festival's ring (`Festival.Ring`/`Dance`) around a performer:
- **Kinds:**
  - **reel:** the ring
  - **jig:** a solo hop in place
  - **pairs:** two friends, `MoodRules.FriendAt`, turning together
- **The beat:** steps land on the performer's beat phase.
- **A floor:** a free 3×3 of floor near the musician, or the festival ring.
- **Shared with the festival:** the existing festival dance becomes this job, keeping its route.

**Player dancing:**
- A new `Emote.Dance` on the wheel.
- With music in earshot it becomes a light mini-game: step on the beat with A/D and Space, which turns.
- **On beat:** the dancer gets `Danced` mood and gives the musician a small "a crowd" bonus.
- **Off beat:** it's just a look.
- **Ghosts:** they can dance too, unseen. It's a nice touch and costs nothing.

- **Visuals** sprites for the instruments, and a professional musician's clothes

**Art** (`tools/chibi_people.py` and the builds):
- **Dance frames:** step left, step right, hop, spin and clap. These replace the sine-hop fake, and the hop stays as a fallback.
- **Cheer and emote frames,** which are missing today.
- **Playing poses** for each of lute, fiddle, pipe and drum, and the instruments themselves as held shapes and item icons (`tools/norse_items.py`).
- **new clothes** add a performer's outfit for the musician: a town's hired player in good cloth, not an adventurer
- **The renderer:** `CreatureAnimator` keys dance frames off `PerceptionView.Music` beat phase, so everyone moves in time.

**Mood** (`mood.json`): `HeardMusic`, `Danced`, `MovedBySong` and `SourMusic` thoughts through `MoodSystem.Remember`.

## 5. Instrument care

- **Drift** (`InstrumentSystem`): tuning drifts with play time and with changes in temperature and damp, read from the map's heat and wetness.
  - **Fiddle:** pegs slip.
  - **Lute:** many courses.
  - **Drum:** the head slackens in damp. Warming it by a fire tightens it, a real hand-drummer's trick.
  - **Pipe:** fixed pitch, but it cracks if left by a fire.
- **`TuneSession`.** Pluck the string against a reference tone and turn the peg with A/D until the beating between the two tones slows and stops.
  - **No meter.** It's heard and shown only as the beating wobble, as heat is only colour at the forge.
  - **Strings:** overtightening snaps one, and so does wear.
- **Restringing:** from spare strings in the musician's kit. Gut from the butcher's block, bought or bartered, is a cross-trade tie later.
- **Effects on playing:** a broken string removes its notes from the lute and fiddle. Untuned courses sound sour.
- **Cosmetics:** wood, stain and inlay are chosen in the lobby and kept in `CharacterProgress`. They're left out of the hash, like `Appearance`.

## 6. Performances that pay, and the support role

- **The hat:** a `Hat` container item the musician puts down. Listeners tip real `SilverCoin` by how much they enjoyed it, capped per listener per night.
- **The inn's draw.** With a performer in the common room, adventurers linger and `Savouring` lasts longer, so more drinks are sold. This is the musician's support for the innkeeper and the cook. Satisfaction from music is capped per stay, on the model of `WatchSatisfaction`/`WatchCap` (`FestivalDefs.cs:63`), so it can't swamp Renown.
- **Requests.** An adventurer asks for a tune they like as an `Order` of a new `ServiceKind.Song`, through the shop line and `ShopPrices` like the other trades. Playing it fulfils the order and they pay.
- **The send-off.** Playing as a party leaves town (`ExpeditionSystem` departure) gives the members a `Heartened` mark. It's a small edge in the resolver, in the spirit of rations and charms. A sour send-off gives nothing.
- **The innkeeper's night.** The innkeeper can chalk "music tonight" on the sign (`ShopPrices` flag). Word spreads through `DirectionsSystem`/`ShopSystem.Fare`, and more custom comes in that evening.

## 7. Lore and rumour songs

- **`ComposeSession`** at a writing desk in the musician's lodging. The musician:
  - picks a subject from what their character knows (`Expeditions/Intel`, a party's deeds, a crisis seen)
  - picks a tune from the book
  - picks verse lines from a word bank (`songs.json` verse templates in the brand's voice): symbolic poetry, not free text
- **Inspiration and writer's block.** A small `Inspiration` meter fills from hearing tales at the inn, watching a fight and the festival. Composing spends it; with none left, the session refuses ("the words won't come").
- **Singing a lore song** sends its verses as heard speech through `SpeechSystem`. Only those who hear them learn the Intel, which keeps the secrets rule.
- **Ballads of a party:** a ballad of a party's deeds raises that party's satisfaction. A ballad of a party that fell raises Renown at the festival.
- **The betrayal hook:** a verse can assert something the musician doesn't know to be true, and listeners take it as Intel with its truth hidden. Wire the data path now, but only expose false verses when traitors arrive (M4).

## 8. The festival centrepiece

When a musician is present, `FestivalSystem`:
- **Revels:** puts the musician by the bonfire and drives the `Dance` activity from the musician's performance, not the band loop.
- **Before the toasts:** adds a "the musician's set" beat.

Watchers' satisfaction comes through `Played`. Without a musician, the festival is unchanged.

## 9. Teaching, bots, tests, docs

- **Lessons** (`Scenarios/Lessons/MusicianLessons.cs`, `trades.json` `lessons`):
  - **core:** hold an instrument; play a scale; strike up a tune by its opening; tune a string
  - **after:** the hat and requests; dancing; composing
- **Practice:** `--practice Musician` with listeners on request.
- **Bot:** `BotPlayer.Musician.cs` plays through commands only. It:
  - strikes up tunes by their lead-in
  - tunes when sour
  - puts the hat down in the evening
  - plays send-offs

  This is enough for `SeasonScenario` with the musician wanted.
- **Tests** (`tests/HeroesNotIncluded.Sim.Tests/`):
  - **The deal:** the musician is never dealt to an AI or an unwilling seat.
  - **No musician, no change:** a round without a musician has unchanged hashes.
  - **`TuneMatcher`:** recognises an opening in any key and refuses a near miss below `Hands`.
  - **`PerformSession` replay determinism:** the predictor and the sim end in the same state.
  - **`Performance` readings:** tempo and steadiness from scripted onsets.
  - **Dancers step on the beat.**
  - **Care:** tuning drifts and strings snap when overtightened.
  - **Tips and request orders pay.**
  - **The send-off** marks the party.
  - **Lore songs:** Intel goes only to those in earshot.
  - **The festival** uses the musician when present.
- **The commands** join `CommandTests.HostileCommandsDoNoHarm`.
- **Docs:** this plan goes in as `docs/MUSICIAN-PLAN.md`. When it lands, update DESIGN.md's musician row and notes, ROADMAP and AUDIO.md ("music voices"), and add a CLAUDE.md architecture bullet for `Music/`.

## Order of work (each step playable and tested)

1. The player-only trade (§0), the `Instrument` item, `PerformSession` free play on the **pipe**, synthesized samples, local and remote hearing.
2. The lute, fiddle and drum; expression; Narsil's `SetPitch` if needed.
3. The tune book: guided play and the lead-in (`TuneMatcher`).
4. `Performance`, the audience, `DanceJob`, the dance emote, dance and playing art.
5. Paying: the hat, requests, the inn's draw, send-offs, mood.
6. Instrument care and tuning.
7. The festival centrepiece.
8. Lore songs, composing, inspiration.
9. Lessons, the bot, docs.

## Verification

- `dotnet build HeroesNotIncluded.sln` with no warnings, and `dotnet test -c Release`.
- `--bench --seed 1234` and `--seed 99`: hashes unchanged after step 1, since no musician is dealt.
- `dotnet run --project src/HeroesNotIncluded -- --new-game --trade Musician`:
  - play free and strike up a tune
  - watch the inn dance
- `--practice Musician` and `--festival-at 5 --trade Musician`.
- **Co-op:** `--host` and `--join` with the musician on one side. Check the other hears the notes placed in the world and sees the dancers in step.
- `--season-sweep` with and without the musician wanted, so the musician's effect on Renown stays modest.
- `--check-art` for the new sprites and sounds.
