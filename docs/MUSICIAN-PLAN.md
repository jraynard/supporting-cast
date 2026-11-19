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

_Built (2026-11-18): the two modes, full songs and keeping time, in `mods/musician` (`PlaySession`, `SongDef`). Held
notes on the pipe too: a note sounds while its key's held (`PlaySession.Ringing`, ended by the note-off), a carried one for
92% of its length (half before a breath mark). Each is a seamless loop (`held` in the instrument def) with a breath's
chiff as it speaks (`attack`), on a voice of its own so a repeated note speaks again. Free play names the notes (`root`)._

- **Where tunes come from:** our own, or public-domain tunes (traditional, pre-1900) written out in our own
  notation, never copied from a modern arrangement. No game or film music (Ocarina of Time, the LOTR and Hobbit
  films' scores), however it's renamed. The first is "Drunken Sailor"; originals of our own are to come.
- **A tune** (`songs` in `mods/musician/defs/musician.json`) gives:
  - its parts by name ("Verse", "Chorus"), each a list of notes `[step, beats, accidental]`. Step −1 is a rest,
    and accidental is −1/+1 for a semitone. A one-part tune may give `notes` instead.
  - its `form`, the parts in the order they're played (verse, chorus, verse...), so the hands carry the whole song
  - its `opening`, how many notes strike it up (5 unless it says)
  - its tempo, its kind (jig, reel, air, march or ballad: the dance it calls for) and its mood
  - optional verse slots for lore (§7), to come
- **The musician's book.** It starts with a few tunes, and more come through the tutorial, from adventurers'
  requests and by composing. It's kept per character in `CharacterProgress`, and the sim gets the list sent from the
  character at the deal, like traits. _For now every tune in the defs is known._
- **A tune's call.** Each tune is called up by its best-known phrase, not necessarily its first: Drunken Sailor by its
  chorus ("Way hay and up she rises", `"call": "Chorus"`, 7 notes). Called by another part, the hands then play the
  tune from the top; called by its first part, they go on from the call.
- **Two modes, switched with the other action (Space)**, so a melody of your own never turns into a tune by accident:
  1. **Free play.** Anything at all. No opening is listened for.
  2. **The book.** W/S picks out a tune, and its opening is drawn as a guide. Play any tune's opening, from any
     note, starting after a pause, and "your hands take over". It's matched by the shape of the steps, sharps
     included; notes played before the book was opened don't count.
- **Minding the tune while the hands carry it.** This is the rhythm game. The hands find every note (the note keys
  do nothing then). The player minds what a real musician must, on a lane where the tune's notes slide toward a line,
  each set as high as it sounds:
  - **The beat:** a tapping foot, its toe rising between beats and coming down on each, a ring closing on where it
    lands: F as it lands. Green on a beat kept, red on one missed; dots count the bar. The first bar is a warm-up (misses
    cost nothing). The window is wider for practised hands (`Hands.Tolerance`).
  - **The breath gauge** beside the foot says what the air's wanted for: "Breathe in!" as a gap opens, a count to the
    next, "swell it" on a long note (swelling spends breath faster).
  - **The breath:** air drains as you play; a full breath lasts about 12 beats. Hold Shift to breathe in the gaps the
    tune marks (`[-2]` after a note in the def, a comma over the lane) and at rests. A breath snatched mid-phrase
    costs the note due, and with no air the notes go missing.
  - **The long notes** (2 beats or more, drawn thicker with a swell under them): tap F on the note's beat as
    usual and keep it held down while the note rings to shape it. Its inner beats are kept by the hold, not by taps.
    One before a breath rings only its first half: let go, then breathe. Each shaped note is a flourish. (It was W at
    first; three keys at once was too many.)
  - **Concentration** holds it all together. It firms with beats kept, breaths taken at the marks and notes shaped.
    It slips with beats missed, stray taps, breaths in the wrong place and running out of air; with no air, keeping
    the beat earns nothing. Below half, notes go missing (from the session's own random stream, so every peer hears
    the same). At nothing, the tune slips away.
  - A/D nudges the pace. Space lets the tune go, back to free play.
- **Known tunes are recognised.** Listeners recognise a known tune whichever way it's played ("they know this one!"),
  a small bonus. _To come._
- **Quality** (§3), from:
  - beats kept and missed, breaths, and the flourishes (`BeatsKept`, `BeatsMissed`, `Breaths`, `Flourishes`, kept on the session)
  - the instrument's state
  - how much expression is kept up

  A tune barely held is pleasant but flat. One kept steady, with its long notes shaped, moves people more.

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

1. ✓ The player-only trade (§0), free play on the **pipe** (`PlaySession`), synthesized samples, local and remote hearing.
2. ✓ The tune book: the two modes, the lead-in, full songs in parts, and minding the beat, the breath and the long notes (§2). Next: playtest, then originals.
3. ✓ (first pass) The lute, fiddle and drum; bends. Each instrument says in its def what a carried tune asks of it
   (`air`: the pipe's breath, the fiddle's bow, turned at the marks; `sustains`: long notes swelled; `strokes`: a drum's).
   The kit brings all four; F plays the one in hand. No `SetPitch` needed: `Hold` retunes a held voice live. Then:
   a rhythm meter in free play (`Performance.Rhythm`: the pulse found under the notes, so a drum's doum-tek-tek is as
   steady as a march; what the audience dances to); the lute's chords (`chords`: Q with a key holds its triad, F strums it
   down and up, the strings spread a little apart); the fiddle a sample for every key, made at its own pitch through a
   body whose resonances stay put, with a wider vibrato and the bow swelling in (`fade`). The drum needs little more
   (the user: fun as it is). Left for later, if wanted: dynamics, the fiddle's double stops.
4. ✓ (first pass) `Performance`, the audience. A performance is read from the session as it stands (`Audience.cs`:
   lively or tender, steady or ragged, its tempo; free play by how it's played, never by right notes). Whoever's free
   (`IPastime`, a core door) and hears it clearly comes to it: to dance in place, hopping to its beat, if it's lively and
   steady, or to stand and listen; off when it stops. `HeardMusic` and `Danced` are remembered. The musician's window counts
   the crowd. Then: a reel or a march danced round the musician in a ring (the festival's ring, where there's floor);
   sour music (steadiness under `SourBelow`) draws no one and drives the audience off after a few seconds (`SourMusic`);
   a player in earshot dances with F where they stand (the musician mod's own action, no new emote: `DanceSession`, a
   step on the beat with A/D, a turn, a clap), judged against the musician's beat in `Settle` and shown at once in the
   window; the renderer hops anyone whose job or session says it's dancing (`Dancing` on both). The lute's strum: a
   down-stroke sweeps four strings from the bass, an up-stroke flicks the top two, quicker and lighter. Left: the dance
   and playing art (frames in `tools/chibi_people.py`), pair dances; the festival's dance waits for step 7.
5. Paying (in progress). Done: the hat (`Takings`): set down at the musician's feet as they start playing, the silver
   listeners toss by it (real coin, lying in the open) taken up with it when they stop; a tip once a listener's heard a
   good while (more for dancing, more from an adventurer, once a day); a tune called for aloud from the audience (an
   adventurer likelier), shown in the musician's window only if they heard it, and tipped well if it's played while
   they're there (`HeardTheirTune`). A request is the audience's, not the shop line's: a musician has no counter. The
   window shows the hat filling and who tipped, and 0 bows (through the session: an emote's refused mid-work). Then the
   inn's draw and the send-off (`MusicDraw`, no door needed): an adventurer hearing good music gets thirsty sooner (more
   drink sold) and is pleased a little, capped a stay; one setting out on a quest to a lively, steady tune goes with more
   satisfaction, which the quest's reckoning already counts as morale (`SentOffWithASong`). Left: the innkeeper's music
   night (it wants a door onto the inn's sign). The rhythm meter forgives flourishes now
   (grace notes merged, notes between the pulse's set aside as ornaments).
6. (first pass) Instrument care and tuning: the lute's five courses and the fiddle's four strings (`strings` in the def;
   a note's played on the highest at or under it) each keep their cents off (`Tuning` on the instrument), which drift
   flat as they're played (`InstrumentSystem`: an evening's playing goes sour) and go straight into the notes' pitch.
   9 in free play puts it in your lap (`TuneSession`): F held sounds each string with its true note on the instrument
   (plucked together, or bowed), A/D turn the peg till the beating slows and stops (shown only as a wobbling wave, never a number), Space the next.
   Wound well past its note a string may snap; a snapped string's notes don't sound, and F fits a spare from the kit
   (`Strings`), slack till it's brought up. Far enough out (`Performance.SourCents`) the music's sour to listeners.
   `--drill tune`. Left: the drum's head (slack in damp, tightened by a fire), the pipe cracking by a fire, wear
   breaking strings, and the cosmetics (wood, stain, inlay).
7. The festival centrepiece.
8. Lore songs, composing, inspiration.
9. Lessons, the bot, docs.
10. A band (the user's idea, 2026-11-18): several musicians in co-op playing one tune together, joining on a tune
    struck up and keeping its beat (the drum's way into the book, too). On the roadmap.

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
