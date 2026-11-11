using DragonWorld.Sim.Maps;

namespace DragonWorld.Sim.Systems;

/// <summary>
/// Fishing from the shore: a villager stands on a marked bank cell next to water and casts. Each
/// stretch of water (an 8×8 patch per level) holds a stock of fish that every catch thins out
/// and that recovers over a few days, so one good spot can't feed a dragon on its own. The stocks
/// are saved with <see cref="WildlifeSystem"/>.
/// </summary>
public static class Fishing
{
    /// <summary>Work-ticks per cast.</summary>
    public const int CastWork = 750;

    /// <summary>Casts a fisher makes before taking a break (so needs get a look in).</summary>
    public const int CastsPerTrip = 4;

    /// <summary>Chance of a bite from fully stocked water.</summary>
    public const float BiteChance = 0.7f;

    /// <summary>Stock lost per fish caught, and regained per day.</summary>
    public const float CatchDepletion = 0.06f, RecoveryPerDay = 0.3f;

    private const int PatchSize = 8;

    /// <summary>A dry cell someone can stand on, with water in one of its 8 neighbors.</summary>
    public static bool CanFishFrom(Simulation sim, int3 cell)
    {
        var map = sim.Map;
        if (!map.InBounds(cell) || !map.Standable(map.Idx(cell)) || map.GroundUnder(map.Idx(cell)) is not { IsWater: false })
            return false;
        return WaterNextTo(sim, cell) != null;
    }

    /// <summary>A neighboring cell over water (the first in a fixed order), if any.</summary>
    public static int3? WaterNextTo(Simulation sim, int3 cell)
    {
        var map = sim.Map;
        for (var dy = -1; dy <= 1; dy++)
        for (var dx = -1; dx <= 1; dx++)
        {
            var n = cell + new int3(dx, dy, 0);
            if ((dx != 0 || dy != 0) && map.InBounds(n) && !map.IsSolid(map.Idx(n)) && map.GroundUnder(map.Idx(n)) is { IsWater: true })
                return n;
        }
        return null;
    }

    /// <summary>Mark the cells that are fishable; returns how many were.</summary>
    public static int Designate(Simulation sim, IEnumerable<int3> cells)
    {
        var count = 0;
        foreach (var c in cells)
            if (CanFishFrom(sim, c))
            {
                sim.Map.Designate(sim.Map.Idx(c), Designation.Fish);
                count++;
            }
        return count;
    }

    /// <summary>Which stock a fishing spot draws on: the patch of the water it faces.</summary>
    public static int PatchOf(Simulation sim, int3 spot)
    {
        var water = WaterNextTo(sim, spot) ?? spot;
        var map = sim.Map;
        var across = (map.Width + PatchSize - 1) / PatchSize;
        var down = (map.Height + PatchSize - 1) / PatchSize;
        return (water.Z * down + water.Y / PatchSize) * across + water.X / PatchSize;
    }
}
