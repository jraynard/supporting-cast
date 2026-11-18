using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Work;

namespace HeroesNotIncluded.Sim.Carpentry;

/// <summary>
/// Firewood split at the chopping block (docs/DESIGN.md, "Carpenter"): a log stood on end, its
/// end grain showing the checks, the cracks it'll split along.
/// <list type="bullet">
/// <item>A/D (or the mouse across the log) puts the axe's edge over a check (<see cref="Aim"/>).</item>
/// <item>F held raises the axe, let go it strikes. On a check, and hard enough
/// (<see cref="NeedRaise"/>), it splits clean and the next check shows. Too soft, it bounces off.</item>
/// <item>A hard blow off the checks sticks the axe in the wood (<see cref="Stuck"/>): Space works it
/// free, a few rocks of the handle.</item>
/// </list>
/// Nothing's wasted: a good splitter is just a quick one.
/// </summary>
public sealed class SplitSession : WorkSession
{
    /// <summary>How fast holding F raises the axe, per step (as the hew).</summary>
    public const float RaisePerStep = SawSession.RaisePerStep * 1.4f;

    /// <summary>How far the aim moves a step on the keys, and how much of the way to the mouse.</summary>
    public const float AimPerStep = 0.025f, MouseEase = 0.3f;

    /// <summary>How near a check (across the log, −1..1) the edge must land, before skill widens it.</summary>
    public const float CheckWidth = 0.1f;

    /// <summary>How high the axe must be raised to split a log, and a great log.</summary>
    public const float NeedRaise = 0.5f, NeedRaiseGreat = 0.7f;

    /// <summary>Rocks of the handle to work a stuck axe free.</summary>
    public const int RocksToFree = 3;

    /// <summary>The chopping block.</summary>
    public Entity Station { get; set; } = Entity.Null;

    /// <summary>A great log on the block: more splits, and harder blows.</summary>
    public bool Great { get; set; }

    /// <summary>Splits it takes, and done so far.</summary>
    public int Splits => Great ? 5 : 3;
    public int SplitsDone { get; private set; }

    /// <summary>Where the edge is across the log's end, −1..1.</summary>
    public float Aim { get; private set; }

    /// <summary>The check showing now, across the log's end (−1..1), and the one after it.</summary>
    public float Check { get; private set; }

    /// <summary>How high the axe is raised (0 down, 1 as high as it goes).</summary>
    public float Raised { get; private set; }

    /// <summary>The axe is stuck in the wood: rocks of the handle still needed to free it.</summary>
    public int Stuck { get; private set; }

    /// <summary>Blows struck, and how the last went (for the panel and the sound).</summary>
    public int Blows { get; private set; }
    public SplitBlow LastBlow { get; private set; }

    public float CheckWidthNow => CheckWidth * Hands.Tolerance;
    public float Need => Great ? NeedRaiseGreat : NeedRaise;

    private bool _raising;
    private int _aim;
    private int _rock;

    public override Skill Skill => Skill.Crafting;
    public override float Xp => Great ? 20f : 12f;
    public override string Title => $"Splitting {(Great ? "a great log" : "a log")} (split {Math.Min(SplitsDone + 1, Splits)} of {Splits})";

    public override int3? Focus(Simulation sim) => sim.World.IsAlive(Station) ? sim.World.Get<Thing>(Station).Cell : null;

    public override bool StillValid(Simulation sim)
    {
        var w = sim.World;
        return Carpenters.IsBlock(sim, Station) && AvatarSystem.InReach(sim, w.Get<Position>(Worker).Cell, Station)
            && w.Get<Workbench>(Station) is { Active: { } r, Stocked: true } bench && bench.By == Worker && Carpenters.Splits(r);
    }

    protected override void Begin() => Check = NextCheck();

    private float NextCheck() => Rand.Range(-0.7f, 0.7f);

    protected override void Axis(int value) => _aim = value;

    /// <summary>Space: rock a stuck axe's handle.</summary>
    protected override void Act() => _rock = 1;

    protected override void Step()
    {
        if (Done)
            return;
        if (Stuck > 0)
        {
            if (_rock > 0)
                Stuck--;
            _rock = 0;
            Raised = 0f;
            _raising = false;
            return;
        }
        _rock = 0;
        if (!_raising)
        {
            Aim = PointU is { } u
                ? Math.Clamp(Aim + (Math.Clamp(u * 2f - 1f, -1f, 1f) - Aim) * MouseEase, -1f, 1f)
                : Math.Clamp(Aim + _aim * AimPerStep, -1f, 1f);
        }
        if (Held)
        {
            Raised = Math.Min(1f, Raised + RaisePerStep);
            _raising = true;
            return;
        }
        if (!_raising)
            return;
        _raising = false;
        Blows++;
        // Where the edge lands: where it was aimed, give or take the hand's shake.
        var landed = Aim + (Rand.NextFloat() * 2f - 1f) * Hands.Tremor * 0.15f;
        var onCheck = MathF.Abs(landed - Check) <= CheckWidthNow;
        var hard = Raised >= Need;
        Raised = 0f;
        if (onCheck && hard)
        {
            SplitsDone++;
            LastBlow = SplitBlow.Split;
            Sample(1f);
            Check = NextCheck();
            if (SplitsDone >= Splits)
                Done = true;
        }
        else if (!onCheck && hard)
        {
            LastBlow = SplitBlow.Stuck;
            Stuck = RocksToFree;
            Sample(0.2f);
        }
        else
        {
            LastBlow = SplitBlow.Bounced;
            Sample(0.6f);
        }
    }

    public override void Finish(Simulation sim)
    {
        var at = sim.LocationOf(Worker);
        var made = Crafting.Finish(sim, Station, Worker, Crafting.QualityFrom(Quality));
        var total = 0;
        foreach (var item in made)
        {
            total += sim.World.Get<Stack>(item).Count;
            Inventory.Stow(sim, Worker, item);
        }
        sim.Messages.Tell(Worker, MessageKind.Info, $"You split {total} firewood.", at);
    }

    protected override void WriteState(SaveWriter w)
    {
        w.Entity(Station);
        w.Bool(Great);
        w.Int(SplitsDone);
        w.Float(Aim);
        w.Float(Check);
        w.Float(Raised);
        w.Int(Stuck);
        w.Int(Blows);
        w.Byte((byte)LastBlow);
        w.Bool(_raising);
        w.Int(_aim);
        w.Int(_rock);
    }

    protected override void ReadState(SaveReader r)
    {
        Station = r.Entity();
        Great = r.Bool();
        SplitsDone = Math.Clamp(r.Int(), 0, Splits);
        Aim = Math.Clamp(r.Float(), -1f, 1f);
        Check = Math.Clamp(r.Float(), -1f, 1f);
        Raised = Math.Clamp(r.Float(), 0f, 1f);
        Stuck = Math.Clamp(r.Int(), 0, RocksToFree);
        Blows = Math.Max(0, r.Int());
        var blow = r.Byte();
        LastBlow = Enum.IsDefined((SplitBlow)blow) ? (SplitBlow)blow : SplitBlow.None;
        _raising = r.Bool();
        _aim = Math.Sign(r.Int());
        _rock = Math.Sign(r.Int());
    }
}

/// <summary>How a blow at the chopping block went.</summary>
public enum SplitBlow : byte
{
    None,
    Split,
    Bounced,
    Stuck,
}
