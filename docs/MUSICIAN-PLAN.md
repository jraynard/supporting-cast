# The Musician: playing, dancing, and a town that listens

_Saved 2026-11-17; being built as a mod (`mods/musician`, docs/MODDING.md). Renamed from the bard on 2026-11-18:
a bard reads as an adventurer's class, and this is a professional musician, the town's own. Brought up to date with
the code and the open questions settled on 2026-11-19 ("Decisions" below)._

## Context

The musician is in DESIGN.md's trade table ("rhythm performance, morale, rumours"), and its trade notes ask for
instrument customisation and care, poetry in some symbolic form, writer's block, and a stretch goal: playing that a
real musician could use to recreate songs. The user wants an **optional, social, support** trade that leaves the round
entirely when no real player takes it. The heart of it is the music:
- the musician picks an instrument
- playing feels close to a real instrument, with a wide range of expression
- an Ocarina-of-Time-style **lead-in**: you play a tune's opening and your hands carry the rest, so non-musicians can
  perform too
- dancing, so people react to the music

Instruments: the lute, fiddle, pipe (tin whistle) and hand drum. Playing: free play plus a tune book, with the lead-in
that takes over. Also: instrument care, performances that pay, the musician leading the festival, and lore songs.

## Decisions (2026-11-19)

- **Lore songs: the slim version** (§7). The musician sings a verse about a quest their character knows of, and those
  who hear it clearly learn what the musician knows. No composing session, no inspiration meter or writer's block, no
  false verses: those wait for M4's traitors, where a false verse matters.
- **The tune book: every tune is known for now.** A book of one's own (kept in `CharacterProgress`, growing through
  lessons, requests or by ear) comes back once there are enough songs to be worth learning.
- **Care kept:** wear breaking strings, and cosmetics (wood, stain, inlay). **Cut:** the drum's head slackening in damp
  and the pipe cracking by a fire.
- **The innkeeper's music night** goes through a new general core door: lines a mod puts on a shop's sign, and a keyed
  word-of-mouth draw. Word of the food (`ShopSystem.Fare`) moves onto the same door.
- **The band** (several musicians in co-op playing one tune) gets a plan of its own once the solo musician is done.
  It stays on the roadmap.

## Built

Each with its tests in `tests/HeroesNotIncluded.Sim.Tests/MusicianModTests.cs`.

### §0. A trade only players take
- `TradeDef.PlayerOnly` (`"playerOnly": true` on the `Musician` trade in `mods/musician/defs/musician.json`).
  `RoundSystem.Deal` gives it only to a seat that wanted it; leftover seats and AI villagers never draw it. The
  musician's seat spawns a newcomer (`Players.SpawnNewcomer`); the villagers stay who they are.
- Everything musician-related checks for someone playing, not for the trade, so a round without a musician plays as
  before (`--bench` hashes, `VillageDayScenario` and the season sweep baseline are untouched). `SeasonSweep.TradesIn`
  leaves it out (it has no workplace) unless it's asked for.
- **Left:** `TakeTrade` (a late joiner) can't take the musician yet: it only finds open villagers. The lobby doesn't yet
  leave player-only trades out of the "trades vs players" count or mark the musician as "comes only if someone plays
  it". When the musician's player leaves, check what the character does as an AI: it should keep its instruments and
  not perform.

### §1. The instruments and how they're played
- **Defs:** `instruments` in `musician.json` (`InstrumentDef`): the voice (breath, plucked, bowed, struck), its range,
  `strings` (courses with their pitch), `air` (what a carried tune asks: the pipe's breath, the fiddle's bow),
  `sustains` (long notes swelled), `chords`, `bend`, `book` (false for the drum), and samples with `held` loops and an
  `attack`.
- **The session: `PlaySession`** (`Musician.Sim/Musician.cs`), started from the instrument in hand (F, through
  `MusicianModule.Perform`), anywhere. `Advance` is pure; the notes everyone hears come out of it. A held key's note
  rings till its key is let go (`Ringing`), on a voice of its own.
- **The instruments:**
  - **Pipe:** breath and a chiff as each note speaks, seamless held loops, overblown octave.
  - **Lute:** plucked, notes ring of themselves; Q with a key holds its triad and F strums it, a down-stroke sweeping
    four strings from the bass, an up-stroke flicking the top two.
  - **Fiddle:** a sample for every key, made through a body whose resonances stay put; the bow runs out like a breath,
    notes slide, a wider vibrato, the bow swelling in.
  - **Hand drum:** three strokes (doum, tek, ka) and a pressed head; no book yet.
- **Sound** (`Musician/MusicianGame.cs`, samples by `mods/musician/tools`): your own notes play at the keypress from the
  predicted session; others' are heard placed in the world, gated like speech. `Hold` retunes a held voice live, so no
  `SetPitch` was needed. Free play names the notes (`root`).
- **Left, if wanted:** dynamics, the fiddle's double stops.

### §2. The tune book and the lead-in
- **Where tunes come from:** our own, or public-domain tunes (traditional, pre-1900) written out in our own notation,
  never copied from a modern arrangement. No game or film music, however it's renamed. In the book: "Drunken Sailor",
  and three of our own ("The Miller's Wheel", "Lantern Home", "Hay in the Loft").
- **A tune** (`songs` in `musician.json`, `SongDef`): its parts by name, each a list of notes `[step, beats,
  accidental]` (step −1 a rest, `[-2]` a breath mark); its `form`; its `opening` (5 notes unless it says) and `call`
  (the part it's called up by: Drunken Sailor by its chorus); its tempo, kind (jig, reel, air, march, ballad) and mood.
- **Two modes, switched with Space:**
  1. **Free play.** Anything at all; no opening is listened for.
  2. **The book.** W/S picks out a tune, its opening drawn as a guide. Play its opening from any note, after a pause,
     and the hands take over. It's matched by the shape of the steps, sharps included.
- **Minding the tune while the hands carry it** (the rhythm game; the note keys do nothing then):
  - **The beat:** a tapping foot and a closing ring; F as it lands. The first bar is a warm-up. Practised hands get a
    wider window (`Hands.Tolerance`).
  - **The breath** (or the bow): a full one lasts about 12 beats; Shift breathes at the marks and rests. A breath
    snatched mid-phrase costs the note due; with no air, notes go missing.
  - **The long notes:** F on the note's beat and held down to shape it, a flourish each.
  - **Concentration:** firms with beats kept, breaths at the marks and notes shaped; slips with misses, stray taps and
    running dry. Below half, notes go missing (from the session's own random stream); at nothing, the tune slips away.
  - A/D nudges the pace; Space lets the tune go.
- **Quality** comes from what's kept on the session (`BeatsKept`, `BeatsMissed`, `Breaths`, `Flourishes`) and the
  instrument's tuning.
- **Left:** listeners recognising a known tune ("they know this one!"), a small bonus (step 11).

### §3. How a performance reads
Read from the session as it stands (`Audience.cs`), not kept as a component: lively or tender, steady or ragged, its
tempo, its kind. Free play is judged by how it's played, never by right notes. `Performance.Rhythm` finds the pulse
under the notes (grace notes merged, notes between the pulse set aside as ornaments), so a drum's doum-tek-tek is as
steady as a march. `Performance.SourCents` sets how far out of tune is sour.

### §4. The audience and dancing
- **Who comes:** whoever's free (`IPastime`, a core door asked after work in the brains) and hears the music clearly.
  - Lively and steady: they dance in place, hopping to its beat.
  - A reel or a march, where there's floor: danced round the musician in a ring (`Festival.Ring` around the musician).
  - Tender: they stand and listen.
  - Sour (steadiness under `SourBelow`, or the tuning far out): it draws no one and drives the audience off after a few
    seconds.
- **Mood:** `HeardMusic`, `Danced` and `SourMusic` (`musician.json`).
- **A player in earshot dances** with F where they stand (the mod's own action, `DanceSession`): a step on the beat with
  A/D, a turn, a clap, judged against the musician's beat in `Settle` and shown at once.
- The renderer hops anyone whose job or session says it's dancing (`Dancing` on both). The musician's window counts
  the crowd.
- **Left:** pair dances (`MoodRules.FriendAt`), the art, and the festival's dance (step 7).

### §5. Instrument care
- The lute's five courses and the fiddle's four strings each keep their cents off (`Tuning` on the instrument). They
  drift flat as they're played (`InstrumentSystem`: an evening's playing goes sour), and the cents go straight into the
  notes' pitch. Nothing hidden makes a noise.
- **`TuneSession`** (9 in free play; `--drill tune`): F held sounds each string against its true note, A/D turn the peg
  till the beating slows and stops (shown only as a wobbling wave, never a number), Space the next. Wound well past
  its note, a string may snap; a snapped string's notes don't sound, and F fits a spare from the kit (`Strings`).
- **Left:** wear and cosmetics (step 9).

### §6. Performances that pay
- **The hat** (`Takings`): set down at the musician's feet as they start, real silver tossed by it, taken up when they
  stop. A listener tips once a day after a good while, more for dancing, more from an adventurer.
- **Requests** are the audience's, not the shop line's (a musician has no counter): a tune called for aloud, an
  adventurer likelier, shown in the musician's window only if they heard it, tipped well if it's played while the
  caller's there (`HeardTheirTune`). The musician bows with 0 (through the session; an emote's refused mid-work).
- **The draw** (`MusicDraw`, no door needed): an adventurer hearing good music gets thirsty sooner (more drink sold) and
  is pleased a little, capped a stay.
- **The send-off:** a party setting out to a lively, steady tune goes with more satisfaction (`SentOffWithASong`),
  which the quest's reckoning already counts as morale.
- **Left:** the innkeeper's music night (step 8).

## To do, in order (each step playable and tested)

### 7. The festival centrepiece
Today festival jobs come before pastimes (`Ai/Brains.cs`: `Festival.Gathering` is asked before `IPastime`; the same in
`AdventurerBrain`), so the musician's audience never forms at the festival. The bonfire dance (`Festival.Dance`,
`FestivalJob.Dancing`) walks its ring at a fixed pace, and `festival.band` plays regardless.
- **Core doors** (game-agnostic, listed in MODDING.md):
  - **A festival act** a mod registers (`IFestivalAct.JobFor(sim, pawn)`), asked inside `Festival.Gathering` before its
    weighted pick.
  - **Watch credit for a mod's show** under the same cap: `FestivalSystem.Played` takes a source key, or a public
    `Watched(sim, who, source)` shares `_watched` and `WatchCap`.
  - **A ceremony beat** a mod adds before the toasts (at the Bell → Toasts step of `RunCeremony`, through `Announce` and
    the step's timing). The empty Accusation step shows where such a hook belongs.
  - **Game side:** a registered check that ducks or silences the `festival.band` loop (`SoundDirector`) while a
    performer plays.
- **The musician's act:**
  - In the revels, the musician is drawn to the bonfire (`FestivalSystem.Prop(sim, "Bonfire")`). The bonfire dance
    becomes the musician's ring (the `Listening.Ring`/`AudienceJob` path, centred on the fire) at the music's tempo.
    Watchers are credited through the door.
  - **"The musician's set"** before the toasts: the town gathers and the musician plays a tune. How it's played moves
    them, and the toasts can name it.
- Without a musician, the festival is unchanged.
- **Tests:** a festival with a musician dances to their beat; watchers are credited within the cap; the set comes
  before the toasts; a festival without one is unchanged.

### 8. The music night: sign lines and word of it
- **Core door:**
  - Lines a mod puts on a shop's sign (`Shops/Signs.Read`), set by a command the keeper sends.
  - A public, keyed word-of-mouth draw in place of the food-only `Fare` (`ShopSystem.HeardOfTheFood`/`TalkFades`):
    word of a shop's thing heard, fading per key, summed into the draw `PartySystem` reads.
  - Fare moves onto it unchanged, so the bench hashes stay the same.
- **The musician's part:**
  - The innkeeper chalks "music tonight" on the inn's sign.
  - Word of it spreads when the musician plays at the inn that evening, and more custom comes.
  - A night chalked up with no music turns the word sour.
- **Tests:** the line is set and read; word of the music brings parties sooner; the bench hashes are unchanged.

### 9. Care: wear and cosmetics
- **Wear** (in the mod):
  - Each string's wear (`Wear` on `Tuning`) grows as its notes are played (`InstrumentDef.StringOf`, in
    `InstrumentSystem`).
  - A worn string may snap on its own while played, and strains sooner when tuned (`TuneSession`).
  - A fitted spare is new. The codec takes it behind a version check.
- **Cosmetics** (the most plumbing here; the same doors later carry a book of one's own):
  - **Core doors:**
    - A mod-keyed bag on `CharacterProgress` (kept by the roster's `Clean`).
    - A per-seat mod payload on `JoinLobbyCommand`/`LobbySeat` and `CreateCharacterCommand`.
    - A deal hook, `ITradeModule.Dealt(sim, body, seat)`.
    - `GameRegistry` hooks for a panel in the lobby or the character screen.
  - **The look in the world:** a held shape for each instrument and a per-entity tint in `WorldRenderer.HeldOf`/
    `HumanLook.AddHeld`, so an instrument is drawn in hand while it's played (it isn't today). The tongs' piece colour
    is the model.
  - **In the mod:** wood, stain and inlay chosen at the character, kept in the bag, sent with the seat, and stamped on
    the kit's instruments at the deal as a cosmetic component. It's left out of `StateHash`, like `Appearance`.
- **Tests:** wear and a snap from wear, the save round trip, and cosmetics reaching the instruments with the hashes
  untouched.

### 10. Lore verses (slim)
- In the book, with a quest the musician has word of (`ExpeditionSystem.IntelOf`), a verse is sung as the tune plays.
  It's picked from verse templates in `musician.json` (in the brand's voice), slotted with the quest's place and how
  grim it sounds.
- It's said through `sim.Speech.Say`; each listener who hears it clearly gets `sim.Expeditions.Hear` at a reduced
  weight, never more than the musician holds. This needs no new door.
- **Tests:** word goes only to those in earshot, and never past what the singer knew.

### 11. Recognition, pair dances, the art
- Listeners recognise a known tune (a small tip and mood bonus).
- Pair dances: two friends turning together.
- **Art** (`tools/chibi_people.py` and the builds):
  - dance frames (step left, step right, hop, spin, clap) in place of the hop, which stays as a fallback
  - cheer and emote frames
  - playing poses for each instrument
  - the instruments as held shapes and item icons
  - a performer's outfit: a town's hired player in good cloth, not an adventurer
  - `CreatureAnimator` keys the dance frames off the musician's beat, so everyone moves in time

### 12. Lessons, the bot, practice, docs
- **Lessons** through `registry.Lesson` (as `mods/carpenter`'s `CarpenterLessons` do), their words as the trade's
  `lessons` in `musician.json`, held to `TradeLessonTests`.
  - **Core:** play a scale, strike up a tune, keep the beat and the breath, tune a string.
  - **After:** the hat and requests, dancing, a lore verse.
- **Practice:** `MusicianModule.PracticeCustomer`, so "send customers" sends listeners.
- **The bot:** `MusicianBot : IBotTrade` (registered with `registry.Bot`).
  - `Play` minds the beat and the breath.
  - `Between` strikes up tunes, tunes when sour, and plays at the inn of an evening and at send-offs.
  - A sweep with the musician wanted (`SeasonSweep.Play(trades: [..., "Musician"])`) compares; the default baseline is
    untouched.
- **Docs:** DESIGN.md's musician row and notes, ROADMAP, AUDIO.md (music voices), MODDING.md (the new doors), a
  CLAUDE.md bullet for the musician mod.

### Afterwards: a band
Several musicians in co-op playing one tune together, joining on a tune struck up and keeping its beat (the drum's way
into the book, too). A plan of its own; on the roadmap.

## Verification
- `dotnet build HeroesNotIncluded.sln` with no warnings; `dotnet test -c Release` (`MusicianModTests`,
  `ModBoundaryTests`, `CommandTests.HostileCommandsDoNoHarm` for each new command).
- `--bench --seed 1234` and `--seed 99`: hashes unchanged after each core door, Fare's move especially.
- `--new-game --trade Musician`: play at the inn of an evening, watch the dance, take the hat up.
- `--festival-at 5 --trade Musician`; `--practice Musician`.
- Co-op: `--host` and `--join` with the musician on one side. Check the other hears the set placed in the world and
  sees the ring dance in step.
- `--season-sweep --seeds 8` with and without the musician wanted, so the musician's effect on Renown stays modest.
- `--check-art` for the new sprites and sounds.
