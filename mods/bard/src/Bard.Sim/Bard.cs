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
                WorkSystem.Start(sim, me, new PlaySession { Instrument = pipe, InstrumentName = "Pipe" });
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

/// <summary>A note as it was played: on what step of the session, which of the scale's keys, which octave.</summary>
public readonly record struct NoteEvent(int Step, int Degree, int Octave);

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
    public readonly List<NoteEvent> Notes = [];

    /// <summary>How many notes have been played in all (a count that only climbs, for sounds to follow).</summary>
    public int Played;

    public override Skill Skill => Skill.Social;
    public override float Xp => 0f;
    public override string Title => $"Playing the {InstrumentName.ToLowerInvariant()}";

    public static int Pack(int degree, bool octaveUp, bool on) => (degree & 15) | (octaveUp ? 1 << 4 : 0) | (on ? 1 << 5 : 0);

    public override bool StillValid(Simulation sim) =>
        sim.World.IsAlive(Instrument) && sim.World.IsAlive(Worker) && Inventory.OnPerson(sim, Worker, Instrument);

    public override void Finish(Simulation sim) { }

    protected override void Step() { } // it plays until it's put down (Esc)

    protected override void Custom(int value)
    {
        var degree = value & 15;
        if ((value & (1 << 5)) == 0 || degree >= 8) // let go, or off the network out of range
            return;
        Notes.Add(new NoteEvent(StepsDone, degree, (value >> 4) & 1));
        if (Notes.Count > Kept)
            Notes.RemoveAt(0);
        Played++;
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
    }

    protected override void ReadState(SaveReader r)
    {
        Instrument = r.Entity();
        InstrumentName = r.String();
        Played = r.Int();
        Notes.Clear();
        for (var n = r.Count(Kept); n > 0; n--)
            Notes.Add(new NoteEvent(r.Int(), Math.Clamp(r.Int(), 0, 7), Math.Clamp(r.Int(), 0, 1)));
    }
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
        WorkSystem.Start(sim, avatar, new PlaySession { Instrument = action.Target, InstrumentName = sim.World.Get<Thing>(action.Target).Def.Name });
    }
}
