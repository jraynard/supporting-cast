using HeroesNotIncluded.Sim.Combat;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Maps;

namespace HeroesNotIncluded.Sim.Ai;

/// <summary>
/// A wild dragon come down on the town (the season's last crisis, <c>Director.DragonCrisis</c>).
/// It makes passes: flies in, comes down within breath's reach of something (a building, or
/// whoever's out in the open), breathes, and stays on the ground a while snapping at anyone who
/// closes on it. That's when the town can hurt it. Hurt enough, it leaves for good.
/// </summary>
public sealed class DragonBrain : IBrain
{
    /// <summary>Pain past which it's had enough and goes.</summary>
    public const float FleePain = 0.3f;

    /// <summary>Damage from wounds past which it's had enough and goes, whatever the pain.</summary>
    public const float FleeDamage = 30f;

    public Job? Think(Simulation sim, Entity pawn)
    {
        var w = sim.World;
        var tile = w.Get<Position>(pawn).Cell;
        if (w.Has<Leaving>(pawn) || DrivenOff(sim, pawn))
        {
            if (!w.Has<Leaving>(pawn))
                w.Add(pawn, new Leaving());
            return sim.NearestEdge(tile) is { } edge ? new LeaveMapJob(edge, "Flying off") : null;
        }
        return Target(sim, pawn) is { } target ? new DragonPassJob(target) : null;
    }

    /// <summary>Hurt enough that it'll give the town up.</summary>
    public static bool DrivenOff(Simulation sim, Entity dragon) =>
        sim.World.TryGet<Health>(dragon, out var h)
        && (h.Pain > FleePain || h.Capacity(Defs.Capacity.Moving) < 0.5f || h.Injuries.Sum(i => i.Damage) > FleeDamage);

    /// <summary>Somewhere to breathe on: whoever of the town is out in the open nearest, else a building.</summary>
    private static int3? Target(Simulation sim, Entity pawn)
    {
        var w = sim.World;
        var tile = w.Get<Position>(pawn).Cell;
        var best = (int3?)null;
        var bestDist = int.MaxValue;
        foreach (var e in sim.Creatures())
        {
            var c = w.Get<Creature>(e);
            if (!Factions.Defends(c.Faction) || c.Dead)
                continue;
            var at = w.Get<Position>(e).Cell;
            if (sim.Rooms.RoomAt(at) is { Indoors: true })
                continue;
            var d = Map.Distance(tile, at);
            if (d < bestDist)
                (best, bestDist) = (at, d);
        }
        if (best != null && sim.Rand.Chance(0.6f))
            return best;
        var buildings = w.Query<PresetBuilding>().Where(b => w.Get<PresetBuilding>(b) is { Phase: Defs.BuildPhase.Done, Demolishing: false }).ToList();
        return buildings.Count > 0 ? w.Get<PresetBuilding>(buildings[sim.Rand.NextInt(buildings.Count)]).Anchor : best;
    }
}

/// <summary>
/// One pass: to the ground within breath's reach of the target, breathe on it, then stay down a
/// while, biting at anyone in reach. Ends early if it's had enough.
/// </summary>
public sealed class DragonPassJob(int3 target) : Job
{
    /// <summary>How long it stays down after a breath, in ticks.</summary>
    public const int GroundTicks = SimClock.TicksPerSecond * 20;

    protected override IEnumerable<JobStatus> Run()
    {
        var range = (int)Environment.Breath.RangeOf(Sim, Pawn);
        Report = "Swooping on the town";
        var from = PawnCell;
        var spots = Sim.SpiralFrom(target, range - 2)
            .Where(c => Map.Distance(c, target) >= 4 && Clear(c))
            .ToList();
        // A pass is a pass: it comes down somewhere new each time, not where it already stands.
        var away = spots.FindAll(c => Map.Distance(c, from) >= MinHop);
        if (away.Count > 0)
            spots = away;
        if (spots.Count == 0)
        {
            Fail("nowhere to come down");
            yield break;
        }
        var landing = spots[Sim.Rand.NextInt(spots.Count)];
        foreach (var s in GoTo(landing, PathEnd.OnTile)) yield return s;
        if (HasFailed) yield break;

        // It rears and draws breath where it can be seen (Combat.Inhaling): a moment to guard, or to dodge late.
        Report = "Drawing breath";
        var at = Sim.Clock.Tick + Blows.InhaleTicks;
        Sim.World.Add(Pawn, new Inhaling { Target = target, Start = Sim.Clock.Tick, At = at });
        // A moment more, as for any blow at a player (Blows.Grace), for their keys to arrive.
        foreach (var s in Wait(Blows.InhaleTicks + Blows.Grace(Sim))) yield return s;
        if (Sim.World.Has<Inhaling>(Pawn))
            Sim.World.Remove<Inhaling>(Pawn);
        Report = "Breathing fire";
        CombatRules.Breathe(Sim, Pawn, target, at);
        foreach (var s in Wait(SimClock.TicksPerSecond)) yield return s;

        Report = "Snapping at the town";
        var attack = CombatRules.Attacks(Sim, Pawn).FirstOrDefault(a => !a.Ranged);
        long next = 0;
        var idle = 0;
        for (var t = 0; t < GroundTicks && idle < RestlessTicks && !DragonBrain.DrivenOff(Sim, Pawn); t++)
        {
            var foe = Beside();
            // Nobody comes at it: it soon takes to the air again for another pass.
            idle = foe.IsNull ? idle + 1 : 0;
            if (attack != null && Sim.Clock.Tick >= next && !foe.IsNull)
            {
                // Its jaws are wound up like any blow (Combat.Blows), quick and hard to dodge.
                if (Blows.Begin(Sim, Pawn, foe, attack))
                    next = Sim.Clock.Tick + attack.CooldownTicks;
            }
            yield return JobStatus.Running;
        }
    }

    /// <summary>Cells it moves at least, from one landing to the next.</summary>
    public const int MinHop = 8;

    /// <summary>How long it stays down with nobody in reach of its jaws before it takes off, in ticks.</summary>
    public const int RestlessTicks = SimClock.TicksPerSecond * 8;

    /// <summary>
    /// Room to come down: open ground around the cell as well as under it, clear of any building
    /// (a burnt-out shell included), so a beast that size isn't left standing half in a house.
    /// </summary>
    private bool Clear(int3 c)
    {
        for (var dy = -1; dy <= 1; dy++)
            for (var dx = -1; dx <= 1; dx++)
            {
                var n = new int3(c.X + dx, c.Y + dy, c.Z);
                if (!Sim.Map.InBounds(n) || !Sim.Map.Passable(n) || !Sim.Map.Standable(Sim.Map.Idx(n))
                    || Sim.Rooms.RoomAt(n) is { Indoors: true } || !Systems.Buildings.At(Sim, n).IsNull)
                    return false;
            }
        return true;
    }

    /// <summary>Someone of the town on their feet within reach of its jaws.</summary>
    private Entity Beside()
    {
        var w = Sim.World;
        foreach (var e in Sim.Creatures())
            if (w.Get<Creature>(e) is { Dead: false, Downed: false } c && Factions.Defends(c.Faction)
                && Pathfinder.Touches(PawnCell, w.Get<Position>(e).Cell))
                return e;
        return Entity.Null;
    }
}
