using HeroesNotIncluded.Sim.Ai;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Maps;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Work;

namespace HeroesNotIncluded.Sim.Tinkering;

/// <summary>
/// A gap a crisis tore in the town: the cells it took and what stood there, so mending can put it
/// back. The cells are really gone (the way across is cut and the pathfinder knows it); this is
/// the town's memory of what was there.
/// </summary>
public sealed class Breach
{
    public int Id;
    public ThingDef Def = null!;
    public List<(int3 Cell, byte Facing)> Cells = [];
    public long BrokenTick;

    /// <summary>How the span has settled, for its mending (<see cref="SpanSession"/>).</summary>
    public uint Seed;

    public string Label => Def.Label;

    /// <summary>The middle of the gap, for messages and the camera.</summary>
    public int3 Middle => Cells.Count == 0 ? default : Cells[Cells.Count / 2].Cell;
}

/// <summary>
/// What crises break in town, and its mending. A raid may pull the bridge down behind it: a line
/// of the span across its middle is gone, the road over the river is cut (parties come round by
/// another way, and the carter can't get to the inn), and the valley's Dread grows each hour it
/// stays down. The tinker mends it (<see cref="TradeDef.MendsTown"/>): two wood to the gap and a
/// span braced level (<see cref="SpanSession"/>), and it's whole again.
/// </summary>
public sealed class Repairs : ISimSystem, ISaveState
{
    public string Name => "Repairs";
    public int Interval => SimClock.TicksPerHour;

    /// <summary>Wood a mending takes: the timber centring the stones are reset on.</summary>
    public const int MendWood = 2;

    private readonly List<Breach> _breaches = [];
    private int _nextId = 1;

    public IReadOnlyList<Breach> Breaches => _breaches;

    /// <summary>A bridge is down: the road over the river is cut.</summary>
    public bool RoadCut => _breaches.Count > 0;

    public void Update(Simulation sim)
    {
        if (!sim.Round.Live || _breaches.Count == 0)
            return;
        sim.Reckoning.Add(sim, 0, _breaches.Count * sim.Defs.Season.DreadPerBreachHour);
    }

    public Breach? Find(int id) => _breaches.Find(b => b.Id == id);

    /// <summary>The breach at a cell (or the cell under it: a click on the gap lands on the water), if any.</summary>
    public Breach? At(int3 cell)
    {
        foreach (var b in _breaches)
            foreach (var (c, _) in b.Cells)
                if (c.X == cell.X && c.Y == cell.Y && Math.Abs(c.Z - cell.Z) <= 1)
                    return b;
        return null;
    }

    /// <summary>
    /// Bring a bridge down: a line of its span across the middle (along its longer side), and any
    /// of it that falls with it. Null if the town has no bridge, or it's already down.
    /// </summary>
    public Breach? BreakBridge(Simulation sim, SimRandom rand)
    {
        if (_breaches.Count > 0)
            return null;
        var w = sim.World;
        var span = new List<(Entity E, int3 Cell, byte Facing, ThingDef Def)>();
        foreach (var e in w.Query<Thing>())
            if (w.Get<Thing>(e) is { Def.IsFloor: true, Def.Name: "StoneBridge" } t && !w.Has<Construction>(e))
                span.Add((e, t.Cell, t.Facing, t.Def));
        if (span.Count == 0)
            return null;
        int minX = span.Min(s => s.Cell.X), maxX = span.Max(s => s.Cell.X);
        int minY = span.Min(s => s.Cell.Y), maxY = span.Max(s => s.Cell.Y);
        var alongX = maxX - minX >= maxY - minY;
        var mid = alongX ? (minX + maxX + 1) / 2 : (minY + maxY + 1) / 2;
        var breach = new Breach { Id = _nextId++, Def = span[0].Def, BrokenTick = sim.Clock.Tick, Seed = rand.NextUInt() };
        foreach (var s in span)
            if ((alongX ? s.Cell.X : s.Cell.Y) == mid)
            {
                breach.Cells.Add((s.Cell, s.Facing));
                sim.DestroyThing(s.E);
            }
        // Whatever fell with it (a stretch left without support) is part of the gap too.
        foreach (var s in span)
            if (!w.IsAlive(s.E) && !breach.Cells.Exists(c => c.Cell == s.Cell))
                breach.Cells.Add((s.Cell, s.Facing));
        _breaches.Add(breach);
        sim.Messages.Add(MessageKind.Danger, $"The {breach.Label} is down! The road over the river is cut until it's mended.", breach.Middle);
        return breach;
    }

    /// <summary>Why this worker can't mend this breach now; null if they can.</summary>
    public static string? MendProblem(Simulation sim, Entity worker, Breach? breach)
    {
        var w = sim.World;
        if (breach == null)
            return "there's nothing broken there";
        if (!w.TryGet<Trade>(worker, out var trade) || !trade.Def.MendsTown)
            return "mending the town is the tinker's work";
        var here = w.Get<Position>(worker).Cell;
        if (!breach.Cells.Exists(c => AvatarSystem.InReach(here, c.Cell)))
            return "you need to be at the gap";
        return Wagons.WoodOn(sim, worker) < MendWood ? $"bracing the span takes {MendWood} wood" : null;
    }

    /// <summary>Begin mending a breach, if this worker can.</summary>
    public static void StartMend(Simulation sim, Entity worker, Breach? breach)
    {
        if (MendProblem(sim, worker, breach) is { } why)
        {
            sim.Messages.Add(MessageKind.Info, $"Can't mend it: {why}.", sim.LocationOf(worker));
            return;
        }
        WorkSystem.Start(sim, worker, new SpanSession { Breach = breach!.Id, Seed = breach.Seed });
    }

    /// <summary>The span's braced: the stones go back, and the way across is open again.</summary>
    internal void Mended(Simulation sim, Entity worker, int id, bool byHand = true)
    {
        if (Find(id) is not { } breach || (byHand && Wagons.WoodOn(sim, worker) < MendWood))
            return;
        if (byHand)
            Wagons.UseWood(sim, worker, MendWood);
        foreach (var (cell, facing) in breach.Cells)
            if (sim.Map.Floor[sim.Map.Idx(cell)].IsNull)
                sim.SpawnStructure(breach.Def, cell, facing);
        _breaches.Remove(breach);
        sim.Messages.Add(MessageKind.Good, $"{sim.World.Get<Creature>(worker).Name} mends the {breach.Label}. The road over the river is open again.", breach.Middle);
    }

    public void Save(SaveWriter w, Simulation sim)
    {
        w.Int(_nextId);
        w.Int(_breaches.Count);
        foreach (var b in _breaches)
        {
            w.Int(b.Id);
            w.Def(b.Def);
            w.Long(b.BrokenTick);
            w.UInt(b.Seed);
            w.Int(b.Cells.Count);
            foreach (var (cell, facing) in b.Cells)
            {
                w.Int(cell.X);
                w.Int(cell.Y);
                w.Int(cell.Z);
                w.Int(facing);
            }
        }
    }

    public void Load(SaveReader r, Simulation sim)
    {
        _nextId = r.Int();
        _breaches.Clear();
        for (var n = r.Count(1000); n > 0; n--)
        {
            var b = new Breach { Id = r.Int(), Def = r.Def<ThingDef>(), BrokenTick = r.Long(), Seed = r.UInt() };
            for (var c = r.Count(10_000); c > 0; c--)
                b.Cells.Add((new int3(r.Int(), r.Int(), r.Int()), (byte)r.Int()));
            _breaches.Add(b);
        }
    }
}

/// <summary>
/// Bracing a broken span. The stones settle on their new centring out of level, and a level slides
/// along the span and back; its bubble reads low where a stone sags and high where it rides up. F
/// drives a wedge under the stone at the level, lifting it; A eases one out, letting it down. Each
/// stone nudges its neighbours a little. With every stone within the band, the span is sound.
/// </summary>
public sealed class SpanSession : WorkSession
{
    public const int Stones = 6;

    /// <summary>Seconds for the level to slide from one end of the span to the other.</summary>
    public const float PassSeconds = 3f;

    /// <summary>How far one wedge moves the stone at the level, and the ones beside it.</summary>
    public const float Lift = 0.06f, LiftBeside = 0.015f;

    /// <summary>Within this everywhere (before skill widens it), the span is level.</summary>
    public const float Band = 0.035f;

    public int Breach { get; set; }
    public uint Seed { get; set; }

    /// <summary>How far each stone stands high (+) or sags (−).</summary>
    public float[] Offsets { get; private set; } = [];

    /// <summary>Where the level is along the span, 0..1, and which way it's sliding.</summary>
    public float At { get; private set; }
    public int Way { get; private set; } = 1;

    /// <summary>What the level reads now: the span under it, with the hand's shake in it.</summary>
    public float Reading { get; private set; }

    public bool Level { get; private set; }

    private int _key;

    public float BandWidth => Band * Hands.Tolerance;

    /// <summary>The stone under the level now (stone i sits at <c>At = i / (Stones - 1)</c>).</summary>
    public int AtStone => Math.Clamp((int)MathF.Round(At * (Stones - 1)), 0, Stones - 1);

    /// <summary>The worst any stone stands out of level.</summary>
    public float Worst => Offsets.Length == 0 ? 0f : Offsets.Max(MathF.Abs);

    public override Skill Skill => Skill.Construction;
    public override float Xp => Level ? 60f : 15f;
    public override string Title => "Bracing the bridge";

    public override int3? Focus(Simulation sim) => sim.Repairs.Find(Breach)?.Middle;

    public override bool StillValid(Simulation sim) => Repairs.MendProblem(sim, Worker, sim.Repairs.Find(Breach)) == null;

    /// <summary>How the span settled: the same every time it's looked at, until it's braced.</summary>
    public static float[] Settle(uint seed)
    {
        var rand = new SimRandom(seed);
        var o = new float[Stones];
        for (var i = 0; i < Stones; i++)
            o[i] = rand.Range(-0.3f, 0.15f); // mostly sagging: they've fallen
        var worst = 0;
        for (var i = 1; i < Stones; i++)
            if (MathF.Abs(o[i]) > MathF.Abs(o[worst]))
                worst = i;
        o[worst] = -0.32f;
        return o;
    }

    protected internal override void Begin() => Offsets = Settle(Seed);

    protected override void Cloned() => Offsets = (float[])Offsets.Clone();

    protected override void Axis(int value)
    {
        if (value < 0)
            _key = -1;
    }

    protected override void Press() => _key = 1;

    protected override void Step()
    {
        if (Done)
            return;
        if (_key != 0)
        {
            Wedge(_key);
            _key = 0;
            if (Worst <= BandWidth)
            {
                Level = true;
                Done = true;
                return;
            }
        }
        At += Way / (PassSeconds * SimClock.TicksPerSecond);
        if (At >= 1f || At <= 0f)
        {
            At = Math.Clamp(At, 0f, 1f);
            Way = -Way;
        }
        // Between two stones the span runs between their heights.
        var x = At * (Stones - 1);
        var i = Math.Clamp((int)MathF.Floor(x), 0, Stones - 2);
        var f = x - i;
        var span = Offsets[i] * (1f - f) + Offsets[i + 1] * f;
        Reading = span + (Rand.NextFloat() * 2f - 1f) * 0.012f * (1f + Hands.Tremor * 3f);
    }

    /// <summary>A wedge under the stone at the level: +1 drives one in (lifts it), −1 eases one out.</summary>
    private void Wedge(int way)
    {
        var s = AtStone;
        var was = MathF.Abs(Offsets[s]);
        var lift = Lift * (1f + (Rand.NextFloat() * 2f - 1f) * (0.2f + Hands.Tremor));
        Offsets[s] += way * lift;
        if (s > 0)
            Offsets[s - 1] += way * LiftBeside;
        if (s < Stones - 1)
            Offsets[s + 1] += way * LiftBeside;
        Sample(MathF.Abs(Offsets[s]) < was ? 1f : 0f);
    }

    public override void Finish(Simulation sim)
    {
        if (Level)
            sim.Repairs.Mended(sim, Worker, Breach);
    }

    protected override void WriteState(SaveWriter w)
    {
        w.Int(Breach);
        w.UInt(Seed);
        foreach (var o in Offsets)
            w.Float(o);
        w.Float(At);
        w.Int(Way);
        w.Float(Reading);
        w.Bool(Level);
        w.Int(_key);
    }

    protected override void ReadState(SaveReader r)
    {
        Breach = r.Int();
        Seed = r.UInt();
        Offsets = new float[Stones];
        for (var i = 0; i < Stones; i++)
            Offsets[i] = r.Float();
        At = r.Float();
        Way = r.Int() < 0 ? -1 : 1;
        Reading = r.Float();
        Level = r.Bool();
        _key = Math.Clamp(r.Int(), -1, 1);
    }
}

/// <summary>
/// The AI tinker mends what's broken in town: to the gap, a while at it, and it's whole. Like its
/// wagons, from what the town keeps by: no wood of its own, and no game.
/// </summary>
public sealed class MendBreachJob(int breach) : Job
{
    /// <summary>Ticks of work a breach takes the AI.</summary>
    public const int Ticks = 1200;

    public static Job? For(Simulation sim, Entity pawn)
    {
        var w = sim.World;
        if (!w.TryGet<Trade>(pawn, out var trade) || !trade.Def.MendsTown)
            return null;
        var from = w.Get<Position>(pawn).Cell;
        foreach (var b in sim.Repairs.Breaches)
            if (b.Cells.Exists(c => Navigation.CanReach(sim, pawn, from, c.Cell, PathEnd.Touch)))
                return new MendBreachJob(b.Id);
        return null;
    }

    protected override IEnumerable<JobStatus> Run()
    {
        if (Sim.Repairs.Find(breach) is not { } b)
            yield break;
        Report = $"Going to mend the {b.Label}";
        var from = PawnCell;
        var near = b.Cells.Select(c => c.Cell).Where(c => Navigation.CanReach(Sim, Pawn, from, c, PathEnd.Touch))
            .OrderBy(c => Map.Distance(c, from)).Select(c => (int3?)c).FirstOrDefault();
        if (near is not { } gap)
        {
            Fail("can't get to it");
            yield break;
        }
        foreach (var s in GoTo(gap, PathEnd.Touch)) yield return s;
        if (HasFailed) yield break;
        Report = $"Mending the {b.Label}";
        for (var i = 0; i < Ticks; i++)
        {
            if (Sim.Repairs.Find(breach) == null)
                yield break;
            SkillRules.Learn(Sim, Pawn, Skill.Construction, SkillRules.XpPerWorkTick);
            yield return JobStatus.Running;
        }
        Sim.Repairs.Mended(Sim, Pawn, breach, byHand: false);
    }
}

/// <summary>Brace a breach's span (<see cref="SpanSession"/>): the tinker at the gap with the wood for it.</summary>
public sealed record MendBreachCommand(Entity Avatar, int Breach) : Core.ISimCommand, Core.IActorCommand
{
    public Entity Actor => Avatar;

    public void Apply(Simulation sim)
    {
        if (sim.World.IsAlive(Avatar) && sim.World.Has<Avatar>(Avatar))
            Repairs.StartMend(sim, Avatar, sim.Repairs.Find(Breach));
    }
}
