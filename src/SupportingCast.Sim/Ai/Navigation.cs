using SupportingCast.Sim.Components;
using SupportingCast.Sim.Defs;
using SupportingCast.Sim.Maps;

namespace SupportingCast.Sim.Ai;

/// <summary>
/// Chooses how a creature gets somewhere: walking (<see cref="Pathfinder"/>) or, for creatures
/// whose life stage can fly, a flight when walking is impossible or much slower. A flight is
/// composed of simple legs: walk out to open sky if needed, climb straight up to a cruise level
/// that clears everything along the way, fly a straight line, descend onto open-sky ground next to
/// the destination, and walk the rest. Paths are plain cell lists; steps through cells that can't
/// be stood in are airborne (see <c>MovementSystem</c>).
/// </summary>
public static class Navigation
{
    /// <summary>Fly when walking would take this many times longer than flying (or is impossible).</summary>
    public const float FlyWhenSlowerBy = 1.4f;

    /// <summary>
    /// Until a dragon has flown this many times it flies every trip long enough to take off for,
    /// even when walking would be about as quick: a juvenile tries its new wings.
    /// </summary>
    public const int FledglingFlights = 3;

    /// <summary>How far to look for open sky to take off from or land on.</summary>
    public const int SkySearchRadius = 8;

    public static LifeStage? FlyingStage(Simulation sim, Entity e)
    {
        if (!sim.World.TryGet<Creature>(e, out var c))
            return null;
        var stage = c.Stage(sim.Clock.Tick);
        if (!stage.CanFly)
            return null;
        // Torn wings ground a dragon.
        return sim.World.TryGet<Health>(e, out var h) && h.Capacity(Capacity.Flying) < 0.5f ? null : stage;
    }

    /// <summary>A route from <paramref name="from"/> to <paramref name="to"/>, or null if there is none.</summary>
    public static List<int3>? PlanRoute(Simulation sim, Entity pawn, int3 from, int3 to, PathEnd end)
    {
        // FindPath copes with impassable starts (a wall finished around us): it steps out to a neighbor.
        var walk = sim.Pathfinder.FindPath(from, to, end);
        if (FlyingStage(sim, pawn) is not { } stage)
            return walk;
        if (walk != null && walk.Count < 12)
            return walk; // short hops aren't worth taking off for

        var flight = FlightRoute(sim, stage, from, to, end);
        if (flight == null)
            return walk;
        if (walk != null && walk.Count <= FlyWhenSlowerBy * (flight.Count / stage.FlySpeedFactor))
            return walk;
        return flight;
    }

    /// <summary>
    /// A flight from <paramref name="from"/> to <paramref name="to"/> however short (a rider asking
    /// to fly): take off, cruise clear of everything, land. Null if the creature can't fly now or
    /// there's no open sky to do it from.
    /// </summary>
    public static List<int3>? PlanFlight(Simulation sim, Entity pawn, int3 from, int3 to, PathEnd end)
    {
        if (FlyingStage(sim, pawn) is not { } stage || FlightRoute(sim, stage, from, to, end) is not { } flight)
            return null;
        return flight;
    }

    /// <summary>Is the creature in this cell in the air (neither standing nor inside rock)?</summary>
    public static bool Airborne(Map map, int3 cell) =>
        map.InBounds(cell) && !map.Standable(map.Idx(cell)) && !map.IsSolid(map.Idx(cell));

    /// <summary>
    /// A hunting dive for a flyer already in the air: straight at <paramref name="to"/> in steps
    /// that keep their height as long as they can and drop as they close, ending just above and
    /// beside it, close enough to strike (<see cref="CanStrikeFromAir"/>). Animals don't look up
    /// (see <c>AnimalBrain</c>), so it's how a dragon catches game that outruns it on foot. Null if
    /// it isn't airborne or something's in the way.
    /// </summary>
    public static List<int3>? StoopRoute(Simulation sim, Entity pawn, int3 from, int3 to)
    {
        var map = sim.Map;
        if (FlyingStage(sim, pawn) == null || !Airborne(map, from) || from.Z <= to.Z)
            return null;
        var columns = Line(from, to).ToList();
        if (columns.Count > 0)
            columns.RemoveAt(columns.Count - 1); // stop beside it, not on it
        var bottom = to.Z + 1;
        var route = new List<int3>();
        var z = from.Z;
        var drop = from.Z - bottom;
        for (var k = 0; k < columns.Count; k++)
        {
            var c = columns[k];
            if (!Open(new int3(c.X, c.Y, z)))
                return null;
            route.Add(new int3(c.X, c.Y, z));
            // Lose height late: most of the drop comes over the last few columns.
            var t = (float)(k + 1) / columns.Count;
            if (Descend(c.X, c.Y, from.Z - (int)MathF.Round(drop * t * t)) == false)
                return null;
        }
        // Right above it already: straight down.
        var last = columns.Count > 0 ? columns[^1] : new int2(from.X, from.Y);
        return Descend(last.X, last.Y, bottom) == false ? null : route;

        bool Descend(int x, int y, int want)
        {
            for (; z > want; )
            {
                z--;
                if (!Open(new int3(x, y, z)))
                    return false;
                route.Add(new int3(x, y, z));
            }
            return true;
        }

        bool Open(int3 c)
        {
            if (!map.InBounds(c))
                return false;
            var i = map.Idx(c);
            return !map.IsSolid(i) && map.Structure[i].IsNull && map.Floor[i].IsNull;
        }
    }

    /// <summary>A flyer in the air can strike what's in the next column, up to two levels below it.</summary>
    public static bool CanStrikeFromAir(Map map, int3 flyer, int3 target) =>
        Airborne(map, flyer) && Math.Max(Math.Abs(flyer.X - target.X), Math.Abs(flyer.Y - target.Y)) <= 1
        && flyer.Z - target.Z is >= 0 and <= 2;

    /// <summary>
    /// A low takeoff for a pounce: straight up <paramref name="height"/> levels where it's open.
    /// Null if something's overhead.
    /// </summary>
    public static List<int3>? HopUp(Simulation sim, int3 from, int height)
    {
        var map = sim.Map;
        var route = new List<int3>();
        for (var z = from.Z + 1; z <= from.Z + height; z++)
        {
            var c = new int3(from.X, from.Y, z);
            if (!map.InBounds(c) || map.IsSolid(map.Idx(c)) || !map.Structure[map.Idx(c)].IsNull || !map.Floor[map.Idx(c)].IsNull)
                return null;
            route.Add(c);
        }
        return route;
    }

    /// <summary>
    /// Can <paramref name="pawn"/> get from one cell to another at all: by walking, or by flying
    /// between two areas that both touch open sky.
    /// </summary>
    /// <summary>
    /// Where to stand for a cell: on it, unless solid furniture's there (a chest, a shelf), when
    /// beside it is as close as anyone gets. What's kept in a container is reached from beside it.
    /// </summary>
    public static PathEnd EndFor(Simulation sim, int3 cell, PathEnd end) =>
        end == PathEnd.OnTile && sim.Map.InBounds(cell) && sim.Map.Structure[sim.Map.Idx(cell)] is { IsNull: false } s
        && sim.World.TryGet<Thing>(s, out var t) && t.Def.Solid ? PathEnd.Touch : end;

    public static bool CanReach(Simulation sim, Entity pawn, int3 from, int3 to, PathEnd end)
    {
        end = EndFor(sim, to, end);
        if (sim.Regions.CanReach(from, to, end))
            return true;
        if (FlyingStage(sim, pawn) == null)
            return false;
        return TouchesSky(sim, from) && TouchesSky(sim, to);
    }

    private static bool TouchesSky(Simulation sim, int3 cell)
    {
        if (sim.Map.InBounds(cell) && !sim.Map.Standable(sim.Map.Idx(cell)) && !sim.Map.IsSolid(sim.Map.Idx(cell)))
            return true; // already airborne
        var r = sim.Regions.RegionAt(cell);
        if (r < 0)
            foreach (var d in Map.Neighbors8)
                if ((r = sim.Regions.RegionAt(Map.Offset(cell, d))) >= 0)
                    break;
        return r >= 0 && sim.Regions.HasSky(r);
    }

    private static List<int3>? FlightRoute(Simulation sim, LifeStage stage, int3 from, int3 to, PathEnd end)
    {
        var map = sim.Map;
        var route = new List<int3>();

        // Take off: here if the sky is open above, else walk to the nearest spot where it is. Already
        // in the air (a chase replanning mid-flight), it just carries on from where it is.
        var takeoff = from;
        var airborne = Airborne(map, from);
        if (!airborne && !OpenAbove(map, from))
        {
            var spot = NearestOpenSky(sim, from, sim.Regions.RegionAt(from));
            if (spot is not { } s || sim.Pathfinder.FindPath(from, s, PathEnd.OnTile) is not { } toSky)
                return null;
            route.AddRange(toSky);
            takeoff = s;
        }

        // Land on the destination if it's under open sky, else on open ground walkable from it.
        var landing = to;
        if (!OpenAbove(map, to) || !map.Passable(to))
        {
            var region = sim.Regions.RegionAt(to);
            if (region < 0)
                foreach (var d in Map.Neighbors8)
                    if ((region = sim.Regions.RegionAt(Map.Offset(to, d))) >= 0)
                        break;
            if (region < 0 || NearestOpenSky(sim, to, region) is not { } l)
                return null;
            landing = l;
        }

        var clearance = CruiseLevel(map, takeoff, landing);
        var cruise = Math.Min(clearance + stage.FlyAltitude, map.Depth - 1);
        if (airborne)
            cruise = Math.Max(cruise, takeoff.Z); // no dropping down to cruise: it may be up at the ceiling
        if (cruise <= clearance - 1 || cruise <= landing.Z || (!airborne && cruise <= takeoff.Z))
            return null; // the map is too low to clear what's in the way
        if (!ClearUpTo(map, takeoff, cruise) || !ClearUpTo(map, landing, cruise))
            return null;

        for (var z = takeoff.Z + 1; z <= cruise; z++)
            route.Add(new int3(takeoff.X, takeoff.Y, z));
        foreach (var c in Line(takeoff, landing))
            route.Add(new int3(c.X, c.Y, cruise));
        for (var z = cruise - 1; z >= landing.Z; z--)
            route.Add(new int3(landing.X, landing.Y, z));

        if (landing != to)
        {
            if (sim.Pathfinder.FindPath(landing, to, end) is not { } rest)
                return null;
            route.AddRange(rest);
        }
        return route;
    }

    /// <summary>
    /// A sightseeing loop for an idle flyer: take off (walking to open sky first if needed), climb
    /// clear of everything under the circle, fly once around <paramref name="center"/> and land back
    /// where it took off. Null if there's no open sky nearby or the map is too low to clear the loop.
    /// </summary>
    public static List<int3>? SoaringRoute(Simulation sim, Entity pawn, int3 center, int radius)
    {
        if (FlyingStage(sim, pawn) is not { } stage)
            return null;
        var map = sim.Map;
        var from = sim.World.Get<Position>(pawn).Cell;
        var route = new List<int3>();

        var takeoff = from;
        if (!OpenAbove(map, from))
        {
            var spot = NearestOpenSky(sim, from, sim.Regions.RegionAt(from));
            if (spot is not { } s || sim.Pathfinder.FindPath(from, s, PathEnd.OnTile) is not { } toSky)
                return null;
            route.AddRange(toSky);
            takeoff = s;
        }

        // Waypoints around the circle (random start and direction), kept on the map.
        const int points = 12;
        var phase = sim.Rand.Range(0f, MathF.Tau);
        var turn = sim.Rand.Chance(0.5f) ? 1f : -1f;
        var loop = new List<int3>();
        for (var k = 0; k <= points; k++)
        {
            var a = phase + turn * k * MathF.Tau / points;
            var x = Math.Clamp(center.X + (int)MathF.Round(DetMath.Cos(a) * radius), 0, map.Width - 1);
            var y = Math.Clamp(center.Y + (int)MathF.Round(DetMath.Sin(a) * radius), 0, map.Height - 1);
            loop.Add(new int3(x, y, 0));
        }
        loop.Add(takeoff);

        var columns = new List<int2>();
        var prev = takeoff;
        foreach (var p in loop)
        {
            columns.AddRange(Line(prev, p));
            prev = p;
        }
        var clearance = Math.Max(takeoff.Z, ColumnTop(map, takeoff.X, takeoff.Y));
        foreach (var c in columns)
            clearance = Math.Max(clearance, ColumnTop(map, c.X, c.Y));
        var cruise = Math.Min(clearance + stage.FlyAltitude, map.Depth - 1);
        if (cruise < clearance || cruise <= takeoff.Z || !ClearUpTo(map, takeoff, cruise))
            return null;

        for (var z = takeoff.Z + 1; z <= cruise; z++)
            route.Add(new int3(takeoff.X, takeoff.Y, z));
        foreach (var c in columns)
            route.Add(new int3(c.X, c.Y, cruise));
        for (var z = cruise - 1; z >= takeoff.Z; z--)
            route.Add(new int3(takeoff.X, takeoff.Y, z));
        return route;
    }

    /// <summary>Lowest level that clears every column on the straight line (and its ends).</summary>
    private static int CruiseLevel(Map map, int3 a, int3 b)
    {
        var level = Math.Max(a.Z, b.Z);
        level = Math.Max(level, ColumnTop(map, a.X, a.Y));
        foreach (var c in Line(a, b))
            level = Math.Max(level, ColumnTop(map, c.X, c.Y));
        return level;
    }

    /// <summary>One above the highest occupied cell (block, structure or floor) in a column.</summary>
    private static int ColumnTop(Map map, int x, int y)
    {
        for (var z = map.Depth - 1; z >= 0; z--)
        {
            var i = map.Idx(x, y, z);
            if (map.IsSolid(i) || !map.Structure[i].IsNull || !map.Floor[i].IsNull)
                return z + 1;
        }
        return 0;
    }

    /// <summary>Nothing (no block, floor or structure) above the cell all the way up.</summary>
    private static bool OpenAbove(Map map, int3 cell) => map.InBounds(cell) && ClearUpTo(map, cell, map.Depth - 1);

    private static bool ClearUpTo(Map map, int3 cell, int top)
    {
        for (var z = cell.Z + 1; z <= top; z++)
        {
            var i = map.Idx(cell.X, cell.Y, z);
            if (map.IsSolid(i) || !map.Structure[i].IsNull || !map.Floor[i].IsNull)
                return false;
        }
        return true;
    }

    private static int3? NearestOpenSky(Simulation sim, int3 near, int region)
    {
        foreach (var c in sim.SpiralFrom(near, SkySearchRadius))
            if (sim.Map.Passable(c) && sim.Regions.RegionAt(c) == region && OpenAbove(sim.Map, c))
                return c;
        return null;
    }

    /// <summary>8-connected columns from a to b, excluding a, including b.</summary>
    private static IEnumerable<int2> Line(int3 a, int3 b)
    {
        int x = a.X, y = a.Y;
        int dx = Math.Abs(b.X - a.X), dy = Math.Abs(b.Y - a.Y);
        int sx = Math.Sign(b.X - a.X), sy = Math.Sign(b.Y - a.Y);
        var err = dx - dy;
        while (x != b.X || y != b.Y)
        {
            var e2 = 2 * err;
            if (e2 > -dy) { err -= dy; x += sx; }
            if (e2 < dx) { err += dx; y += sy; }
            yield return new int2(x, y);
        }
    }
}
