using HeroesNotIncluded.Sim;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Work;

namespace HeroesNotIncluded.Musician;

/// <summary>
/// An instrument's strings as they stand (docs/MUSICIAN-PLAN.md §5): how many cents off its note each is (flat under
/// nothing, sharp over), and which are snapped (a bit each). Never shown as a number: it's heard, in the notes played and
/// in the beating against its true note as it's tuned (<see cref="TuneSession"/>).
/// </summary>
public sealed class Tuning
{
    public float[] Cents = [];
    public int Broken;
}

/// <summary>An instrument's tuning, made on first asking: a little off, as any instrument is from the case.</summary>
public static class Tunings
{
    /// <summary>The tuning of a strung instrument (made now if it's never been asked for), or null with no strings.</summary>
    public static Tuning? Of(Simulation sim, Entity instrument, InstrumentDef def)
    {
        if (def.Strings.Count == 0 || !sim.World.IsAlive(instrument))
            return null;
        if (sim.World.TryGet<Tuning>(instrument, out var t) && t.Cents.Length == def.Strings.Count)
            return t;
        // A hash of the instrument, not the sim's dice: asking for it changes nothing else.
        var rand = new SimRandom(SimRandom.Hash(instrument.Index, instrument.Generation, 0x7E11));
        t = new Tuning { Cents = [.. def.Strings.Select(_ => rand.Range(-6f, 6f))] };
        sim.World.Add(instrument, t);
        return t;
    }

    public static void Write(SaveWriter w, Tuning t)
    {
        w.Int(t.Cents.Length);
        foreach (var c in t.Cents)
            w.Float(c);
        w.Int(t.Broken);
    }

    public static Tuning Read(SaveReader r)
    {
        var t = new Tuning { Cents = new float[r.Count(16)] };
        for (var i = 0; i < t.Cents.Length; i++)
            t.Cents[i] = Math.Clamp(r.Float(), -TuneSession.Furthest, TuneSession.Furthest);
        t.Broken = r.Int();
        return t;
    }
}

/// <summary>
/// Strings drift as they're played: each note played pulls them a little, mostly flat (they stretch and slip), so an
/// evening's playing leaves an instrument noticeably out (and a sour one empties the room: <see cref="Performance.Sour"/>).
/// Nothing runs with no one playing.
/// </summary>
public sealed class InstrumentSystem : ISimSystem
{
    public string Name => "Instruments";
    public int Interval => 600;

    /// <summary>
    /// Cents a string goes flat for each note played (it stretches and slips: an evening's playing, a couple of thousand
    /// notes, leaves it sour), and how far it wanders either way besides (that times the square root of the notes: a
    /// walk of many small slips, not one lurch).
    /// </summary>
    public const float DriftPerNote = -0.015f, Wander = 0.12f;

    private readonly SimRandom _rand = new(0x57121);

    public void Update(Simulation sim)
    {
        var w = sim.World;
        foreach (var e in w.Query<WorkSession>())
        {
            if (w.Get<WorkSession>(e) is not PlaySession s || s.Cents.Length == 0 || s.Played <= s.DriftedAt
                || Tunings.Of(sim, s.Instrument, s.Def) is not { } tuning)
                continue;
            var notes = s.Played - s.DriftedAt;
            s.DriftedAt = s.Played;
            for (var i = 0; i < tuning.Cents.Length; i++)
                tuning.Cents[i] = Math.Clamp(tuning.Cents[i] + notes * DriftPerNote + _rand.Range(-1f, 1f) * Wander * MathF.Sqrt(notes),
                    -TuneSession.Furthest, TuneSession.Furthest);
            s.Cents = [.. tuning.Cents]; // the playing hears it at once
        }
    }
}

/// <summary>
/// Tuning a lute or a fiddle by ear: F held sounds the string with the same note true on the instrument, and the two beat
/// against each other, slower as they come together, till the beating stops. A/D turns the peg; Space goes on to the next
/// string. Turned well past its note a string may snap (a little more likely the further); F on a snapped one fits a spare
/// from the pack, slack till it's brought up. Never a number: the window shows only the wobble of the beating, as the
/// forge's heat is only colour.
/// </summary>
public sealed class TuneSession : WorkSession
{
    public Entity Instrument;
    public string InstrumentName = "";

    /// <summary>The instrument's def, given at the start: its strings.</summary>
    public InstrumentDef Def = InstrumentDef.PipeLike();

    /// <summary>The strings as they're tuned (written back to the instrument as the sim settles), and which is in hand.</summary>
    public float[] Cents = [];
    public int Broken;
    public int String;

    /// <summary>The peg being turned: -1 down, +1 up, 0 left be (A/D held).</summary>
    public int Turning;

    /// <summary>Spare strings in the pack as tuning began, and how many have been fitted (taken from the pack as it settles).</summary>
    public int Spares, Fitted, Taken;

    /// <summary>Strings snapped this tuning, and when the last went (session steps, for its twang).</summary>
    public int Snapped;
    public int SnappedAt = -1000;

    /// <summary>
    /// Cents the peg turns in a step held near the note: a slow turn, as a peg's is, so the beating can be heard to
    /// slow. Further out it turns quicker, <see cref="TurnPerCent"/> more a step for each cent off (a playtest found it
    /// slow and fiddly), so a string well out comes in fast and slows as it nears.
    /// </summary>
    public const float TurnRate = 0.25f, TurnPerCent = 0.02f;

    /// <summary>Let go this near its note, a string settles onto it (seated as the peg's let go): near is enough.</summary>
    public const float SeatCents = 5f;

    /// <summary>How far the peg turns a step with the string this far off.</summary>
    public static float RateAt(float cents) => TurnRate + MathF.Abs(cents) * TurnPerCent;

    /// <summary>Past this sharp a string's under strain, and may snap: likelier the further, a step at a time.</summary>
    public const float StrainAt = 35f, SnapPerStep = 0.0015f;

    /// <summary>Furthest a string goes either way; a new string comes on this far flat (slack, till it's brought up).</summary>
    public const float Furthest = 150f, NewString = -90f;

    public override Skill Skill => Skill.Social;
    public override float Xp => 0f;
    public override string Title => $"Tuning the {(Def.Label is { Length: > 0 } label ? label : InstrumentName.ToLowerInvariant())}";

    /// <summary>The string in hand is snapped.</summary>
    public bool StringBroken => (Broken & (1 << String)) != 0;

    /// <summary>The string in hand sounding (F held, and not snapped).</summary>
    public bool Sounding => Held && !StringBroken;

    protected override void Axis(int value)
    {
        Turning = Math.Sign(value);
        // Let go near its note: it seats there.
        if (Turning == 0 && Cents.Length > 0 && !StringBroken && MathF.Abs(Cents[String]) <= SeatCents)
            Cents[String] = 0f;
    }

    /// <summary>Space: the next string.</summary>
    protected override void Act()
    {
        if (Cents.Length > 0)
            String = (String + 1) % Cents.Length;
    }

    /// <summary>F on a snapped string: a spare fitted, if there's one (the playing sounds it held, otherwise).</summary>
    protected override void Press()
    {
        if (!StringBroken || Fitted >= Spares)
            return;
        Broken &= ~(1 << String);
        Cents[String] = NewString;
        Fitted++;
    }

    protected override void Step()
    {
        if (Turning == 0 || Cents.Length == 0 || StringBroken)
            return;
        ref var c = ref Cents[String];
        c = Math.Clamp(c + Turning * RateAt(c), -Furthest, Furthest);
        // Wound up past its note: it may go.
        if (Turning > 0 && c > StrainAt && Rand.NextFloat() < SnapPerStep * (c - StrainAt) / 10f)
        {
            Broken |= 1 << String;
            c = 0f;
            Snapped++;
            SnappedAt = StepsDone;
            Turning = 0;
        }
    }

    protected override void Cloned() => Cents = [.. Cents];

    public override bool StillValid(Simulation sim) => Inventory.OnPerson(sim, Worker, Instrument);

    /// <summary>The strings written back to the instrument as they're turned, and a spare fitted taken from the pack.</summary>
    public override void Settle(Simulation sim)
    {
        if (Tunings.Of(sim, Instrument, Def) is { } tuning)
        {
            tuning.Cents = [.. Cents];
            tuning.Broken = Broken;
        }
        for (; Taken < Fitted; Taken++)
            if (sim.Defs.AllThings.FirstOrDefault(t => t.Name == SpareDef) is { } def
                && Inventory.CarriedOf(sim, Worker, def).FirstOrDefault(Entity.Null) is { IsNull: false } spare)
                sim.DestroyThing(Inventory.TakeFromPerson(sim, Worker, spare, 1));
    }

    public override void Interrupted(Simulation sim) => Settle(sim);

    public override void Finish(Simulation sim) => Settle(sim);

    public const string SpareDef = "Strings";

    /// <summary>Tuning <paramref name="instrument"/>: its strings as they stand, the spares in the pack.</summary>
    public static TuneSession For(Simulation sim, Entity who, Entity instrument)
    {
        var def = sim.Defs.ModDef<InstrumentDef>(InstrumentDef.Kind, sim.World.Get<Thing>(instrument).Def.Name) ?? InstrumentDef.PipeLike();
        var tuning = Tunings.Of(sim, instrument, def);
        var spares = sim.Defs.AllThings.FirstOrDefault(t => t.Name == SpareDef) is { } sd
            ? Inventory.CarriedOf(sim, who, sd).Sum(e => sim.World.Get<Stack>(e).Count)
            : 0;
        return new()
        {
            Instrument = instrument,
            InstrumentName = sim.World.Get<Thing>(instrument).Def.Name,
            Def = def,
            Cents = tuning == null ? [] : [.. tuning.Cents],
            Broken = tuning?.Broken ?? 0,
            Spares = spares,
        };
    }

    protected override void WriteState(SaveWriter w)
    {
        w.Entity(Instrument);
        w.String(InstrumentName);
        w.Int(Cents.Length);
        foreach (var c in Cents)
            w.Float(c);
        w.Int(Broken);
        w.Int(String);
        w.Int(Turning);
        w.Int(Spares);
        w.Int(Fitted);
        w.Int(Taken);
        w.Int(Snapped);
        w.Int(SnappedAt);
    }

    protected override void ReadState(SaveReader r)
    {
        Instrument = r.Entity();
        InstrumentName = r.String();
        Def = r.Defs.ModDef<InstrumentDef>(InstrumentDef.Kind, InstrumentName) ?? InstrumentDef.PipeLike();
        Cents = new float[r.Count(16)];
        for (var i = 0; i < Cents.Length; i++)
            Cents[i] = Math.Clamp(r.Float(), -Furthest, Furthest);
        Broken = r.Int();
        String = Math.Clamp(r.Int(), 0, Math.Max(0, Cents.Length - 1));
        Turning = Math.Clamp(r.Int(), -1, 1);
        Spares = r.Int();
        Fitted = Math.Clamp(r.Int(), 0, Spares);
        Taken = Math.Clamp(r.Int(), 0, Fitted);
        Snapped = r.Int();
        SnappedAt = r.Int();
    }
}
