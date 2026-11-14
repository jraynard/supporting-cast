using HeroesNotIncluded.Sim.Components;

namespace HeroesNotIncluded.Sim.Environment;

/// <summary>
/// Light level per cell (0 = pitch dark, 1 = full daylight): sky light where unroofed plus
/// point lights from lit fires, buildings' lamps (<see cref="Defs.BuildingDef.Lamps"/>) and burning tiles. Drawn by the renderer; the sim only reads it
/// for mood (sitting in the dark indoors).
/// </summary>
public sealed class LightingSystem : ISimSystem
{
    public string Name => "Lighting";
    public int Interval => 20;

    public float NightAmbient { get; set; } = 0.12f;

    /// <summary>Incremented on every update, so renderers know when to re-upload the grid.</summary>
    public int Version { get; private set; }

    /// <summary>Light level under the open sky right now.</summary>
    public float SkyLight(Simulation sim) => NightAmbient + (1 - NightAmbient) * sim.Clock.Daylight;

    public void Update(Simulation sim)
    {
        var map = sim.Map;
        Version++;
        var sky = SkyLight(sim);
        // Only cells where things can be; open sky keeps whatever it had (renderers treat it as sky).
        foreach (var i in map.ActiveCells)
            map.Light[i] = map.Roofed[i] ? Math.Min(sky, 0.35f + 0.3f * sim.Clock.Daylight) : sky;

        foreach (var e in sim.World.Query<Fueled>())
        {
            var thing = sim.World.Get<Thing>(e);
            if (thing.Spawned && sim.Burning(e))
                Splat(sim, Warmth.FireCell(thing), thing.Def.Building!.LightRadius, 1f);
        }

        foreach (var (cell, lamp, _) in Systems.Buildings.LitLamps(sim))
            if (!Afterlife.Hauntings.Guttered(sim, cell)) // a ghost has it dark a while
                Splat(sim, cell, lamp.Building!.LightRadius, LampStrength);

        foreach (var i in sim.Fire.BurningTiles)
            Splat(sim, map.CellOf(i), 2f + 3f * map.Fire[i], map.Fire[i]);

        sim.Rooms.UpdateTemperatures();
    }

    /// <summary>A lamp is softer than a fire.</summary>
    public const float LampStrength = 0.8f;

    /// <summary>
    /// A point light: full strength at the center fading to nothing at the radius. It reaches up and
    /// down a few levels (vertical distance counts double) unless a floor or rock is in the way.
    /// </summary>
    private static void Splat(Simulation sim, int3 center, float radius, float strength)
    {
        if (radius <= 0)
            return;
        var map = sim.Map;
        var levels = (int)(radius / 2);
        foreach (var t in map.CellsInRadius(center, radius))
        for (var dz = -levels; dz <= levels; dz++)
        {
            var z = t.Z + dz;
            if (z < 0 || z >= map.Depth)
                continue;
            var i = map.Idx(t.X, t.Y, z);
            if (!map.Active[i] || (dz != 0 && !OpenBetween(map, t.X, t.Y, t.Z, z)))
                continue;
            var dx = t.X - center.X;
            var dy = t.Y - center.Y;
            var falloff = 1f - MathF.Sqrt(dx * dx + dy * dy + 4 * dz * dz) / (radius + 0.5f);
            if (falloff <= 0)
                continue;
            map.Light[i] = Math.Min(1f, Math.Max(map.Light[i], falloff * strength));
        }
    }

    /// <summary>No floor or solid block between two levels of a column.</summary>
    private static bool OpenBetween(Maps.Map map, int x, int y, int z0, int z1)
    {
        var (lo, hi) = z0 < z1 ? (z0, z1) : (z1, z0);
        for (var z = lo + 1; z <= hi; z++)
        {
            var i = map.Idx(x, y, z);
            if (map.Ceiling(i) || map.IsSolid(i))
                return false;
        }
        return true;
    }
}
