using HeroesNotIncluded.Sim;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Modding;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Work;

namespace HeroesNotIncluded.Bard;

/// <summary>
/// The bard's sim half (docs/BARD-PLAN.md), built as a mod from nothing (docs/MODDING.md): a trade only
/// players take (<see cref="TradeDef.PlayerOnly"/>), and an instrument played anywhere it's carried.
/// </summary>
public sealed class BardMod : ISimMod
{
    public void Register(SimRegistry registry)
    {
        registry.Trade(new BardModule());
        // --drill pipe: a pipe in your hands and playing it, wherever you are.
        registry.Drill("pipe", (sim, me) =>
        {
            var pipe = BardModule.InstrumentOf(sim, me);
            if (pipe.IsNull && sim.Defs.AllThings.FirstOrDefault(t => t.Name == "Pipe") is { } def)
                Inventory.Stow(sim, me, pipe = sim.CreateItem(def, 1));
            if (!pipe.IsNull && !sim.World.Has<WorkSession>(me))
                WorkSystem.Start(sim, me, PlaySession.For(sim, pipe));
            return Entity.Null;
        });
    }
}

/// <summary>An instrument (the bard mod's own kind of def, <c>instruments</c>): how it sounds and what its keys play.</summary>
public sealed class InstrumentDef : Def
{
    /// <summary>Plucked, Bowed, Breath or Struck (only Breath, the pipe, so far).</summary>
    public string Voice { get; set; } = "Breath";

    /// <summary>A sound id per octave, its root note; the scale's notes are pitched up from it.</summary>
    public List<string> Octaves { get; set; } = [];

    /// <summary>Each note key's step up from the root, in semitones.</summary>
    public List<int> Scale { get; set; } = [0, 2, 4, 5, 7, 9, 11, 12];

    public const string Kind = "instruments";
}

/// <summary>A tune in the book (the bard mod's own kind of def, <c>songs</c>): original tunes only.</summary>
public sealed class SongDef : Def
{
    /// <summary>Jig, Reel, Air, March or Ballad: the dance it calls for (to come).</summary>
    public string Kind { get; set; } = "Air";

    /// <summary>Lively, Tender, Stirring or Sombre.</summary>
    public string Mood { get; set; } = "Lively";

    /// <summary>Beats a minute.</summary>
    public float Tempo { get; set; } = 100;

    /// <summary>Each note as [step, beats]: the scale's step from the root (0..14 over two octaves) and how long it lasts.</summary>
    public List<List<float>> Notes { get; set; } = [];

    public const string Kind_ = "songs";

    public int StepAt(int i) => (int)Notes[i][0];

    public float BeatsAt(int i) => Notes[i].Count > 1 ? Notes[i][1] : 1f;
}

/// <summary>A note as it was played: on what step of the session, which of the scale's keys, which octave.</summary>
public readonly record struct NoteEvent(int Step, int Degree, int Octave)
{
    /// <summary>Its step of the scale over both octaves (0..14): the octave key is 7, the second octave's keys 7 up.</summary>
    public int Index => Octave * 7 + Degree;

    public static NoteEvent At(int step, int index) => index <= 7 ? new(step, index, 0) : new(step, index - 7, 1);
}

/// <summary>
/// Playing an instrument (free play, for now): each note key pressed is a note, kept in order so
/// everyone in earshot hears the same tune. Started from the instrument carried, anywhere. A note is
/// a <see cref="WorkInputKind.Custom"/> input: its degree in the low four bits, the octave above in
/// bit 4, sounded (rather than let go) in bit 5.
/// </summary>
public sealed class PlaySession : WorkSession
{
    /// <summary>The most notes kept: enough for a listener a few batches behind to catch up.</summary>
    public const int Kept = 64;

    public Entity Instrument;
    public string InstrumentName = "";

    /// <summary>Every note played this session, the newest last (the oldest beyond <see cref="Kept"/> forgotten).</summary>
    public List<NoteEvent> Notes = [];

    /// <summary>How many notes have been played in all (a count that only climbs, for sounds to follow).</summary>
    public int Played;

    /// <summary>The tunes the bard knows, given when the session starts (it reads no defs while it plays).</summary>
    public List<SongDef> Book = [];

    /// <summary>Notes of a tune's opening that strike it up (the lead-in), and the longest pause between them, in steps.</summary>
    public const int Opening = 5, MostPause = 90;

    /// <summary>The tune the bard's hands are carrying (struck up by its opening), or null in free play.</summary>
    public SongDef? Carrying;

    /// <summary>The carried tune's next note, how far it's moved from the book's (the opening began elsewhere), and when it's due (session steps).</summary>
    public int Next, Shift;
    public float Due;

    /// <summary>The carried tune's pace against the book's: A/D nudge it.</summary>
    public float Pace = 1f;

    /// <summary>Tunes played through to their end this session.</summary>
    public int Finished;

    public const float LeastPace = 0.75f, MostPace = 1.35f, PaceStep = 0.05f;

    public override Skill Skill => Skill.Social;
    public override float Xp => 0f;
    public override string Title => $"Playing the {InstrumentName.ToLowerInvariant()}";

    public static int Pack(int degree, bool octaveUp, bool on) => (degree & 15) | (octaveUp ? 1 << 4 : 0) | (on ? 1 << 5 : 0);

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
    private float StepsPerBeat => SimClock.TicksPerSecond * 60f / Math.Max(20f, Carrying!.Tempo * Pace);

    // It plays until it's put down (Esc); while a tune's carried, its notes come when they're due.
    protected override void Step()
    {
        while (Carrying != null && StepsDone >= Due)
        {
            if (Next >= Carrying.Notes.Count)
            {
                Carrying = null;
                Finished++;
                break;
            }
            Sound(NoteEvent.At(StepsDone, Math.Clamp(Carrying.StepAt(Next) + Shift, 0, 14)));
            Due += Carrying.BeatsAt(Next) * StepsPerBeat;
            Next++;
        }
    }

    private void Sound(NoteEvent note)
    {
        Notes.Add(note);
        if (Notes.Count > Kept)
            Notes.RemoveAt(0);
        Played++;
    }

    protected override void Custom(int value)
    {
        var degree = value & 15;
        if ((value & (1 << 5)) == 0 || degree >= 8) // let go, or off the network out of range
            return;
        Carrying = null; // a note of your own takes the tune back from your hands
        Sound(new NoteEvent(StepsDone, degree, (value >> 4) & 1));
        StrikeUp();
    }

    /// <summary>A tune's pace nudged while it's carried.</summary>
    protected override void Axis(int value)
    {
        if (Carrying != null && value != 0)
            Pace = Math.Clamp(Pace + value * PaceStep, LeastPace, MostPace);
    }

    /// <summary>Space lets a carried tune go.</summary>
    protected override void Act() => Carrying = null;

    /// <summary>
    /// The lead-in: the last notes played, close enough together, against each tune's opening, by the shape
    /// of the steps between them (so it may start on any step). On a match the hands carry the rest, from the
    /// opening's last note on, as long as the book's beats say.
    /// </summary>
    private void StrikeUp()
    {
        if (Notes.Count < Opening)
            return;
        var played = Notes.GetRange(Notes.Count - Opening, Opening);
        for (var i = 1; i < Opening; i++)
            if (played[i].Step - played[i - 1].Step > MostPause)
                return;
        foreach (var tune in Book)
        {
            if (tune.Notes.Count <= Opening)
                continue;
            var shift = played[0].Index - tune.StepAt(0);
            var same = true;
            for (var i = 1; i < Opening && same; i++)
                same = played[i].Index - tune.StepAt(i) == shift;
            if (!same)
                continue;
            Carrying = tune;
            Shift = shift;
            Next = Opening;
            Pace = 1f;
            Due = played[^1].Step + tune.BeatsAt(Opening - 1) * StepsPerBeat;
            return;
        }
    }

    protected override void WriteState(SaveWriter w)
    {
        w.Entity(Instrument);
        w.String(InstrumentName);
        w.Int(Played);
        w.Int(Notes.Count);
        foreach (var n in Notes)
        {
            w.Int(n.Step);
            w.Int(n.Degree);
            w.Int(n.Octave);
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
    }

    protected override void ReadState(SaveReader r)
    {
        Instrument = r.Entity();
        InstrumentName = r.String();
        Played = r.Int();
        Notes.Clear();
        for (var n = r.Count(Kept); n > 0; n--)
            Notes.Add(new NoteEvent(r.Int(), Math.Clamp(r.Int(), 0, 7), Math.Clamp(r.Int(), 0, 1)));
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
    }

    /// <summary>A playing session with the tunes the defs have (every tune is known, for now: a book of one's own is to come).</summary>
    public static PlaySession For(Simulation sim, Entity instrument) => new()
    {
        Instrument = instrument,
        InstrumentName = sim.World.Get<Thing>(instrument).Def.Name,
        Book = [.. sim.Defs.ModDefs<SongDef>(SongDef.Kind_)],
    };
}

/// <summary>The bard's hooks: F with an instrument carried plays it, where you stand.</summary>
public sealed class BardModule : ITradeModule
{
    public string? Trade => "Bard";

    /// <summary>The first instrument the character carries (in hand, then the pack), Entity.Null if none.</summary>
    public static Entity InstrumentOf(Simulation sim, Entity who)
    {
        foreach (var def in sim.Defs.ModDefs<InstrumentDef>(InstrumentDef.Kind))
            if (sim.Defs.AllThings.FirstOrDefault(t => t.Name == def.Name) is { } thing
                && Inventory.CarriedOf(sim, who, thing).FirstOrDefault(Entity.Null) is { IsNull: false } carried)
                return carried;
        return Entity.Null;
    }

    public AvatarAction? EarlyActionAt(Simulation sim, Entity avatar, int3 cell, int3 here)
    {
        if (cell != here || sim.World.Has<WorkSession>(avatar) || InstrumentOf(sim, avatar) is not { IsNull: false } instrument)
            return null;
        return new AvatarAction(AvatarActionKind.Custom, $"Play the {sim.World.Get<Thing>(instrument).Label}", cell, instrument);
    }

    public void Perform(Simulation sim, Entity avatar, AvatarAction action)
    {
        if (!sim.World.IsAlive(action.Target) || !Inventory.OnPerson(sim, avatar, action.Target) || sim.World.Has<WorkSession>(avatar))
            return;
        WorkSystem.Start(sim, avatar, PlaySession.For(sim, action.Target));
    }
}
