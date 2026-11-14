using HeroesNotIncluded.Sim;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Maps;

namespace HeroesNotIncluded.Rendering;

/// <summary>
/// How a ramp is drawn and walked, so the two agree. The sim climbs a whole level in the ramp's
/// one cell, and lifts a walker only as they step off it, so drawn as solid steps (a def's
/// <see cref="ThingDef.Steps"/>) the climb would be steep and the walker sunk into it. Instead, when
/// the cell at the foot is clear, the flight starts half a cell out into it, a gentler pitch; and a
/// walker is drawn standing on the tread under their feet (a natural slope: on its surface).
/// Presentation only: the sim never sees any of it.
/// </summary>
public static class Stairs
{
    /// <summary>How far past its low edge a flight reaches into the cell at its foot, when that's clear.</summary>
    public const float Lead = 0.5f;

    /// <summary>The way a ramp climbs (<see cref="Map.ClimbOf"/>, which free movement follows too).</summary>
    public static int2 ClimbOf(Map map, int x, int y, int z) => map.ClimbOf(x, y, z);

    /// <summary>The ramp's def at a cell, or null.</summary>
    public static ThingDef? RampAt(Simulation sim, int x, int y, int z)
    {
        var map = sim.Map;
        if (!map.InBounds(x, y, z) || map.Structure[map.Idx(x, y, z)] is not { IsNull: false } s)
            return null;
        var def = sim.World.Get<Thing>(s).Def;
        return def.Building?.Climb == ClimbKind.Ramp ? def : null;
    }

    /// <summary>A stair's flight: its climb, whether it leads out into the cell at its foot, and its steps.</summary>
    public static (int2 Climb, bool Leads, int Steps) Flight(Simulation sim, int x, int y, int z, ThingDef def)
    {
        var map = sim.Map;
        var climb = ClimbOf(map, x, y, z);
        int fx = x - climb.X, fy = y - climb.Y;
        var leads = map.InBounds(fx, fy, z) && map.Structure[map.Idx(fx, fy, z)].IsNull && map.Standable(map.Idx(fx, fy, z));
        var steps = leads ? (int)MathF.Round(def.Steps * (1f + Lead)) : def.Steps;
        return (climb, leads, steps);
    }

    /// <summary>
    /// The height a walker at <paramref name="p"/> is drawn standing at, if they're on (or at the foot
    /// of, or stepping off) a ramp; else null, and they're drawn where the sim has them.
    /// </summary>
    public static float? FloorAt(Simulation sim, float3 p)
    {
        var map = sim.Map;
        int x = (int)MathF.Floor(p.X), y = (int)MathF.Floor(p.Y), z = (int)MathF.Floor(p.Z + 0.001f);
        if (!map.InBounds(x, y, z))
            return null;
        float fx = p.X - x, fy = p.Y - y;
        // How far along a climb the point is through its cell: 0 at the edge it comes from, 1 at the edge it heads for.
        float Along(int2 d) => d.X > 0 ? fx : d.X < 0 ? 1 - fx : d.Y > 0 ? fy : 1 - fy;

        if (RampAt(sim, x, y, z) is { } here)
        {
            if (here.Steps <= 0)
                return z + Along(ClimbOf(map, x, y, z));
            var (climb, leads, steps) = Flight(sim, x, y, z, here);
            return z + Tread(Along(climb) + (leads ? Lead : 0), leads ? 1 + Lead : 1, steps);
        }
        foreach (var d in Map.Neighbors4)
        {
            // At the foot of a stair that leads out into this cell.
            if (RampAt(sim, x + d.X, y + d.Y, z) is { Steps: > 0 } below)
            {
                var (climb, leads, steps) = Flight(sim, x + d.X, y + d.Y, z, below);
                if (leads && climb == d && Along(d) > 1 - Lead)
                    return z + Tread(Along(d) - (1 - Lead), 1 + Lead, steps);
            }
            // Stepping between the top of a ramp and the floor it climbs to (the sim lifts them partway).
            if (p.Z - z > 0.001f && RampAt(sim, x + d.X, y + d.Y, z) is { } ramp && ClimbOf(map, x + d.X, y + d.Y, z) == new int2(-d.X, -d.Y))
                return z + 1;
        }
        return null;
    }

    /// <summary>The top of the step under a point <paramref name="run"/> along a flight <paramref name="length"/> cells long.</summary>
    private static float Tread(float run, float length, int steps)
    {
        var step = Math.Clamp((int)(run / length * steps), 0, steps - 1);
        return (step + 1f) / steps;
    }
}
