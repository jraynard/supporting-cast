using SupportingCast.Sim.Ai;
using SupportingCast.Sim.Components;
using SupportingCast.Sim.Defs;
using SupportingCast.Sim.Maps;
using SupportingCast.Sim.Systems;

namespace SupportingCast.Sim.Combat;

/// <summary>Everywhere a unit can get to with the TU it has to spend, with the cheapest path (in TU) to each cell.</summary>
public sealed class MoveRange
{
    public required int3 From;
    /// <summary>TU the search could spend.</summary>
    public required float Budget;
    internal readonly Dictionary<int, (float Cost, int Parent)> Nodes = [];
    internal readonly HashSet<int> Endpoints = [];

    /// <summary>Cells the unit may finish its move in (standing, or hovering for flyers).</summary>
    public IEnumerable<int> Cells => Endpoints;

    public bool CanEndAt(Map map, int3 c) => map.InBounds(c) && Endpoints.Contains(map.Idx(c));

    /// <summary>TU to get to <paramref name="c"/> (infinite if it can't be reached).</summary>
    public float CostTo(Map map, int3 c) => Nodes.TryGetValue(map.Idx(c), out var n) ? n.Cost : float.PositiveInfinity;

    /// <summary>Cells from the start (excluded) to <paramref name="c"/> (included), ready for a <see cref="Mover"/>.</summary>
    public List<int3>? PathTo(Map map, int3 c)
    {
        if (!CanEndAt(map, c))
            return null;
        var path = new List<int3>();
        var start = map.Idx(From);
        for (var i = map.Idx(c); i != start; i = Nodes[i].Parent)
            path.Add(map.CellOf(i));
        path.Reverse();
        return path;
    }
}

/// <summary>
/// Turn-based movement: a TU-budgeted Dijkstra search over the same walking graph the pathfinder
/// uses (<see cref="NavGraph"/>, costs from <see cref="Map.PathCost"/>), plus, for creatures that
/// can fly, steps through open air at their flying speed. A cell costs <see cref="TimeUnits.WalkCost"/>
/// (speed, life stage, leg wounds) times its terrain cost; a riders' mount does their walking.
/// </summary>
public static class TacticalMoves
{
    /// <summary>Is the creature in mid-air (not standing on anything)?</summary>
    public static bool Airborne(Simulation sim, int3 cell) =>
        sim.Map.InBounds(cell) && !sim.Map.Passable(cell) && !sim.Map.IsSolid(sim.Map.Idx(cell));

    /// <summary>Every cell <paramref name="e"/> can reach spending at most <paramref name="budget"/> TU.</summary>
    public static MoveRange Reachable(Simulation sim, Entity e, float budget)
    {
        var map = sim.Map;
        var w = sim.World;
        var from = w.Get<Position>(e).Cell;
        var range = new MoveRange { From = from, Budget = budget };
        var fly = Navigation.FlyingStage(sim, e);
        var faction = w.Get<Creature>(e).Faction;
        var perCell = TimeUnits.WalkCost(sim, e);
        if (float.IsInfinity(perCell))
            budget = 0; // riding, or can't walk: nowhere to go

        // Who's where: enemies block the way, anyone (but us) blocks the cell they're in.
        var occupied = new Dictionary<int, bool>();
        foreach (var o in sim.Creatures())
        {
            if (o == e || w.Has<Carried>(o) || w.Has<Riding>(o))
                continue;
            var oc = w.Get<Creature>(o);
            occupied[map.Idx(w.Get<Position>(o).Cell)] = oc.Able && Factions.Hostile(faction, oc.Faction);
        }

        var start = map.Idx(from);
        range.Nodes[start] = (0, -1);
        range.Endpoints.Add(start);
        var open = new PriorityQueue<int, float>();
        open.Enqueue(start, 0);
        Span<NavGraph.Step> steps = stackalloc NavGraph.Step[NavGraph.MaxSteps];
        while (open.TryDequeue(out var cur, out var g))
        {
            if (g > range.Nodes[cur].Cost)
                continue; // stale entry

            void Relax(int next, float cost)
            {
                var ng = g + cost;
                if (ng > budget + 1e-3f || (range.Nodes.TryGetValue(next, out var n) && n.Cost <= ng))
                    return;
                if (occupied.TryGetValue(next, out var enemy) && enemy)
                    return;
                range.Nodes[next] = (ng, cur);
                if (!occupied.ContainsKey(next))
                    range.Endpoints.Add(next);
                open.Enqueue(next, ng);
            }

            // Walking, exactly as the pathfinder costs it.
            if (map.PathCost[cur] > 0)
            {
                var count = NavGraph.Neighbors(new MapNav(map), cur, steps);
                for (var k = 0; k < count; k++)
                    Relax(steps[k].Index, perCell * steps[k].Length * (map.PathCost[steps[k].Index] + map.PathCost[cur]) * 0.5f);
            }

            if (fly != null)
                foreach (var (next, length) in AirSteps(sim, cur))
                    Relax(next, perCell * length / fly.FlySpeedFactor);
        }
        return range;
    }

    /// <summary>
    /// Flying steps out of a cell: 8-way on the level (no corner cutting) and straight up or
    /// down, into open air. Only steps that leave or enter the air count; ground to ground is a walk.
    /// Floors can't be flown through.
    /// </summary>
    private static IEnumerable<(int Index, float Length)> AirSteps(Simulation sim, int cur)
    {
        var map = sim.Map;
        var c = map.CellOf(cur);
        var curAir = map.PathCost[cur] <= 0;
        bool Open(int3 n) => map.InBounds(n) && MovementSystem.OpenAir(sim, map.Idx(n));

        for (var k = 0; k < 8; k++)
        {
            var d = Map.Neighbors8[k];
            var n = new int3(c.X + d.X, c.Y + d.Y, c.Z);
            if (!Open(n) || (!curAir && map.PathCost[map.Idx(n)] > 0))
                continue;
            if (k >= 4 && (!Open(new int3(n.X, c.Y, c.Z)) || !Open(new int3(c.X, n.Y, c.Z))))
                continue;
            yield return (map.Idx(n), k >= 4 ? 1.41421356f : 1f);
        }

        var up = new int3(c.X, c.Y, c.Z + 1);
        if (Open(up) && !map.Ceiling(map.Idx(up)))
            yield return (map.Idx(up), 1f);
        var down = new int3(c.X, c.Y, c.Z - 1);
        if (Open(down) && !map.Ceiling(cur))
            yield return (map.Idx(down), 1f);
    }
}
