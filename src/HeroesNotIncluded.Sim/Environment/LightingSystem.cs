using HeroesNotIncluded.Sim.Components;

namespace HeroesNotIncluded.Sim.Environment;

/// <summary>
/// Light level per cell (0 = pitch dark, 1 = full daylight): sky light where unroofed plus
/// daylight through windows, point lights from lit fires, buildings' lamps (<see cref="Defs.BuildingDef.Lamps"/>) and burning tiles. Drawn by the renderer; the sim only reads it
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
            map.Light[i] = map.Roofed[i] ? Indoors(sim) : sky;

        // By day, windows let the sky in: brighter by them than the room's dim, fading across it.
        sim.Rooms.UpdateIfNeeded();
        if (sim.Clock.Daylight > 0)
            foreach (var (cell, room) in Windows(sim))
                SkyThrough(sim, cell, room, Indoors(sim), sky * WindowStrength);

        foreach (var e in sim.World.Query<Fueled>())
        {
            var thing = sim.World.Get<Thing>(e);
            if (thing.Spawned && sim.Burning(e))
                Splat(sim, Warmth.FireCell(thing), thing.Def.Building!.LightRadius, 1f);
        }

        foreach (var (cell, lamp, _) in Systems.Buildings.LitLamps(sim))
            if (!Afterlife.Hauntings.Guttered(sim, cell)) // a ghost has it dark a while
                Splat(sim, cell, lamp.Building!.LightRadius, LampStrength);
        foreach (var (cell, lamp) in StandingLamps(sim))
            if (!Afterlife.Hauntings.Guttered(sim, cell))
                Splat(sim, cell, lamp.Building!.LightRadius, LampStrength);

        foreach (var i in sim.Fire.BurningTiles)
            Splat(sim, map.CellOf(i), 2f + 3f * map.Fire[i], map.Fire[i]);

        sim.Rooms.UpdateTemperatures();
    }

    private readonly List<(int3 Cell, Defs.ThingDef Def)> _standing = [];
    private int _standingVersion = -1;

    /// <summary>
    /// The lamps standing on their own, lit for good (<see cref="Defs.BuildingProps.AlwaysLit"/>: a
    /// quay's lanterns), found again only when the map's structures change.
    /// </summary>
    public IReadOnlyList<(int3 Cell, Defs.ThingDef Def)> StandingLamps(Simulation sim)
    {
        if (_standingVersion == sim.Map.StructureVersion)
            return _standing;
        _standingVersion = sim.Map.StructureVersion;
        _standing.Clear();
        foreach (var e in sim.World.Each<Thing>())
        {
            var t = sim.World.Get<Thing>(e);
            if (t.Spawned && t.Def.Building is { AlwaysLit: true, LightRadius: > 0 })
                _standing.Add((t.Cell, t.Def));
        }
        return _standing;
    }

    /// <summary>How far a window's daylight reaches into its room (cells), and how much of the sky's it lets in.</summary>
    public const float WindowReach = 4f;
    public const float WindowStrength = 0.9f;

    private readonly List<(int3 Cell, int Room)> _windows = [];
    private int _windowsVersion = -1;

    /// <summary>
    /// Every finished window (<see cref="Defs.BuildingProps.Window"/>) with the indoor room it opens
    /// onto (a window between two rooms lights both), found again only when the map's structures change.
    /// </summary>
    /// <remarks>Reads the rooms as they stand (the lighting pass brings them up to date first), so a renderer may ask too.</remarks>
    public IReadOnlyList<(int3 Cell, int Room)> Windows(Simulation sim)
    {
        if (_windowsVersion == sim.Map.StructureVersion)
            return _windows;
        _windowsVersion = sim.Map.StructureVersion;
        _windows.Clear();
        var map = sim.Map;
        foreach (var e in sim.World.Each<Thing>())
        {
            var t = sim.World.Get<Thing>(e);
            if (!t.Spawned || t.Def.Building is not { Window: true } || sim.World.Has<Construction>(e))
                continue;
            foreach (var d in Sides)
            {
                var n = t.Cell + d;
                if (!map.InBounds(n))
                    continue;
                var id = map.RoomId[map.Idx(n)];
                if (id > 0 && sim.Rooms.All[id - 1].Indoors && !_windows.Contains((t.Cell, id)))
                    _windows.Add((t.Cell, id));
            }
        }
        return _windows;
    }

    private static readonly int3[] Sides = [new(1, 0, 0), new(-1, 0, 0), new(0, 1, 0), new(0, -1, 0)];

    /// <summary>The light under a roof with no window or lamp: dim even at noon.</summary>
    public float Indoors(Simulation sim) => Math.Min(SkyLight(sim), 0.35f + 0.3f * sim.Clock.Daylight);

    /// <summary>
    /// Daylight through a window: its room's cells on its level, from the room's dim up towards the
    /// window's <paramref name="strength"/>, brightest beside it.
    /// </summary>
    private static void SkyThrough(Simulation sim, int3 window, int room, float dim, float strength)
    {
        var map = sim.Map;
        foreach (var t in map.CellsInRadius(window, WindowReach))
        {
            var i = map.Idx(t.X, t.Y, window.Z);
            if (map.RoomId[i] != room)
                continue;
            var dx = t.X - window.X;
            var dy = t.Y - window.Y;
            var falloff = 1f - MathF.Sqrt(dx * dx + dy * dy) / (WindowReach + 0.5f);
            if (falloff > 0 && strength > dim)
                map.Light[i] = Math.Min(1f, Math.Max(map.Light[i], dim + (strength - dim) * falloff));
        }
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
