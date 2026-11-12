using SupportingCast.Sim.Components;
using SupportingCast.Sim.Defs;
using SupportingCast.Sim.Saving;
using SupportingCast.Sim.Systems;

namespace SupportingCast.Sim.Work;

/// <summary>
/// The piece in the fire (<see cref="Smithing"/>). Hold to work the bellows: the fire climbs toward
/// all the fuel and bellows can give, and sinks back to a sullen glow when you stop. An unblown fire
/// is too cool for iron, so every heat starts there and the bellows bring it up, unless they were
/// worked moments ago and it's still blown. The piece
/// takes the fire's heat, and you read it only by its colour: draw it out (the axis, D) when it's right.
/// Left past white heat it burns, costing it quality, and at last burns through.
/// </summary>
public sealed class HeatSession : WorkSession
{
    /// <summary>Share of the gap to the bellows' heat the fire closes each step while pumped.</summary>
    public const float PumpRate = 0.006f;

    /// <summary>Share of the gap back to its resting heat the fire closes each step when left alone.</summary>
    public const float SettleRate = 0.0014f;

    /// <summary>Share of the gap to the fire's heat the piece closes each step.</summary>
    public const float SoakRate = 0.006f;

    /// <summary>An unblown fire rests at this share of its fuel's reach: a dull red on charcoal, blood red on coal, short of iron's heat.</summary>
    public const float RestShare = 0.65f;

    /// <summary>The forge.</summary>
    public Entity Station { get; set; }

    public string ItemLabel { get; set; } = "";

    /// <summary>The fire's heat, and what it settles to and climbs to.</summary>
    public float Fire { get; private set; }
    public float FireRest { get; private set; }
    public float FireMax { get; private set; }

    /// <summary>The piece's heat.</summary>
    public float Temp { get; private set; }

    /// <summary>The piece's working heat, and where it starts to burn.</summary>
    public float Lo { get; private set; }
    public float Hi { get; private set; }
    public float BurnAt { get; private set; }

    /// <summary>Steps spent past burning heat, this and earlier heats.</summary>
    public int BurnSteps { get; private set; }

    /// <summary>Burnt through: the piece is lost.</summary>
    public bool Ruined { get; private set; }

    /// <summary>Steps since the bellows were last worked (for keeping the forge blown afterward).</summary>
    public int SincePumped { get; private set; } = int.MaxValue / 2;

    public bool Pumping => Held;

    /// <summary>
    /// A practised eye's moment to draw it out: glowing the colour in the middle of its working
    /// heat, or as hot as this fire will get it if that's short of the middle (for bots and tests).
    /// </summary>
    public bool LooksReady => Glow >= Smithing.GlowOf((Lo + Hi) * 0.5f) || (Fire >= FireMax - 15f && Temp >= Math.Min((Lo + Hi) * 0.5f, FireMax - 40f));
    public Glow Glow => Smithing.GlowOf(Temp);
    public bool Burning => Temp > BurnAt;

    public override Skill Skill => Skill.Crafting;
    public override float Xp => 5f;
    public override string Title => $"Heating the {ItemLabel}";

    internal static HeatSession For(Simulation sim, Entity forge, WorkPiece p)
    {
        var w = sim.World;
        var (lo, hi, burn) = Smithing.Window(p.Recipe);
        var reach = w.Get<Thing>(forge).Def.Building?.Fuel is { } props && w.TryGet<Fueled>(forge, out var f) ? f.Burn(props).Reach : 0f;
        var lift = Crafting.BellowsOf(sim, forge)?.Lift ?? 0f;
        // The air on the forge's cell is the building's heat, held near its cap all day; the fire in
        // the hearth is only that hot while it's being blown.
        var now = sim.TemperatureAt(w.Get<Thing>(forge).Cell);
        var rest = reach * RestShare;
        var max = MathF.Max(now, reach + lift);
        var blown = w.TryGet<Blown>(forge, out var b) && b.Until >= sim.Clock.Tick;
        return new HeatSession
        {
            Station = forge, ItemLabel = Smithing.PieceLabel(sim, p), Temp = p.Temp, BurnSteps = p.BurnSteps,
            Lo = lo, Hi = hi, BurnAt = burn,
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
            Fire += (FireMax - Fire) * PumpRate;
            SincePumped = 0;
        }
        else
        {
            Fire += (FireRest - Fire) * SettleRate;
            SincePumped++;
        }
        Temp += (Fire - Temp) * SoakRate;
        if (Temp > BurnAt)
        {
            BurnSteps++;
            if (BurnSteps >= Smithing.BurnSecondsToRuin * SimClock.TicksPerSecond)
            {
                Ruined = true;
                Done = true;
            }
        }
    }

    /// <summary>Draw it out of the fire (D, a push toward the anvil). The work key itself is the bellows.</summary>
    protected override void Axis(int value)
    {
        if (value > 0)
            Done = true;
    }

    public override void Finish(Simulation sim)
    {
        if (WriteBack(sim) is not { } p)
            return;
        if (Ruined)
        {
            Smithing.Ruin(sim, Worker, p);
            return;
        }
        var cell = sim.World.Get<Position>(Worker).Cell;
        sim.Messages.Add(MessageKind.Info, $"Drew the {ItemLabel} out {Smithing.GlowLabel(Glow)}: {Smithing.NextStep(sim, Worker, p)}.", cell);
    }

    public override void Interrupted(Simulation sim) => WriteBack(sim);

    private WorkPiece? WriteBack(Simulation sim)
    {
        // A fire just blown stays hot a while: the forge itself is kept blown, so the room warms too.
        if (SincePumped < SimClock.TicksPerSecond * 2)
            Crafting.Pump(sim, Station);
        if (Smithing.PieceOf(sim, Worker) is not { } p)
            return null;
        p.Temp = Temp;
        p.BurnSteps = BurnSteps;
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
        w.Float(Hi);
        w.Float(BurnAt);
        w.Int(BurnSteps);
        w.Bool(Ruined);
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
        Hi = r.Float();
        BurnAt = r.Float();
        BurnSteps = r.Int();
        Ruined = r.Bool();
        SincePumped = r.Int();
    }
}
