using HeroesNotIncluded.Sim.Ai;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Work;

namespace HeroesNotIncluded.Sim.Carpentry;

/// <summary>
/// A tree whose hinge has given (<see cref="FellSession"/>): it creaks a moment, then comes down
/// along <see cref="Dir"/>. The renderer topples it from <see cref="At"/>; the sim lands it after
/// <see cref="Felling.CreakTicks"/>.
/// </summary>
public struct Falling
{
    /// <summary>The way it falls, degrees (0 east, 90 south).</summary>
    public float Dir;

    /// <summary>The tick the hinge gave.</summary>
    public long At;

    /// <summary>Who felled it.</summary>
    public Entity By;

    /// <summary>The hinge was cut through: it went its own way.</summary>
    public bool Through;
}

/// <summary>
/// A felled trunk lying on the ground (the <c>FelledTrunk</c> structure at its stump), bucked into
/// logs by hand (<see cref="BuckSession"/>). What's left of it is world state, so bucking can stop
/// and start again, or pass to someone else.
/// </summary>
public sealed class Trunk
{
    /// <summary>Which way it lies from the stump, degrees: where the tree fell.</summary>
    public float Dir;

    /// <summary>Logs' lengths still in it.</summary>
    public int Lengths;

    /// <summary>The butt's two lengths are thick enough to leave whole as a great log (an oak's, unbroken).</summary>
    public bool GreatButt;

    /// <summary>The wood it is (oak, pine, birch): what its logs are.</summary>
    public MaterialDef? Material;
}

/// <summary>
/// Felling by hand: who may, how it starts, and the fall. The carpenter fells (<see cref="TradeDef.Fells"/>)
/// with <see cref="FellSession"/>; the trunk's bucked with <see cref="BuckSession"/>. A tree that
/// falls lands on whatever's in its way: it hurts anyone it falls on, more toward the butt, and
/// stops short (and breaks, losing logs) at a wall.
/// </summary>
public sealed class Felling : ISimSystem
{
    public const string TrunkDef = "FelledTrunk";
    public const string LogDef = "Log";
    public const string GreatLogDef = "GreatLog";
    public const string AxeKind = "Axe";

    /// <summary>The creak between the hinge giving and the tree landing: time to shout, and to get out of the way.</summary>
    public const int CreakTicks = SimClock.TicksPerSecond * 3 / 2;

    /// <summary>The hurt to someone a trunk lands on at the butt; half that under the crown.</summary>
    public const float Crush = 22f;

    public string Name => "Felling";
    public int Interval => 1;

    public void Update(Simulation sim)
    {
        var w = sim.World;
        List<Entity>? due = null;
        foreach (var e in w.Each<Falling>())
            if (sim.Clock.Tick >= w.Get<Falling>(e).At + CreakTicks)
                (due ??= []).Add(e);
        if (due == null)
            return;
        foreach (var e in due.OrderBy(e => e.Index))
            Land(sim, e);
    }

    /// <summary>The cells a tree at <paramref name="stump"/> falling along <paramref name="dir"/> sweeps, butt to crown, as far as <paramref name="height"/>.</summary>
    public static IEnumerable<int3> Line(int3 stump, float dir, int height)
    {
        var rad = dir * MathF.PI / 180f;
        var (dx, dy) = (DetMath.Cos(rad), DetMath.Sin(rad));
        var last = stump;
        for (var i = 1; i <= height; i++)
        {
            var c = new int3(stump.X + (int)MathF.Round(dx * i), stump.Y + (int)MathF.Round(dy * i), stump.Z);
            if (c == last)
                continue;
            last = c;
            yield return c;
        }
    }

    /// <summary>The tree comes down: anyone under it hurt, the trunk laid where it reached.</summary>
    public static void Land(Simulation sim, Entity tree)
    {
        var w = sim.World;
        var fall = w.Get<Falling>(tree);
        w.Remove<Falling>(tree);
        var t = w.Get<Thing>(tree);
        if (t.Def.Felling is not { } props)
            return;
        var map = sim.Map;
        var line = Line(t.Cell, fall.Dir, props.Height).ToList();
        var reach = 0;
        var hit = new List<Entity>();
        for (var i = 0; i < line.Count; i++)
        {
            var c = line[i];
            if (!map.InBounds(c) || !map.Passable(c))
                break; // a wall, a house, a rock: it lands against it
            reach = i + 1;
            foreach (var e in sim.Creatures().Where(e => e != tree && w.Get<Position>(e).Cell == c && !w.Get<Creature>(e).Dead).OrderBy(e => e.Index))
            {
                HealthSystem.Damage(sim, e, Crush * (1f - 0.5f * i / Math.Max(1, line.Count)), DamageType.Blunt);
                hit.Add(e);
            }
        }
        var whole = reach >= line.Count;
        var lengths = whole ? props.Logs : Math.Max(1, (int)MathF.Ceiling(props.Logs * reach / (float)Math.Max(1, line.Count)));
        var material = t.Def.Harvest?.Yield.FirstOrDefault()?.MaterialDef;
        var stump = t.Cell;
        var label = t.Def.Label;
        sim.Map.ClearDesignation(sim.Map.Idx(stump), Maps.Designation.Chop);
        sim.DestroyThing(tree);
        var trunk = sim.SpawnStructure(sim.Defs.Thing(TrunkDef), stump, (int)MathF.Round(FellSession.Wrap360(fall.Dir) / 90f) & 3);
        w.Get<Thing>(trunk).Material = material;
        w.Add(trunk, new Trunk { Dir = fall.Dir, Lengths = lengths, GreatButt = props.GreatButt && whole, Material = material });
        sim.Messages.Add(MessageKind.Info, whole ? $"The {label} comes down with a crash." : $"The {label} comes down against something and breaks.", stump);
        foreach (var e in hit)
            sim.Messages.Tell(e, MessageKind.Warning, $"The {label} comes down on you!", w.Get<Position>(e).Cell);
    }

    /// <summary>Why a tree can't be felled by hand now, or null.</summary>
    public static string? Problem(Simulation sim, Entity worker, Entity tree)
    {
        var w = sim.World;
        if (!w.IsAlive(tree) || !w.TryGet<Thing>(tree, out var t) || t.Def.Felling == null)
            return "there's no tree there";
        if (w.Has<Falling>(tree))
            return "it's already coming down";
        if (Crafting.ToolSpeed(sim, worker, AxeKind) <= 0f)
            return "you need an axe";
        return null;
    }

    /// <summary>Should a player's F at this tree be the felling game? The carpenter's, in a round.</summary>
    public static bool ByHand(Simulation sim, Entity worker, Entity tree) =>
        sim.Round.Live && Players.Steered(sim, worker) && sim.World.TryGet<Trade>(worker, out var trade) && trade.Def.Fells
        && sim.World.TryGet<Thing>(tree, out var t) && t.Def.Felling != null;

    /// <summary>A player sets about a tree: the felling game, aimed at first away from them.</summary>
    public static void StartFelling(Simulation sim, Entity worker, Entity tree)
    {
        var w = sim.World;
        if (w.Has<WorkSession>(worker))
            return;
        var t = w.Get<Thing>(tree);
        if (Problem(sim, worker, tree) is { } why)
        {
            sim.Messages.Tell(worker, MessageKind.Info, $"Can't fell it: {why}.", t.Cell);
            return;
        }
        var me = w.Get<Position>(worker).Cell;
        var away = FellSession.Wrap360(DetMath.Atan2(t.Cell.Y - me.Y, t.Cell.X - me.X) * 180f / MathF.PI);
        var (lean, pull) = FellSession.LeanOf(t.Cell);
        WorkSystem.Start(sim, worker, new FellSession
        {
            Tree = tree, Aim = MathF.Round(away), Lean = lean, LeanPull = pull, Girth = t.Def.Felling!.Girth,
        });
    }

    public static bool IsTrunk(Simulation sim, Entity e) =>
        sim.World.IsAlive(e) && sim.World.Has<Trunk>(e) && sim.World.Get<Trunk>(e).Lengths > 0;

    /// <summary>A player sets about a felled trunk with the saw: bucking it into logs.</summary>
    public static void StartBucking(Simulation sim, Entity worker, Entity trunk)
    {
        var w = sim.World;
        if (w.Has<WorkSession>(worker) || !IsTrunk(sim, trunk))
            return;
        var at = w.Get<Thing>(trunk).Cell;
        if (Crafting.ToolSpeed(sim, worker, BuckSession.SawKind) <= 0f)
        {
            sim.Messages.Tell(worker, MessageKind.Info, "Can't buck it: you need a saw.", at);
            return;
        }
        var t = w.Get<Trunk>(trunk);
        WorkSystem.Start(sim, worker, new BuckSession { Trunk = trunk, Lengths = t.Lengths, GreatButt = t.GreatButt });
    }

    /// <summary>
    /// A whole tree's logs laid by its stump, as bucking it would give them (a great log off an oak's
    /// butt, the rest logs): what the AI carpenter's plain fell leaves (<see cref="FellJob"/>).
    /// </summary>
    public static void LayLogsOf(Simulation sim, ThingDef tree, int3 stump)
    {
        if (tree.Felling is not { } props)
            return;
        var material = tree.Harvest?.Yield.FirstOrDefault()?.MaterialDef;
        var logs = props.Logs;
        if (props.GreatButt && logs >= 2)
        {
            sim.PlaceItemNear(sim.CreateItem(sim.Defs.Thing(GreatLogDef), 1, material), stump);
            logs -= 2;
        }
        for (var i = 0; i < logs; i++)
            sim.PlaceItemNear(sim.CreateItem(sim.Defs.Thing(LogDef), 1, material), stump);
    }

    /// <summary>A log off a trunk, laid beside it by hand (so it waits for its cutter a while before anyone hauls it).</summary>
    public static Entity LayLog(Simulation sim, Entity trunk, bool great)
    {
        var w = sim.World;
        var t = w.Get<Trunk>(trunk);
        var log = sim.CreateItem(sim.Defs.Thing(great ? GreatLogDef : LogDef), 1, t.Material);
        return sim.PlaceItemNear(log, w.Get<Thing>(trunk).Cell, byHand: true);
    }
}

/// <summary>
/// An AI carpenter fells a tree the plain way (no game, no fall to dodge: it's laid down with care)
/// and leaves its logs by the stump, for splitting when the town's firewood runs low.
/// </summary>
public sealed class FellJob(Entity tree) : Job
{
    public override Entity Focus => tree;

    public override string? ToolKind => Felling.AxeKind;

    protected override IEnumerable<JobStatus> Run()
    {
        var def = Get<Thing>(tree).Def;
        Report = $"Felling {def.Label}";
        if (!Reserve(tree)) yield break;
        var stump = Get<Thing>(tree).Cell;
        foreach (var s in GoTo(stump, PathEnd.Touch)) yield return s;
        if (HasFailed) yield break;
        var work = def.Harvest?.Work ?? 180f;
        var tool = Crafting.GatherFactor(Sim, Pawn, Felling.AxeKind);
        for (var done = 0f; done < work; done += Work(Skill.Crafting) * tool)
        {
            if (!Alive(tree) || Sim.World.Has<Falling>(tree))
            {
                Fail("the tree's gone");
                yield break;
            }
            yield return JobStatus.Running;
        }
        Sim.Map.ClearDesignation(Sim.Map.Idx(stump), Maps.Designation.Chop);
        Sim.DestroyThing(tree);
        Felling.LayLogsOf(Sim, def, stump);
    }
}
