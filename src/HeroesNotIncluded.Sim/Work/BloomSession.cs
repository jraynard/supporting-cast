using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Work;

/// <summary>
/// Ore in the fire, run down to a bloom (<see cref="Smithing.BeginBloom"/>). Not a dash to the
/// right colour but a hold: the ore only comes together while the fire keeps it orange, so the
/// smith works the bellows (the work key) to keep it there. Let it sink and it stalls; blow it
/// yellow and the iron runs off into the slag, lost. The fire is <see cref="HeatSession"/>'s, and
/// the smith reads it the same way, by colour only. Draw the bloom out (Space) when it's grown,
/// or sooner for a smaller one. Back in the fire between heats on the anvil, it keeps growing.
/// </summary>
public sealed class BloomSession : WorkSession
{
    /// <summary>Seconds in the band it takes the ore to come all together.</summary>
    public const float ReduceSeconds = 20f;

    /// <summary>Seconds just past the band (<see cref="SlagSpread"/> over it) that would run all the iron to slag.</summary>
    public const float SlagSeconds = 12f;

    /// <summary>Degrees past the band at which the slag runs at the full <see cref="SlagSeconds"/> rate; hotter, faster.</summary>
    public const float SlagSpread = 30f;

    /// <summary>The forge.</summary>
    public Entity Station { get; set; }

    public string ItemLabel { get; set; } = "";

    /// <summary>The fire's heat, and what it settles to and climbs to (as <see cref="HeatSession"/>).</summary>
    public float Fire { get; private set; }
    public float FireRest { get; private set; }
    public float FireMax { get; private set; }

    /// <summary>The bloom's heat.</summary>
    public float Temp { get; private set; }

    /// <summary>The band it comes together in: from <see cref="Lo"/>, and past <see cref="SlagAt"/> the iron runs to slag.</summary>
    public float Lo { get; private set; }
    public float SlagAt { get; private set; }

    /// <summary>How far the ore's come together, 0..1, across every heat.</summary>
    public float Reduced { get; private set; }

    /// <summary>How much of the iron's run off to slag, 0..1, across every heat.</summary>
    public float Slag { get; private set; }

    /// <summary>Steps since the bellows were last worked (for keeping the forge blown afterward).</summary>
    public int SincePumped { get; private set; } = int.MaxValue / 2;

    public bool Pumping => Held;
    public Glow Glow => Smithing.GlowOf(Temp);
    public bool InBand => Temp >= Lo && Temp <= SlagAt;
    public bool TooHot => Temp > SlagAt;

    /// <summary>All come together: nothing more to gain in the fire.</summary>
    public bool Grown => Reduced >= 1f;

    public override int3? Focus(Simulation sim) => sim.World.IsAlive(Station) && sim.World.TryGet<Thing>(Station, out var t) ? t.Cell : null;

    public override Skill Skill => Skill.Crafting;
    public override float Xp => 10f;
    public override string Title => $"Running the {ItemLabel}";

    internal static BloomSession For(Simulation sim, Entity forge, WorkPiece p)
    {
        var w = sim.World;
        var (lo, slagAt) = Smithing.BloomBand(p.Recipe);
        var reach = w.Get<Thing>(forge).Def.Building?.Fuel is { } props && w.TryGet<Fueled>(forge, out var f) ? f.Burn(props).Reach : 0f;
        var lift = Crafting.BellowsOf(sim, forge)?.Lift ?? 0f;
        var now = sim.TemperatureAt(w.Get<Thing>(forge).Cell);
        var rest = reach * HeatSession.RestShare;
        var max = MathF.Max(now, reach + lift);
        var blown = w.TryGet<Blown>(forge, out var b) && b.Until >= sim.Clock.Tick;
        return new BloomSession
        {
            Station = forge, ItemLabel = Smithing.PieceLabel(sim, p), Temp = p.Temp, Lo = lo, SlagAt = slagAt,
            Reduced = p.Reduced, Slag = p.Slag,
            Fire = blown ? max : MathF.Min(now, rest), FireRest = rest, FireMax = max,
        };
    }

    public override bool StillValid(Simulation sim)
    {
        var w = sim.World;
        return w.IsAlive(Station) && AvatarSystem.InReach(sim, w.Get<Position>(Worker).Cell, Station)
            && w.TryGet<Fueled>(Station, out var f) && f.Fuel > 0 && Smithing.PieceOf(sim, Worker) != null;
    }

    protected override void Step()
    {
        if (Held)
        {
            Fire += (FireMax - Fire) * HeatSession.PumpRate;
            SincePumped = 0;
        }
        else
        {
            Fire += (FireRest - Fire) * HeatSession.SettleRate;
            SincePumped++;
        }
        Temp += (Fire - Temp) * HeatSession.SoakRate;
        if (Temp >= Lo)
            Reduced = Math.Min(1f, Reduced + 1f / (ReduceSeconds * SimClock.TicksPerSecond));
        if (Temp > SlagAt)
            Slag = Math.Min(1f, Slag + (Temp - SlagAt) / SlagSpread / (SlagSeconds * SimClock.TicksPerSecond));
    }

    /// <summary>Draw the bloom out (Space). The work key itself is the bellows.</summary>
    protected override void Act() => Done = true;

    public override void Finish(Simulation sim)
    {
        if (WriteBack(sim) is not { } p)
            return;
        var cell = sim.World.Get<Position>(Worker).Cell;
        sim.Messages.Add(MessageKind.Info, $"Drew the {ItemLabel} out {Smithing.GlowLabel(Glow)}: {Smithing.BloomWords(Reduced)}. {Smithing.NextStep(sim, Worker, p)}.", cell);
    }

    public override void Interrupted(Simulation sim) => WriteBack(sim);

    private WorkPiece? WriteBack(Simulation sim)
    {
        if (SincePumped < SimClock.TicksPerSecond * 2)
            Crafting.Pump(sim, Station);
        if (Smithing.PieceOf(sim, Worker) is not { } p)
            return null;
        p.Temp = Temp;
        p.Reduced = Reduced;
        p.Slag = Slag;
        return p;
    }

    protected override void WriteState(SaveWriter w)
    {
        w.Entity(Station);
        w.String(ItemLabel);
        w.Float(Fire);
        w.Float(FireRest);
        w.Float(FireMax);
        w.Float(Temp);
        w.Float(Lo);
        w.Float(SlagAt);
        w.Float(Reduced);
        w.Float(Slag);
        w.Int(SincePumped);
    }

    protected override void ReadState(SaveReader r)
    {
        Station = r.Entity();
        ItemLabel = r.String();
        Fire = r.Float();
        FireRest = r.Float();
        FireMax = r.Float();
        Temp = r.Float();
        Lo = r.Float();
        SlagAt = r.Float();
        Reduced = r.Float();
        Slag = r.Float();
        SincePumped = r.Int();
    }
}
