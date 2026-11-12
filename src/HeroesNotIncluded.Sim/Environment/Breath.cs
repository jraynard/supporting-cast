using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;

namespace HeroesNotIncluded.Sim.Environment;

/// <summary>Shape of one breath: how far it reaches, how wide, how many rays, how hot.</summary>
/// <param name="SparesBuildings">
/// A sneeze, not a real breath: it scorches, but won't set alight structures, floors or anything
/// in an indoor room, so a hatchling in its nest doesn't burn the hall down.
/// </param>
public readonly record struct BreathShape(float Range, float ConeDegrees, int Rays, float Intensity, float SplashRadius, bool SparesBuildings = false);

/// <summary>What one breath did, for messages, tests and the renderer.</summary>
public sealed class BreathEvent
{
    public long Tick;

    /// <summary>Who breathed (null for unattributed test breaths). Read by the renderer to animate the breather.</summary>
    public Entity Source = Entity.Null;

    public float3 Mouth;
    public readonly List<float3> RayEnds = [];

    /// <summary>Cells set alight.</summary>
    public int Ignited;
}

/// <summary>
/// A dragon's breath as a deterministic cone of rays (<see cref="Maps.Map.Raycast"/>), jittered
/// with <see cref="Simulation.Rand"/>. Fire sets alight anything flammable in
/// the cells it passes through and stops at rock and walls. Where it strikes a surface the flames
/// splash outward in a ring, so breath poured down into a forest spills across it; ordinary fire
/// spread takes over from there. Recent breaths are kept briefly for the renderer.
/// </summary>
public static class Breath
{
    /// <summary>How long a breath stays in <see cref="Simulation.RecentBreaths"/> (and on screen).</summary>
    public const int ShowTicks = 40;

    /// <summary>A breather's reach: its life stage's.</summary>
    public static float RangeOf(Simulation sim, Entity dragon) => sim.World.Get<Creature>(dragon).Stage(sim.Clock.Tick).BreathRange;

    /// <summary>The breath a creature looses on purpose: its life stage's cone.</summary>
    public static BreathShape ShapeOf(Simulation sim, Entity dragon)
    {
        var stage = sim.World.Get<Creature>(dragon).Stage(sim.Clock.Tick);
        return new BreathShape(stage.BreathRange, stage.BreathCone, stage.BreathRays, 0.9f, 1.5f);
    }

    public static BreathEvent Breathe(Simulation sim, float3 mouth, float3 target, BreathShape shape, Entity? source = null)
    {
        var map = sim.Map;
        var ev = new BreathEvent { Tick = sim.Clock.Tick, Mouth = mouth, Source = source ?? Entity.Null };
        var aim = Normalize(target - mouth);
        var spread = DetMath.Tan(shape.ConeDegrees * MathF.PI / 360f);

        // Two axes perpendicular to the aim, for jittering rays inside the cone.
        var side = MathF.Abs(aim.Z) > 0.9f ? new float3(1, 0, 0) : Normalize(Cross(aim, new float3(0, 0, 1)));
        var up = Cross(side, aim);

        for (var r = 0; r < shape.Rays; r++)
        {
            var dir = Normalize(aim + side * (sim.Rand.Range(-1f, 1f) * spread) + up * (sim.Rand.Range(-1f, 1f) * spread));
            var traversed = 0;
            var lastAir = -1;
            var hit = map.Raycast(
                mouth,
                dir,
                shape.Range,
                i =>
                {
                    if (Blocks(sim, i))
                        return true;
                    lastAir = i;
                    // Fades along the ray; the first cell (the dragon's own) is spared.
                    if (traversed++ > 0 && Burn(sim, i, shape.Intensity * (1f - 0.5f * traversed / (shape.Range + 1)), shape))
                        ev.Ignited++;
                    return false;
                },
                out var cell,
                out _
            );

            if (hit && lastAir >= 0)
            {
                var impact = map.CellOf(lastAir);
                ev.RayEnds.Add(new float3(impact.X + 0.5f, impact.Y + 0.5f, impact.Z + 0.3f));
                ev.Ignited += Splash(sim, impact, shape);
            }
            else
            {
                ev.RayEnds.Add(mouth + dir * shape.Range);
            }
        }

        sim.RecentBreaths.Add(ev);
        return ev;
    }

    /// <summary>Rock and walls stop the flames.</summary>
    private static bool Blocks(Simulation sim, int i)
    {
        if (sim.Map.IsSolid(i))
            return true;
        var s = sim.Map.Structure[i];
        return !s.IsNull && sim.World.Get<Thing>(s).Def.Building?.IsWall == true && !sim.World.Has<Construction>(s);
    }

    private static bool Burn(Simulation sim, int i, float intensity, BreathShape shape)
    {
        var map = sim.Map;
        if (map.Active[i] && map.Temperature[i] < 300f)
            map.Temperature[i] = Math.Min(300f, map.Temperature[i] + 40f * intensity);
        if (shape.SparesBuildings && Sheltered(sim, i))
            return false;
        return map.Fire[i] <= 0 && sim.Fire.Ignite(sim, map.CellOf(i), intensity);
    }

    /// <summary>Part of a building: a structure or floor, or inside an indoor room.</summary>
    private static bool Sheltered(Simulation sim, int i)
    {
        var map = sim.Map;
        return !map.Structure[i].IsNull || !map.Floor[i].IsNull || sim.Rooms.RoomAt(map.CellOf(i))?.Indoors == true;
    }

    /// <summary>Flames spilling across the surface around where a ray struck, thinning out with distance.</summary>
    private static int Splash(Simulation sim, int3 impact, BreathShape shape)
    {
        var ignited = 0;
        foreach (var c in sim.SpiralFrom(impact, (int)MathF.Ceiling(shape.SplashRadius)))
        {
            var dx = c.X - impact.X;
            var dy = c.Y - impact.Y;
            var d = MathF.Sqrt(dx * dx + dy * dy);
            if (d > shape.SplashRadius || Math.Abs(c.Z - impact.Z) > 1)
                continue;
            var falloff = 1f - d / (shape.SplashRadius + 1f);
            if (sim.Rand.Chance(0.6f * falloff) && Burn(sim, sim.Map.Idx(c), shape.Intensity * falloff, shape))
                ignited++;
        }
        return ignited;
    }

    /// <summary>Forget breaths that are no longer shown.</summary>
    public static void Prune(Simulation sim) =>
        sim.RecentBreaths.RemoveAll(b => sim.Clock.Tick - b.Tick > ShowTicks);

    private static float3 Normalize(float3 v)
    {
        var len = MathF.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);
        return len < 1e-6f ? new float3(1, 0, 0) : v * (1f / len);
    }

    private static float3 Cross(float3 a, float3 b) =>
        new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
}
