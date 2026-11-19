using HeroesNotIncluded.Sim;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Modding;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Work;

namespace HeroesNotIncluded.Musician;

/// <summary>
/// The musician's sim half (docs/MUSICIAN-PLAN.md), built as a mod from nothing (docs/MODDING.md): a trade only
/// players take (<see cref="TradeDef.PlayerOnly"/>), and an instrument played anywhere it's carried.
/// </summary>
public sealed class MusicianMod : ISimMod
{
    /// <summary>The instrument <paramref name="name"/> in <paramref name="me"/>'s pack (given one if they've none) and playing it: the session, or null if they're busy.</summary>
    private static PlaySession? Play(Simulation sim, Entity me, string name = "Pipe")
    {
        if (sim.Defs.AllThings.FirstOrDefault(t => t.Name == name) is not { } def || sim.World.Has<WorkSession>(me))
            return null;
        var instrument = Inventory.CarriedOf(sim, me, def).FirstOrDefault(Entity.Null);
        if (instrument.IsNull)
            Inventory.Stow(sim, me, instrument = sim.CreateItem(def, 1));
        WorkSystem.Start(sim, me, PlaySession.For(sim, instrument));
        return sim.World.Get<WorkSession>(me) as PlaySession;
    }

    public void Register(SimRegistry registry)
    {
        registry.Trade(new MusicianModule());
        registry.Pastime(new Listening()); // the audience: whoever's free goes to hear the music, or dance to it
        // --drill pipe (lute, fiddle, drum): that instrument in your pack and playing it, wherever you are.
        foreach (var (drill, name) in new[] { ("pipe", "Pipe"), ("lute", "Lute"), ("fiddle", "Fiddle"), ("drum", "HandDrum") })
            registry.Drill(drill, (sim, me) =>
            {
                Play(sim, me, name);
                return Entity.Null;
            });
        // --drill sailor: the same, the hands already carrying Drunken Sailor, for keeping time at once.
        registry.Drill("sailor", (sim, me) =>
        {
            if (Play(sim, me, "Pipe") is { } s && s.Book.Find(t => t.Name == "DrunkenSailor") is { } tune)
                s.Carry(tune, 0, 2 * SimClock.TicksPerSecond);
            return Entity.Null;
        });
    }
}

/// <summary>An instrument (the musician mod's own kind of def, <c>instruments</c>): how it sounds and what its keys play.</summary>
public sealed class InstrumentDef : Def
{
    /// <summary>Plucked, Bowed, Breath or Struck: shown only (what it does is said by the fields below).</summary>
    public string Voice { get; set; } = "Breath";

    /// <summary>
    /// What a carried tune spends and must be given back at the marked gaps: "Breath" (a pipe), "Bow" (a fiddle's bow
    /// drawn to its end and turned), or null for nothing (a lute, a drum): then there's no gauge and no gaps to mind.
    /// </summary>
    public string? Air { get; set; }

    /// <summary>A note lasts while it's held (a pipe, a bow), and a carried tune's long notes are swelled; otherwise each note is plucked or struck and dies away of itself.</summary>
    public bool Sustains { get; set; }

    /// <summary>How far A/D bend a sounding note, in semitones (0: not at all); for a drum, how far W pressing its head raises it.</summary>
    public float Bend { get; set; }

    /// <summary>
    /// A drum's strokes, by name ("doum", "tek", "ka"): the first keys play them, each its own sound in <see cref="Octaves"/>,
    /// with no scale, octave or semitone. Empty for an instrument of notes.
    /// </summary>
    public List<string> Strokes { get; set; } = [];

    /// <summary>Plays tunes from the book (the drum, of strokes rather than notes, doesn't yet).</summary>
    public bool Book { get; set; } = true;

    /// <summary>Holds chords in free play (the chord key with a note key: the triad on it) and strums them (F).</summary>
    public bool Chords { get; set; }

    /// <summary>
    /// Seconds a held note takes to come up and die away (a breath's quick, a bow's slower). Shown only: the sim
    /// never reads it.
    /// </summary>
    public float Fade { get; set; } = 0.04f;

    /// <summary>The chord on a key, by name ("D", "Em", "C#dim"): the triad of the scale built on it. Shown only.</summary>
    public string ChordName(int degree)
    {
        degree = Math.Clamp(degree, 0, 7) % 7;
        int Semis(int step) => Scale[step % 7] + step / 7 * 12;
        var third = Semis(degree + 2) - Semis(degree);
        var fifth = Semis(degree + 4) - Semis(degree);
        var root = NoteName(degree, 0, 0).TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
        return root + (third, fifth) switch { (4, 7) => "", (3, 7) => "m", (3, 6) => "dim", (4, 8) => "aug", _ => "" };
    }

    /// <summary>Struck, of strokes rather than notes.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsDrum => Strokes.Count > 0;

    /// <summary>What a session plays when it was made with no instrument def (a test's): a pipe.</summary>
    public static InstrumentDef PipeLike() => new() { Name = "Pipe", Label = "pipe", Air = "Breath", Sustains = true, Bend = 1f };

    /// <summary>
    /// A sound id per octave, its root note; the scale's notes are pitched up from it. Or one per key, octave by
    /// octave (sixteen), each its own note, so nothing's pitched far from where it was made (a fiddle's body would
    /// go with it).
    /// </summary>
    public List<string> Octaves { get; set; } = [];

    /// <summary>
    /// A looping sound per octave for a note held (as <see cref="Octaves"/>), and a sound as one starts (the breath's
    /// burst). With none, each note is its one-shot from <see cref="Octaves"/>, however long it's held.
    /// </summary>
    public List<string> Held { get; set; } = [];
    public string? Attack { get; set; }

    /// <summary>Each note key's step up from the root, in semitones.</summary>
    public List<int> Scale { get; set; } = [0, 2, 4, 5, 7, 9, 11, 12];

    /// <summary>The root's name and octave (a pipe in D: "D", 5), for naming the notes played. Shown only, never read by the sim.</summary>
    public string Root { get; set; } = "C";
    public int RootOctave { get; set; } = 4;

    public const string Kind = "instruments";

    private static readonly string[] Chromatic = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];

    /// <summary>The note a key plays, by name and octave ("F#5"): its degree, the octave above, a semitone up.</summary>
    public string NoteName(int degree, int octave, int accidental)
    {
        if (IsDrum)
            return degree < Strokes.Count ? Strokes[degree] : "";
        var root = Math.Max(0, Array.IndexOf(Chromatic, Root));
        var semis = root + Scale[Math.Clamp(degree, 0, Scale.Count - 1)] + octave * 12 + accidental;
        var oct = RootOctave + (int)Math.Floor(semis / 12f);
        return $"{Chromatic[((semis % 12) + 12) % 12]}{oct}";
    }
}

/// <summary>
/// One note of a tune as the hands play it: its step of the scale (<see cref="SongDef.Rest"/> for none), how long,
/// a semitone off, in which part, and whether a breath is marked after it (the gap it leaves is where to breathe).
/// </summary>
public readonly record struct SongNote(int Step, float Beats, int Accidental, int Section, bool BreathAfter = false)
{
    public bool IsRest => Step == SongDef.Rest;

    /// <summary>A note long enough to be shaped: swelled through (F held) it's a flourish.</summary>
    public bool IsLong => !IsRest && Beats >= SongDef.LongBeats;
}

/// <summary>
/// A tune in the book (the musician mod's own kind of def, <c>songs</c>): original or public-domain tunes only,
/// written out in our own notation (never a game's or a film's). A tune is parts played in the order of its
/// <see cref="Form"/> (verse, chorus, verse...), so the hands carry it the whole way through.
/// </summary>
public sealed class SongDef : Def, IModDef
{
    /// <summary>Jig, Reel, Air, March or Ballad: the dance it calls for (to come).</summary>
    public string Kind { get; set; } = "Air";

    /// <summary>Lively, Tender, Stirring or Sombre.</summary>
    public string Mood { get; set; } = "Lively";

    /// <summary>Beats a minute.</summary>
    public float Tempo { get; set; } = 100;

    /// <summary>
    /// A tune of one part, its notes as [step, beats, accidental]: the scale's step from the root (0..14 over two
    /// octaves; −1 a rest, −2 a breath mark after the note before), how long it lasts, and −1/+1 for a semitone off
    /// (optional). Shorthand for <see cref="Parts"/>.
    /// </summary>
    public List<List<float>> Notes { get; set; } = [];

    /// <summary>The tune's parts by name ("Verse", "Chorus", "A"), each laid out as <see cref="Notes"/> is.</summary>
    public Dictionary<string, List<List<float>>> Parts { get; set; } = [];

    /// <summary>The parts in the order they're played (a part may come round again). Empty: each part once, as listed.</summary>
    public List<string> Form { get; set; } = [];

    /// <summary>How many of the call's first notes strike the tune up (the lead-in).</summary>
    public int Opening { get; set; } = 5;

    /// <summary>
    /// The part whose first notes call the tune up: its best-known phrase (a chorus) rather than its first, which may
    /// be slow or plain. Called by another part, the hands play the tune from the top; by the first, they go on
    /// from the call. Unset: the first part of the form.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("call")]
    public string? CallPart { get; set; }

    /// <summary>The notes that call the tune up, played by the musician (built when the defs are read).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public List<SongNote> Call { get; private set; } = [];

    /// <summary>Where in <see cref="Sequence"/> the hands begin once it's called: the top, or straight on from the call.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public int CarriesFrom { get; private set; }

    public const string Kind_ = "songs";

    /// <summary>A rest's step: a beat or more of quiet (a place to breathe, too).</summary>
    public const int Rest = -1;

    /// <summary>A breath mark's step: no note, just where to take a breath, after the note before it ([-2] in a part).</summary>
    public const int BreathMark = -2;

    /// <summary>How long a note must be (beats) to be shaped.</summary>
    public const float LongBeats = 2f;

    /// <summary>Every note the tune plays from first to last, its form laid out (built when the defs are read).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public List<SongNote> Sequence { get; private set; } = [];

    public int StepAt(int i) => Sequence[i].Step;

    public float BeatsAt(int i) => Sequence[i].Beats;

    /// <summary>The name of the part at <paramref name="section"/> of the form, and which time round it is (1 the first).</summary>
    public (string Part, int Time) SectionAt(int section)
    {
        section = Math.Clamp(section, 0, Math.Max(0, Form.Count - 1));
        var name = Form.Count == 0 ? "" : Form[section];
        var time = 0;
        for (var i = 0; i <= section && i < Form.Count; i++)
            if (Form[i] == name)
                time++;
        return (name, time);
    }

    public void Resolve(DefDatabase db)
    {
        if (Parts.Count == 0)
            Parts = new() { ["Tune"] = Notes };
        if (Form.Count == 0)
            Form = [.. Parts.Keys];
        Sequence = [];
        for (var section = 0; section < Form.Count; section++)
        {
            if (!Parts.TryGetValue(Form[section], out var part))
                throw new Exception($"Song '{Name}': its form names a part '{Form[section]}' it doesn't have");
            foreach (var n in part)
            {
                if (n.Count == 0)
                    throw new Exception($"Song '{Name}': an empty note in '{Form[section]}'");
                var step = (int)n[0];
                if (step == BreathMark)
                {
                    if (Sequence.Count == 0 || Sequence[^1].Section != section)
                        throw new Exception($"Song '{Name}': a breath mark in '{Form[section]}' with no note before it");
                    Sequence[^1] = Sequence[^1] with { BreathAfter = true };
                    continue;
                }
                if (step != Rest && step is < 0 or > 14)
                    throw new Exception($"Song '{Name}': step {step} in '{Form[section]}' is off the instrument (0..14, or -1 for a rest)");
                Sequence.Add(new SongNote(step, n.Count > 1 ? Math.Max(0.125f, n[1]) : 1f, n.Count > 2 ? Math.Clamp((int)n[2], -1, 1) : 0, section));
            }
        }
        Opening = Math.Clamp(Opening, 3, 12);
        CallPart ??= Form[0];
        if (!Parts.TryGetValue(CallPart, out var called))
            throw new Exception($"Song '{Name}': it's called by a part '{CallPart}' it doesn't have");
        Call = [.. called.Where(n => n.Count > 0 && (int)n[0] != BreathMark).Take(Opening)
            .Select(n => new SongNote((int)n[0], n.Count > 1 ? Math.Max(0.125f, n[1]) : 1f, n.Count > 2 ? Math.Clamp((int)n[2], -1, 1) : 0, 0))];
        if (Call.Count < Opening || Call.Exists(n => n.IsRest))
            throw new Exception($"Song '{Name}': its call (the first {Opening} notes of '{CallPart}') needs {Opening} notes and no rests");
        CarriesFrom = CallPart == Form[0] ? Opening : 0;
    }
}

/// <summary>A note as it was played: on what step of the session, which of the scale's keys, which octave, a semitone up or down.</summary>
public readonly record struct NoteEvent(int Step, int Degree, int Octave, int Accidental = 0)
{
    /// <summary>Its step of the scale over both octaves (0..14): the octave key is 7, the second octave's keys 7 up.</summary>
    public int Index => Octave * 7 + Degree;

    public static NoteEvent At(int step, int index, int accidental = 0) =>
        index <= 7 ? new(step, index, 0, accidental) : new(step, index - 7, 1, accidental);
}

/// <summary>How the musician's playing: their own tune, or from the book (where a tune's opening strikes it up).</summary>
public enum PlayMode : byte
{
    /// <summary>Anything at all: no opening is listened for, so a melody of your own never turns into a tune from the book.</summary>
    Free,

    /// <summary>From the book: play a tune's opening and your hands carry the rest, while you keep it going.</summary>
    Book,
}

/// <summary>
/// Playing an instrument, started from the instrument carried, anywhere. Notes are kept in order so everyone in
/// earshot hears the same tune. Two modes (<see cref="PlayMode"/>), switched with the other action (Space):
/// free play, and the book, where a tune's opening hands the tune to the musician's hands. The hands find every
/// note; the musician keeps the rest going, as a real player must: the beat (F on it), the breath (held at the
/// marked gaps), and the long notes shaped (F held through). Each slip costs <see cref="Concentration"/>; lose it
/// all and the tune slips away.
/// A note is a <see cref="WorkInputKind.Custom"/> input: its degree in the low four bits, the octave above in
/// bit 4, sounded (rather than let go) in bit 5, a semitone up in bit 6. Bit 7 marks a breath instead: taken
/// (bit 0 set) or let out.
/// </summary>
public sealed class PlaySession : WorkSession
{
    /// <summary>The most notes kept: enough for a listener a few batches behind to catch up.</summary>
    public const int Kept = 64;

    public Entity Instrument;
    public string InstrumentName = "";

    /// <summary>The instrument's def, given when the session starts (it reads no defs while it plays): what its keys and its air are.</summary>
    public InstrumentDef Def = InstrumentDef.PipeLike();

    /// <summary>The note sounding bent, -1..1 of the instrument's <see cref="InstrumentDef.Bend"/> (a drum's head pressed, 0..1), and where A/D (W) are taking it.</summary>
    public float Bend, BendTo;

    /// <summary>A bend goes all the way in this many steps: a slide, not a jump.</summary>
    public const float BendSteps = 10f;

    /// <summary>The chord held (its key's degree, -1 for none) and in which octave, and whether the next strum's an up-stroke.</summary>
    public int Chord = -1, ChordOctave;
    public bool StrumUp;

    /// <summary>Strums this session (a chord struck as it's taken, or F over it).</summary>
    public int Strums;

    /// <summary>A <see cref="WorkInputKind.Custom"/> value's flag: the note key's chord, not the note (the chord key held).</summary>
    public const int ChordBit = 1 << 8;

    /// <summary>Every note played this session, the newest last (the oldest beyond <see cref="Kept"/> forgotten).</summary>
    public List<NoteEvent> Notes = [];

    /// <summary>How many notes have been played in all (a count that only climbs, for sounds to follow).</summary>
    public int Played;

    /// <summary>The tunes the musician knows, given when the session starts (it reads no defs while it plays).</summary>
    public List<SongDef> Book = [];

    public PlayMode Mode;

    /// <summary>When the book was opened (session steps): notes before it, played free, are no tune's opening.</summary>
    public int BookSince;

    /// <summary>The tune picked out in the book (W/S), its opening lit as a guide.</summary>
    public int Picked;

    /// <summary>The longest pause between an opening's notes, and the least quiet before its first, in steps.</summary>
    public const int MostPause = 90;

    /// <summary>The tune the musician's hands are carrying (struck up by its opening), or null.</summary>
    public SongDef? Carrying;

    /// <summary>The carried tune's next note, how far it's moved from the book's (the opening began elsewhere), and when it's due (session steps).</summary>
    public int Next, Shift;
    public float Due;

    /// <summary>The carried tune's pace against the book's: A/D nudge it.</summary>
    public float Pace = 1f;

    /// <summary>Tunes played through to their end this session, and tunes that slipped away (concentration lost).</summary>
    public int Finished, Slipped;

    /// <summary>
    /// How well the musician's mind is on the tune, 0..1: beats kept, breaths taken at the marks and long notes
    /// shaped hold it; a beat missed, a stray tap, a breath snatched mid-phrase or running out of air cost it.
    /// Under <see cref="Faltering"/> notes go missing; at nothing the tune slips away.
    /// </summary>
    public float Concentration = StartConcentration;

    /// <summary>
    /// The note still sounding, by its place in the count (<see cref="Played"/> as it was played; always the newest),
    /// or -1 for none: a pipe sounds while its key's held, a carried note for its length. Until when (session steps).
    /// </summary>
    public int Ringing = -1;
    public float RingingUntil;

    /// <summary>The note sounding now, if one is.</summary>
    public NoteEvent? RingingNote => Ringing >= 0 && Ringing == Played && Notes.Count > 0 ? Notes[^1] : null;

    /// <summary>A carried note sounds this share of its length (a breath of space after it), half when a breath's marked after it.</summary>
    public const float Legato = 0.92f, BeforeBreath = 0.5f;

    /// <summary>The carried tune's next beat (session steps), and whether it's been kept yet.</summary>
    public float Beat;
    public bool BeatKept;

    /// <summary>Air left, 0..1: it goes as the tune's played and comes back only while a breath's taken.</summary>
    public float Breath = 1f;

    /// <summary>A breath being taken (the breath key held).</summary>
    public bool Breathing;

    /// <summary>The marked gap to breathe in, from and to (session steps), and whether this one's been taken.</summary>
    public float GapFrom = -1000, GapTo = -1000;
    public bool GapTaken;

    /// <summary>
    /// Swelling the long note sounding: F, tapped on its beat, held down through it (while it rings; one before a
    /// breath rings only its first half). How many steps of it have been swelled.
    /// </summary>
    public bool Swelling => Held && LongAt >= 0 && Ringing >= 0;
    public int Swelled;

    /// <summary>The long note sounding, if one is: its index in the tune, and how many steps it lasts.</summary>
    public int LongAt = -1;
    public float LongSteps;

    /// <summary>Counts this session: beats kept and missed, breaths taken at the marks and out of place, long notes shaped (a flourish each).</summary>
    public int BeatsKept, BeatsMissed, Breaths, BreathsMisplaced, Flourishes;

    /// <summary>Beats of the carried tune gone by: the first <see cref="WarmUpBeats"/> cost nothing missed, a bar to find the beat in.</summary>
    public int BeatsGone;

    /// <summary>Carried notes that went missing: faltering, out of air, or breathed over.</summary>
    public int Dropped;

    /// <summary>When something last firmed or cost concentration, and which (for the window's flash), and what.</summary>
    public int LastJudged = -1000;
    public bool LastJudgedKept;
    public string LastWhy = "";

    public const float StartConcentration = 1f, Faltering = 0.5f;

    /// <summary>Beats at the start of a carried tune when a miss costs nothing: a bar to find the beat.</summary>
    public const int WarmUpBeats = 4;

    /// <summary>Swelling a long note spends breath this much faster.</summary>
    public const float SwellCost = 1.6f;

    /// <summary>What each thing done or missed gives or costs in concentration.</summary>
    public const float BeatGain = 0.05f, BeatLoss = 0.07f, StrayLoss = 0.03f, BreathGain = 0.05f, MisplacedLoss = 0.05f, WindedLoss = 0.1f, ShapedGain = 0.06f;

    /// <summary>A full breath lasts this many beats of playing, and comes back whole in this many steps of breathing.</summary>
    public const float BreathBeats = 14f, RefillSteps = 24f;

    /// <summary>A long note held swelled this share of its length is shaped.</summary>
    public const float ShapedShare = 0.7f;

    public const float LeastPace = 0.75f, MostPace = 1.35f, PaceStep = 0.05f;

    /// <summary>
    /// Steps of quiet between the opening's last note and the first the hands carry, on top of that note's
    /// length: a breath, so the last key pressed and the tune going on never run into each other.
    /// </summary>
    public const int LeadInBreath = 24;

    /// <summary>A <see cref="WorkInputKind.Custom"/> value's flag: a breath taken or let out, not a note.</summary>
    public const int BreathBit = 1 << 7;

    public override Skill Skill => Skill.Social;
    public override float Xp => 0f;
    public override string Title => $"Playing the {(Def.Label is { Length: > 0 } label ? label : InstrumentName.ToLowerInvariant())}";

    public static int Pack(int degree, bool octaveUp, bool on, bool sharp = false, bool chord = false) =>
        (degree & 15) | (octaveUp ? 1 << 4 : 0) | (on ? 1 << 5 : 0) | (sharp ? 1 << 6 : 0) | (chord ? ChordBit : 0);

    /// <summary>A breath taken (the key down) or let out (up), as a <see cref="WorkInputKind.Custom"/> value.</summary>
    public static int PackBreath(bool taking) => BreathBit | (taking ? 1 : 0);

    /// <summary>Steps either side of a beat that still keep it: steadier with practised hands, never half a beat.</summary>
    public float Window => Carrying == null ? 0f : Math.Min(12f * Math.Max(0.75f, Hands.Tolerance), StepsPerBeat * 0.45f);

    /// <summary>The carried tune's part being played now (an index into its form).</summary>
    public int Section => Carrying == null ? 0 : Carrying.Sequence[Math.Clamp(Next - 1, 0, Carrying.Sequence.Count - 1)].Section;

    /// <summary>A marked gap is open to breathe in now.</summary>
    public bool InGap => StepsDone >= GapFrom && StepsDone <= GapTo;

    /// <summary>The predictor's copy plays ahead on lists of its own: it never writes into the sim's.</summary>
    protected override void Cloned()
    {
        Notes = [.. Notes];
        Book = [.. Book];
    }

    public override bool StillValid(Simulation sim) =>
        sim.World.IsAlive(Instrument) && sim.World.IsAlive(Worker) && Inventory.OnPerson(sim, Worker, Instrument);

    public override void Finish(Simulation sim) { }

    /// <summary>Steps of the session a beat lasts at the carried tune's tempo and pace.</summary>
    public float StepsPerBeat => SimClock.TicksPerSecond * 60f / Math.Max(20f, (Carrying?.Tempo ?? 100f) * Pace);

    // It plays until it's put down (Esc); while a tune's carried, its notes come when they're due, its beats pass,
    // and its air goes.
    protected override void Step()
    {
        if (Ringing >= 0 && StepsDone >= RingingUntil)
            Ringing = -1;
        if (Bend != BendTo)
            Bend = BendTo > Bend ? Math.Min(BendTo, Bend + 1f / BendSteps) : Math.Max(BendTo, Bend - 1f / BendSteps);
        if (Breathing)
            Breath = Math.Min(1f, Breath + 1f / RefillSteps);
        if (Carrying == null)
            return;
        if (Breathing && InGap && !GapTaken)
        {
            GapTaken = true;
            Breaths++;
            Judge(true, BreathGain, "a breath");
        }
        if (Swelling)
            Swelled++;
        while (Carrying != null && StepsDone >= Due)
        {
            EndLong();
            if (Next >= Carrying.Sequence.Count)
            {
                Carrying = null;
                Finished++;
                return;
            }
            var note = Carrying.Sequence[Next];
            var length = note.Beats * StepsPerBeat;
            if (note.IsRest)
                OpenGap(StepsDone, StepsDone + length);
            else
                SoundCarried(note, length);
            Due += length;
            Next++;
        }
        if (Carrying == null)
            return;
        if (!Breathing && Def.Air != null && Next > Carrying.CarriesFrom) // air goes once the hands are playing, not while they wait to begin
            Breath = Math.Max(0f, Breath - (Swelling ? SwellCost : 1f) / (BreathBeats * StepsPerBeat));
        if (StepsDone > Beat + Window)
        {
            if (!BeatKept && InLong)
            {
                if (Held)
                    BeatsKept++; // held through: the long note's own beats, kept by the hold
            }
            else if (!BeatKept && BeatsGone >= WarmUpBeats)
            {
                BeatsMissed++;
                Judge(false, BeatLoss, "a beat missed");
            }
            BeatsGone++;
            Beat += StepsPerBeat;
            BeatKept = false;
        }
    }

    private void SoundCarried(SongNote note, float length)
    {
        var index = Math.Clamp(note.Step + Shift, 0, 14);
        var sounded = true;
        if (Breathing && !InGap)
        {
            // A breath snatched mid-phrase: this note goes for it.
            sounded = false;
            BreathsMisplaced++;
            Judge(false, MisplacedLoss, "a breath mid-phrase");
        }
        else if (Breath <= 0f)
        {
            sounded = false; // no air for it
            Judge(false, WindedLoss, "out of breath");
        }
        else
        {
            // A wandering mind falters: the less concentration, the more notes go missing.
            var falter = Math.Max(0f, Faltering - Concentration) * 1.4f;
            sounded = falter <= 0f || Rand.NextFloat() >= falter;
        }
        if (sounded)
        {
            Sound(NoteEvent.At(StepsDone, index, note.Accidental));
            if (Ringing >= 0)
                RingingUntil = StepsDone + length * (note.BreathAfter ? BeforeBreath : Legato);
        }
        else
        {
            Dropped++;
            Ringing = -1; // a gap where it should have been
        }
        if (note.IsLong && sounded && Def.Sustains) // a long note that never sounded can't be shaped, nor a plucked one
            (LongAt, LongSteps, Swelled) = (Next, length, 0);
        if (note.BreathAfter && Def.Air != null)
            OpenGap(StepsDone, StepsDone + length + Window); // the whole note, into the next
    }

    /// <summary>A gap to breathe in, from and to (session steps); one still open runs on (a rest right after a marked note).</summary>
    private void OpenGap(float from, float to)
    {
        if (StepsDone > GapTo)
            (GapFrom, GapTaken) = (from, false);
        GapTo = Math.Max(GapTo, to);
    }

    /// <summary>The beat being judged falls inside the long note sounding (not on its first beat): held, not tapped.</summary>
    private bool InLong => LongAt >= 0 && Beat > Due - LongSteps + Window;

    /// <summary>The steps of the long note sounding that can be swelled: as long as it rings.</summary>
    public float ShapeSteps => LongAt >= 0 && Carrying != null && Carrying.Sequence[LongAt].BreathAfter ? LongSteps * BeforeBreath : LongSteps * Legato;

    /// <summary>How far the long note sounding has been shaped, 0..1 (1: it counts).</summary>
    public float Shaping => LongAt < 0 ? 0f : Math.Clamp(Swelled / (ShapeSteps * ShapedShare), 0f, 1f);

    /// <summary>The long note sounding has ended: shaped if it was swelled through most of it.</summary>
    private void EndLong()
    {
        if (LongAt < 0)
            return;
        if (Shaping >= 1f)
        {
            Flourishes++;
            Judge(true, ShapedGain, "a long note shaped");
        }
        LongAt = -1;
        Swelled = 0;
    }

    private void Judge(bool kept, float change, string why)
    {
        (LastJudged, LastJudgedKept, LastWhy) = (StepsDone, kept, why);
        Concentration = Math.Clamp(Concentration + (kept ? change : -change), 0f, 1f);
        if (Concentration <= 0f)
        {
            Carrying = null; // the mind's gone off it and the tune with it
            Ringing = -1;
            LongAt = -1;
            Slipped++;
        }
    }

    private void Sound(NoteEvent note)
    {
        Notes.Add(note);
        if (Notes.Count > Kept)
            Notes.RemoveAt(0);
        Played++;
        // Held till it's let go (a carried note: till its length is up); a plucked or struck one dies away of itself.
        (Ringing, RingingUntil) = Def.Sustains ? (Played, float.MaxValue) : (-1, 0f);
    }

    /// <summary>F keeps time: on the carried tune's beat it holds concentration, off it costs a little.</summary>
    protected override void Press()
    {
        if (Carrying == null)
        {
            if (Chord >= 0)
                Strum();
            return;
        }
        if (!BeatKept && MathF.Abs(StepsDone - Beat) <= Window)
        {
            BeatKept = true;
            BeatsKept++;
            Judge(true, Breath > 0f ? BeatGain : 0f, "on the beat"); // with no air, the beat alone won't hold a tune
        }
        else
            Concentration = Math.Max(0.01f, Concentration - StrayLoss); // a stray tap jars, but never loses the tune by itself
    }

    protected override void Custom(int value)
    {
        if ((value & BreathBit) != 0)
        {
            Breathing = (value & 1) != 0 && Def.Air != null;
            return;
        }
        var degree = value & 15;
        if (degree >= 8 || Def.IsDrum && degree >= Def.Strokes.Count) // off the network out of range, or a drum key with no stroke
            return;
        if ((value & ChordBit) != 0)
        {
            // A chord: taken (and struck) as its key goes down, let go as it comes up. Free play only.
            if (!Def.Chords || Carrying != null || Mode != PlayMode.Free)
                return;
            if ((value & (1 << 5)) == 0)
            {
                if (Chord == degree)
                    Chord = -1;
                return;
            }
            (Chord, ChordOctave, StrumUp) = (degree, (value >> 4) & 1, false);
            Strum();
            return;
        }
        if ((value & (1 << 5)) == 0)
        {
            // Its key let go: the note you're holding stops (the hands' notes keep their own lengths).
            if (Carrying == null && RingingNote is { } held && held.Degree == degree)
                Ringing = -1;
            return;
        }
        if (Carrying != null)
            return; // the hands have the notes: yours are the beat, the breath and the long notes
        Sound(Def.IsDrum ? new NoteEvent(StepsDone, degree, 0) : new NoteEvent(StepsDone, degree, (value >> 4) & 1, (value & (1 << 6)) != 0 ? 1 : 0));
        if (Mode == PlayMode.Book)
            StrikeUp();
    }

    /// <summary>
    /// The held chord struck, as a hand strums: a down-stroke sweeps all its strings from the bass up (the triad of the
    /// scale on its key, and the root's octave over it), an up-stroke catches only the top two, high to low. They alternate.
    /// </summary>
    private void Strum()
    {
        var root = ChordOctave * 7 + Chord;
        List<int> strings = [];
        foreach (var step in (ReadOnlySpan<int>)[root, root + 2, root + 4, root + 7])
            if ((step > 14 ? step - 7 : step) is var index && !strings.Contains(index)) // off the top: the octave below, if it isn't there already
                strings.Add(index);
        strings.Sort();
        if (StrumUp)
            for (var i = strings.Count - 1; i >= strings.Count - 2; i--)
                Sound(NoteEvent.At(StepsDone, strings[i]));
        else
            foreach (var index in strings)
                Sound(NoteEvent.At(StepsDone, index));
        StrumUp = !StrumUp;
        Strums++;
    }

    /// <summary>A/D: a carried tune's pace nudged; otherwise the note sounding bent down or up while they're held.</summary>
    protected override void Axis(int value)
    {
        if (Carrying != null)
        {
            if (value != 0)
                Pace = Math.Clamp(Pace + value * PaceStep, LeastPace, MostPace);
        }
        else if (Def.Bend > 0f && !Def.IsDrum)
            BendTo = value;
    }

    /// <summary>W/S: another tune picked out in the book; on a drum, W held presses its head.</summary>
    protected override void Axis2(int value)
    {
        if (Def.IsDrum)
            BendTo = value < 0 && Def.Bend > 0f ? 1f : 0f;
        else if (Mode == PlayMode.Book && Carrying == null && value != 0 && Book.Count > 0)
            Picked = ((Picked + value) % Book.Count + Book.Count) % Book.Count;
    }

    /// <summary>Space: from free play to the book and back. Leaving the book lets a carried tune go.</summary>
    protected override void Act()
    {
        if (!Def.Book)
            return; // nothing in the book for it yet
        Carrying = null;
        Chord = -1;
        Ringing = -1;
        LongAt = -1;
        Mode = Mode == PlayMode.Free ? PlayMode.Book : PlayMode.Free;
        if (Mode == PlayMode.Book)
            BookSince = StepsDone;
    }

    /// <summary>
    /// The lead-in: the last notes played in the book, close enough together and after a quiet, against each tune's
    /// opening, by the shape of the steps between them (so it may start on any step). On a match the hands carry the
    /// rest, from the opening's last note on, as long as the book's beats say.
    /// </summary>
    private void StrikeUp()
    {
        foreach (var tune in Book)
        {
            var n = tune.Call.Count;
            if (n == 0 || tune.Sequence.Count <= tune.CarriesFrom || Notes.Count < n)
                continue;
            var played = Notes.GetRange(Notes.Count - n, n);
            if (played[0].Step < BookSince)
                continue; // played free, before the book was opened
            var close = true;
            for (var i = 1; i < n && close; i++)
                close = played[i].Step - played[i - 1].Step <= MostPause;
            // The opening begins a phrase: a quiet before it, so playing on from a phrase of your own never strikes one up.
            if (!close || Notes.Count > n && Notes[^(n + 1)].Step >= BookSince && played[0].Step - Notes[^(n + 1)].Step < MostPause)
                continue;
            var shift = played[0].Index - tune.Call[0].Step;
            var same = true;
            for (var i = 0; i < n && same; i++)
                same = played[i].Index - tune.Call[i].Step == shift && played[i].Accidental == tune.Call[i].Accidental;
            if (!same)
                continue;
            Carry(tune, shift, played[^1].Step + tune.Call[^1].Beats * SimClock.TicksPerSecond * 60f / Math.Max(20f, tune.Tempo) + LeadInBreath);
            return;
        }
    }

    /// <summary>The hands take <paramref name="tune"/> from where its call leaves it (<see cref="SongDef.CarriesFrom"/>), <paramref name="shift"/> steps from the book's, its next note due at <paramref name="due"/>.</summary>
    public void Carry(SongDef tune, int shift, float due)
    {
        Mode = PlayMode.Book;
        Carrying = tune;
        Shift = shift;
        Next = Math.Min(tune.CarriesFrom, tune.Sequence.Count);
        Pace = 1f;
        Concentration = StartConcentration;
        Breath = 1f;
        Due = due;
        Beat = Due; // time is kept from the first note the hands carry
        BeatsGone = 0;
        BeatKept = false;
        (GapFrom, GapTo, GapTaken) = (-1000, -1000, false);
        (LongAt, Swelled) = (-1, 0);
    }

    /// <summary>Written first, so a save of the session from before (which began with the instrument, never negative) still reads.</summary>
    private const int Layout = -3;

    protected override void WriteState(SaveWriter w)
    {
        w.Int(Layout);
        w.Entity(Instrument);
        w.String(InstrumentName);
        w.Int(Played);
        w.Int(Notes.Count);
        foreach (var n in Notes)
        {
            w.Int(n.Step);
            w.Int(n.Degree);
            w.Int(n.Octave);
            w.Int(n.Accidental);
        }
        w.Int(Book.Count);
        foreach (var tune in Book)
            w.String(tune.Name);
        w.NullableString(Carrying?.Name);
        w.Int(Next);
        w.Int(Shift);
        w.Float(Due);
        w.Float(Pace);
        w.Int(Finished);
        w.Byte((byte)Mode);
        w.Int(BookSince);
        w.Int(Picked);
        w.Int(Slipped);
        w.Float(Concentration);
        w.Float(Beat);
        w.Bool(BeatKept);
        w.Float(Breath);
        w.Bool(Breathing);
        w.Float(GapFrom);
        w.Float(GapTo);
        w.Bool(GapTaken);
        w.Int(Swelled);
        w.Int(LongAt);
        w.Float(LongSteps);
        w.Int(BeatsKept);
        w.Int(BeatsMissed);
        w.Int(Breaths);
        w.Int(BreathsMisplaced);
        w.Int(Flourishes);
        w.Int(Dropped);
        w.Int(LastJudged);
        w.Bool(LastJudgedKept);
        w.String(LastWhy);
        w.Int(Ringing);
        w.Float(RingingUntil);
        w.Int(BeatsGone);
        w.Float(Bend);
        w.Float(BendTo);
        w.Int(Chord);
        w.Int(ChordOctave);
        w.Bool(StrumUp);
        w.Int(Strums);
    }

    protected override void ReadState(SaveReader r)
    {
        var first = r.Int();
        var current = first == Layout;
        Instrument = current ? r.Entity() : new Entity(first, r.Int());
        InstrumentName = r.String();
        Def = r.Defs.ModDef<InstrumentDef>(InstrumentDef.Kind, InstrumentName) ?? InstrumentDef.PipeLike();
        Played = r.Int();
        Notes.Clear();
        for (var n = r.Count(Kept); n > 0; n--)
            Notes.Add(new NoteEvent(r.Int(), Math.Clamp(r.Int(), 0, 7), Math.Clamp(r.Int(), 0, 1), current ? Math.Clamp(r.Int(), -1, 1) : 0));
        SongDef? Tune(string? name) => name == null ? null : r.Defs.ModDef<SongDef>(SongDef.Kind_, name);
        Book.Clear();
        for (var n = r.Count(256); n > 0; n--)
            if (Tune(r.String()) is { } tune) // a tune whose def has gone is forgotten
                Book.Add(tune);
        Carrying = Tune(r.NullableString());
        Next = r.Int();
        Shift = r.Int();
        Due = r.Float();
        Pace = Math.Clamp(r.Float(), LeastPace, MostPace);
        Finished = r.Int();
        if (!current)
        {
            // From before the book had modes: a tune carried goes on from here, its air and mind fresh.
            if (Carrying != null)
                Carry(Carrying, Shift, Due);
            return;
        }
        Mode = (PlayMode)Math.Min(r.Byte(), (byte)PlayMode.Book);
        BookSince = r.Int();
        Picked = Math.Clamp(r.Int(), 0, Math.Max(0, Book.Count - 1));
        Slipped = r.Int();
        Concentration = Math.Clamp(r.Float(), 0f, 1f);
        Beat = r.Float();
        BeatKept = r.Bool();
        Breath = Math.Clamp(r.Float(), 0f, 1f);
        Breathing = r.Bool();
        GapFrom = r.Float();
        GapTo = r.Float();
        GapTaken = r.Bool();
        Swelled = r.Int();
        LongAt = r.Int();
        LongSteps = r.Float();
        BeatsKept = r.Int();
        BeatsMissed = r.Int();
        Breaths = r.Int();
        BreathsMisplaced = r.Int();
        Flourishes = r.Int();
        Dropped = r.Int();
        LastJudged = r.Int();
        LastJudgedKept = r.Bool();
        LastWhy = r.String();
        Ringing = r.Int();
        RingingUntil = r.Float();
        BeatsGone = r.Int();
        Bend = Math.Clamp(r.Float(), -1f, 1f);
        BendTo = Math.Clamp(r.Float(), -1f, 1f);
        Chord = Math.Clamp(r.Int(), -1, 7);
        ChordOctave = Math.Clamp(r.Int(), 0, 1);
        StrumUp = r.Bool();
        Strums = r.Int();
        if (Carrying != null && Next > Carrying.Sequence.Count)
            Next = Carrying.Sequence.Count; // the tune's def grew shorter
        if (Carrying == null || LongAt >= Carrying.Sequence.Count)
            LongAt = -1;
    }

    /// <summary>A playing session with the tunes the defs have (every tune is known, for now: a book of one's own is to come).</summary>
    public static PlaySession For(Simulation sim, Entity instrument) => new()
    {
        Instrument = instrument,
        InstrumentName = sim.World.Get<Thing>(instrument).Def.Name,
        Def = sim.Defs.ModDef<InstrumentDef>(InstrumentDef.Kind, sim.World.Get<Thing>(instrument).Def.Name) ?? InstrumentDef.PipeLike(),
        Book = [.. sim.Defs.ModDefs<SongDef>(SongDef.Kind_)],
    };
}

/// <summary>The musician's hooks: F with an instrument carried plays it, where you stand.</summary>
public sealed class MusicianModule : ITradeModule
{
    public string? Trade => "Musician";

    /// <summary>The instrument the character has in hand, or else the first in their pack; Entity.Null if none.</summary>
    public static Entity InstrumentOf(Simulation sim, Entity who)
    {
        if (sim.World.TryGet<Carrying>(who, out var carrying) && sim.World.IsAlive(carrying.Item)
            && sim.Defs.ModDef<InstrumentDef>(InstrumentDef.Kind, sim.World.Get<Thing>(carrying.Item).Def.Name) != null)
            return carrying.Item;
        foreach (var def in sim.Defs.ModDefs<InstrumentDef>(InstrumentDef.Kind))
            if (sim.Defs.AllThings.FirstOrDefault(t => t.Name == def.Name) is { } thing
                && Inventory.CarriedOf(sim, who, thing).FirstOrDefault(Entity.Null) is { IsNull: false } carried)
                return carried;
        return Entity.Null;
    }

    /// <summary>F where you stand: with an instrument, play it; without one, and music in earshot, dance to it (anyone, any trade).</summary>
    public AvatarAction? EarlyActionAt(Simulation sim, Entity avatar, int3 cell, int3 here)
    {
        if (cell != here || sim.World.Has<WorkSession>(avatar))
            return null;
        if (InstrumentOf(sim, avatar) is { IsNull: false } instrument)
            return new AvatarAction(AvatarActionKind.Custom, $"Play the {sim.World.Get<Thing>(instrument).Label}", cell, instrument);
        return DanceSession.MusicHeard(sim, avatar) is { IsNull: false } musician
            ? new AvatarAction(AvatarActionKind.Custom, "Dance to the music", cell, musician)
            : null;
    }

    public void Perform(Simulation sim, Entity avatar, AvatarAction action)
    {
        var w = sim.World;
        if (!w.IsAlive(action.Target) || w.Has<WorkSession>(avatar))
            return;
        if (w.Has<Creature>(action.Target)) // a musician: to dance to
        {
            if (DanceSession.MusicHeard(sim, avatar) == action.Target)
                WorkSystem.Start(sim, avatar, new DanceSession { Musician = action.Target, HeardAt = sim.Clock.Tick });
        }
        else if (Inventory.OnPerson(sim, avatar, action.Target))
            WorkSystem.Start(sim, avatar, PlaySession.For(sim, action.Target));
    }
}
